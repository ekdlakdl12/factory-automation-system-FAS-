#include <opencv2/opencv.hpp>
#include <opencv2/core.hpp>
#include <iostream>
#include <vector>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <cstdio>
#include <cctype>
#include <algorithm>
#include <cmath>

#include <modbus/modbus.h>
#include <filesystem>
#include <cerrno>
#include <chrono>
#include <thread>

using namespace cv;
using namespace std;

// =====================
// MODBUS CONFIG
// =====================
static const char* PLC_IP = "192.168.0.202";
static const int   PLC_PORT = 502;

static const int START_COIL = 200;  // PLC -> PC 트리거
static const int COIL_BASE = 202;   // PC -> PLC 결과(펄스): BASE
static const int COIL_TOP = 201;    // PC -> PLC 결과(펄스): TOP
static const int PULSE_MS = 100;    // 펄스 폭(ms)

// =====================
// 판정 조건 (x만 보고 BASE/TOP/defect)
// =====================
static inline string DecideTypeByX(double xMm) {
    const double baseCenter = 60.0;
    const double tol = 3.0;
    const double defectCut = baseCenter - tol; // 57

    if (xMm >= baseCenter - tol && xMm <= baseCenter + tol) {
        return "TOP";
    }
    else if (xMm < defectCut) {
        return "defect";
    }
    else {
        return "BASE";
    }
}

// =====================
// ms 타임스탬프(표시용)
// =====================
static inline long long NowMillis() {
    using clock = chrono::system_clock;
    return chrono::duration_cast<chrono::milliseconds>(clock::now().time_since_epoch()).count();
}

// =====================
// 사람이 읽는 KST 시간 문자열 만들기
// =====================
static inline string NowKstString() {
    using clock = chrono::system_clock;
    auto now = clock::now();
    auto ms = chrono::duration_cast<chrono::milliseconds>(now.time_since_epoch()) % 1000;

    time_t tt = clock::to_time_t(now);

    tm tmLocal{};
#if defined(_WIN32)
    localtime_s(&tmLocal, &tt);
#else
    localtime_r(&tt, &tmLocal);
#endif

    ostringstream ss;
    ss << put_time(&tmLocal, "%Y-%m-%d %H:%M:%S")
        << '.' << setw(3) << setfill('0') << ms.count()
        << "+09:00";
    return ss.str();
}

// =====================
// Non-blocking 펄스 상태
// =====================
static bool      g_pulseActive = false;
static int       g_pulseAddr = -1;
static long long g_pulseStartMs = 0;

// =====================
// Modbus helpers
// =====================
static modbus_t* ConnectModbus(const char* ip, int port) {
    modbus_t* ctx = modbus_new_tcp(ip, port);
    if (!ctx) return nullptr;

    modbus_set_response_timeout(ctx, 0, 300000);

    if (modbus_connect(ctx) == -1) {
        modbus_free(ctx);
        return nullptr;
    }
    return ctx;
}

static bool ReadCoil(modbus_t* ctx, int addr, bool& outVal) {
    uint8_t bit = 0;
    int rc = modbus_read_bits(ctx, addr, 1, &bit);
    if (rc != 1) return false;
    outVal = (bit != 0);
    return true;
}

static bool WriteCoil(modbus_t* ctx, int addr, bool val) {
    int rc = modbus_write_bit(ctx, addr, val ? 1 : 0);
    return (rc == 1);
}

static bool StartPulse(modbus_t* ctx, int addr) {
    // 기존 펄스가 켜져있다면 일단 끄고 새로 시작(중첩 방지)
    if (g_pulseActive && g_pulseAddr >= 0) {
        WriteCoil(ctx, g_pulseAddr, false);
        g_pulseActive = false;
        g_pulseAddr = -1;
    }

    // 두 코일 OFF 먼저(원-핫 유지)
    if (!WriteCoil(ctx, COIL_BASE, false)) return false;
    if (!WriteCoil(ctx, COIL_TOP, false)) return false;

    // 목표 코일 ON (즉시)
    if (!WriteCoil(ctx, addr, true)) return false;

    g_pulseActive = true;
    g_pulseAddr = addr;
    g_pulseStartMs = NowMillis();
    return true;
}

