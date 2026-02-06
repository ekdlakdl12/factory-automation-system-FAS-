#include <opencv2/opencv.hpp>
#include <iostream>
#include <vector>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <cstdio>
#include <cctype>
#include <filesystem>
#include <cerrno>
#include <chrono>
#include <thread>
#include <cstdlib>

#include <modbus/modbus.h>
namespace fs = std::filesystem;

using namespace cv;
using namespace std;

// =====================
// ? TEST MODE
// =====================
static const bool TEST_MODE = true;          // ? true면 트리거 없이 자동 저장
static const int  TEST_INTERVAL_MS = 500;    // ? 저장 주기(ms) - 너무 많으면 500~1000 추천

// =====================
// USER CONFIG
// =====================
static const char* PLC_IP = "192.168.0.202";
static const int   PLC_PORT = 502;

// PLC -> PC 트리거 코일
static const int START_COIL = 100;

// PC -> PLC 원-핫 출력 코일(색상)
static const int BASE_ADDR_100 = 100;
static const int COIL_GREEN = BASE_ADDR_100 + 1; // 101
static const int COIL_BLUE = BASE_ADDR_100 + 2; // 102
static const int COIL_RED = BASE_ADDR_100 + 3; // 103
static const int COIL_NONE = BASE_ADDR_100 + 4; // 104

static const int PULSE_MS = 2000; // 코일 ON 유지 시간(ms)

// 재접속 정책
static const int RECONNECT_MIN_MS = 5000;
static const int RECONNECT_MAX_MS = 10000;

// Camera
static int  DEVICE_INDEX = 1;
static bool USE_DSHOW = true;
static int  CAM_W = 1280;
static int  CAM_H = 720;

// Project paths (? JSON은 하나만)
static const string CAPTURE_DIR = "./Colorcaptures";
static const string TOTAL_JSON = "./total.json";

// ? ROI (고정)
static const Rect ROI_FIXED(475, 50, 345, 1000);

// Object ROI detection tuning (이번 로직에서는 고정 ROI만 사용)
static double MIN_CONTOUR_AREA = 2000.0;
static int ROI_PAD = 10;

// Color decision tuning
static int MIN_COLOR_PIXELS = 100;
static double MIN_COLOR_RATIO = 0.01;

// =====================
// File helpers
// =====================
static bool WriteTextFileAtomic(const string& path, const string& text)
{
    string tmp = path + ".tmp";
    {
        ofstream ofs(tmp, ios::out | ios::trunc);
        if (!ofs.is_open()) return false;
        ofs << text;
        ofs.close();
    }
    ::remove(path.c_str());
    if (::rename(tmp.c_str(), path.c_str()) != 0) {
        ofstream ofs(path, ios::out | ios::trunc);
        if (!ofs.is_open()) { ::remove(tmp.c_str()); return false; }
        ofs << text;
        ofs.close();
        ::remove(tmp.c_str());
        return true;
    }
    return true;
}

static bool EnsureJsonArrayFile(const string& path)
{
    ifstream ifs(path);
    if (ifs.is_open()) return true;
    return WriteTextFileAtomic(path, "[]\n");
}

static bool AppendJsonArray(const string& path, const string& recordJson)
{
    string content;
    {
        ifstream ifs(path, ios::in);
        if (ifs.is_open()) {
            ostringstream ss;
            ss << ifs.rdbuf();
            content = ss.str();
            ifs.close();
        }
        else content = "[]";
    }

    auto rtrim = [&](string& s) {
        while (!s.empty() && isspace((unsigned char)s.back())) s.pop_back();
        };
    auto ltrim = [&](string& s) {
        size_t i = 0;
        while (i < s.size() && isspace((unsigned char)s[i])) i++;
        s.erase(0, i);
        };

    rtrim(content);
    ltrim(content);
    if (content.empty()) content = "[]";
    if (content.front() != '[' || content.back() != ']') content = "[]";

    bool hasAny = false;
    for (size_t i = 1; i + 1 < content.size(); i++) {
        if (content[i] == '{') { hasAny = true; break; }
    }

    string out;
    if (!hasAny) {
        out = "[\n" + recordJson + "\n]\n";
    }
    else {
        out = content.substr(0, content.size() - 1);
        rtrim(out);
        if (!out.empty() && out.back() != '[') out += ",";
        out += "\n" + recordJson + "\n]\n";
    }

    return WriteTextFileAtomic(path, out);
}

