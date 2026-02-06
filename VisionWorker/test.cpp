// test.cpp
// - PLC 트리거 없이(START_COIL 없음) 키 입력으로 측정 테스트
// - ROI(710,50,550,1000) 내 컨투어 시각화 + 마스크 창 출력
// - Space: 측정 1회(DoMeasureNow) -> total.json에서 label 찾아 x/y/ms/type 덮어쓰기
// - A: 자동 측정(1초마다) ON/OFF
// - ESC: 종료
//
// 빌드: OpenCV 필요 (libmodbus 불필요)
// 주의: scale.yaml의 mmPerPx가 0이면 측정 저장이 진행되지 않습니다.

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

using namespace cv;
using namespace std;

// =====================
// CONFIG
// =====================
static const string TOTAL_JSON = "./total.json";

// ROI (요청값 유지)
static const Rect ROI_FIXED(710, 50, 550, 1000);

// 카메라
static int DEVICE_INDEX = 1;       // 필요하면 0/1 바꾸세요
static bool USE_DSHOW = true;      // Windows면 true 권장
static int CAM_W = 1920;           // 필요하면 1280으로 낮추면 더 빨라집니다
static int CAM_H = 1080;

// 성능 튜닝(가능하면 효과)
static bool USE_MJPG = true;
static int  BUFFER_SIZE = 1;

// 측정 루틴 타임아웃
static const int MEASURE_TIMEOUT_MS = 1500;

// 자동 측정 간격(ms)
static const int AUTO_INTERVAL_MS = 1000;

// 색상 판별 기준
static int MIN_COLOR_PIXELS = 100;
static double MIN_COLOR_RATIO = 0.01;

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
// time helper
// =====================
static inline long long NowMillis() {
    using clock = chrono::system_clock;
    return chrono::duration_cast<chrono::milliseconds>(clock::now().time_since_epoch()).count();
}

// =====================
// File helpers (atomic write + ensure json)
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

static bool EnsureJsonArrayFile(const string& path) {
    ifstream ifs(path);
    if (ifs.is_open()) return true;
    return WriteTextFileAtomic(path, "[]\n");
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
    HsvRange R1{ Scalar(0,   60,  60), Scalar(15,  255, 255) };
    HsvRange R2{ Scalar(165, 60,  60), Scalar(179, 255, 255) };
    HsvRange G{ Scalar(40,  60,  60), Scalar(80,  255, 255) };
    HsvRange B{ Scalar(95,  60,  60), Scalar(125, 255, 255) };
};

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
// total.json 업데이트: label 찾아서 x,y,ms,type 추가/덮어쓰기
// =====================
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

