#include "scale_calib.h"
#include <iostream>

static Point g_p1(-1, -1), g_p2(-1, -1);
static bool g_done = false;

static void OnMouse(int event, int x, int y, int, void*)
{
    if (event != EVENT_LBUTTONDOWN) return;

    if (g_p1.x < 0) g_p1 = Point(x, y);
    else if (g_p2.x < 0) { g_p2 = Point(x, y); g_done = true; }
}

double PickScaleMmPerPx(const Mat& viewBgr, double realMm, const string& winName)
{
    g_p1 = Point(-1, -1);
    g_p2 = Point(-1, -1);
    g_done = false;

    namedWindow(winName, WINDOW_NORMAL);
    setMouseCallback(winName, OnMouse);

    cout << "[Scale] Click 2 points on ruler. realMm=" << realMm << "\n";
    cout << "ESC = cancel\n";

    while (true) {
        Mat disp = viewBgr.clone();

        putText(disp, "Click 2 points on ruler (ESC=cancel).",
            Point(20, 30), FONT_HERSHEY_SIMPLEX, 0.8, Scalar(0, 255, 255), 2);

        if (g_p1.x >= 0) circle(disp, g_p1, 6, Scalar(0, 255, 0), -1);
        if (g_p2.x >= 0) circle(disp, g_p2, 6, Scalar(0, 255, 0), -1);

        if (g_p1.x >= 0 && g_p2.x >= 0) {
            line(disp, g_p1, g_p2, Scalar(0, 255, 0), 2);
            double pxDist = norm(g_p1 - g_p2);
            putText(disp, format("pxDist=%.2f", pxDist),
                Point(20, 70), FONT_HERSHEY_SIMPLEX, 0.8, Scalar(0, 255, 0), 2);
        }

        imshow(winName, disp);

        int key = waitKey(10);
        if (key == 27) return -1.0;

        if (g_done) {
            double pxDist = norm(g_p1 - g_p2);
            if (pxDist < 1.0) return -1.0;
            return realMm / pxDist;
        }
    }
}

bool SaveScaleYaml(const string& yamlPath, const ScaleInfo& s)
{
    FileStorage fs(yamlPath, FileStorage::WRITE);
    if (!fs.isOpened()) return false;

    fs << "mmPerPx" << s.mmPerPx;
    fs << "realMm" << s.realMm;

    fs << "imageWidth" << s.imageSize.width;
    fs << "imageHeight" << s.imageSize.height;

    fs << "roiX" << s.roi.x;
    fs << "roiY" << s.roi.y;
    fs << "roiW" << s.roi.width;
    fs << "roiH" << s.roi.height;

    fs << "note" << s.note;
    fs.release();
    return true;
}

bool LoadScaleYaml(const string& yamlPath, ScaleInfo& s)
{
    FileStorage fs(yamlPath, FileStorage::READ);
    if (!fs.isOpened()) return false;

    fs["mmPerPx"] >> s.mmPerPx;
    fs["realMm"] >> s.realMm;

    int w = 0, h = 0;
    fs["imageWidth"] >> w;
    fs["imageHeight"] >> h;
    s.imageSize = Size(w, h);

    int x = 0, y = 0, rw = 0, rh = 0;
    fs["roiX"] >> x;
    fs["roiY"] >> y;
    fs["roiW"] >> rw;
    fs["roiH"] >> rh;
    s.roi = Rect(x, y, rw, rh);

    fs["note"] >> s.note;

    fs.release();
    return (s.mmPerPx > 0.0);
}