static string NowTimeString()
{
    using namespace chrono;
    auto now = system_clock::now();
    auto ms = duration_cast<milliseconds>(now.time_since_epoch()) % 1000;
    time_t t = system_clock::to_time_t(now);
    tm lt{};
#ifdef _WIN32
    localtime_s(&lt, &t);
#else
    lt = *localtime(&t);
#endif
    ostringstream ss;
    ss << put_time(&lt, "%Y-%m-%d %H:%M:%S")
        << "." << setw(3) << setfill('0') << (int)ms.count();
    return ss.str();
}

static long long NowMillis()
{
    using clock = chrono::system_clock;
    return chrono::duration_cast<chrono::milliseconds>(
        clock::now().time_since_epoch()).count();
}

// =====================
// Label/Count helpers (total.json 기준)
// =====================
static bool ReadAllText(const string& path, string& out)
{
    ifstream ifs(path, ios::in);
    if (!ifs.is_open()) return false;
    ostringstream ss;
    ss << ifs.rdbuf();
    out = ss.str();
    return true;
}

static bool ExtractQuotedValueAt(const string& s, size_t keyPos, string& outVal)
{
    size_t colon = s.find(':', keyPos);
    if (colon == string::npos) return false;

    size_t q1 = s.find('"', colon + 1);
    if (q1 == string::npos) return false;
    size_t q2 = s.find('"', q1 + 1);
    if (q2 == string::npos) return false;

    outVal = s.substr(q1 + 1, q2 - (q1 + 1));
    return true;
}

static bool ExtractIntValueAfterKey(const string& s, size_t keyPos, int& outInt)
{
    size_t colon = s.find(':', keyPos);
    if (colon == string::npos) return false;

    size_t p = colon + 1;
    while (p < s.size() && isspace((unsigned char)s[p])) p++;

    bool neg = false;
    if (p < s.size() && s[p] == '-') { neg = true; p++; }

    if (p >= s.size() || !isdigit((unsigned char)s[p])) return false;

    long long v = 0;
    while (p < s.size() && isdigit((unsigned char)s[p])) {
        v = v * 10 + (s[p] - '0');
        p++;
    }
    if (neg) v = -v;
    outInt = (int)v;
    return true;
}

static int GetNextCountFromTotalJson(const string& path, const string& color)
{
    string s;
    if (!ReadAllText(path, s)) return 1;

    int maxCount = 0;
    size_t pos = 0;

    while (true) {
        size_t colorKey = s.find("\"color\"", pos);
        if (colorKey == string::npos) break;

        string cval;
        if (!ExtractQuotedValueAt(s, colorKey, cval)) {
            pos = colorKey + 7;
            continue;
        }

        size_t objEnd = s.find('}', colorKey);
        if (objEnd == string::npos) objEnd = min(s.size(), colorKey + 400);

        if (cval == color) {
            size_t countKey = s.find("\"count\"", colorKey);
            if (countKey != string::npos && countKey < objEnd) {
                int cnt = 0;
                if (ExtractIntValueAfterKey(s, countKey, cnt)) {
                    if (cnt > maxCount) maxCount = cnt;
                }
            }
        }

        pos = colorKey + 7;
    }

    return maxCount + 1;
}

static string MakeLabel(const string& color, int count)
{
    char prefix = 'n';
    if (color == "RED") prefix = 'r';
    else if (color == "GREEN") prefix = 'g';
    else if (color == "BLUE") prefix = 'b';
    else prefix = 'n';

    return string(1, prefix) + to_string(count);
}

// =====================
// HSV thresholds
// =====================
struct HsvRange { Scalar L; Scalar U; };

struct ColorThresholds {
    HsvRange R1{ Scalar(0,   60,  40), Scalar(12,  255, 255) };
    HsvRange R2{ Scalar(168, 60,  40), Scalar(179, 255, 255) };
    HsvRange G{ Scalar(30,  40,  40), Scalar(95,  255, 255) };
    HsvRange B{ Scalar(85,  40,  40), Scalar(140, 255, 255) };
};

