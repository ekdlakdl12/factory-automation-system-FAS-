#include <iostream>
#include <opencv2/opencv.hpp>
#include <vector>
#include <fstream>
#include <filesystem>
#include <chrono>

using namespace std;
using namespace cv;

// ===================== 유틸 =====================
static void ShowFit(const string& name, const Mat& src)
{
    Mat disp;
    double scale = min(1920.0 / src.cols, 1080.0 / src.rows);
    if (scale < 1.0) resize(src, disp, Size(), scale, scale, INTER_AREA);
    else disp = src;
    imshow(name, disp);
}

static int GetNextIdFromCsv(const string& csvPath)
{
    if (!filesystem::exists(csvPath)) return 1;

    ifstream in(csvPath);
    if (!in.is_open()) return 1;

    string line;
    int lastId = 0;

    // 첫 줄(헤더) 스킵
    if (!getline(in, line)) return 1;

    while (getline(in, line)) {
        // id,elapsed_ms,color
        // 안전하게 맨 앞 id만 파싱
        size_t comma = line.find(',');
        if (comma == string::npos) continue;
        string idStr = line.substr(0, comma);
        try {
            int id = stoi(idStr);
            lastId = max(lastId, id);
        }
        catch (...) {}
    }
    return lastId + 1;
}

static bool AppendColorToCsv(const string& csvPath, int id, double elapsed_ms, const string& color)
{
    bool needHeader = !filesystem::exists(csvPath);

    ofstream out(csvPath, ios::app);
    if (!out.is_open()) return false;

    if (needHeader) {
        out << "id,elapsed_ms,color\n";
    }

    out << id << "," << fixed << setprecision(3) << elapsed_ms << "," << color << "\n";
    return true;
}

// ===================== HSV 마스크 =====================
// OpenCV HSV: H 0~179, S 0~255, V 0~255
static void BuildMasksRGB(const Mat& hsv, Mat& maskR, Mat& maskG, Mat& maskB)
{
    // 빨강: 0근처 + 179근처(랩어라운드)
    Scalar r1L(0, 80, 80), r1U(10, 255, 255);
    Scalar r2L(170, 80, 80), r2U(179, 255, 255);

    Scalar gL(35, 70, 70), gU(85, 255, 255);
    Scalar bL(90, 70, 70), bU(140, 255, 255);

    Mat rA, rB;
    inRange(hsv, r1L, r1U, rA);
    inRange(hsv, r2L, r2U, rB);
    maskR = rA | rB;

    inRange(hsv, gL, gU, maskG);
    inRange(hsv, bL, bU, maskB);

    Mat k = getStructuringElement(MORPH_RECT, Size(5, 5));

    morphologyEx(maskR, maskR, MORPH_OPEN, k, Point(-1, -1), 1);
    morphologyEx(maskR, maskR, MORPH_CLOSE, k, Point(-1, -1), 2);

    morphologyEx(maskG, maskG, MORPH_OPEN, k, Point(-1, -1), 1);
    morphologyEx(maskG, maskG, MORPH_CLOSE, k, Point(-1, -1), 2);

    morphologyEx(maskB, maskB, MORPH_OPEN, k, Point(-1, -1), 1);
    morphologyEx(maskB, maskB, MORPH_CLOSE, k, Point(-1, -1), 2);
}

static double LargestContourArea(const Mat& mask, RotatedRect& outRR)
{
    vector<vector<Point>> contours;
    findContours(mask, contours, RETR_EXTERNAL, CHAIN_APPROX_SIMPLE);

    int best = -1;
    double bestArea = 0.0;

    for (int i = 0; i < (int)contours.size(); i++) {
        double a = contourArea(contours[i]);
        if (a > bestArea) {
            bestArea = a;
            best = i;
        }
    }

    if (best >= 0) outRR = minAreaRect(contours[best]);
    else outRR = RotatedRect();

    return bestArea;
}

