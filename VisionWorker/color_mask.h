#pragma once
#include <opencv2/opencv.hpp>

using namespace std;
using namespace cv;

struct HsvRange
{
    Scalar brownL;
    Scalar brownU;
	Scalar whiteL;
    Scalar whiteU;
};

Mat MakeMaskHSV(
    const Mat& bgr,
    const HsvRange& range,
    int blurK = 3,
    double blurSigma = 1.0,
    int morphK = 3,
    int openIter = 2,
    int closeIter = 2
);
#pragma once
