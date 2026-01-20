#include "box_measure.h"

BoxMeasure MeasureLargestBoxFromMask(const Mat& mask, double minArea)
{
    BoxMeasure out;

    vector<vector<Point>> contours;
    findContours(mask, contours, RETR_EXTERNAL, CHAIN_APPROX_SIMPLE);
    if (contours.empty()) return out;

    int best = -1;
    double bestA = 0.0;

    for (int i = 0; i < (int)contours.size(); i++) {
        double a = contourArea(contours[i]);
        if (a >= minArea && a > bestA) {
            bestA = a;
            best = i;
        }
    }
    if (best < 0) return out;

    RotatedRect rr = minAreaRect(contours[best]);
    double w = rr.size.width;
    double h = rr.size.height;
    if (w < h) swap(w, h);

    out.ok = true;
    out.rr = rr;
    out.wPx = w;
    out.hPx = h;
    out.area = bestA;
    return out;
}

void DrawRotatedRect(Mat& bgr, const RotatedRect& rr, const Scalar& color, int thickness)
{
    Point2f pts[4];
    rr.points(pts);
    for (int i = 0; i < 4; i++) {
        line(bgr, pts[i], pts[(i + 1) % 4], color, thickness);
    }
}
