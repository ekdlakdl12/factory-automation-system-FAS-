#include <opencv2/opencv.hpp>
#include <iostream>
#include <vector>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <cstdio>
#include <cctype>
#include <filesystem>

using namespace cv;
using namespace std;

// ===================== 시각화 유틸 =====================
static void ShowFit(const string& name, const Mat& src)
{
    Mat disp;
    double scale = min(1920.0 / src.cols, 1080.0 / src.rows);
    if (scale < 1.0) resize(src, disp, Size(), scale, scale, INTER_AREA);
    else disp = src;
    imshow(name, disp);
}

// ===================== JSON 유틸 (라이브러리 없이) =====================
static bool WriteTextFile(const string& path, const string& text) {
    ofstream ofs(path, ios::out | ios::trunc);
    if (!ofs.is_open()) return false;
    ofs << text;
    ofs.close();
    return true;
}

static bool EnsureJsonArrayFile(const string& path) {
    ifstream ifs(path);
    if (ifs.is_open()) return true; // 이미 있으면 OK
    return WriteTextFile(path, "[]\n");
}

static bool AppendJsonArray(const string& path, const string& recordJson) {
    // 1) 기존 파일 읽기(없으면 []로 시작)
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

    // tmp로 쓰고 교체(원자적에 가깝게)
    string tmpPath = path + ".tmp";
    {
        ofstream ofs(tmpPath, ios::out | ios::trunc);
        if (!ofs.is_open()) return false;
        ofs << out;
        ofs.close();
    }

    // 교체 시도
    std::remove(path.c_str());
    if (std::rename(tmpPath.c_str(), path.c_str()) != 0) {
        // rename 실패(파일 잠김 등) 대비: 직접 덮어쓰기 fallback
        ofstream ofs(path, ios::out | ios::trunc);
        if (!ofs.is_open()) {
            std::remove(tmpPath.c_str());
            return false;
        }
        ofs << out;
        ofs.close();
        std::remove(tmpPath.c_str());
        return true;
    }

    return true;
}

// ===================== HSV 마스크 =====================
// OpenCV HSV: H 0~179, S 0~255, V 0~255
static void BuildMasksRGB(const Mat& hsv, Mat& maskR, Mat& maskG, Mat& maskB)
{
    // 빨강: 0근처 + 179근처(랩어라운드)
    Scalar r1L(0, 80, 80), r1U(10, 255, 255);
    Scalar r2L(170, 80, 80), r2U(179, 255, 255);

    // 초록/파랑
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
    string imgPath = R"(C:\Users\dbsdm\Desktop\testimg\green_in.jpg)"; // 테스트 이미지
    string colorJsonPath = "color_result.json";                    // <<<< 색상 전용 JSON 파일
    Rect roi(0, 0, 0, 0);       // 0이면 전체
    double minArea = 3000.0;    // 작은 잡음 제외
    // =======================

    // 저장 위치 확인용(헷갈리면 확인하세요)
    cout << "[CWD] " << std::filesystem::current_path().string() << "\n";
    cout << "[JSON] " << colorJsonPath << "\n";

    // color_result.json은 무조건 존재하게
    if (!EnsureJsonArrayFile(colorJsonPath)) {
        cerr << "EnsureJsonArrayFile failed: " << colorJsonPath << "\n";
        return -1;
    }

    // ==============================
    // [현재 모드] 테스트 이미지 1장 처리
    // ==============================
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

    // ===== JSON 저장(label, color, ms) =====
    static int labelCounter = 0;
    labelCounter++;

    ostringstream rec;
    rec << "  {\n";
    rec << "    \"label\": " << labelCounter << ",\n";
    rec << "    \"color\": \"" << color << "\",\n";
    rec << "    \"ms\": " << fixed << setprecision(3) << elapsed_ms << "\n";
    rec << "  }";

    bool ok = AppendJsonArray(colorJsonPath, rec.str());
    cout << "[SAVE] " << (ok ? "OK" : "FAIL")
        << " label=" << labelCounter
        << " color=" << color
        << " ms=" << fixed << setprecision(3) << elapsed_ms
        << " -> " << colorJsonPath << "\n";

    // ===== 시각화 유지 =====
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

    // =====================================================================
    // [나중에 카메라 연동할 때] 아래 블록을 사용하세요.
    // 지금은 테스트 이미지 모드라서 주석 처리해 둡니다.
    // =====================================================================
    /*
    int deviceIndex = 1;
    VideoCapture cap(deviceIndex, CAP_DSHOW);
    if (!cap.isOpened()) {
        cerr << "camera open failed\n";
        return -1;
    }
    cap.set(CAP_PROP_FOURCC, VideoWriter::fourcc('M','J','P','G'));
    cap.set(CAP_PROP_FRAME_WIDTH, 1920);
    cap.set(CAP_PROP_FRAME_HEIGHT, 1080);

    // 이때는 imread/imgPath 관련 코드는 필요 없으니 위 테스트 이미지 블록을 주석 처리하세요.

    namedWindow("result", WINDOW_NORMAL);
    namedWindow("maskR", WINDOW_NORMAL);
    namedWindow("maskG", WINDOW_NORMAL);
    namedWindow("maskB", WINDOW_NORMAL);

    int labelCounter = 0;

    for (;;) {
        Mat frame;
        cap >> frame;
        if (frame.empty()) continue;

        // ROI를 카메라에서도 쓰려면 안전하게 클램프
        // Rect camRoi = roi & Rect(0,0, frame.cols, frame.rows);
        // Mat view = (camRoi.width>0 && camRoi.height>0) ? frame(camRoi).clone() : frame.clone();
        Mat view = frame;

        int64 t0 = getTickCount();

        Mat hsv;
        cvtColor(view, hsv, COLOR_BGR2HSV);
        GaussianBlur(hsv, hsv, Size(3,3), 0);

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

        // JSON append
        labelCounter++;
        ostringstream rec;
        rec << "  {\n";
        rec << "    \"label\": " << labelCounter << ",\n";
        rec << "    \"color\": \"" << color << "\",\n";
        rec << "    \"ms\": " << fixed << setprecision(3) << elapsed_ms << "\n";
        rec << "  }";
        AppendJsonArray(colorJsonPath, rec.str());

        // 시각화
        Mat vis = view.clone();
        if (color == "RED")   DrawRR(vis, bestRR, Scalar(0,0,255), 2);
        if (color == "GREEN") DrawRR(vis, bestRR, Scalar(0,255,0), 2);
        if (color == "BLUE")  DrawRR(vis, bestRR, Scalar(255,0,0), 2);

        putText(vis, format("COLOR=%s (%.2f ms)", color.c_str(), elapsed_ms),
            Point(20,40), FONT_HERSHEY_SIMPLEX, 1.0, Scalar(0,255,255), 2);

        imshow("result", vis);
        imshow("maskR", maskR);
        imshow("maskG", maskG);
        imshow("maskB", maskB);

        int k = waitKey(1);
        if (k == 27) break; // ESC
    }
    */
    // =====================================================================

    return 0;
}
