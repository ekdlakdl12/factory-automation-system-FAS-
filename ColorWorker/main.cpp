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

using namespace cv;
using namespace std;

// =====================
// USER CONFIG
// =====================
static const char* PLC_IP = "192.168.0.202";
static const int   PLC_PORT = 502;

// PLC -> PC 트리거 코일
static const int START_COIL = 100; // 실제 주소로 맞추세요.

// PC -> PLC 원-핫 출력 코일(색상)
static const int BASE_ADDR_100 = 100;
static const int COIL_GREEN = BASE_ADDR_100 + 1; // 101
static const int COIL_BLUE = BASE_ADDR_100 + 2; // 102
static const int COIL_RED = BASE_ADDR_100 + 3; // 103
static const int COIL_NONE = BASE_ADDR_100 + 4; // 104

static const int PULSE_MS = 2000; // 코일 ON 유지 시간(ms)

// ===== 재접속 정책(끊겼을 때만, 5~10초 간격) =====
static const int RECONNECT_MIN_MS = 5000;
static const int RECONNECT_MAX_MS = 10000;

// Camera
static int  DEVICE_INDEX = 1;
static bool USE_DSHOW = true;
static int  CAM_W = 1280;
static int  CAM_H = 720;

// JSON files (요구사항: time, ms, color, pix만 저장)
static const string HISTORY_JSON = "color_history.json";  // 누적 append (배열)
static const string CURRENT_JSON = "color_current.json";  // 현재 1건 overwrite (객체)

// Object ROI detection tuning
static double MIN_CONTOUR_AREA = 5000.0;
static int ROI_PAD = 10;

// Color decision tuning
static int MIN_COLOR_PIXELS = 500;
static double MIN_COLOR_RATIO = 0.02;

// =====================
// Visualization
// =====================
static void ShowFit(const string& name, const Mat& src)
{
    Mat disp;
    double scale = min(1280.0 / src.cols, 720.0 / src.rows);
    if (scale < 1.0) resize(src, disp, Size(), scale, scale, INTER_AREA);
    else disp = src;
    imshow(name, disp);
}

// =====================
// JSON helpers (no json lib)
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

static long long NowMillis()
{
    using clock = chrono::system_clock;
    return chrono::duration_cast<chrono::milliseconds>(clock::now().time_since_epoch()).count();
}

// 사람이 보기 좋은 "현재시간" 문자열
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

static void BuildMasksRGB(const Mat& hsv, Mat& maskR, Mat& maskG, Mat& maskB, const ColorThresholds& th)
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

static int CountMaskPixels(const Mat& mask) { return countNonZero(mask); }

// =====================
// Object ROI by contour (edge-based)
// =====================
static bool FindObjectROI(const Mat& bgr, Rect& outRoi)
{
    Mat gray;
    cvtColor(bgr, gray, COLOR_BGR2GRAY);
    GaussianBlur(gray, gray, Size(5, 5), 0);

    Mat edges;
    Canny(gray, edges, 50, 150);

    Mat k = getStructuringElement(MORPH_RECT, Size(5, 5));
    morphologyEx(edges, edges, MORPH_CLOSE, k, Point(-1, -1), 2);
    morphologyEx(edges, edges, MORPH_DILATE, k, Point(-1, -1), 1);

    vector<vector<Point>> contours;
    findContours(edges, contours, RETR_EXTERNAL, CHAIN_APPROX_SIMPLE);

    int best = -1;
    double bestArea = 0.0;
    for (int i = 0; i < (int)contours.size(); i++) {
        double a = contourArea(contours[i]);
        if (a > bestArea) { bestArea = a; best = i; }
    }

    if (best < 0) return false;
    if (bestArea < MIN_CONTOUR_AREA) return false;

    Rect br = boundingRect(contours[best]);

    br.x -= ROI_PAD; br.y -= ROI_PAD;
    br.width += ROI_PAD * 2; br.height += ROI_PAD * 2;

    Rect imgRect(0, 0, bgr.cols, bgr.rows);
    br = br & imgRect;
    if (br.width <= 0 || br.height <= 0) return false;

    outRoi = br;
    return true;
}