static void BuildMasksRGB(const Mat& hsv, Mat& maskR, Mat& maskG, Mat& maskB,
    const ColorThresholds& th)
{
    Mat rA, rB;
    inRange(hsv, th.R1.L, th.R1.U, rA);
    inRange(hsv, th.R2.L, th.R2.U, rB);
    maskR = rA | rB;

    inRange(hsv, th.G.L, th.G.U, maskG);
    inRange(hsv, th.B.L, th.B.U, maskB);

    Mat k = getStructuringElement(MORPH_RECT, Size(5, 5));
    morphologyEx(maskR, maskR, MORPH_OPEN, k, Point(-1, -1), 1);
    morphologyEx(maskR, maskR, MORPH_CLOSE, k, Point(-1, -1), 2);
    morphologyEx(maskG, maskG, MORPH_OPEN, k, Point(-1, -1), 1);
    morphologyEx(maskG, maskG, MORPH_CLOSE, k, Point(-1, -1), 2);
    morphologyEx(maskB, maskB, MORPH_OPEN, k, Point(-1, -1), 1);
    morphologyEx(maskB, maskB, MORPH_CLOSE, k, Point(-1, -1), 2);
}

static int CountMaskPixels(const Mat& mask) {
    return countNonZero(mask);
}

// =====================
// Image save (color_<label>.jpg)
// =====================
static string SaveColorCroppedJpg_ByLabel(
    const Mat& roiBgr,
    const string& label,
    const string& color)
{
    std::error_code ec;
    filesystem::create_directories(CAPTURE_DIR, ec);

    ostringstream fn;
    fn << "color_" << label << ".jpg";
    filesystem::path outPath = filesystem::path(CAPTURE_DIR) / fn.str();

    Mat canvas = roiBgr.clone();
    int baseLine = 0;

    string t1 = "Label: " + label;
    string t2 = "Color: " + color;

    Size s1 = getTextSize(t1, FONT_HERSHEY_SIMPLEX, 0.8, 1, &baseLine);
    Size s2 = getTextSize(t2, FONT_HERSHEY_SIMPLEX, 0.8, 1, &baseLine);

    Rect bg1(10, 10, s1.width + 6, s1.height + 6);
    Rect bg2(10, 45, s2.width + 6, s2.height + 6);

    rectangle(canvas, bg1, Scalar(255, 255, 255), FILLED);
    rectangle(canvas, bg1, Scalar(0, 0, 0), 1);
    rectangle(canvas, bg2, Scalar(255, 255, 255), FILLED);
    rectangle(canvas, bg2, Scalar(0, 0, 0), 1);

    putText(canvas, t1, Point(13, 10 + s1.height),
        FONT_HERSHEY_SIMPLEX, 0.8, Scalar(0, 0, 0), 1);
    putText(canvas, t2, Point(13, 45 + s2.height),
        FONT_HERSHEY_SIMPLEX, 0.8, Scalar(0, 0, 0), 1);

    vector<int> params = { IMWRITE_JPEG_QUALITY, 92 };
    if (!imwrite(outPath.string(), canvas, params)) return "";

    return outPath.generic_string();
}

// =====================
// Color classification (ROI에서만)
// =====================
static string ClassifyColorROI(const Mat& roiBgr, const ColorThresholds& th,
    int& outRpix, int& outGpix, int& outBpix)
{
    Mat hsv;
    cvtColor(roiBgr, hsv, COLOR_BGR2HSV);
    GaussianBlur(hsv, hsv, Size(3, 3), 0);

    Mat maskR, maskG, maskB;
    BuildMasksRGB(hsv, maskR, maskG, maskB, th);

    int rPix = CountMaskPixels(maskR);
    int gPix = CountMaskPixels(maskG);
    int bPix = CountMaskPixels(maskB);

    outRpix = rPix;
    outGpix = gPix;
    outBpix = bPix;

    int bestPix = 0;
    string color = "NONE";

    if (rPix > bestPix && rPix > gPix && rPix > bPix) { bestPix = rPix; color = "RED"; }
    else if (gPix > bestPix && gPix > rPix && gPix > bPix) { bestPix = gPix; color = "GREEN"; }
    else if (bPix > bestPix && bPix > rPix && bPix > gPix) { bestPix = bPix; color = "BLUE"; }

    int roiPixels = roiBgr.rows * roiBgr.cols;
    double ratio = (roiPixels > 0) ? (double)bestPix / (double)roiPixels : 0.0;

    if (bestPix < MIN_COLOR_PIXELS || ratio < MIN_COLOR_RATIO)
        return "NONE";

    return color;
}

