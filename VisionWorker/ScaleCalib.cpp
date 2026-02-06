#include <opencv2/opencv.hpp>
#include <iostream>
#include <vector>
#include <numeric>
#include <cmath>
#include <limits>

using namespace cv;
using namespace std;

// ===== 설정 =====
static int DEVICE_INDEX = 1;
static bool USE_DSHOW = true;

static int CAM_W = 1920;
static int CAM_H = 1080;

static const Rect ROI_FIXED(710, 50, 550, 1000);
static const int REPEAT_N = 5;
static double REF_MM = 50.0;

static const string SCALE_YAML = "scale.yaml";

// ===== 런타임 상태 =====
static Rect g_roi;
static vector<Point2f> g_pts;      // 현재 클릭 2점
static vector<double> g_samples;   // mmPerPx 샘플들
static string g_win = "SCALE_CALIB";

// ROI 안에서만 클릭 받기
static void OnMouse(int event, int x, int y, int, void*)
{
    if (event != EVENT_LBUTTONDOWN) return;

    if (!g_roi.contains(Point(x, y))) {
        cout << "[CLICK] ROI 밖 클릭 -> 무시\n";
        return;
    }

    if ((int)g_pts.size() >= 2) return;

    g_pts.push_back(Point2f((float)x, (float)y));
    cout << "[CLICK] " << g_pts.size() << "/2 : (" << x << "," << y << ")\n";
}

static double DistPx(const Point2f& a, const Point2f& b)
{
    double dx = (double)a.x - (double)b.x;
    double dy = (double)a.y - (double)b.y;
    return sqrt(dx * dx + dy * dy);
}

static void DrawText(Mat& img, const string& s, int y)
{
    putText(img, s, Point(10, y), FONT_HERSHEY_SIMPLEX, 0.7, Scalar(0, 0, 0), 3, LINE_AA);
    putText(img, s, Point(10, y), FONT_HERSHEY_SIMPLEX, 0.7, Scalar(255, 255, 255), 1, LINE_AA);
}

