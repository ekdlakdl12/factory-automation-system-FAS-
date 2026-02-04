#include <opencv2/opencv.hpp>
#include <iostream>
#include <vector>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <cstdio>
#include <cctype>
#include <algorithm>
#include <cmath>
#include <filesystem>
#include <cerrno>
#include <chrono>
#include <thread>

#include <modbus/modbus.h>

using namespace cv;
using namespace std;

// =====================
// MODBUS CONFIG (읽기만)
// =====================
static const char* PLC_IP = "192.168.0.202";
static const int   PLC_PORT = 502;

// PLC -> PC 트리거 코일 (읽기만)
static const int START_COIL = 200;

// 주소 오프셋(안 맞으면 -1 테스트)
static const int ADDR_OFFSET = 0;
static inline int A(int addr) { return addr + ADDR_OFFSET; }

// 연결 재시도
static const int RECONNECT_EVERY_MS = 2000;

// 트리거 폴링
static const int TRIG_POLL_MS = 20;

// 측정 루틴 타임아웃
static const int MEASURE_TIMEOUT_MS = 1500;

// =====================
// JSON (오직 total.json만)
// =====================
static const string TOTAL_JSON = "./total.json";

// =====================
// 판정 조건 (x만 보고 BASE/TOP/defect)
// =====================
static inline string DecideTypeByX(double xMm) {
    const double baseCenter = 60.0;
    const double tol = 3.0;
    const double defectCut = baseCenter - tol; // 57

    if (xMm >= baseCenter - tol && xMm <= baseCenter + tol) return "TOP";
    else if (xMm < defectCut) return "defect";
    else return "BASE";
}

// =====================
// time helpers
// =====================
static inline long long NowMillis() {
    using clock = chrono::system_clock;
    return chrono::duration_cast<chrono::milliseconds>(clock::now().time_since_epoch()).count();
}

// =====================
// File helpers (atomic write)
// =====================
static bool ReadAllText(const string& path, string& out) {
    ifstream ifs(path, ios::in);
    if (!ifs.is_open()) return false;
    ostringstream ss;
    ss << ifs.rdbuf();
    out = ss.str();
    return true;
}

static bool WriteTextFileAtomic(const string& path, const string& text) {
    string tmp = path + ".tmp";
    {
        ofstream ofs(tmp, ios::out | ios::trunc);
        if (!ofs.is_open()) return false;
        ofs << text;
        ofs.close();
    }
    ::remove(path.c_str());
    if (::rename(tmp.c_str(), path.c_str()) != 0) {
        // fallback
        ofstream ofs(path, ios::out | ios::trunc);
        if (!ofs.is_open()) { ::remove(tmp.c_str()); return false; }
        ofs << text;
        ofs.close();
        ::remove(tmp.c_str());
        return true;
    }
    return true;
}

static inline void TrimRight(string& s) {
    while (!s.empty() && isspace((unsigned char)s.back())) s.pop_back();
}

// =====================
// Label helper
// =====================
static string MakeLabel(const string& color, int count) {
    char prefix = 'n';
    if (color == "RED") prefix = 'r';
    else if (color == "GREEN") prefix = 'g';
    else if (color == "BLUE") prefix = 'b';
    else prefix = 'n';
    return string(1, prefix) + to_string(count);
}

// =====================
// HSV thresholds (색상 판별)
// =====================
struct HsvRange { Scalar L; Scalar U; };
struct ColorThresholds {
    HsvRange R1{ Scalar(0,   60,  40), Scalar(12,  255, 255) };
    HsvRange R2{ Scalar(168, 60,  40), Scalar(179, 255, 255) };
    HsvRange G{ Scalar(30,  40,  40), Scalar(95,  255, 255) };
    HsvRange B{ Scalar(85,  40,  40), Scalar(140, 255, 255) };
};

// 색상 판별 민감도 (필요시 조절)
static int MIN_COLOR_PIXELS = 100;
static double MIN_COLOR_RATIO = 0.01;