// =====================
// UI helper
// =====================
static void DrawTextLine(Mat& img, const string& text, int lineIndex, Scalar color = Scalar(0, 255, 0))
{
    int y = 25 + lineIndex * 25;
    putText(img, text, Point(10, y), FONT_HERSHEY_SIMPLEX, 0.7, Scalar(0, 0, 0), 3, LINE_AA);
    putText(img, text, Point(10, y), FONT_HERSHEY_SIMPLEX, 0.7, color, 1, LINE_AA);
}

// =====================
// MAIN
// =====================
int main()
{
    cout << "[CWD] " << filesystem::current_path().string() << "\n";
    cout << "[MODE] RGB Detection (TEST_MODE=" << (TEST_MODE ? "ON" : "OFF") << ")\n";
    cout << "[JSON] " << TOTAL_JSON << " (single file)\n";
    cout << "[IMG]  " << CAPTURE_DIR << "/color_<label>.jpg\n";
    cout << "[ROI]  fixed=(" << ROI_FIXED.x << "," << ROI_FIXED.y << ","
        << ROI_FIXED.width << "," << ROI_FIXED.height << ")\n\n";

    std::error_code ec;
    filesystem::create_directories(CAPTURE_DIR, ec);

    if (!EnsureJsonArrayFile(TOTAL_JSON)) {
        cerr << "[FATAL] Cannot create " << TOTAL_JSON
            << " (cwd=" << filesystem::current_path().string() << ")\n";
        return -1;
    }
    cout << "[JSON] Ready: " << TOTAL_JSON << "\n\n";

    VideoCapture cap;
    if (USE_DSHOW) cap.open(DEVICE_INDEX, CAP_DSHOW);
    else cap.open(DEVICE_INDEX);

    if (!cap.isOpened()) {
        cerr << "Camera open failed\n";
        return -1;
    }

    cap.set(CAP_PROP_FRAME_WIDTH, CAM_W);
    cap.set(CAP_PROP_FRAME_HEIGHT, CAM_H);
    cout << "[CAMERA] Opened (device " << DEVICE_INDEX << ")\n";

    // ? 테스트 모드에서는 Modbus를 아예 사용하지 않음
    cout << (TEST_MODE ? "[TEST] No PLC trigger. Auto-detect+save.\n" : "[PLC] Trigger mode.\n");

    ColorThresholds th;

    // 시각화용 마지막 결과
    string lastColor = "NONE";
    string lastLabel = "";
    int lastCount = 0;
    string lastImgPath = "";
    int lastRPix = 0, lastGPix = 0, lastBPix = 0;
    long long lastSaveMs = 0;

    namedWindow("VIEW", WINDOW_NORMAL);
    resizeWindow("VIEW", 1280, 720);
    namedWindow("ROI_CROP", WINDOW_NORMAL);
    resizeWindow("ROI_CROP", 700, 700);

    cout << "[RUN] Press ESC to quit\n\n";

    while (true) {
        Mat frame;
        if (!cap.read(frame) || frame.empty()) {
            this_thread::sleep_for(chrono::milliseconds(10));
            continue;
        }

        Rect roi = ROI_FIXED & Rect(0, 0, frame.cols, frame.rows);

        // ? 테스트 모드: 일정 주기마다 자동 측정/저장
        bool doProcess = false;
        if (TEST_MODE) {
            long long nowMs = NowMillis();
            if (lastSaveMs == 0 || (nowMs - lastSaveMs) >= TEST_INTERVAL_MS) {
                doProcess = true;
                lastSaveMs = nowMs;
            }
        }

        if (doProcess) {
            string color = "NONE";
            int rPix = 0, gPix = 0, bPix = 0;
            string label = "";
            int count = 0;
            string imgPath = "";

            if (roi.width > 0 && roi.height > 0) {
                Mat roiBgr = frame(roi).clone();
                color = ClassifyColorROI(roiBgr, th, rPix, gPix, bPix);

                count = GetNextCountFromTotalJson(TOTAL_JSON, color);
                label = MakeLabel(color, count);
                imgPath = SaveColorCroppedJpg_ByLabel(roiBgr, label, color);

                Mat cropShow = roiBgr.clone();
                string title = "AUTO label=" + label + " color=" + color;
                putText(cropShow, title, Point(10, 30), FONT_HERSHEY_SIMPLEX, 0.9, Scalar(0, 0, 0), 3, LINE_AA);
                putText(cropShow, title, Point(10, 30), FONT_HERSHEY_SIMPLEX, 0.9, Scalar(0, 255, 255), 1, LINE_AA);
                imshow("ROI_CROP", cropShow);
            }
            else {
                color = "NONE";
                count = GetNextCountFromTotalJson(TOTAL_JSON, color);
                label = MakeLabel(color, count);
                imgPath = "";
            }

            string tsStr = NowTimeString();

            // total.json 저장
            {
                ostringstream rec;
                rec << "  {\n";
                rec << "    \"time\": \"" << tsStr << "\",\n";
                rec << "    \"label\": \"" << label << "\",\n";
                rec << "    \"color\": \"" << color << "\",\n";
                rec << "    \"count\": " << count << ",\n";
                rec << "    \"image\": \"" << imgPath << "\"\n";
                rec << "  }";
                AppendJsonArray(TOTAL_JSON, rec.str());
            }

            cout << "[AUTO] label=" << label
                << " | color=" << color
                << " | count=" << count
                << " | image=" << imgPath
                << " | pix(R/G/B)=" << rPix << "/" << gPix << "/" << bPix
                << "\n";

            // 시각화용 저장
            lastColor = color;
            lastLabel = label;
            lastCount = count;
            lastImgPath = imgPath;
            lastRPix = rPix; lastGPix = gPix; lastBPix = bPix;
        }

        // ===== VIEW 시각화(항상) =====
        Mat vis = frame.clone();
        rectangle(vis, roi, Scalar(0, 0, 255), 2);

        DrawTextLine(vis, "ROI: (" + to_string(roi.x) + "," + to_string(roi.y) + "," +
            to_string(roi.width) + "," + to_string(roi.height) + ")", 0, Scalar(0, 255, 255));

        DrawTextLine(vis, string("MODE: ") + (TEST_MODE ? "TEST(AUTO)" : "PLC(TRIGGER)"), 1,
            TEST_MODE ? Scalar(0, 255, 255) : Scalar(0, 255, 0));

        DrawTextLine(vis, "Last: label=" + lastLabel + " | color=" + lastColor + " | count=" + to_string(lastCount),
            2,
            (lastColor == "RED") ? Scalar(0, 0, 255) :
            (lastColor == "GREEN") ? Scalar(0, 255, 0) :
            (lastColor == "BLUE") ? Scalar(255, 0, 0) :
            Scalar(200, 200, 200));

        DrawTextLine(vis, "Pix(R/G/B)=" + to_string(lastRPix) + "/" + to_string(lastGPix) + "/" + to_string(lastBPix),
            3, Scalar(255, 255, 0));

        DrawTextLine(vis, "JSON: " + TOTAL_JSON, 4, Scalar(180, 180, 180));
        DrawTextLine(vis, "IMG : " + lastImgPath, 5, Scalar(180, 180, 180));
        DrawTextLine(vis, "AUTO interval(ms): " + to_string(TEST_INTERVAL_MS), 6, Scalar(180, 180, 180));
        DrawTextLine(vis, "ESC to quit", 7, Scalar(180, 180, 180));

        imshow("VIEW", vis);

        int key = waitKey(1);
        if (key == 27) break;

        this_thread::sleep_for(chrono::milliseconds(10));
    }

    cap.release();
    destroyAllWindows();
    return 0;
}
