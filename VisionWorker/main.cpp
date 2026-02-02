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
// MODBUS CONFIG (200번대)
// =====================
static const char* PLC_IP = "192.168.0.202";
static const int   PLC_PORT = 502;

static const int START_COIL = 200;   // PLC -> PC 트리거(가정: 200)
static const int COIL_BASE = 202;   // PC -> PLC 결과(펄스): BASE
static const int COIL_TOP = 201;   // PC -> PLC 결과(펄스): TOP
static const int PULSE_MS = 100;   // 펄스 폭(ms)

// =====================
// 판정 조건 (x만 보고 BASE/TOP/defect)
// =====================
static inline string DecideTypeByX(double xMm) {
    const double baseCenter = 60.0;
    const double tol = 3.0;
    const double defectCut = baseCenter - tol; // 57

    // ※ 사용자가 최근에 TOP/BASE를 뒤집어둔 상태를 그대로 유지했습니다.
    // 필요하면 여기만 바꾸세요.
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
static inline long long NowMillis()
{
    using clock = chrono::system_clock;
    return chrono::duration_cast<chrono::milliseconds>(clock::now().time_since_epoch()).count();
}

// =====================
// 사람이 읽는 KST 시간 문자열 만들기
// - 시스템 타임존이 KST(한국 PC)면 OK
// =====================
static inline string NowKstString()
{
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
// UI용 통신/출력 상태 표시
// =====================
static bool      g_mbOnline = false;
static long long g_lastMbOkMs = 0;
static long long g_lastMbFailMs = 0;

static string g_lastType = "-";
static int    g_lastCoil = -1;
static string g_lastTimeKst = "-";
static double g_lastX = 0.0, g_lastY = 0.0, g_lastMs = 0.0;

// =====================
// Non-blocking 펄스 상태
// =====================
static bool      g_pulseActive = false;
static int       g_pulseAddr = -1;
static long long g_pulseStartMs = 0;

// =====================
// Modbus helpers
// =====================
static modbus_t* ConnectModbus(const char* ip, int port)
{
    modbus_t* ctx = modbus_new_tcp(ip, port);
    if (!ctx) return nullptr;

    // 응답 타임아웃 300ms
    modbus_set_response_timeout(ctx, 0, 300000);

    if (modbus_connect(ctx) == -1) {
        modbus_free(ctx);
        return nullptr;
    }
    return ctx;
}

static bool ReadCoil(modbus_t* ctx, int addr, bool& outVal)
{
    uint8_t bit = 0;
    int rc = modbus_read_bits(ctx, addr, 1, &bit); // FC1
    if (rc != 1) return false;
    outVal = (bit != 0);
    return true;
}

static bool WriteCoil(modbus_t* ctx, int addr, bool val)
{
    int rc = modbus_write_bit(ctx, addr, val ? 1 : 0); // FC5
    return (rc == 1);
}

// ---- (핵심) 슬립 없는 펄스 시작 ----
static bool StartPulse(modbus_t* ctx, int addr)
{
    // 기존 펄스가 켜져있다면 일단 끄고 새로 시작(중첩 방지)
    if (g_pulseActive && g_pulseAddr >= 0) {
        WriteCoil(ctx, g_pulseAddr, false);
        g_pulseActive = false;
        g_pulseAddr = -1;
    }

    // 두 코일 OFF 먼저(원-핫 유지)
    if (!WriteCoil(ctx, COIL_BASE, false)) return false;
    if (!WriteCoil(ctx, COIL_TOP, false))  return false;

    // 목표 코일 ON (즉시)
    if (!WriteCoil(ctx, addr, true)) return false;

    g_pulseActive = true;
    g_pulseAddr = addr;
    g_pulseStartMs = NowMillis();
    return true;
}

// ---- (핵심) 매 프레임마다 펄스 OFF 타이밍 체크 ----
static void UpdatePulse(modbus_t* ctx)
{
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

static double Avg(const vector<double>& v) {
    if (v.empty()) return 0.0;
    double s = 0.0;
    for (double x : v) s += x;
    return s / (double)v.size();
}

static bool SaveScaleYaml(
    const string& path,
    double mmPerPx,
    double realMm,
    int samples,
    const string& method,
    const vector<double>& mmPerPxList
) {
    FileStorage fs(path, FileStorage::WRITE);
    if (!fs.isOpened()) return false;

    fs << "mmPerPx" << mmPerPx;
    fs << "realMm" << realMm;
    fs << "samples" << samples;
    fs << "method" << method;

    fs << "mmPerPx_list" << "[";
    for (double v : mmPerPxList) fs << v;
    fs << "]";

    fs.release();
    return true;
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
// Mouse Calibration State
// =====================
static bool g_calibActive = false;
static const int g_calibNeed = 5;
static vector<Point2f> g_pairPts;
static vector<double> g_mmPerPxCandList;
static double g_lastDistPx = 0.0;
static double g_lastCand = 0.0;
static bool g_pairDoneJustNow = false;

static void OnMouse(int event, int x, int y, int flags, void* userdata) {
    (void)flags; (void)userdata;
    if (!g_calibActive) return;

    if (event == EVENT_LBUTTONDOWN) {
        if ((int)g_mmPerPxCandList.size() >= g_calibNeed) return;
        if (g_pairPts.size() >= 2) g_pairPts.clear();
        g_pairPts.push_back(Point2f((float)x, (float)y));
        g_pairDoneJustNow = false;
    }
}

// =====================
// Candidate buffer
// =====================
struct Cand {
    double score = 0.0;
    double wMm = 0.0;
    double hMm = 0.0;
    double ms = 0.0;
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

    // === 실세계 기준(mm): 자로 50mm ===
    double realMm = 50.0;

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

            double loadedRealMm = 0.0;
            if (!fs["realMm"].empty()) fs["realMm"] >> loadedRealMm;
            if (loadedRealMm > 0.0) realMm = loadedRealMm;

            fs.release();
        }
    }
    else {
        WriteTextFile(statusPath,
            "{\n"
            "  \"ok\": true,\n"
            "  \"note\": \"scale.yaml not found. Press 'w' and click 2 points x5 to create it.\",\n"
            "  \"hint\": \"w -> click A,B (50mm) repeat 5 times\"\n"
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

    Rect roi(710, 50, 550, 1000);

    namedWindow("roi_view", WINDOW_NORMAL);
    resizeWindow("roi_view", 700, 700);
    setMouseCallback("roi_view", OnMouse, nullptr);

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

    // 프레임 타임아웃
    int64 startTicks = getTickCount();
    double freq = getTickFrequency();

    int invalidRoiStreak = 0;

    // TOP-K 평균
    const int TOP_K = 1;
    vector<Cand> buf;

    bool inCooldown = false;
    int presentStreak = 0;
    int absentStreak = 0;
    const int presentNeed = 2;
    const int absentNeed = 1;

    // ===== 트리거 상태 =====
    bool prevStart = false;
    bool busyWaitStartLow = false;
    bool armed = false; // rising-edge 이후 측정 허용

    cout << "[RUN] waiting START rising-edge... (q/ESC quit)\n";

    for (;;) {

        // (핵심) 펄스 OFF 타이밍 체크 (슬립 없음)
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
                return -1;
            }
            continue;
        }
        startTicks = getTickCount();

        // ===== PLC START 읽기 + 통신 상태 표시용 업데이트 =====
        bool start = false;
        if (!ReadCoil(ctx, START_COIL, start)) {
            g_mbOnline = false;
            g_lastMbFailMs = NowMillis();
            cerr << "[MODBUS] read START failed: " << modbus_strerror(errno) << "\n";
            this_thread::sleep_for(chrono::milliseconds(5));
            continue;
        }
        else {
            g_mbOnline = true;
            g_lastMbOkMs = NowMillis();
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
                return -1;
            }
            continue;
        }
        invalidRoiStreak = 0;

        int64 t0 = getTickCount();

        // NOTE: clone는 비용 큼. 하지만 화면 표시/그리기 때문에 유지.
        Mat roiFrame = frame(r).clone();

        // ===== 기존 탐지 파이프라인 유지 =====
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

        if (detected) {
            Point2f pts[4];
            rr.points(pts);
            for (int k = 0; k < 4; k++) {
                line(roiFrame, pts[k], pts[(k + 1) % 4], Scalar(0, 255, 0), 2);
            }
        }

        int64 t1 = getTickCount();
        double elapsedMs = (t1 - t0) * 1000.0 / freq;

        // present/absent
        if (detected) { presentStreak++; absentStreak = 0; }
        else { absentStreak++; presentStreak = 0; }

        // ===== 캘리브레이션(기존 유지) =====
        if (g_calibActive && g_pairPts.size() == 2) {
            double dx = (double)g_pairPts[0].x - (double)g_pairPts[1].x;
            double dy = (double)g_pairPts[0].y - (double)g_pairPts[1].y;
            double distPx = sqrt(dx * dx + dy * dy);

            if (distPx > 1.0) {
                double cand = realMm / distPx;

                g_lastDistPx = distPx;
                g_lastCand = cand;
                g_mmPerPxCandList.push_back(cand);
                g_pairDoneJustNow = true;

                cout << "[CALIB] " << g_mmPerPxCandList.size() << "/" << g_calibNeed
                    << " distPx=" << fixed << setprecision(3) << distPx
                    << " cand=" << fixed << setprecision(12) << cand << "\n";

                g_pairPts.clear();

                if ((int)g_mmPerPxCandList.size() >= g_calibNeed) {
                    double newMmPerPx = Avg(g_mmPerPxCandList);

                    bool ok = SaveScaleYaml(yamlPath, newMmPerPx, realMm,
                        (int)g_mmPerPxCandList.size(),
                        "avg", g_mmPerPxCandList);

                    if (ok) {
                        mmPerPx = newMmPerPx;
                        cout << "[CALIB] DONE. mmPerPx=" << fixed << setprecision(12) << mmPerPx << "\n";
                    }
                    else {
                        cout << "[CALIB] Failed to write scale.yaml\n";
                    }

                    g_calibActive = false;
                    g_pairPts.clear();
                    g_mmPerPxCandList.clear();
                }
            }
            else {
                cout << "[CALIB] distPx too small.\n";
                g_pairPts.clear();
            }
        }

        // ==========================
        // 트리거 기반 측정/저장/출력
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

                        // ===== PLC 펄스 먼저 (즉각 반응) =====
                        int targetCoil = (type == "BASE") ? COIL_BASE : COIL_TOP; // TOP/defect -> TOP코일
                        bool pulseOk = StartPulse(ctx, targetCoil);
                        if (!pulseOk) {
                            cerr << "[MODBUS] StartPulse failed: " << modbus_strerror(errno) << "\n";
                        }

                        // ===== JSON 저장: time_kst, x, y, ms, type =====
                        string timeKst = NowKstString();

                        ostringstream rec;
                        rec << "  {\n";
                        rec << "    \"time_kst\": \"" << timeKst << "\",\n";
                        rec << "    \"x\": " << fixed << setprecision(3) << avgW << ",\n";
                        rec << "    \"y\": " << fixed << setprecision(3) << avgH << ",\n";
                        rec << "    \"ms\": " << fixed << setprecision(3) << avgMs << ",\n";
                        rec << "    \"type\": \"" << type << "\"\n";
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
                                << "  \"type\": \"" << type << "\"\n"
                                << "}\n";
                            WriteTextFile(statusPath, ss.str());
                        }

                        // UI 마지막 결과 갱신
                        g_lastType = type;
                        g_lastTimeKst = timeKst;
                        g_lastX = avgW;
                        g_lastY = avgH;
                        g_lastMs = avgMs;
                        g_lastCoil = targetCoil;

                        cout << "[SAVED] label=" << labelCounter
                            << " time_kst=" << timeKst
                            << " x=" << fixed << setprecision(3) << avgW
                            << " y=" << fixed << setprecision(3) << avgH
                            << " type=" << type
                            << " ms=" << fixed << setprecision(3) << avgMs
                            << " pulse=" << (pulseOk ? "OK" : "FAIL")
                            << "\n";

                        // 이번 트리거 종료: START가 0이 될 때까지 재실행 금지
                        armed = false;
                        busyWaitStartLow = true;

                        inCooldown = true;
                        buf.clear();
                    }
                }
            }
        }

        // ==========================
        // UI 텍스트
        // ==========================
        {
            int tx = 10, ty = 24;

            ostringstream ss;
            ss << "START=" << (start ? 1 : 0)
                << "  state=" << (busyWaitStartLow ? "BUSY(wait START=0)" : (armed ? "ARMED" : "IDLE"))
                << "  mmPerPx=" << fixed << setprecision(12) << mmPerPx
                << "  buf=" << (int)buf.size() << "/" << TOP_K
                << "  pulse=" << (g_pulseActive ? "ON" : "OFF");
            putText(roiFrame, ss.str(), Point(tx, ty), FONT_HERSHEY_SIMPLEX, 0.65, Scalar(255, 255, 255), 2);

            putText(roiFrame, "[w] calib  [q/ESC] quit", Point(tx, ty + 28),
                FONT_HERSHEY_SIMPLEX, 0.56, Scalar(255, 255, 255), 2);

            if (detected) {
                ostringstream ss4;
                ss4 << "px: long=" << fixed << setprecision(1) << longSidePx
                    << " short=" << fixed << setprecision(1) << shortSidePx;
                putText(roiFrame, ss4.str(), Point(tx, ty + 56), FONT_HERSHEY_SIMPLEX, 0.56, Scalar(0, 255, 0), 2);

                if (mmPerPx > 0.0) {
                    ostringstream ss5;
                    ss5 << "mm: W(long)=" << fixed << setprecision(2) << wOut
                        << " H(short)=" << fixed << setprecision(2) << hOut;
                    putText(roiFrame, ss5.str(), Point(tx, ty + 84), FONT_HERSHEY_SIMPLEX, 0.56, Scalar(0, 255, 0), 2);
                }
            }
            else {
                putText(roiFrame, "No detection", Point(tx, ty + 56),
                    FONT_HERSHEY_SIMPLEX, 0.62, Scalar(0, 0, 255), 2);
            }

            // 통신 상태 + 마지막 출력값 표시
            {
                long long nowMs = NowMillis();

                ostringstream sMb;
                sMb << "MODBUS=" << (g_mbOnline ? "ONLINE" : "OFFLINE");
                if (g_mbOnline && g_lastMbOkMs > 0) sMb << "  lastOK(msAgo)=" << (nowMs - g_lastMbOkMs);
                if (!g_mbOnline && g_lastMbFailMs > 0) sMb << "  lastFail(msAgo)=" << (nowMs - g_lastMbFailMs);

                putText(roiFrame, sMb.str(), Point(tx, ty + 112),
                    FONT_HERSHEY_SIMPLEX, 0.56,
                    g_mbOnline ? Scalar(0, 255, 0) : Scalar(0, 0, 255), 2);

                ostringstream sOut;
                sOut << "LAST: type=" << g_lastType
                    << " coil=" << g_lastCoil
                    << " x=" << fixed << setprecision(2) << g_lastX
                    << " y=" << fixed << setprecision(2) << g_lastY
                    << " ms=" << fixed << setprecision(2) << g_lastMs;

                putText(roiFrame, sOut.str(), Point(tx, ty + 140),
                    FONT_HERSHEY_SIMPLEX, 0.56, Scalar(255, 255, 0), 2);

                ostringstream sTime;
                sTime << "time_kst=" << g_lastTimeKst;
                putText(roiFrame, sTime.str(), Point(tx, ty + 168),
                    FONT_HERSHEY_SIMPLEX, 0.56, Scalar(255, 255, 0), 2);
            }
        }

        imshow("roi_view", roiFrame);

        int key = waitKey(1);
        if (key == 'q' || key == 27) break;

        if (key == 'w') {
            g_calibActive = true;
            g_pairPts.clear();
            g_mmPerPxCandList.clear();
            g_lastDistPx = 0.0;
            g_lastCand = 0.0;
            cout << "[CALIB] START: click 2 points for 50mm, repeat 5 times.\n";
        }
        if (key == 'r') {
            if (g_calibActive) {
                g_pairPts.clear();
                cout << "[CALIB] current pair reset.\n";
            }
        }
        if (key == 'c') {
            g_calibActive = false;
            g_pairPts.clear();
            g_mmPerPxCandList.clear();
            cout << "[CALIB] canceled.\n";
        }

        // 너무 큰 슬립 금지. 필요하면 0~1ms 정도만
        // this_thread::sleep_for(chrono::milliseconds(1));
    }

    // cleanup
    WriteCoil(ctx, COIL_BASE, false);
    WriteCoil(ctx, COIL_TOP, false);

    modbus_close(ctx);
    modbus_free(ctx);
    ctx = nullptr;

    cap.release();
    return 0;
}