static void BuildMasksRGB(const Mat& hsv, Mat& maskR, Mat& maskG, Mat& maskB, const ColorThresholds& th) {
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

static string ClassifyColorROI(const Mat& roiBgr, const ColorThresholds& th, int& outRpix, int& outGpix, int& outBpix) {
    Mat hsv;
    cvtColor(roiBgr, hsv, COLOR_BGR2HSV);
    GaussianBlur(hsv, hsv, Size(3, 3), 0);

    Mat maskR, maskG, maskB;
    BuildMasksRGB(hsv, maskR, maskG, maskB, th);

    int rPix = countNonZero(maskR);
    int gPix = countNonZero(maskG);
    int bPix = countNonZero(maskB);

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

    if (bestPix < MIN_COLOR_PIXELS || ratio < MIN_COLOR_RATIO) return "NONE";
    return color;
}

// =====================
// total.json 업데이트: label 찾아서 x,y,ms,type 추가
// - label 없으면 실패
// - 이미 "x"가 있으면(이미 측정됨) 실패
// =====================
static bool ObjectAlreadyHasX(const string& obj) {
    return (obj.find("\"x\"") != string::npos);
}

// label 값 파싱: "\"label\": \"g1\"" 형태에서 g1 꺼내기
static bool ExtractLabelValueNear(const string& s, size_t labelKeyPos, string& outLabel) {
    size_t colon = s.find(':', labelKeyPos);
    if (colon == string::npos) return false;
    size_t q1 = s.find('"', colon + 1);
    if (q1 == string::npos) return false;
    size_t q2 = s.find('"', q1 + 1);
    if (q2 == string::npos) return false;
    outLabel = s.substr(q1 + 1, q2 - (q1 + 1));
    return true;
}

static bool UpdateTotalJson_AddMeasure(const string& path, const string& label,
    double x, double y, double ms, const string& type,
    string& outReason) {
    string s;
    if (!ReadAllText(path, s)) {
        outReason = "total.json read failed (file missing?)";
        return false;
    }

    // label key들을 훑어서 해당 label 오브젝트를 찾음
    size_t pos = 0;
    while (true) {
        size_t labelKey = s.find("\"label\"", pos);
        if (labelKey == string::npos) break;

        string foundLabel;
        if (!ExtractLabelValueNear(s, labelKey, foundLabel)) {
            pos = labelKey + 7;
            continue;
        }

        if (foundLabel == label) {
            // 이 label이 포함된 오브젝트 경계 찾기: 가까운 '{' (앞) ~ 다음 '}' (뒤)
            size_t objStart = s.rfind('{', labelKey);
            size_t objEnd = s.find('}', labelKey);
            if (objStart == string::npos || objEnd == string::npos || objEnd <= objStart) {
                outReason = "json object boundary parse failed";
                return false;
            }

            string obj = s.substr(objStart, objEnd - objStart + 1);

            if (ObjectAlreadyHasX(obj)) {
                outReason = "already measured (x exists)";
                return false;
            }

            // 닫는 '}' 바로 앞에 4개 필드 삽입
            // 기존 obj가 "}"로 끝나므로, 마지막 '}'를 떼고 추가 후 다시 닫음
            string objBody = obj.substr(0, obj.size() - 1);
            TrimRight(objBody);

            ostringstream add;
            add << ",\n"
                << "    \"x\": " << fixed << setprecision(3) << x << ",\n"
                << "    \"y\": " << fixed << setprecision(3) << y << ",\n"
                << "    \"ms\": " << fixed << setprecision(3) << ms << ",\n"
                << "    \"type\": \"" << type << "\"\n"
                << "  }";

            // 들여쓰기 2칸 기준(컬러 워커가 보통 "  {" 시작)일 때 깔끔하게 맞춤
            // 만약 들여쓰기가 달라도 JSON 파싱엔 영향 없음
            string newObj = objBody + add.str();

            // 전체 텍스트에서 해당 오브젝트 부분만 교체
            string newS = s.substr(0, objStart) + newObj + s.substr(objEnd + 1);

            if (!WriteTextFileAtomic(path, newS)) {
                outReason = "total.json write failed";
                return false;
            }

            outReason = "OK";
            return true;
        }

        pos = labelKey + 7;
    }

    outReason = "label not found in total.json";
    return false;
}

// =====================
// Modbus helpers (읽기만)
// =====================
static modbus_t* ConnectModbus(const char* ip, int port) {
    modbus_t* ctx = modbus_new_tcp(ip, port);
    if (!ctx) return nullptr;

    // 타임아웃 너무 짧으면 timed out 자주 뜹니다.
    // 0.3s는 빡빡할 수 있어서 1초로 여유 주는 걸 권장합니다.
    modbus_set_response_timeout(ctx, 1, 0);

    if (modbus_connect(ctx) == -1) {
        modbus_free(ctx);
        return nullptr;
    }
    return ctx;
}

static bool ReadCoil(modbus_t* ctx, int addr, bool& outVal) {
    uint8_t bit = 0;
    int rc = modbus_read_bits(ctx, A(addr), 1, &bit);
    if (rc != 1) return false;
    outVal = (bit != 0);
    return true;
}

static void MarkDisconnected(modbus_t*& ctx) {
    if (ctx) {
        modbus_close(ctx);
        modbus_free(ctx);
        ctx = nullptr;
    }
}

// =====================
// Candidate buffer (측정 후보)
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

static void KeepTopK(vector<Cand>& v, int K) {
    sort(v.begin(), v.end(), [](const Cand& a, const Cand& b) { return a.score > b.score; });
    if ((int)v.size() > K) v.resize(K);
}

// =====================
// 측정 루틴: 트리거 들어오면 측정 + 색상판별 + label 카운트
// =====================
static bool DoMeasureNow(
    VideoCapture& cap,
    const Rect& roi,
    double mmPerPx,
    const ColorThresholds& colorTh,
    int& rCount,
    int& gCount,
    int& bCount,
    int& nCount
) {
    const int TOP_K = 1;
    vector<Cand> buf;

    const int presentNeed = 2;
    const int absentNeed = 1;

    bool inCooldown = false;
    int presentStreak = 0;
    int absentStreak = 0;

    long long tStart = NowMillis();
    double freq = getTickFrequency();

    while (true) {
        long long now = NowMillis();
        if (now - tStart > MEASURE_TIMEOUT_MS) {
            cout << "[MEASURE] TIMEOUT (no stable detection)\n";
            return false;
        }

        Mat frame;
        cap >> frame;
        if (frame.empty()) {
            this_thread::sleep_for(chrono::milliseconds(1));
            continue;
        }

        Rect r = roi & Rect(0, 0, frame.cols, frame.rows);
        if (r.width <= 0 || r.height <= 0) {
            this_thread::sleep_for(chrono::milliseconds(1));
            continue;
        }

        int64 t0 = getTickCount();
        Mat roiFrame = frame(r).clone();

        // ===== 기존 측정 탐지 파이프라인 (유지) =====
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
                if (a > bestArea) { bestArea = a; best = i; }
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

        if (mmPerPx > 0.0) {
            if (inCooldown) {
                if (absentStreak >= absentNeed) { inCooldown = false; buf.clear(); }
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
                        double xMm = buf[0].wMm;
                        double yMm = buf[0].hMm;
                        double ms = buf[0].ms;

                        // ===== 여기서 색상 판별(측정 ROI 기준) =====
                        int rp = 0, gp = 0, bp = 0;
                        string color = ClassifyColorROI(buf[0].roiImg, colorTh, rp, gp, bp);

                        // ===== 색상별 카운팅(런타임) =====
                        int curCount = 0;
                        if (color == "RED") { rCount++; curCount = rCount; }
                        else if (color == "GREEN") { gCount++; curCount = gCount; }
                        else if (color == "BLUE") { bCount++; curCount = bCount; }
                        else { nCount++; curCount = nCount; }

                        string label = MakeLabel(color, curCount);
                        string type = DecideTypeByX(xMm);

                        // 디버그 출력 (왜 FAIL인지 보이게)
                        cout << "[MEASURE] detected=1"
                            << " color=" << color
                            << " label=" << label
                            << " (rPix/gPix/bPix=" << rp << "/" << gp << "/" << bp << ")"
                            << " x=" << fixed << setprecision(3) << xMm
                            << " y=" << fixed << setprecision(3) << yMm
                            << " ms=" << fixed << setprecision(3) << ms
                            << " type=" << type
                            << "\n";

                        // ===== total.json에 label 찾아서 4개만 추가 =====
                        string reason;
                        bool ok = UpdateTotalJson_AddMeasure(TOTAL_JSON, label, xMm, yMm, ms, type, reason);

                        if (!ok) {
                            cout << "[MEASURE] SAVE FAIL: " << reason << " (label=" << label << ")\n";
                        }
                        else {
                            cout << "[MEASURE] SAVE OK -> total.json updated (label=" << label << ")\n";
                        }

                        return ok; // 실패/성공 모두 이번 트리거는 종료
                    }
                }
            }
        }

        this_thread::sleep_for(chrono::milliseconds(1));
    }
}

