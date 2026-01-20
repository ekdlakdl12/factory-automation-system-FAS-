#include "vision_common.h"

Rect ClampROI(const Rect& r, const Size& sz)
{
    Rect imgRect(0, 0, sz.width, sz.height);
    return r & imgRect;
}

double CalcWhiteRatio(const Mat& binaryMask)
{
    return (double)countNonZero(binaryMask) / (binaryMask.rows * binaryMask.cols);
}

void ShowFit(const string& name, const Mat& src, int maxW, int maxH)
{
    Mat disp;
    double scale = min((double)maxW / src.cols, (double)maxH / src.rows);
    if (scale < 1.0) resize(src, disp, Size(), scale, scale, INTER_AREA);
    else disp = src;
    imshow(name, disp);
}