// =====================
// Modbus helpers
// =====================
static modbus_t* ConnectModbus(const char* ip, int port)
{
    modbus_t* ctx = modbus_new_tcp(ip, port);
    if (!ctx) return nullptr;

    modbus_set_response_timeout(ctx, 0, 300000); // 300ms
    if (modbus_connect(ctx) == -1) {
        modbus_free(ctx);
        return nullptr;
    }
    return ctx;
}

static bool WriteCoil(modbus_t* ctx, int addr, bool val)
{
    int rc = modbus_write_bit(ctx, addr, val ? 1 : 0); // FC5
    return (rc == 1);
}

static bool ReadCoil(modbus_t* ctx, int addr, bool& outVal)
{
    uint8_t bit = 0;
    int rc = modbus_read_bits(ctx, addr, 1, &bit); // FC1
    if (rc != 1) return false;
    outVal = (bit != 0);
    return true;
}

static bool SendColorPulse(modbus_t* ctx, const string& color)
{
    // 먼저 다 0
    if (!WriteCoil(ctx, COIL_GREEN, false)) return false;
    if (!WriteCoil(ctx, COIL_BLUE, false)) return false;
    if (!WriteCoil(ctx, COIL_RED, false)) return false;
    if (!WriteCoil(ctx, COIL_NONE, false)) return false;

    int target = COIL_NONE;
    if (color == "GREEN") target = COIL_GREEN;
    else if (color == "BLUE") target = COIL_BLUE;
    else if (color == "RED")  target = COIL_RED;

    if (!WriteCoil(ctx, target, true)) return false;
    this_thread::sleep_for(chrono::milliseconds(PULSE_MS));
    if (!WriteCoil(ctx, target, false)) return false;

    return true;
}

// ===== 재접속 간격(5~10초 랜덤) =====
static int NextReconnectDelayMs()
{
    static bool seeded = false;
    if (!seeded) {
        seeded = true;
        srand((unsigned int)chrono::high_resolution_clock::now().time_since_epoch().count());
    }
    int span = RECONNECT_MAX_MS - RECONNECT_MIN_MS;
    return RECONNECT_MIN_MS + (span > 0 ? (rand() % (span + 1)) : 0);
}

static void MarkDisconnected(modbus_t*& ctx, long long& nextReconnectMs)
{
    if (ctx) {
        modbus_close(ctx);
        modbus_free(ctx);
        ctx = nullptr;
    }
    nextReconnectMs = NowMillis() + NextReconnectDelayMs();
}

// =====================
// Color classify on ROI
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

    outRpix = rPix; outGpix = gPix; outBpix = bPix;

    int bestPix = 0;
    string color = "NONE";
    if (rPix > bestPix) { bestPix = rPix; color = "RED"; }
    if (gPix > bestPix) { bestPix = gPix; color = "GREEN"; }
    if (bPix > bestPix) { bestPix = bPix; color = "BLUE"; }

    int roiPixels = roiBgr.rows * roiBgr.cols;
    double ratio = (roiPixels > 0) ? (double)bestPix / (double)roiPixels : 0.0;

    if (bestPix < MIN_COLOR_PIXELS || ratio < MIN_COLOR_RATIO) return "NONE";
    return color;
}

// =====================
// Capture "fresh" frame (buffer flush)
// =====================
static bool CaptureFreshFrame(VideoCapture& cap, Mat& out, int warmupGrabs = 3)
{
    for (int i = 0; i < warmupGrabs; i++) cap.grab();
    return cap.read(out);
}

