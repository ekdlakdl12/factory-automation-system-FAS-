#include <iostream>
#include <opencv2/opencv.hpp>
#include <vector>
#include <string>
#include <fstream>
#include <sstream>
#include <iomanip>
#include <chrono>
#include <filesystem>
#include <windows.h>

using namespace std;
using namespace cv;
using namespace std::chrono;
using namespace std::filesystem;

// -------------------------------
// JSON용 문자열 escape (최소 처리)
// -------------------------------
static string jsonEscape(const string& s)
{
    string out;
    out.reserve(s.size() + 8);

    for (char c : s) {
        switch (c) {
        case '\"': out += "\\\""; break;
        case '\\': out += "\\\\"; break;
        case '\b': out += "\\b";  break;
        case '\f': out += "\\f";  break;
        case '\n': out += "\\n";  break;
        case '\r': out += "\\r";  break;
        case '\t': out += "\\t";  break;
        default:
            if ((unsigned char)c < 0x20) out += ' ';
            else out += c;
            break;
        }
    }
    return out;
}

// -------------------------------
// 현재 시간 ISO8601 문자열(로컬)
// -------------------------------
static string nowIso8601Local()
{
    auto now = system_clock::now();
    time_t t = system_clock::to_time_t(now);

    tm tmLocal{};
    localtime_s(&tmLocal, &t);

    ostringstream oss;
    oss << put_time(&tmLocal, "%Y-%m-%dT%H:%M:%S");
    return oss.str();
}

// -------------------------------
// 실행 파일(.exe) 폴더 경로 얻기
// -------------------------------
static path getExeDir()
{
    char buf[MAX_PATH]{};
    GetModuleFileNameA(NULL, buf, MAX_PATH);
    path p(buf);
    return p.parent_path();
}

// -------------------------------
// JSON 파일 원자적(atomic) 덮어쓰기: tmp -> rename
// -------------------------------
static bool writeJsonAtomically(const path& outPath, const string& jsonText)
{
    try {
        path tmp = outPath;
        tmp += ".tmp";

        {
            ofstream ofs(tmp, ios::binary | ios::trunc);
            if (!ofs) return false;
            ofs << jsonText;
            ofs.flush();
        }

        if (exists(outPath)) {
            remove(outPath);
        }
        rename(tmp, outPath);
        return true;
    }
    catch (...) {
        return false;
    }
}

// -------------------------------
// 1) HSV 기반 색 분류 함수 (ROI 평균)
// -------------------------------
static string classifyColorFromHSV(const Scalar& meanHSV)
{
    // OpenCV HSV: H[0..179], S[0..255], V[0..255]
    int H = (int)meanHSV[0];
    int S = (int)meanHSV[1];
    int V = (int)meanHSV[2];

    // 무채색(흰/회/검) 먼저 판정
    const int S_MIN = 40;
    const int V_BLACK = 50;
    const int V_WHITE = 200;

    if (S < S_MIN) {
        if (V <= V_BLACK) return "black";
        if (V >= V_WHITE) return "white";
        return "gray";
    }

    // 유채색(Hue) 범위 분류 (환경에 따라 조정 필요)
    if (H < 10 || H >= 170) return "red";
    if (H >= 10 && H < 25)  return "orange";
    if (H >= 25 && H < 35)  return "yellow";
    if (H >= 35 && H < 85)  return "green";
    if (H >= 85 && H < 125) return "blue";
    if (H >= 125 && H < 170) return "purple";

    return "unknown";
}

// -------------------------------
// 2) 가장 큰 외곽 컨투어 선택
// -------------------------------
static bool selectLargestContour(const vector<vector<Point>>& contours, int& outIdx, double minArea = 500.0)
{
    outIdx = -1;
    double bestArea = 0.0;

    for (int i = 0; i < (int)contours.size(); ++i) {
        double a = contourArea(contours[i]);
        if (a > bestArea && a >= minArea) {
            bestArea = a;
            outIdx = i;
        }
    }
    return (outIdx != -1);
}

