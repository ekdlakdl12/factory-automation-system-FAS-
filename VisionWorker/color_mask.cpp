#include "color_mask.h"

Mat MakeMaskHSV(
    const Mat& bgr,
    const HsvRange& range,
    int blurK,
    double blurSigma,
    int morphK,
    int openIter,
    int closeIter
)
{
    Mat hsv;
    cvtColor(bgr, hsv, COLOR_BGR2HSV);

    if (blurK >= 3) {
        if (blurK % 2 == 0) blurK++; // È¦¼ö º¸Á¤
        GaussianBlur(hsv, hsv, Size(blurK, blurK), blurSigma);
    }
	
    Mat mask, maskBrown, maskWhite;
    inRange(hsv, range.brownL, range.brownU, maskBrown);
    inRange(hsv, range.whiteL, range.whiteU, maskWhite);

    mask = maskBrown | maskWhite;

    Mat k = getStructuringElement(MORPH_RECT, Size(morphK, morphK));
    if (openIter > 0)  morphologyEx(mask, mask, MORPH_OPEN, k, Point(-1, -1), openIter);
    if (closeIter > 0) morphologyEx(mask, mask, MORPH_CLOSE, k, Point(-1, -1), closeIter);

    return mask;
}