static void DrawRR(Mat& img, const RotatedRect& rr, const Scalar& color, int thick)
{
    if (rr.size.width <= 0 || rr.size.height <= 0) return;
    Point2f pts[4];
    rr.points(pts);
    for (int i = 0; i < 4; i++) line(img, pts[i], pts[(i + 1) % 4], color, thick);
}

int main()
{
    // ===== 사용자 설정 =====
    string imgPath = R"(C:\Users\dbsdm\Desktop\testimg\green.jpg)"; // 테스트 이미지
    string csvPath = "color_log.csv";                                   // 실행 폴더에 누적 저장

    Rect roi(0, 0, 0, 0);       // 0이면 전체. ROI 쓰려면 (x,y,w,h) 넣기
    double minArea = 3000.0;    // 작은 잡음 제외
    // =======================

    Mat frame = imread(imgPath, IMREAD_COLOR);
    if (frame.empty()) {
        cerr << "imread failed. path=" << imgPath << "\n";
        return -1;
    }

    if (frame.rows > frame.cols) rotate(frame, frame, ROTATE_90_CLOCKWISE);

    if (roi.width > 0 && roi.height > 0) {
        Rect imgRect(0, 0, frame.cols, frame.rows);
        roi = roi & imgRect;
        if (roi.width <= 0 || roi.height <= 0) {
            cerr << "ROI out of range\n";
            return -1;
        }
        frame = frame(roi).clone();
    }

    // ==========================
    // 여기부터 "색상 판별 시간" 측정
    // ==========================
    int64 t0 = getTickCount();

    Mat hsv;
    cvtColor(frame, hsv, COLOR_BGR2HSV);
    GaussianBlur(hsv, hsv, Size(3, 3), 0);

    Mat maskR, maskG, maskB;
    BuildMasksRGB(hsv, maskR, maskG, maskB);

    RotatedRect rrR, rrG, rrB;
    double areaR = LargestContourArea(maskR, rrR);
    double areaG = LargestContourArea(maskG, rrG);
    double areaB = LargestContourArea(maskB, rrB);

    string color = "NONE";
    double bestArea = 0.0;
    RotatedRect bestRR;

    if (areaR > bestArea) { bestArea = areaR; color = "RED";   bestRR = rrR; }
    if (areaG > bestArea) { bestArea = areaG; color = "GREEN"; bestRR = rrG; }
    if (areaB > bestArea) { bestArea = areaB; color = "BLUE";  bestRR = rrB; }

    if (bestArea < minArea) color = "NONE";

    int64 t1 = getTickCount();
    double elapsed_ms = (t1 - t0) * 1000.0 / getTickFrequency();

    cout << "color=" << color << " elapsed_ms=" << elapsed_ms
        << " areas(R,G,B)=(" << areaR << "," << areaG << "," << areaB << ")\n";

    // ===== CSV 누적 저장 (id, elapsed_ms, color) =====
    int id = GetNextIdFromCsv(csvPath);
    if (!AppendColorToCsv(csvPath, id, elapsed_ms, color)) {
        cerr << "[Warn] CSV save failed: " << csvPath << "\n";
    }
    else {
        cout << "[CSV] appended id=" << id << " -> " << csvPath << "\n";
    }

    // ===== 시각화 =====
    Mat vis = frame.clone();

    if (color == "RED")   DrawRR(vis, bestRR, Scalar(0, 0, 255), 2);
    if (color == "GREEN") DrawRR(vis, bestRR, Scalar(0, 255, 0), 2);
    if (color == "BLUE")  DrawRR(vis, bestRR, Scalar(255, 0, 0), 2);

    putText(vis, format("COLOR=%s (%.2f ms)", color.c_str(), elapsed_ms),
        Point(20, 40), FONT_HERSHEY_SIMPLEX, 1.0, Scalar(0, 255, 255), 2);

    ShowFit("result", vis);
    ShowFit("maskR", maskR);
    ShowFit("maskG", maskG);
    ShowFit("maskB", maskB);

    waitKey(0);
    return 0;
}