// =====================
// MAIN (Trigger based)
// =====================
int main()
{
    cout << "[CWD] " << filesystem::current_path().string() << "\n";
    cout << "[JSON] history=" << HISTORY_JSON << " current=" << CURRENT_JSON << "\n";
    cout << "[MODBUS] " << PLC_IP << ":" << PLC_PORT
        << " START=" << START_COIL
        << " coils: G=" << COIL_GREEN << " B=" << COIL_BLUE
        << " R=" << COIL_RED << " N=" << COIL_NONE
        << " pulse=" << PULSE_MS << "ms\n";

    // history는 배열 유지
    if (!EnsureJsonArrayFile(HISTORY_JSON)) {
        cerr << "EnsureJsonArrayFile failed: " << HISTORY_JSON << "\n";
        return -1;
    }

    // current는 요구사항 필드만 가진 초기값
    WriteTextFileAtomic(CURRENT_JSON,
        "{\n"
        "  \"time\": \"\",\n"
        "  \"ms\": 0.0,\n"
        "  \"color\": \"NONE\",\n"
        "  \"pix\": {\"r\": 0, \"g\": 0, \"b\": 0}\n"
        "}\n"
    );

    // Camera open
    VideoCapture cap;
    if (USE_DSHOW) cap.open(DEVICE_INDEX, CAP_DSHOW);
    else cap.open(DEVICE_INDEX);

    if (!cap.isOpened()) {
        cerr << "camera open failed. deviceIndex=" << DEVICE_INDEX << "\n";
        return -1;
    }
    cap.set(CAP_PROP_FRAME_WIDTH, CAM_W);
    cap.set(CAP_PROP_FRAME_HEIGHT, CAM_H);

    // Modbus connect (처음 실패해도 종료 X)
    modbus_t* ctx = ConnectModbus(PLC_IP, PLC_PORT);
    long long nextReconnectMs = 0;

    if (!ctx) {
        cerr << "[MODBUS] initial connect failed: " << modbus_strerror(errno) << "\n";
        nextReconnectMs = NowMillis() + NextReconnectDelayMs();
    }
    else {
        cout << "[MODBUS] connected\n";
        // 안전 OFF
        WriteCoil(ctx, COIL_GREEN, false);
        WriteCoil(ctx, COIL_BLUE, false);
        WriteCoil(ctx, COIL_RED, false);
        WriteCoil(ctx, COIL_NONE, false);
    }

    ColorThresholds th;
    namedWindow("result", WINDOW_NORMAL);

    bool prevStart = false;
    bool busyWaitStartLow = false; // 한번 실행하면 start 내려갈 때까지 재실행 금지

    cout << "[RUN] waiting START rising-edge... (ESC quit)\n";

    while (true) {
        Mat frame;
        if (!cap.read(frame) || frame.empty()) {
            this_thread::sleep_for(chrono::milliseconds(5));
            continue;
        }

        // OFFLINE이면: 5~10초 간격으로만 재접속 시도 + 화면은 계속
        if (!ctx) {
            long long nowMs = NowMillis();
            if (nowMs >= nextReconnectMs) {
                modbus_t* newCtx = ConnectModbus(PLC_IP, PLC_PORT);
                if (newCtx) {
                    ctx = newCtx;
                    cout << "[MODBUS] reconnected\n";

                    // 재연결 직후 안전 OFF
                    WriteCoil(ctx, COIL_GREEN, false);
                    WriteCoil(ctx, COIL_BLUE, false);
                    WriteCoil(ctx, COIL_RED, false);
                    WriteCoil(ctx, COIL_NONE, false);

                    prevStart = false;
                    busyWaitStartLow = false;
                }
                else {
                    cerr << "[MODBUS] reconnect failed: " << modbus_strerror(errno) << "\n";
                    nextReconnectMs = NowMillis() + NextReconnectDelayMs();
                }
            }

            Mat visOff = frame.clone();
            putText(visOff, "MODBUS=OFFLINE (auto reconnect 5~10s)", Point(20, 40),
                FONT_HERSHEY_SIMPLEX, 0.9, Scalar(0, 0, 255), 2);
            putText(visOff, "ESC quit", Point(20, 80),
                FONT_HERSHEY_SIMPLEX, 0.8, Scalar(255, 255, 255), 2);
            ShowFit("result", visOff);

            int k = waitKey(1);
            if (k == 27) break;

            this_thread::sleep_for(chrono::milliseconds(5));
            continue;
        }

        // START 읽기
        bool start = false;
        if (!ReadCoil(ctx, START_COIL, start)) {
            cerr << "[MODBUS] read START failed -> DISCONNECT: " << modbus_strerror(errno) << "\n";
            MarkDisconnected(ctx, nextReconnectMs);
            continue;
        }

        bool rising = (start && !prevStart);
        prevStart = start;

        // 한번 실행 후에는 start가 0이 될 때까지 대기
        if (busyWaitStartLow) {
            if (!start) {
                busyWaitStartLow = false;
                cout << "[TRIG] START back to 0 -> ready next\n";
            }
        }

        // 대기 시각화
        Mat vis = frame.clone();
        putText(vis, format("MODBUS=ONLINE  START=%d  state=%s",
            start ? 1 : 0, busyWaitStartLow ? "BUSY(wait START=0)" : "IDLE"),
            Point(20, 40), FONT_HERSHEY_SIMPLEX, 0.9, Scalar(0, 255, 255), 2);
        putText(vis, "ESC quit", Point(20, 80), FONT_HERSHEY_SIMPLEX, 0.8, Scalar(255, 255, 255), 2);
        ShowFit("result", vis);

        int key = waitKey(1);
        if (key == 27) break;

        // 트리거 상승엣지 + IDLE일 때만 1회 처리
        if (rising && !busyWaitStartLow) {
            cout << "[TRIG] rising-edge -> measure once\n";

            Mat snap;
            if (!CaptureFreshFrame(cap, snap, 3) || snap.empty()) {
                cerr << "[CAM] capture failed\n";
                busyWaitStartLow = true;
                continue;
            }

            Rect objRoi;
            bool found = FindObjectROI(snap, objRoi);

            string color = "NONE";
            int rPix = 0, gPix = 0, bPix = 0;

            int64 t0 = getTickCount();
            if (found) {
                Mat roiBgr = snap(objRoi).clone();
                color = ClassifyColorROI(roiBgr, th, rPix, gPix, bPix);
            }
            int64 t1 = getTickCount();
            double elapsed_ms = (t1 - t0) * 1000.0 / getTickFrequency();

            // ===== 요구사항 JSON: time, ms, color, pix만 저장 =====
            string tsStr = NowTimeString();

            {
                ostringstream rec;
                rec << "  {\n";
                rec << "    \"time\": \"" << tsStr << "\",\n";
                rec << "    \"ms\": " << fixed << setprecision(3) << elapsed_ms << ",\n";
                rec << "    \"color\": \"" << color << "\",\n";
                rec << "    \"pix\": {\"r\": " << rPix << ", \"g\": " << gPix << ", \"b\": " << bPix << "}\n";
                rec << "  }";
                AppendJsonArray(HISTORY_JSON, rec.str());
            }

            {
                ostringstream cur;
                cur << "{\n";
                cur << "  \"time\": \"" << tsStr << "\",\n";
                cur << "  \"ms\": " << fixed << setprecision(3) << elapsed_ms << ",\n";
                cur << "  \"color\": \"" << color << "\",\n";
                cur << "  \"pix\": {\"r\": " << rPix << ", \"g\": " << gPix << ", \"b\": " << bPix << "}\n";
                cur << "}\n";
                WriteTextFileAtomic(CURRENT_JSON, cur.str());
            }

            cout << "[SAVE] time=" << tsStr << " color=" << color
                << " ms=" << fixed << setprecision(3) << elapsed_ms
                << " pix(r,g,b)=(" << rPix << "," << gPix << "," << bPix << ")\n";

            // PLC 펄스 전송
            if (!SendColorPulse(ctx, color)) {
                cerr << "[MODBUS] SendColorPulse failed -> DISCONNECT: " << modbus_strerror(errno) << "\n";
                MarkDisconnected(ctx, nextReconnectMs);
            }
            else {
                cout << "[PULSE] sent color=" << color << "\n";
            }

            // 다음 트리거 대기
            busyWaitStartLow = true;

            // 스냅 결과 표시(원하면 제거 가능)
            Mat snapVis = snap.clone();
            if (found) rectangle(snapVis, objRoi, Scalar(0, 255, 255), 2);
            putText(snapVis, format("MEASURED: %s (%.2f ms)", color.c_str(), elapsed_ms),
                Point(20, 40), FONT_HERSHEY_SIMPLEX, 0.9, Scalar(0, 255, 255), 2);
            ShowFit("result", snapVis);
        }

        this_thread::sleep_for(chrono::milliseconds(5));
    }

    // cleanup
    if (ctx) {
        WriteCoil(ctx, COIL_GREEN, false);
        WriteCoil(ctx, COIL_BLUE, false);
        WriteCoil(ctx, COIL_RED, false);
        WriteCoil(ctx, COIL_NONE, false);

        modbus_close(ctx);
        modbus_free(ctx);
        ctx = nullptr;
    }

    cap.release();
    return 0;
}