int main()
{
    
    string imgPath = R"(C:\Users\JUNYEONG\Desktop\images\BoxTestImg.jpg)";

    Mat img = imread(imgPath, IMREAD_COLOR);
    if (img.empty()) {
        cerr << "Could not open or find the image!\n";
        return -1;
    }

    // -------------------------------
    // A) 물체 영역(ROI) 얻기: 그레이 → 이진화 → 컨투어
    // -------------------------------
    Mat gray;
    cvtColor(img, gray, COLOR_BGR2GRAY);

    GaussianBlur(gray, gray, Size(5, 5), 0);

    Mat bin;
    // 배경이 밝고 물체가 어두운 편이면 THRESH_BINARY_INV가 유리
    threshold(gray, bin, 220, 255, THRESH_BINARY_INV);

    // 노이즈 정리
    Mat kernel = getStructuringElement(MORPH_RECT, Size(3, 3));
    morphologyEx(bin, bin, MORPH_OPEN, kernel, Point(-1, -1), 1);
    // morphologyEx(bin, bin, MORPH_CLOSE, kernel, Point(-1, -1), 1);

    // 컨투어 추출 (외곽만)
    Mat contourInput = bin.clone();
    vector<vector<Point>> contours;
    vector<Vec4i> hierarchy;
    findContours(contourInput, contours, hierarchy, RETR_EXTERNAL, CHAIN_APPROX_SIMPLE);

    if (contours.empty()) {
        cerr << "No contours found.\n";
        imshow("Binary", bin);
        waitKey(0);
        return -1;
    }

    // 가장 큰 물체 선택
    int bestIdx = -1;
    if (!selectLargestContour(contours, bestIdx, 500.0)) {
        cerr << "No contour above minArea.\n";
        imshow("Binary", bin);
        waitKey(0);
        return -1;
    }

    // -------------------------------
    // B) 물체 마스크 생성 (물체 영역만 색 샘플링)
    // -------------------------------
    Mat objectMask = Mat::zeros(bin.size(), CV_8UC1);
    drawContours(objectMask, contours, bestIdx, Scalar(255), FILLED);

    // -------------------------------
    // C) HSV로 변환 후, 물체 마스크로 평균 HSV 계산
    // -------------------------------
    Mat hsv;
    cvtColor(img, hsv, COLOR_BGR2HSV);

    Scalar meanHSV = mean(hsv, objectMask);
    string detectedColor = classifyColorFromHSV(meanHSV);

    // -------------------------------
    // D) bbox/area 계산 (JSON/시각화 공통)
    // -------------------------------
    Rect bbox = boundingRect(contours[bestIdx]);
    double area = contourArea(contours[bestIdx]);

    // -------------------------------
    // E) JSON 파일로 결과 저장 (output.json)
    //     - exe 폴더에 저장 (배포 시 경로 문제 최소화)
    // -------------------------------
    ostringstream json;
    json << "{\n";
    json << "  \"ts\": \"" << nowIso8601Local() << "\",\n";
    json << "  \"source\": \"" << jsonEscape(imgPath) << "\",\n";
    json << "  \"detected_color\": \"" << jsonEscape(detectedColor) << "\",\n";
    json << "  \"mean_hsv\": {"
        << "\"h\": " << (int)meanHSV[0] << ", "
        << "\"s\": " << (int)meanHSV[1] << ", "
        << "\"v\": " << (int)meanHSV[2] << "},\n";
    json << "  \"bbox\": {"
        << "\"x\": " << bbox.x << ", "
        << "\"y\": " << bbox.y << ", "
        << "\"w\": " << bbox.width << ", "
        << "\"h\": " << bbox.height << "},\n";
    json << "  \"area_px\": " << fixed << setprecision(1) << area << "\n";
    json << "}\n";

    path outPath = getExeDir() / "output.json";

    if (!writeJsonAtomically(outPath, json.str())) {
        cerr << "Failed to write JSON: " << outPath.string() << endl;
    }
    else {
        cout << "JSON saved: " << outPath.string() << endl;
    }

    // -------------------------------
    // F) 시각화 (결과 표시)
    // -------------------------------
    Mat vis = img.clone();
    drawContours(vis, contours, bestIdx, Scalar(0, 255, 0), 2);
    rectangle(vis, bbox, Scalar(255, 0, 0), 2);

    string label = "Detected: " + detectedColor +
        "  (H=" + to_string((int)meanHSV[0]) +
        ", S=" + to_string((int)meanHSV[1]) +
        ", V=" + to_string((int)meanHSV[2]) + ")";
    putText(vis, label, Point(10, 30), FONT_HERSHEY_SIMPLEX, 0.8, Scalar(0, 0, 255), 2);

    imshow("Binary", bin);
    imshow("ObjectMask", objectMask);
    imshow("Result", vis);

    waitKey(0);
    return 0;
}