static void UpdatePulse(modbus_t* ctx) {
    if (!g_pulseActive || g_pulseAddr < 0) return;

    long long now = NowMillis();
    if (now - g_pulseStartMs >= PULSE_MS) {
        WriteCoil(ctx, g_pulseAddr, false);
        g_pulseActive = false;
        g_pulseAddr = -1;
    }
}

// =====================
// File helpers
// =====================
static bool WriteTextFile(const string& path, const string& text) {
    ofstream ofs(path, ios::out | ios::trunc);
    if (!ofs.is_open()) return false;
    ofs << text;
    ofs.close();
    return true;
}

static bool EnsureJsonArrayFile(const string& path) {
    ifstream ifs(path);
    if (ifs.is_open()) return true;
    return WriteTextFile(path, "[]\n");
}

static bool AppendJsonArray(const string& path, const string& recordJson) {
    string content;
    {
        ifstream ifs(path, ios::in);
        if (ifs.is_open()) {
            ostringstream ss;
            ss << ifs.rdbuf();
            content = ss.str();
            ifs.close();
        }
        else {
            content = "[]";
        }
    }

    auto rtrim = [&](string& s) { while (!s.empty() && isspace((unsigned char)s.back())) s.pop_back(); };
    auto ltrim = [&](string& s) { size_t i = 0; while (i < s.size() && isspace((unsigned char)s[i])) i++; s.erase(0, i); };

    rtrim(content); ltrim(content);
    if (content.empty()) content = "[]";
    if (content.front() != '[' || content.back() != ']') content = "[]";

    bool hasAny = false;
    for (size_t i = 1; i + 1 < content.size(); i++) {
        if (content[i] == '{') { hasAny = true; break; }
    }

    string out;
    if (!hasAny) out = "[\n" + recordJson + "\n]\n";
    else {
        out = content.substr(0, content.size() - 1);
        rtrim(out);
        if (!out.empty() && out.back() != '[') out += ",";
        out += "\n" + recordJson + "\n]\n";
    }

    string tmpPath = path + ".tmp";
    { ofstream ofs(tmpPath, ios::out | ios::trunc); if (!ofs.is_open()) return false; ofs << out; }

    std::remove(path.c_str());
    if (std::rename(tmpPath.c_str(), path.c_str()) != 0) { std::remove(tmpPath.c_str()); return false; }
    return true;
}

static bool FileExists(const string& path) {
    ifstream ifs(path);
    return ifs.is_open();
}

// =====================
// Sharpness score
// =====================
static double SharpnessScore(const Mat& bgrOrGray) {
    Mat gray;
    if (bgrOrGray.channels() == 3) cvtColor(bgrOrGray, gray, COLOR_BGR2GRAY);
    else gray = bgrOrGray;

    Mat lap;
    Laplacian(gray, lap, CV_64F);

    Scalar mu, sigma;
    meanStdDev(lap, mu, sigma);
    return sigma[0] * sigma[0];
}

// =====================
// 저장 폴더/파일명
// =====================
static string g_captureDir = "captures";

static inline string NowFileStamp() {
    using clock = chrono::system_clock;
    auto now = clock::now();
    auto ms = chrono::duration_cast<chrono::milliseconds>(now.time_since_epoch()) % 1000;

    time_t tt = clock::to_time_t(now);
    tm tmLocal{};
#if defined(_WIN32)
    localtime_s(&tmLocal, &tt);
#else
    localtime_r(&tt, &tmLocal);
#endif

    ostringstream ss;
    ss << put_time(&tmLocal, "%Y%m%d_%H%M%S")
        << '_' << setw(3) << setfill('0') << ms.count();
    return ss.str();
}