static bool UpdateTotalJson_OverwriteMeasure(const string& path, const string& label,
    double x, double y, double ms, const string& type,
    string& outReason)
{
    string s;
    if (!ReadAllText(path, s)) {
        outReason = "total.json read failed";
        return false;
    }

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
            size_t objStart = s.rfind('{', labelKey);
            size_t objEnd = s.find('}', labelKey);
            if (objStart == string::npos || objEnd == string::npos || objEnd <= objStart) {
                outReason = "json object boundary parse failed";
                return false;
            }

            string obj = s.substr(objStart, objEnd - objStart + 1);

            auto RemoveKeyLine = [&](string& o, const string& key) {
                while (true) {
                    size_t k = o.find("\"" + key + "\"");
                    if (k == string::npos) break;

                    size_t lineStart = o.rfind('\n', k);
                    if (lineStart == string::npos) lineStart = 0;
                    else lineStart += 1;

                    size_t lineEnd = o.find('\n', k);
                    if (lineEnd == string::npos) {
                        o.erase(lineStart);
                    }
                    else {
                        o.erase(lineStart, (lineEnd - lineStart) + 1);
                    }
                }
                };

            string cleaned = obj;
            RemoveKeyLine(cleaned, "x");
            RemoveKeyLine(cleaned, "y");
            RemoveKeyLine(cleaned, "ms");
            RemoveKeyLine(cleaned, "type");

            size_t closePos = cleaned.rfind('}');
            if (closePos == string::npos) {
                outReason = "object close brace not found";
                return false;
            }

            string body = cleaned.substr(0, closePos);
            TrimRight(body);

            // 콤마 보정
            {
                size_t i = body.size();
                while (i > 0 && isspace((unsigned char)body[i - 1])) i--;
                char last = (i > 0) ? body[i - 1] : '\0';
                if (last != '{' && last != ',') body += ",";
            }

            ostringstream add;
            add << "\n"
                << "    \"x\": " << fixed << setprecision(3) << x << ",\n"
                << "    \"y\": " << fixed << setprecision(3) << y << ",\n"
                << "    \"ms\": " << fixed << setprecision(3) << ms << ",\n"
                << "    \"type\": \"" << type << "\"\n"
                << "  }";

            string newObj = body + add.str();
            string newS = s.substr(0, objStart) + newObj + s.substr(objEnd + 1);

            if (!WriteTextFileAtomic(path, newS)) {
                outReason = "total.json write failed";
                return false;
            }

            outReason = "OK(overwrite)";
            return true;
        }

        pos = labelKey + 7;
    }

    outReason = "label not found in total.json";
    return false;
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
// VISUALIZATION HELPERS
// - ROI 안에서 가장 큰 컨투어 박스 표시
// - mask도 창으로 보여줌
// =====================
static void DrawRoiAndLargestContourBox(
    const Mat& fullFrame,
    const Rect& roi,
    Mat& outVisFrame,
    Mat& outMaskVis
) {
    // 성능 때문에 clone을 줄이고 싶으면, 여기 outVisFrame을 fullFrame으로 두고 바로 그리셔도 됩니다.
    outVisFrame = fullFrame.clone();
    outMaskVis = Mat();

    Rect r = roi & Rect(0, 0, fullFrame.cols, fullFrame.rows);
    if (r.width <= 0 || r.height <= 0) {
        rectangle(outVisFrame, roi, Scalar(0, 0, 255), 2);
        return;
    }

    rectangle(outVisFrame, r, Scalar(0, 255, 255), 2);

    Mat roiFrame = fullFrame(r).clone();

    Mat hsv;
    cvtColor(roiFrame, hsv, COLOR_BGR2HSV);

    Mat maskR1, maskR2, maskR, maskG, maskB, mask;

    inRange(hsv, Scalar(0, 60, 60), Scalar(20, 255, 255), maskR1);
    inRange(hsv, Scalar(160, 60, 60), Scalar(179, 255, 255), maskR2);
    maskR = maskR1 | maskR2;

    inRange(hsv, Scalar(40, 60, 60), Scalar(85, 255, 255), maskG);
    inRange(hsv, Scalar(95, 60, 60), Scalar(125, 255, 255), maskB);

    mask = maskR | maskG | maskB;

    Mat blurred;
    GaussianBlur(mask, blurred, Size(3, 3), 0);
    threshold(blurred, blurred, 150, 255, THRESH_BINARY);

    Mat kernel = getStructuringElement(MORPH_RECT, Size(3, 3));
    morphologyEx(blurred, blurred, MORPH_OPEN, kernel, Point(-1, -1), 1);
    morphologyEx(blurred, blurred, MORPH_CLOSE, kernel, Point(-1, -1), 1);

    outMaskVis = blurred.clone();

    vector<vector<Point>> contours;
    vector<Vec4i> hierarchy;
    findContours(blurred, contours, hierarchy, RETR_EXTERNAL, CHAIN_APPROX_SIMPLE);

    int best = -1;
    double bestArea = 0.0;
    for (int i = 0; i < (int)contours.size(); i++) {
        double a = contourArea(contours[i]);
        if (a < 2000) continue;
        if (a > bestArea) { bestArea = a; best = i; }
    }

    Point2f offset((float)r.x, (float)r.y);

    if (best >= 0) {
        Rect br = boundingRect(contours[best]);
        Rect brFull(br.x + r.x, br.y + r.y, br.width, br.height);
        rectangle(outVisFrame, brFull, Scalar(0, 255, 0), 2);

        RotatedRect rr = minAreaRect(contours[best]);
        Point2f pts[4];
        rr.points(pts);

        for (int k = 0; k < 4; k++) {
            Point2f p1 = pts[k] + offset;
            Point2f p2 = pts[(k + 1) % 4] + offset;
            line(outVisFrame, p1, p2, Scalar(255, 0, 0), 2);
        }

        ostringstream ss;
        ss << "area=" << fixed << setprecision(0) << bestArea;
        putText(outVisFrame, ss.str(), Point(r.x + 10, r.y + 30),
            FONT_HERSHEY_SIMPLEX, 0.8, Scalar(0, 255, 255), 2);
    }
    else {
        putText(outVisFrame, "no contour (area>=2000)", Point(r.x + 10, r.y + 30),
            FONT_HERSHEY_SIMPLEX, 0.8, Scalar(0, 0, 255), 2);
    }
}