int main()
{
    cout << "[ScaleCalib] start\n";
    cout << "REF_MM default=50. Enter to keep, or type number: ";

    // ✅ 입력 때문에 멈추는 거 싫으면 이 5줄 통째로 지우셔도 됩니다.
    string line;
    getline(cin, line);
    if (!line.empty()) {
        try { REF_MM = stod(line); }
        catch (...) { REF_MM = 50.0; }
    }
    cout << "[REF] " << REF_MM << " mm\n";

    VideoCapture cap;
    if (USE_DSHOW) cap.open(DEVICE_INDEX, CAP_DSHOW);
    else cap.open(DEVICE_INDEX);

    if (!cap.isOpened()) {
        cerr << "[FATAL] camera open failed. try DEVICE_INDEX=0/1\n";
        return -1;
    }

    cap.set(CAP_PROP_FRAME_WIDTH, CAM_W);
    cap.set(CAP_PROP_FRAME_HEIGHT, CAM_H);

    Mat frame;
    cap >> frame;
    if (frame.empty()) {
        cerr << "[FATAL] first frame empty\n";
        return -1;
    }

    // ROI clamp
    g_roi = ROI_FIXED & Rect(0, 0, frame.cols, frame.rows);
    cout << "[ROI] " << g_roi.x << "," << g_roi.y << "," << g_roi.width << "," << g_roi.height << "\n";

    // ✅ 창 + 콜백 등록
    namedWindow(g_win, WINDOW_NORMAL);
    resizeWindow(g_win, 1280, 720);
    setMouseCallback(g_win, OnMouse);

    cout << "[HOW] ROI 안에서 자의 양 끝을 2번 클릭하세요. (총 " << REPEAT_N << "회)\n";
    cout << "      r=현재 클릭 리셋, u=마지막 샘플 취소, ESC=종료\n";

    while (true) {
        cap >> frame;
        if (frame.empty()) continue;

        Mat vis = frame.clone();

        // ROI 표시
        rectangle(vis, g_roi, Scalar(0, 255, 255), 2);

        // 클릭 표시
        for (int i = 0; i < (int)g_pts.size(); i++) {
            circle(vis, g_pts[i], 6, Scalar(0, 0, 255), FILLED);
            putText(vis, to_string(i + 1), g_pts[i] + Point2f(10, -10),
                FONT_HERSHEY_SIMPLEX, 0.8, Scalar(0, 0, 0), 3, LINE_AA);
            putText(vis, to_string(i + 1), g_pts[i] + Point2f(10, -10),
                FONT_HERSHEY_SIMPLEX, 0.8, Scalar(255, 255, 255), 1, LINE_AA);
        }

        // 2점이면 선 표시 + px 표시
        if (g_pts.size() == 2) {
            cv::line(vis, g_pts[0], g_pts[1], Scalar(0, 0, 255), 2);
            double px = DistPx(g_pts[0], g_pts[1]);
            ostringstream ss;
            ss.setf(std::ios::fixed); ss.precision(2);
            ss << "px=" << px;
            DrawText(vis, ss.str(), 150);
        }

        // 안내 텍스트
        DrawText(vis, "REF_MM=" + to_string((int)REF_MM) + "mm", 30);
        DrawText(vis, "Sample " + to_string((int)g_samples.size()) + "/" + to_string(REPEAT_N), 60);
        DrawText(vis, "Click 2 points inside ROI", 90);
        DrawText(vis, "Keys: r=reset, u=undo, ESC=quit", 120);

        // 현재 평균
        if (!g_samples.empty()) {
            double avg = accumulate(g_samples.begin(), g_samples.end(), 0.0) / (double)g_samples.size();
            ostringstream ss;
            ss.setf(std::ios::fixed); ss.precision(10);
            ss << "Current avg mmPerPx=" << avg;
            DrawText(vis, ss.str(), 180);
        }

        // ✅ 이 두 줄이 있어야 창이 유지되고 클릭도 됨
        imshow(g_win, vis);
        int key = waitKey(1);

        if (key == 27) { // ESC
            cout << "[EXIT] canceled\n";
            break;
        }
        if (key == 'r' || key == 'R') {
            g_pts.clear();
            cout << "[RESET] points cleared\n";
        }
        if (key == 'u' || key == 'U') {
            if (!g_samples.empty()) {
                g_samples.pop_back();
                cout << "[UNDO] last sample removed\n";
            }
            g_pts.clear();
        }

        // ✅ 2점 찍히면 샘플 저장 후 다음 회차로
        if (g_pts.size() == 2) {
            double px = DistPx(g_pts[0], g_pts[1]);
            if (px < 1.0) {
                cout << "[WARN] px too small -> redo\n";
                g_pts.clear();
                continue;
            }

            double mmPerPx = REF_MM / px;
            g_samples.push_back(mmPerPx);

            cout.setf(std::ios::fixed);
            cout << "[SAMPLE] " << g_samples.size() << "/" << REPEAT_N
                << "  px=" << setprecision(3) << px
                << "  mmPerPx=" << setprecision(10) << mmPerPx << "\n";

            g_pts.clear();

            if ((int)g_samples.size() >= REPEAT_N) {
                double avg = accumulate(g_samples.begin(), g_samples.end(), 0.0) / (double)g_samples.size();
                cout << "[DONE] avg mmPerPx=" << setprecision(10) << avg << "\n";

                FileStorage fs(SCALE_YAML, FileStorage::WRITE);
                fs << "mmPerPx" << avg;
                fs << "ref_mm" << REF_MM;
                fs << "repeat_n" << (int)g_samples.size();
                fs << "samples" << "[";
                for (double v : g_samples) fs << v;
                fs << "]";
                fs.release();

                cout << "[SAVE] " << SCALE_YAML << "\n";
                break;
            }
        }
    }

    cap.release();
    destroyAllWindows();
    return 0;
}
