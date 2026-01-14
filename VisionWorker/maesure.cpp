#include "vision.h"
#include <stdexcept>

Mat LoadColorImageOrThrow(const string& path)
{
    Mat img = imread(path, IMREAD_COLOR);
    if (img.empty())
    {
        throw runtime_error("Could not open or find the image: " + path);
    }
    return img;
}

Mat MakeGrayMask(const Mat& bgr)
{
    if (bgr.empty()) return Mat();

    Mat gray;
    cvtColor(bgr, gray, COLOR_BGR2GRAY);
    return gray;
}

void ApplyGaussianBlurInplace(Mat& grayOrMask, const Size& ksize, double sigmaX)
{
    if (grayOrMask.empty()) return;
    GaussianBlur(grayOrMask, grayOrMask, ksize, sigmaX);
}

void ApplyThresholdInplace(Mat& grayOrMask, double thresh, double maxVal, int threshType)
{
    if (grayOrMask.empty()) return;
    threshold(grayOrMask, grayOrMask, thresh, maxVal, threshType);
}

Mat CreateMorphKernel(int shape, const Size& ksize)
{
    return getStructuringElement(shape, ksize);
}

void ApplyMorphologyInplace(Mat& binaryMask, int morphOp, const Mat& kernel, int iterations)
{
    if (binaryMask.empty()) return;
    if (kernel.empty()) return;

    // morphologyEx의 마지막 인자로 iterations를 넣어 반복 적용
    morphologyEx(binaryMask, binaryMask, morphOp, kernel, Point(-1, -1), iterations);
}

Mat BuildBinaryMaskForMeasurement(const Mat& bgr, const PreprocessParams& params)
{
    Mat mask = MakeGrayMask(bgr);
    if (mask.empty()) return mask;

    ApplyGaussianBlurInplace(mask, params.blurKsize, params.blurSigmaX);
    ApplyThresholdInplace(mask, params.thresh, params.maxVal, params.threshType);

    Mat kernel = CreateMorphKernel(params.morphShape, params.kernelSize);
    ApplyMorphologyInplace(mask, params.morphOp, kernel, params.morphIterations);

    return mask; // 최종 이진 마스크
}

void ExtractContours(
    const Mat& binaryMask,
    vector<vector<Point>>& contours,
    vector<Vec4i>& hierarchy,
    int mode,
    int method)
{
    contours.clear();
    hierarchy.clear();

    if (binaryMask.empty()) return;

    // findContours는 입력을 변경할 수 있으므로 clone을 사용
    Mat tmp = binaryMask.clone();
    findContours(tmp, contours, hierarchy, mode, method);
}

bool SelectLargestContourByArea(
    const vector<vector<Point>>& contours,
    vector<Point>& outLargest,
    double minArea)
{
    outLargest.clear();
    if (contours.empty()) return false;

    double bestArea = -1.0;
    int bestIdx = -1;

    for (int i = 0; i < (int)contours.size(); ++i)
    {
        double area = contourArea(contours[i]);
        if (area < minArea) continue;

        if (area > bestArea)
        {
            bestArea = area;
            bestIdx = i;
        }
    }

    if (bestIdx < 0) return false;

    outLargest = contours[bestIdx];
    return true;
}
