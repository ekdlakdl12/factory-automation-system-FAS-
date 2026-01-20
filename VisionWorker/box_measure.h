#pragma once
#include <opencv2/opencv.hpp>

using namespace std;
using namespace cv;

struct BoxMeasure
{
    bool ok = false;
    RotatedRect rr;
    double wPx = 0.0;
    double hPx = 0.0;
    double area = 0.0;
};

BoxMeasure MeasureLargestBoxFromMask(const Mat& mask, double minArea = 1000.0);
void DrawRotatedRect(Mat& bgr, const RotatedRect& rr, const Scalar& color, int thickness = 2);
#pragma once