// =====================
// MAIN
// =====================
int main() {
    cout << "[CWD] " << filesystem::current_path().string() << "\n";
    cout << "[MODBUS] " << PLC_IP << ":" << PLC_PORT
        << " START=" << START_COIL << " (read only)\n";
    cout << "[MODBUS] ADDR_OFFSET=" << ADDR_OFFSET << " (If trigger fails, try -1)\n";
    cout << "[TRIG] poll=" << TRIG_POLL_MS << "ms\n";
    cout << "[JSON] only " << TOTAL_JSON << "\n";

    // total.json 없으면 측정 업데이트 자체가 불가능하므로 여기서 중단하는 편이 안전
    {
        ifstream ifs(TOTAL_JSON);
        if (!ifs.is_open()) {
            cerr << "[FATAL] total.json not found. Color worker must create it first.\n";
            return -1;
        }
    }

    int deviceIndex = 1;

    // scale.yaml
    double mmPerPx = 0.0;
    if (ifstream("scale.yaml").good()) {
        FileStorage fs("scale.yaml", FileStorage::READ);
        if (fs.isOpened()) {
            fs["mmPerPx"] >> mmPerPx;
            fs.release();
        }
    }
    cout << "[SCALE] mmPerPx=" << fixed << setprecision(6) << mmPerPx << "\n";

    VideoCapture cap(deviceIndex, CAP_DSHOW);
    if (!cap.isOpened()) {
        cerr << "[FATAL] camera open failed\n";
        return -1;
    }

    cap.set(CAP_PROP_FOURCC, VideoWriter::fourcc('M', 'J', 'P', 'G'));
    cap.set(CAP_PROP_FRAME_WIDTH, 1920);
    cap.set(CAP_PROP_FRAME_HEIGHT, 1080);

    int actualWidth = (int)cap.get(CAP_PROP_FRAME_WIDTH);
    int actualHeight = (int)cap.get(CAP_PROP_FRAME_HEIGHT);
    cout << "[CAMERA] Actual resolution: " << actualWidth << "x" << actualHeight << "\n";

    Rect roi(710, 50, 550, 1000);
    if (roi.x + roi.width > actualWidth) roi.width = actualWidth - roi.x;
    if (roi.y + roi.height > actualHeight) roi.height = actualHeight - roi.y;
    cout << "[ROI] x=" << roi.x << " y=" << roi.y
        << " w=" << roi.width << " h=" << roi.height << "\n";

    // modbus connect (until success)
    modbus_t* ctx = nullptr;
    while (!ctx) {
        ctx = ConnectModbus(PLC_IP, PLC_PORT);
        if (!ctx) {
            cerr << "[MODBUS] connect failed: " << modbus_strerror(errno)
                << " -> retry in " << RECONNECT_EVERY_MS << "ms\n";
            this_thread::sleep_for(chrono::milliseconds(RECONNECT_EVERY_MS));
        }
    }
    cout << "[MODBUS] connected\n";
    cout << "[RUN] waiting START=1 ...\n";

    ColorThresholds th;

    // 런타임 색상 카운터(측정쪽)
    int rCount = 0, gCount = 0, bCount = 0, nCount = 0;

    bool busyWaitStartLow = false;

    while (true) {
        // reconnect if needed
        if (!ctx) {
            while (!ctx) {
                ctx = ConnectModbus(PLC_IP, PLC_PORT);
                if (!ctx) {
                    cerr << "[MODBUS] reconnect failed: " << modbus_strerror(errno)
                        << " -> retry in " << RECONNECT_EVERY_MS << "ms\n";
                    this_thread::sleep_for(chrono::milliseconds(RECONNECT_EVERY_MS));
                }
            }
            cout << "[MODBUS] reconnected\n";
            busyWaitStartLow = false;
        }

        bool start = false;
        if (!ReadCoil(ctx, START_COIL, start)) {
            cerr << "[MODBUS] read START failed: " << modbus_strerror(errno) << " -> reconnect\n";
            MarkDisconnected(ctx);
            continue;
        }

        // START=1 유지 동안 중복측정 방지: 0으로 내려올 때까지 대기
        if (busyWaitStartLow) {
            if (!start) {
                busyWaitStartLow = false;
                cout << "[TRIG] START back to 0 -> ready next\n";
            }
            this_thread::sleep_for(chrono::milliseconds(TRIG_POLL_MS));
            continue;
        }

        if (start) {
            cout << "[TRIG] START=1 -> MEASURE NOW\n";

            bool ok = DoMeasureNow(cap, roi, mmPerPx, th, rCount, gCount, bCount, nCount);

            // PLC가 START를 0으로 내릴 때까지 대기 모드
            busyWaitStartLow = true;

            if (!ok) {
                cout << "[MEASURE] FAIL (no update to total.json)\n";
            }
        }

        this_thread::sleep_for(chrono::milliseconds(TRIG_POLL_MS));
    }

    // cleanup
    if (ctx) {
        modbus_close(ctx);
        modbus_free(ctx);
        ctx = nullptr;
    }
    cap.release();
    return 0;
}