// =====================
// 측정 루틴(키 입력 시 1회 수행)
// - 기존 파이프라인 유지
// - 색상판별 + label 카운트(테스트용)
// - total.json에서 label 찾아 x/y/ms/type 덮어쓰기
// =====================
static bool DoMeasureNow(
    VideoCapture& cap,
    const Rect& roi,
    double mmPerPx,
    const ColorThresholds& colorTh,
    int& rCount,
    int& gCount,
    int& bCount,
    int& nCount,
    string& outLabel,
    string& outType
) {
    if (mmPerPx <= 0.0) {
        cout << "[MEASURE] mmPerPx is 0.0 (scale.yaml missing or invalid). Stop.\n";
        return false;
    }

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

        Mat hsv;
        cvtColor(roiFrame, hsv, COLOR_BGR2HSV);

        Mat maskR1, maskR2, maskR, maskG, maskB, mask;

        inRange(hsv, Scalar(0, 60, 60), Scalar(20, 255, 255), maskR1);
        inRange(hsv, Scalar(160, 60, 60), Scalar(179, 255, 255), maskR2);
        maskR = maskR1 | maskR2;

        inRange(hsv, Scalar(40, 60, 60), Scalar(85, 255, 255), maskG);
        inRange(hsv, Scalar(95, 60, 60), Scalar(125, 255, 255), maskB);

        mask = maskR | maskG | maskB;

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

                wOut = (double)longSidePx * mmPerPx;
                hOut = (double)shortSidePx * mmPerPx;
            }
        }

        int64 t1 = getTickCount();
        double elapsedMs = (t1 - t0) * 1000.0 / freq;

        if (detected) { presentStreak++; absentStreak = 0; }
        else { absentStreak++; presentStreak = 0; }

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

                    int rp = 0, gp = 0, bp = 0;
                    string color = ClassifyColorROI(buf[0].roiImg, colorTh, rp, gp, bp);

                    int curCount = 0;
                    if (color == "RED") { rCount++; curCount = rCount; }
                    else if (color == "GREEN") { gCount++; curCount = gCount; }
                    else if (color == "BLUE") { bCount++; curCount = bCount; }
                    else { nCount++; curCount = nCount; }

                    string label = MakeLabel(color, curCount);
                    string type = DecideTypeByX(xMm);

                    cout << "[MEASURE] detected=1"
                        << " color=" << color
                        << " label=" << label
                        << " (rPix/gPix/bPix=" << rp << "/" << gp << "/" << bp << ")"
                        << " x=" << fixed << setprecision(3) << xMm
                        << " y=" << fixed << setprecision(3) << yMm
                        << " ms=" << fixed << setprecision(3) << ms
                        << " type=" << type
                        << "\n";

                    string reason;
                    bool ok = UpdateTotalJson_OverwriteMeasure(TOTAL_JSON, label, xMm, yMm, ms, type, reason);

                    if (!ok) {
                        cout << "[MEASURE] SAVE FAIL: " << reason << " (label=" << label << ")\n";
                    }
                    else {
                        cout << "[MEASURE] SAVE OK -> total.json updated (label=" << label << ")\n";
                    }

                    outLabel = label;
                    outType = type;
                    return ok;
                }
            }
        }

        this_thread::sleep_for(chrono::milliseconds(1));
    }
}