// ✅ 객체 회전 정렬 + 크롭 + 치수 표기
static string SaveMeasuredCroppedJpg_CadStyle(
    const Mat& roiFrame,
    bool detected,
    const RotatedRect& rr,
    double xMm,
    double yMm,
    int labelNo,
    const string& type,
    double mmPerPx
) {
    std::error_code ec;
    filesystem::create_directories(g_captureDir, ec);

    const int MARGIN = 5;

    float rotationAngle = rr.angle;

    if (rotationAngle < -45) {
        rotationAngle += 90;
    }

    Point2f center = rr.center;
    Mat rotationMatrix = getRotationMatrix2D(center, rotationAngle, 1.0);

    Mat rotatedFrame;
    warpAffine(roiFrame, rotatedFrame, rotationMatrix, roiFrame.size(), INTER_LINEAR, BORDER_CONSTANT, Scalar(255, 255, 255));

    Point2f pts[4];
    rr.points(pts);

    Point2f rotatedPts[4];
    float cosA = cos(rotationAngle * CV_PI / 180.0);
    float sinA = sin(rotationAngle * CV_PI / 180.0);

    for (int i = 0; i < 4; i++) {
        float x = pts[i].x - center.x;
        float y = pts[i].y - center.y;

        rotatedPts[i].x = center.x + (x * cosA - y * sinA);
        rotatedPts[i].y = center.y + (x * sinA + y * cosA);
    }

    float minX = rotatedPts[0].x, maxX = rotatedPts[0].x;
    float minY = rotatedPts[0].y, maxY = rotatedPts[0].y;

    for (int i = 1; i < 4; i++) {
        minX = min(minX, rotatedPts[i].x);
        maxX = max(maxX, rotatedPts[i].x);
        minY = min(minY, rotatedPts[i].y);
        maxY = max(maxY, rotatedPts[i].y);
    }

    int cropX = max(0, (int)minX - MARGIN);
    int cropY = max(0, (int)minY - MARGIN);
    int cropW = min(rotatedFrame.cols - cropX, (int)(maxX - minX) + 2 * MARGIN);
    int cropH = min(rotatedFrame.rows - cropY, (int)(maxY - minY) + 2 * MARGIN);

    Rect cropRect(cropX, cropY, cropW, cropH);

    Mat croppedImg = rotatedFrame(cropRect).clone();

    float objLeft = minX - cropX;
    float objRight = maxX - cropX;
    float objTop = minY - cropY;
    float objBottom = maxY - cropY;
    float objCenterX = (objLeft + objRight) / 2.0;
    float objCenterY = (objTop + objBottom) / 2.0;

    const int CANVAS_MARGIN = 80;
    int finalW = croppedImg.cols + 2 * CANVAS_MARGIN;
    int finalH = croppedImg.rows + 2 * CANVAS_MARGIN;

    Mat canvas = Mat::ones(finalH, finalW, croppedImg.type()) * 255;

    croppedImg.copyTo(canvas(Rect(CANVAS_MARGIN, CANVAS_MARGIN, croppedImg.cols, croppedImg.rows)));

    float canvasObjLeft = objLeft + CANVAS_MARGIN;
    float canvasObjRight = objRight + CANVAS_MARGIN;
    float canvasObjTop = objTop + CANVAS_MARGIN;
    float canvasObjBottom = objBottom + CANVAS_MARGIN;
    float canvasObjCenterX = objCenterX + CANVAS_MARGIN;
    float canvasObjCenterY = objCenterY + CANVAS_MARGIN;

    int baseLine = 0;
    int textThickness = 1;
    double fontSize = 0.6;

    // X 치수
    {
        ostringstream ss;
        ss << fixed << setprecision(2) << xMm << "mm";
        string xLabel = ss.str();
        Size xSz = getTextSize(xLabel, FONT_HERSHEY_SIMPLEX, fontSize, textThickness, &baseLine);

        float textX = canvasObjCenterX - xSz.width / 2;
        float textY = canvasObjBottom + 30;

        Rect bgRect((int)textX - 4, (int)textY - xSz.height - 4, xSz.width + 8, xSz.height + 8);
        rectangle(canvas, bgRect, Scalar(255, 255, 255), FILLED);
        rectangle(canvas, bgRect, Scalar(0, 0, 0), 2);

        putText(canvas, xLabel, Point((int)textX, (int)textY), FONT_HERSHEY_SIMPLEX, fontSize, Scalar(0, 0, 0), textThickness);
    }

    // Y 치수
    {
        ostringstream ss;
        ss << fixed << setprecision(2) << yMm << "mm";
        string yLabel = ss.str();
        Size ySz = getTextSize(yLabel, FONT_HERSHEY_SIMPLEX, fontSize, textThickness, &baseLine);

        float textX = canvasObjRight + 15;
        float textY = canvasObjCenterY + ySz.height / 2;

        Rect bgRect((int)textX - 4, (int)textY - ySz.height - 4, ySz.width + 8, ySz.height + 8);
        rectangle(canvas, bgRect, Scalar(255, 255, 255), FILLED);
        rectangle(canvas, bgRect, Scalar(0, 0, 0), 2);

        putText(canvas, yLabel, Point((int)textX, (int)textY), FONT_HERSHEY_SIMPLEX, fontSize, Scalar(0, 0, 0), textThickness);
    }

    // Type 라벨
    {
        ostringstream ss;
        ss << "Type: " << type;
        string typeLabel = ss.str();

        Size typeSz = getTextSize(typeLabel, FONT_HERSHEY_SIMPLEX, 0.8, 1, &baseLine);
        Rect bgRect(10, 10, typeSz.width + 4, typeSz.height + 4);
        rectangle(canvas, bgRect, Scalar(255, 255, 255), FILLED);
        rectangle(canvas, bgRect, Scalar(0, 0, 0), 1);

        putText(canvas, typeLabel, Point(12, 10 + typeSz.height), FONT_HERSHEY_SIMPLEX, 0.8, Scalar(0, 0, 0), 1);
    }

    ostringstream fn;
    fn << "measure_" << NowFileStamp() << "_L" << labelNo << ".jpg";
    filesystem::path outPath = filesystem::path(g_captureDir) / fn.str();

    vector<int> params = { IMWRITE_JPEG_QUALITY, 92 };
    bool ok = imwrite(outPath.string(), canvas, params);
    if (!ok) return "";

    return outPath.generic_string();
}

