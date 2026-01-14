// BoxMeasure.hpp
#pragma once

#include <string>
#include <vector>
#include <opencv2/opencv.hpp>

using namespace std;
using namespace cv;

// 전처리 파라미터를 한 곳에 모아서 관리하기 위한 구조체
struct PreprocessParams
{
    // Blur
    Size blurKsize = Size(5, 5);
    double blurSigmaX = 0.0;

    // Threshold
    double thresh = 220.0;
    double maxVal = 255.0;
    int threshType = THRESH_BINARY; // THRESH_BINARY, THRESH_BINARY_INV 등

    // Morphology
    int morphShape = MORPH_RECT;    // MORPH_RECT, MORPH_ELLIPSE, MORPH_CROSS
    Size kernelSize = Size(3, 3);
    int morphOp = MORPH_OPEN;       // MORPH_OPEN, MORPH_CLOSE, MORPH_GRADIENT ...
    int morphIterations = 1;
};

// 1) 이미지 로드 (BGR 컬러)
Mat LoadColorImageOrThrow(const string& path);

// 2) 측정용 마스크 생성: BGR -> GRAY
Mat MakeGrayMask(const Mat& bgr);

// 3) 블러
void ApplyGaussianBlurInplace(Mat& grayOrMask, const Size& ksize, double sigmaX = 0.0);

// 4) 이진화 (threshold)
void ApplyThresholdInplace(Mat& grayOrMask, double thresh, double maxVal, int threshType);

// 5) 모폴로지 커널 생성
Mat CreateMorphKernel(int shape, const Size& ksize);

// 6) 모폴로지 연산 (open/close/gradient 등)
void ApplyMorphologyInplace(Mat& binaryMask, int morphOp, const Mat& kernel, int iterations = 1);

// 7) 전처리 파이프라인: bgr -> gray -> blur -> threshold -> morphology
Mat BuildBinaryMaskForMeasurement(const Mat& bgr, const PreprocessParams& params);

// 8) 컨투어 추출
void ExtractContours(
    const Mat& binaryMask,
    vector<vector<Point>>& contours,
    vector<Vec4i>& hierarchy,
    int mode = RETR_EXTERNAL,
    int method = CHAIN_APPROX_SIMPLE
);

// 9) (선택) 외곽 컨투어 중 가장 큰 것(면적 기준) 선택
bool SelectLargestContourByArea(
    const vector<vector<Point>>& contours,
    vector<Point>& outLargest,
    double minArea = 0.0
);