// =====================
// MAIN (NO PLC)
// =====================
int main() {
    cout << "[CWD] " << filesystem::current_path().string() << "\n";
    cout << "[MODE] TEST (NO PLC)\n";
    cout << "[KEY] SPACE=measure once | A=auto toggle | ESC=quit\n";
    cout << "[JSON] only " << TOTAL_JSON << "\n";

    // total.json 없으면 새로 생성
    if (!EnsureJsonArrayFile(TOTAL_JSON)) {
        cerr << "[FATAL] failed to create " << TOTAL_JSON << "\n";
        return -1;
    }

    // scale.yaml 읽기
    double mmPerPx = 0.0;
    if (ifstream("scale.yaml").good()) {
        FileStorage fs("scale.yaml", FileStorage::READ);
        if (fs.isOpened()) {
            fs["mmPerPx"] >> mmPerPx;
            fs.release();
        }
    }
    cout << "[SCALE] mmPerPx=" << fixed << setprecision(6) << mmPerPx << "\n";

    VideoCapture cap;
    if (USE_DSHOW) cap.open(DEVICE_INDEX, CAP_DSHOW);
    else cap.open(DEVICE_INDEX);

    if (!cap.isOpened()) {
        cerr << "[FATAL] camera open failed (device=" << DEVICE_INDEX << ")\n";
        return -1;
    }

    if (BUFFER_SIZE > 0) cap.set(CAP_PROP_BUFFERSIZE, BUFFER_SIZE);
    if (USE_MJPG) cap.set(CAP_PROP_FOURCC, VideoWriter::fourcc('M', 'J', 'P', 'G'));

    cap.set(CAP_PROP_FRAME_WIDTH, CAM_W);
    cap.set(CAP_PROP_FRAME_HEIGHT, CAM_H);

    int actualWidth = (int)cap.get(CAP_PROP_FRAME_WIDTH);
    int actualHeight = (int)cap.get(CAP_PROP_FRAME_HEIGHT);
    cout << "[CAMERA] Actual resolution: " << actualWidth << "x" << actualHeight << "\n";

    Rect roi = ROI_FIXED & Rect(0, 0, actualWidth, actualHeight);
    cout << "[ROI] x=" << roi.x << " y=" << roi.y
        << " w=" << roi.width << " h=" << roi.height << "\n";

    ColorThresholds th;

    // 런타임 색상 카운터(측정용)
    int rCount = 0, gCount = 0, bCount = 0, nCount = 0;

    // 창
    namedWindow("VIEW", WINDOW_NORMAL);
    namedWindow("MASK(ROI)", WINDOW_NORMAL);

    bool autoMode = false;
    long long nextAutoMs = 0;

    while (true) {
        Mat live;
        cap >> live;
        if (!live.empty()) {
            Mat vis, maskVis;
            DrawRoiAndLargestContourBox(live, roi, vis, maskVis);

            // 상태 텍스트
            string s1 = string("auto=") + (autoMode ? "ON" : "OFF");
            putText(vis, s1, Point(10, 30), FONT_HERSHEY_SIMPLEX, 0.9, Scalar(0, 255, 255), 2);

            imshow("VIEW", vis);
            if (!maskVis.empty()) imshow("MASK(ROI)", maskVis);
        }

        int key = waitKey(1);
        if (key == 27) {
            cout << "[EXIT] ESC pressed\n";
            break;
        }

        if (key == 'a' || key == 'A') {
            autoMode = !autoMode;
            cout << "[TEST] autoMode=" << (autoMode ? "ON" : "OFF") << "\n";
            nextAutoMs = NowMillis(); // 바로 실행 가능
        }

        bool doOneShot = (key == ' '); // Space

        if (autoMode) {
            long long now = NowMillis();
            if (now >= nextAutoMs) {
                doOneShot = true;
                nextAutoMs = now + AUTO_INTERVAL_MS;
            }
        }

        if (doOneShot) {
            cout << "[TEST] MEASURE NOW\n";
            string label, type;
            bool ok = DoMeasureNow(cap, roi, mmPerPx, th, rCount, gCount, bCount, nCount, label, type);
            if (!ok) {
                cout << "[TEST] MEASURE FAIL (check: mmPerPx>0 AND total.json has label)\n";
            }
            else {
                cout << "[TEST] MEASURE OK label=" << label << " type=" << type << "\n";
            }
        }

        this_thread::sleep_for(chrono::milliseconds(1));
    }

    cap.release();
    destroyAllWindows();
    return 0;
}