// =====================
// Candidate buffer
// =====================
struct Cand {
    double score = 0.0;
    double wMm = 0.0;
    double hMm = 0.0;
    double ms = 0.0;

    RotatedRect rr;
    bool detected = false;

    Mat roiImg;
};

static void KeepTopK(vector<Cand>& v, int K) {
    sort(v.begin(), v.end(), [](const Cand& a, const Cand& b) { return a.score > b.score; });
    if ((int)v.size() > K) v.resize(K);
}

// =====================
// MAIN
// =====================
int main() {
    cout << "[CWD] " << filesystem::current_path().string() << "\n";
    cout << "[MODBUS] " << PLC_IP << ":" << PLC_PORT
        << " START=" << START_COIL
        << " BASE=" << COIL_BASE
        << " TOP=" << COIL_TOP
        << " PULSE=" << PULSE_MS << "ms\n";

    int deviceIndex = 1;

    double mmPerPx = 0.0;
    string yamlPath = "scale.yaml";

    string jsonPath = "result.json";
    string statusPath = "status.json";
    int labelCounter = 0;

    EnsureJsonArrayFile(jsonPath);

    // ===== YAML 로드 =====
    if (FileExists(yamlPath)) {
        FileStorage fs(yamlPath, FileStorage::READ);
        if (fs.isOpened()) {
            double loadedMmPerPx = 0.0;
            fs["mmPerPx"] >> loadedMmPerPx;
            if (loadedMmPerPx > 0.0) mmPerPx = loadedMmPerPx;
            fs.release();
        }
    }
    else {
        WriteTextFile(statusPath,
            "{\n"
            "  \"ok\": true,\n"
            "  \"note\": \"scale.yaml not found. Please create scale.yaml (mmPerPx) before measurement.\",\n"
            "  \"hint\": \"Create scale.yaml with key mmPerPx\"\n"
            "}\n");
    }

    VideoCapture cap(deviceIndex, CAP_DSHOW);
    if (!cap.isOpened()) {
        WriteTextFile(statusPath,
            "{\n"
            "  \"ok\": false,\n"
            "  \"reason\": \"camera open failed\",\n"
            "  \"hint\": \"Try deviceIndex 0/1/2 or remove CAP_DSHOW\"\n"
            "}\n");
        return -1;
    }

    cap.set(CAP_PROP_FOURCC, VideoWriter::fourcc('M', 'J', 'P', 'G'));
    cap.set(CAP_PROP_FRAME_WIDTH, 1920);
    cap.set(CAP_PROP_FRAME_HEIGHT, 1080);

    int actualWidth = (int)cap.get(CAP_PROP_FRAME_WIDTH);
    int actualHeight = (int)cap.get(CAP_PROP_FRAME_HEIGHT);
    cout << "[CAMERA] Actual resolution: " << actualWidth << "x" << actualHeight << "\n";

    Rect roi(710, 50, 550, 1000);

    if (roi.x + roi.width > actualWidth) {
        roi.width = actualWidth - roi.x;
    }
    if (roi.y + roi.height > actualHeight) {
        roi.height = actualHeight - roi.y;
    }

    cout << "[ROI] Adjusted ROI: x=" << roi.x << " y=" << roi.y
        << " width=" << roi.width << " height=" << roi.height << "\n";

    // ===== Modbus connect =====
    modbus_t* ctx = ConnectModbus(PLC_IP, PLC_PORT);
    if (!ctx) {
        cerr << "[MODBUS] connect failed: " << modbus_strerror(errno) << "\n";
        return -1;
    }
    cout << "[MODBUS] connected\n";

    // 안전 OFF
    WriteCoil(ctx, COIL_BASE, false);
    WriteCoil(ctx, COIL_TOP, false);

    Mat frame;

    int64 startTicks = getTickCount();
    double freq = getTickFrequency();

    int invalidRoiStreak = 0;

    const int TOP_K = 1;
    vector<Cand> buf;

    bool inCooldown = false;
    int presentStreak = 0;
    int absentStreak = 0;
    const int presentNeed = 2;
    const int absentNeed = 1;

    bool prevStart = false;
    bool busyWaitStartLow = false;
    bool armed = false;

    cout << "[RUN] waiting START rising-edge... (CTRL+C to quit)\n";

    for (;;) {
        // 펄스 OFF 타이밍 체크
        UpdatePulse(ctx);

        cap >> frame;

        if (frame.empty()) {
            double sec = (getTickCount() - startTicks) / freq;
            if (sec > 2.0) {
                WriteTextFile(statusPath,
                    "{\n"
                    "  \"ok\": false,\n"
                    "  \"reason\": \"frame empty timeout\",\n"
                    "  \"hint\": \"Capture card may not be delivering frames yet\"\n"
                    "}\n");
                break;
            }
            continue;
        }
        startTicks = getTickCount();

        // ===== PLC START 읽기 =====
        bool start = false;
        if (!ReadCoil(ctx, START_COIL, start)) {
            cerr << "[MODBUS] read START failed: " << modbus_strerror(errno) << "\n";
            this_thread::sleep_for(chrono::milliseconds(5));
            continue;
        }

        bool rising = (start && !prevStart);
        prevStart = start;

        if (busyWaitStartLow) {
            if (!start) {
                busyWaitStartLow = false;
                cout << "[TRIG] START back to 0 -> ready next\n";
            }
        }

        if (rising && !busyWaitStartLow) {
            cout << "[TRIG] rising-edge -> ARM measure\n";
            armed = true;
            inCooldown = false;
            buf.clear();
            presentStreak = 0;
            absentStreak = 0;
        }

        // ✅ ROI 범위 체크
        Rect r = roi & Rect(0, 0, frame.cols, frame.rows);
        if (r.width <= 0 || r.height <= 0) {
            invalidRoiStreak++;
            if (invalidRoiStreak > 60) {
                ostringstream ss;
                ss << "{\n"
                    << "  \"ok\": false,\n"
                    << "  \"reason\": \"ROI out of range\",\n"
                    << "  \"frameW\": " << frame.cols << ",\n"
                    << "  \"frameH\": " << frame.rows << ",\n"
                    << "  \"roi\": [" << roi.x << "," << roi.y << "," << roi.width << "," << roi.height << "]\n"
                    << "}\n";
                WriteTextFile(statusPath, ss.str());
                break;
            }
            continue;
        }
        invalidRoiStreak = 0;

        int64 t0 = getTickCount();

        Mat roiFrame = frame(r).clone();

        // ===== 탐지 파이프라인 =====
        Mat hsv;
        cvtColor(roiFrame, hsv, COLOR_BGR2HSV);

        Scalar lower(0, 50, 80);
        Scalar upper(70, 255, 255);

        Mat mask;
        inRange(hsv, lower, upper, mask);

        Mat blurred;
        GaussianBlur(mask, blurred, Size(3, 3), 0);
        threshold(blurred, blurred, 150, 255, THRESH_BINARY);

        Mat kernel = getStructuringElement(MORPH_RECT, Size(3, 3));
        morphologyEx(blurred, blurred, MORPH_OPEN, kernel, Point(-1, -1), 1);
        morphologyEx(blurred, blurred, MORPH_CLOSE, kernel, Point(-1, -1), 1);

        vector<vector<Point>> contours;
        vector<Vec4i> hierarchy;
        findContours(blurred, contours, hierarchy, RETR_EXTERNAL, CHAIN_APPROX_SIMPLE);

        bool detected = false;
        double wOut = 0.0, hOut = 0.0;

        int best = -1;
        double bestArea = 0.0;

        RotatedRect rr;
        float longSidePx = 0.0f, shortSidePx = 0.0f;

        if (!contours.empty()) {
            for (int i = 0; i < (int)contours.size(); i++) {
                double a = contourArea(contours[i]);
                if (a < 2000) continue;
                if (a > bestArea) {
                    bestArea = a;
                    best = i;
                }
            }

            if (best >= 0) {
                rr = minAreaRect(contours[best]);
                float rawW = rr.size.width;
                float rawH = rr.size.height;

                longSidePx = rawW;
                shortSidePx = rawH;
                if (longSidePx < shortSidePx) swap(longSidePx, shortSidePx);

                detected = true;

                if (mmPerPx > 0.0) {
                    wOut = (double)longSidePx * mmPerPx;
                    hOut = (double)shortSidePx * mmPerPx;
                }
            }
        }

        int64 t1 = getTickCount();
        double elapsedMs = (t1 - t0) * 1000.0 / freq;

        if (detected) { presentStreak++; absentStreak = 0; }
        else { absentStreak++; presentStreak = 0; }

        // ==========================
        // 측정/저장/출력
        // ==========================
        if (armed && mmPerPx > 0.0) {
            if (inCooldown) {
                if (absentStreak >= absentNeed) {
                    inCooldown = false;
                    buf.clear();
                }
            }
            else {
                if (presentStreak >= presentNeed && detected) {
                    double score = SharpnessScore(roiFrame);

                    Cand c;
                    c.score = score;
                    c.wMm = wOut;
                    c.hMm = hOut;
                    c.ms = elapsedMs;
                    c.rr = rr;
                    c.detected = detected;
                    c.roiImg = roiFrame.clone();

                    buf.push_back(c);
                    KeepTopK(buf, TOP_K);

                    if ((int)buf.size() >= TOP_K) {
                        double sumW = 0.0, sumH = 0.0, sumMs = 0.0;
                        for (auto& cc : buf) {
                            sumW += cc.wMm;
                            sumH += cc.hMm;
                            sumMs += cc.ms;
                        }
                        double avgW = sumW / (double)buf.size();
                        double avgH = sumH / (double)buf.size();
                        double avgMs = sumMs / (double)buf.size();

                        string type = DecideTypeByX(avgW);
                        labelCounter++;

                        // ===== PLC 펄스 =====
                        int targetCoil = (type == "BASE") ? COIL_BASE : COIL_TOP;
                        bool pulseOk = StartPulse(ctx, targetCoil);
                        if (!pulseOk) {
                            cerr << "[MODBUS] StartPulse failed: " << modbus_strerror(errno) << "\n";
                        }

                        // ===== 이미지 저장 =====
                        string imgPath = SaveMeasuredCroppedJpg_CadStyle(
                            buf[0].roiImg,
                            buf[0].detected,
                            buf[0].rr,
                            avgW,
                            avgH,
                            labelCounter,
                            type,
                            mmPerPx
                        );

                        // ===== JSON 저장 =====
                        string timeKst = NowKstString();

                        ostringstream rec;
                        rec << "  {\n";
                        rec << "    \"time_kst\": \"" << timeKst << "\",\n";
                        rec << "    \"x\": " << fixed << setprecision(3) << avgW << ",\n";
                        rec << "    \"y\": " << fixed << setprecision(3) << avgH << ",\n";
                        rec << "    \"ms\": " << fixed << setprecision(3) << avgMs << ",\n";
                        rec << "    \"type\": \"" << type << "\",\n";
                        rec << "    \"image\": \"" << imgPath << "\"\n";
                        rec << "  }";

                        bool ok = AppendJsonArray(jsonPath, rec.str());

                        {
                            ostringstream ss;
                            ss << "{\n"
                                << "  \"ok\": " << (ok ? "true" : "false") << ",\n"
                                << "  \"time_kst\": \"" << timeKst << "\",\n"
                                << "  \"x\": " << fixed << setprecision(3) << avgW << ",\n"
                                << "  \"y\": " << fixed << setprecision(3) << avgH << ",\n"
                                << "  \"ms\": " << fixed << setprecision(3) << avgMs << ",\n"
                                << "  \"type\": \"" << type << "\",\n"
                                << "  \"image\": \"" << imgPath << "\"\n"
                                << "}\n";
                            WriteTextFile(statusPath, ss.str());
                        }

                        cout << "[SAVED] label=" << labelCounter
                            << " time_kst=" << timeKst
                            << " x=" << fixed << setprecision(3) << avgW
                            << " y=" << fixed << setprecision(3) << avgH
                            << " type=" << type
                            << " ms=" << fixed << setprecision(3) << avgMs
                            << " pulse=" << (pulseOk ? "OK" : "FAIL")
                            << " image=" << imgPath
                            << "\n";

                        armed = false;
                        busyWaitStartLow = true;
                        inCooldown = true;

                        buf.clear();
                    }
                }
            }
        }
    }

    // cleanup
    if (ctx) {
        WriteCoil(ctx, COIL_BASE, false);
        WriteCoil(ctx, COIL_TOP, false);

        modbus_close(ctx);
        modbus_free(ctx);
        ctx = nullptr;
    }

    cap.release();
    return 0;
}