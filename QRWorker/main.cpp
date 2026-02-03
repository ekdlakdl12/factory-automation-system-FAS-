// QR코드 인식 코드
// 라즈베리파이 내부에서만 작동.
/*
#include <opencv2/opencv.hpp>
#include <opencv2/objdetect.hpp>
#include <opencv2/core/utils/logger.hpp>

#include <fcntl.h>
#include <unistd.h>
#include <termios.h>
#include <curl/curl.h>
#include <iostream>
#include <vector>
#include <algorithm>
#include <cmath>
#include <string>
#include <cctype>
#include <climits>
#include <thread>
#include <chrono>

using namespace std;
using namespace cv;

// ================== 캡처 해상도(디코드용 원본) ==================
static const int CAP_W = 1280;
static const int CAP_H = 720;

// ================== 탐지 해상도(빠른 detect용) ==================
static const int DET_W = 640;
static const int DET_H = 360;

// ================== "세로 라인 2개" 게이트(DET 기준 비율) ==================
static const double DEFAULT_GATE_LX = 0.02;
static const double DEFAULT_GATE_RX = 0.98;

// ================== QR 디코드 튜닝 ==================
static const int QR_PAD_PX = 40;
static const int QR_DECODE_EVERY_N = 2;
static const int QR_MIN_SIZE_DET = 70;
static const int UPSCALE_TO = 500;

static inline int clampi(int v, int lo, int hi) { return max(lo, min(hi, v)); }

// ========================= Serial (POSIX) =========================
static speed_t BaudToSpeed(int baud)
{
    switch (baud) {
    case 9600: return B9600;
    case 19200: return B19200;
    case 38400: return B38400;
    case 57600: return B57600;
    case 115200: return B115200;
    default: return B115200;
    }
}

static int SerialOpen(const string& dev, int baud)
{
    int fd = open(dev.c_str(), O_RDWR | O_NOCTTY | O_SYNC);
    if (fd < 0) { perror("open(serial)"); return -1; }

    termios tty{};
    if (tcgetattr(fd, &tty) != 0) { perror("tcgetattr"); close(fd); return -1; }

    cfmakeraw(&tty);

    // ---- 8N1 강제 ----
    tty.c_cflag &= ~CSIZE;
    tty.c_cflag |= CS8;

    tty.c_cflag &= ~PARENB;   // parity off
    tty.c_cflag &= ~CSTOPB;   // 1 stop
    tty.c_cflag &= ~CRTSCTS;  // HW flow off
    tty.c_cflag |= (CLOCAL | CREAD);

    tty.c_iflag &= ~(IXON | IXOFF | IXANY); // SW flow off

    // read timeout (원하면 유지)
    tty.c_cc[VMIN] = 0;
    tty.c_cc[VTIME] = 1;

    speed_t spd = BaudToSpeed(baud);
    cfsetispeed(&tty, spd);
    cfsetospeed(&tty, spd);

    if (tcsetattr(fd, TCSANOW, &tty) != 0) { perror("tcsetattr"); close(fd); return -1; }

    tcflush(fd, TCIOFLUSH);
    return fd;
}

static bool SerialWriteLine(int fd, const string& line)
{
    if (fd < 0) return false;
    const string out = (!line.empty() && line.back() == '\n') ? line : (line + "\n");

    ssize_t n = write(fd, out.data(), out.size());
    if (n < 0) {
        perror("write(serial)");
        return false;
    }
    return (size_t)n == out.size();
}

// ========================= HTTP 전송 함수 =========================
static bool SendToServerHTTP(const string& ip, int port, int x, int y)
{
    CURL* curl = curl_easy_init();
    if (!curl) return false;

    // JSON 데이터 생성
    string jsonData = "{\"x\":" + to_string(x) + ",\"y\":" + to_string(y) + "}";
    string url = "http://" + ip + ":" + to_string(port) + "/qr";

    struct curl_slist* headers = NULL;
    headers = curl_slist_append(headers, "Content-Type: application/json");

    curl_easy_setopt(curl, CURLOPT_URL, url.c_str());
    curl_easy_setopt(curl, CURLOPT_POSTFIELDS, jsonData.c_str());
    curl_easy_setopt(curl, CURLOPT_HTTPHEADER, headers);
    curl_easy_setopt(curl, CURLOPT_TIMEOUT, 2L); // 2초 타임아웃

    CURLcode res = curl_easy_perform(curl);

    curl_slist_free_all(headers);
    curl_easy_cleanup(curl);

    return (res == CURLE_OK);
}

// ========================= QR helper =========================
static bool ValidateCorners(const Mat& corners4)
{
    if (corners4.empty() || corners4.total() != 4) return false;

    vector<Point2f> pts(4);
    for (int i = 0; i < 4; i++) pts[i] = corners4.at<Point2f>(i);

    for (int i = 0; i < 4; i++) {
        if (!isfinite(pts[i].x) || !isfinite(pts[i].y)) return false;
        for (int j = i + 1; j < 4; j++) {
            if (norm(pts[i] - pts[j]) < 1.0) return false;
        }
    }

    double a = fabs(contourArea(pts));
    if (a <= 200.0) return false;

    vector<Point> ip(4);
    for (int i = 0; i < 4; i++) ip[i] = Point((int)round(pts[i].x), (int)round(pts[i].y));
    if (!isContourConvex(ip)) return false;

    return true;
}

static vector<Point2f> OrderQuadTLTRBRBL(const vector<Point2f>& p)
{
    vector<Point2f> out(4);
    float minSum = 1e9f, maxSum = -1e9f, minDiff = 1e9f, maxDiff = -1e9f;
    int tl = 0, tr = 0, br = 0, bl = 0;

    for (int i = 0; i < 4; i++) {
        float s = p[i].x + p[i].y;
        float d = p[i].x - p[i].y;
        if (s < minSum) { minSum = s; tl = i; }
        if (s > maxSum) { maxSum = s; br = i; }
        if (d > maxDiff) { maxDiff = d; tr = i; }
        if (d < minDiff) { minDiff = d; bl = i; }
    }
    out[0] = p[tl]; out[1] = p[tr]; out[2] = p[br]; out[3] = p[bl];
    return out;
}

static Rect PointsToRect(const vector<Point2f>& pts, int maxW, int maxH)
{
    float minx = 1e9f, miny = 1e9f, maxx = -1e9f, maxy = -1e9f;
    for (auto& p : pts) {
        minx = min(minx, p.x); miny = min(miny, p.y);
        maxx = max(maxx, p.x); maxy = max(maxy, p.y);
    }
    int x = clampi((int)floor(minx), 0, maxW - 1);
    int y = clampi((int)floor(miny), 0, maxH - 1);
    int x2 = clampi((int)ceil(maxx), 0, maxW);
    int y2 = clampi((int)ceil(maxy), 0, maxH);
    return Rect(x, y, max(1, x2 - x), max(1, y2 - y));
}

static Mat CLAHE_Gray(const Mat& g)
{
    Ptr<CLAHE> c = createCLAHE(2.0, Size(8, 8));
    Mat out; c->apply(g, out);
    return out;
}

static Mat Sharpen(const Mat& g)
{
    Mat blur, out;
    GaussianBlur(g, blur, Size(0, 0), 1.0);
    addWeighted(g, 1.30, blur, -0.30, 0, out);
    return out;
}

static bool WarpWithPadding(const Mat& grayFull, const vector<Point2f>& quadFull, Mat& uprightOut)
{
    vector<Point2f> q = OrderQuadTLTRBRBL(quadFull);

    Rect r = PointsToRect(q, grayFull.cols, grayFull.rows);
    int side = max(r.width, r.height);
    side = clampi(side, 200, 900);

    int outSide = side + 2 * QR_PAD_PX;
    vector<Point2f> dst = {
        Point2f((float)QR_PAD_PX, (float)QR_PAD_PX),
        Point2f((float)(QR_PAD_PX + side - 1), (float)QR_PAD_PX),
        Point2f((float)(QR_PAD_PX + side - 1), (float)(QR_PAD_PX + side - 1)),
        Point2f((float)QR_PAD_PX, (float)(QR_PAD_PX + side - 1))
    };

    Mat Hm = getPerspectiveTransform(q, dst);
    if (Hm.empty()) return false;

    warpPerspective(grayFull, uprightOut, Hm, Size(outSide, outSide), INTER_LINEAR, BORDER_REPLICATE);
    return !uprightOut.empty();
}

static void DrawQuad(Mat& img, const vector<Point2f>& pts, const Scalar& color)
{
    if (pts.size() != 4) return;
    for (int i = 0; i < 4; i++) {
        line(img, pts[i], pts[(i + 1) % 4], color, 2);
        circle(img, pts[i], 4, color, FILLED);
    }
}

// "50,50" 파싱
static bool ParseXY_CSV(const string& s, int& x, int& y)
{
    string t;
    t.reserve(s.size());
    for (char c : s) if (!isspace((unsigned char)c)) t.push_back(c);

    size_t comma = t.find(',');
    if (comma == string::npos) return false;

    string sx = t.substr(0, comma);
    string sy = t.substr(comma + 1);
    if (sx.empty() || sy.empty()) return false;

    try {
        x = stoi(sx);
        y = stoi(sy);
        return true;
    }
    catch (...) {
        return false;
    }
}

// ---------------- 카메라 오픈: Raspberry Pi CSI는 libcamera(GStreamer) 권장 ----------------
static string BuildLibcameraPipeline(int w, int h, int fps)
{
    // appsink로 BGR로 들어오게 강제, drop=true sync=false 로 지연 최소화
    return string("libcamerasrc ! ")
        + "video/x-raw,width=" + to_string(w)
        + ",height=" + to_string(h)
        + ",framerate=" + to_string(fps) + "/1 ! "
        + "videoconvert ! video/x-raw,format=BGR ! "
        + "appsink drop=true sync=false";
}

static bool OpenCamera(VideoCapture& cap, const string& devPath, bool preferLibcamera)
{
    cap.release();

#ifdef _WIN32
    (void)devPath;
    if (!cap.open(0, CAP_DSHOW)) return false;
    cap.set(CAP_PROP_FRAME_WIDTH, CAP_W);
    cap.set(CAP_PROP_FRAME_HEIGHT, CAP_H);
    cap.set(CAP_PROP_FPS, 30);
    cap.set(CAP_PROP_BUFFERSIZE, 1);
#else
    if (preferLibcamera) {
        string pipe = BuildLibcameraPipeline(CAP_W, CAP_H, 30);
        if (!cap.open(pipe, CAP_GSTREAMER)) return false;
    }
    else {
        if (!cap.open(devPath, CAP_V4L2)) return false;
        cap.set(CAP_PROP_FRAME_WIDTH, CAP_W);
        cap.set(CAP_PROP_FRAME_HEIGHT, CAP_H);
        cap.set(CAP_PROP_FPS, 30);
        cap.set(CAP_PROP_BUFFERSIZE, 1);
        cap.set(CAP_PROP_CONVERT_RGB, 1);
    }
#endif

    Mat tmp;
    if (!cap.read(tmp) || tmp.empty()) return false;

    // 완전 흰 화면 의심(2프레임 연속 평균이 너무 높으면 실패로 간주 후 재오픈)
    Scalar m = mean(tmp);
    if (m[0] > 250 && m[1] > 250 && m[2] > 250) {
        Mat tmp2;
        if (cap.read(tmp2) && !tmp2.empty()) {
            Scalar m2 = mean(tmp2);
            if (m2[0] > 250 && m2[1] > 250 && m2[2] > 250) return false;
        }
    }
    return true;
}

int main(int argc, char** argv)
{
    cv::utils::logging::setLogLevel(cv::utils::logging::LOG_LEVEL_ERROR);

    // ===== 옵션 =====
    bool headless = false;
    string devPath = "/dev/video0";

    // flipCode: 0=상하, 1=좌우, -1=둘다
    // doFlip=true면 flipCode로 1회만 적용
    bool doFlip = true;
    int flipCode = 1; // 기본: 좌우 반전(원하신 피드백)

    double gateLX = DEFAULT_GATE_LX;
    double gateRX = DEFAULT_GATE_RX;

    bool preferLibcamera = true; // 기본: CSI(IMX219)면 libcamera 경로 권장

    // ===== Serial 옵션 =====
    string serialDev = "";
    int serialBaud = 115200;
    int serialFd = -1;

    // ===== HTTP 서버 설정 =====
    string serverIP = "192.168.0.8";  // 라즈베리파이 B IP
    int serverPort = 5000;

    for (int i = 1; i < argc; i++) {
        string a = argv[i];
        if (a == "--headless") headless = true;
        else if (a == "--dev" && i + 1 < argc) devPath = argv[++i];
        else if (a == "--flip" && i + 1 < argc) { flipCode = stoi(argv[++i]); doFlip = true; }
        else if (a == "--no-flip") doFlip = false;
        else if (a == "--gate" && i + 2 < argc) { gateLX = stod(argv[++i]); gateRX = stod(argv[++i]); }
        else if (a == "--v4l2") preferLibcamera = false;
        else if (a == "--serial" && i + 1 < argc) serialDev = argv[++i];
        else if (a == "--baud" && i + 1 < argc) serialBaud = stoi(argv[++i]);
        else if (a == "--server" && i + 1 < argc) serverIP = argv[++i];
        else if (a == "--port" && i + 1 < argc) serverPort = stoi(argv[++i]);
    }

    if (!(0.0 <= gateLX && gateLX < gateRX && gateRX <= 1.0)) {
        cerr << "Invalid gate range.\n";
        return 1;
    }

    // ===== Camera open with retry =====
    VideoCapture cap;
    bool opened = false;

    for (int t = 0; t < 5; t++) {
        if (OpenCamera(cap, devPath, preferLibcamera)) { opened = true; break; }
        cerr << "[WARN] camera open failed, retry " << (t + 1) << "/5\n";
        this_thread::sleep_for(chrono::milliseconds(300));
    }

    if (!opened) {
        cerr << "Camera open failed.\n";
        cerr << "If CSI(imx219) -> default(libcamera) recommended.\n";
        cerr << "If OpenCV has no GStreamer support, OpenCamera(libcamera) will fail.\n";
        cerr << "Try USB cam: --v4l2 --dev /dev/video0\n";
        return -1;
    }

    // ===== Serial open (optional) =====
    if (!serialDev.empty()) {
        serialFd = SerialOpen(serialDev, serialBaud);
        if (serialFd < 0) {
            cerr << "[WARN] Serial open failed: " << serialDev << " (continue without serial)\n";
        }
        else {
            cerr << "[OK] Serial opened: " << serialDev << " baud=" << serialBaud << "\n";
        }
    }

    // ===== Window =====
    if (!headless) {
        namedWindow("OPENCV_VIEW", WINDOW_NORMAL);
        resizeWindow("OPENCV_VIEW", 900, 600);
        moveWindow("OPENCV_VIEW", 20, 20);
    }

    QRCodeDetector qrd;
    int frameCount = 0;
    int lastX = INT_MIN, lastY = INT_MIN;
    int emptyStreak = 0;

    cout << "[INFO] HTTP Server: " << serverIP << ":" << serverPort << "\n";

    while (true) {
        Mat frameCap;
        if (!cap.read(frameCap) || frameCap.empty()) {
            emptyStreak++;
            cerr << "[DBG] empty frame streak=" << emptyStreak << "\n";

            if (emptyStreak >= 30) {
                cerr << "[WARN] too many empty frames. reopening camera...\n";
                bool ok = false;
                for (int t = 0; t < 5; t++) {
                    if (OpenCamera(cap, devPath, preferLibcamera)) { ok = true; break; }
                    this_thread::sleep_for(chrono::milliseconds(300));
                }
                emptyStreak = 0;
                if (!ok) {
                    cerr << "[ERR] reopen failed. exit.\n";
                    break;
                }
            }

            if (!headless) {
                Mat blank(DET_H, DET_W, CV_8UC3, Scalar(30, 30, 30));
                putText(blank, "EMPTY FRAME", Point(20, 60), FONT_HERSHEY_SIMPLEX, 1.2, Scalar(0, 0, 255), 2);
                imshow("OPENCV_VIEW", blank);
                int key = waitKey(30);
                if (key == 27 || key == 'q' || key == 'Q') break;
            }
            continue;
        }

        emptyStreak = 0;

        // ===== flip =====
        if (doFlip) flip(frameCap, frameCap, flipCode);

        // ===== DET scale =====
        Mat frameDet;
        resize(frameCap, frameDet, Size(DET_W, DET_H), 0, 0, INTER_LINEAR);

        Mat grayDet;
        cvtColor(frameDet, grayDet, COLOR_BGR2GRAY);

        int xL = clampi((int)round(gateLX * DET_W), 0, DET_W - 2);
        int xR = clampi((int)round(gateRX * DET_W), xL + 1, DET_W - 1);

        Rect gateRect(xL, 0, xR - xL, DET_H);
        Mat gateGray = grayDet(gateRect).clone();

        bool qrFound = false;
        vector<Point2f> quadDet;
        vector<Point2f> quadCap;
        string decodedRaw;

        try {
            Mat corners;
            bool ok = qrd.detect(gateGray, corners);
            if (ok && ValidateCorners(corners)) {
                qrFound = true;
                quadDet.resize(4);

                for (int i = 0; i < 4; i++) {
                    Point2f p = corners.at<Point2f>(i);
                    p.x += (float)gateRect.x;
                    p.y += (float)gateRect.y;
                    quadDet[i] = p;
                }

                Rect detRect = PointsToRect(quadDet, DET_W, DET_H);
                int detSize = min(detRect.width, detRect.height);

                float sx = (float)frameCap.cols / (float)DET_W;
                float sy = (float)frameCap.rows / (float)DET_H;

                quadCap.resize(4);
                for (int i = 0; i < 4; i++) {
                    quadCap[i] = Point2f(quadDet[i].x * sx, quadDet[i].y * sy);
                }

                frameCount++;
                if (frameCount % QR_DECODE_EVERY_N == 0 && detSize >= QR_MIN_SIZE_DET) {
                    Mat grayCap;
                    cvtColor(frameCap, grayCap, COLOR_BGR2GRAY);

                    Mat upright;
                    if (WarpWithPadding(grayCap, quadCap, upright)) {
                        if (upright.cols < UPSCALE_TO) {
                            Mat up2;
                            resize(upright, up2, Size(), 2.0, 2.0, INTER_LINEAR);
                            upright = up2;
                        }

                        Mat u1 = CLAHE_Gray(upright);
                        Mat u2 = Sharpen(u1);

                        Mat dc, st;
                        string d = qrd.detectAndDecode(u2, dc, st);
                        if (d.empty()) {
                            Mat dc2, st2;
                            d = qrd.detectAndDecodeCurved(u2, dc2, st2);
                        }
                        if (!d.empty()) decodedRaw = d;
                    }
                }
            }
        }
        catch (const cv::Exception&) {}

        // ===== 좌표 갱신 시 출력 + HTTP + 시리얼 전송 =====
        if (!decodedRaw.empty()) {
            int x = 0, y = 0;
            if (ParseXY_CSV(decodedRaw, x, y)) {
                if (x != lastX || y != lastY) {
                    string payload = to_string(x) + "," + to_string(y);

                    // 콘솔 출력
                    cout << "QR Decoded: " << decodedRaw << " -> (" << x << ", " << y << ")\n" << flush;

                    // HTTP 서버 전송
                    if (SendToServerHTTP(serverIP, serverPort, x, y)) {
                        cout << "[HTTP] ✓ Sent to " << serverIP << ":" << serverPort << "\n" << flush;
                    }
                    else {
                        cerr << "[HTTP] ✗ Failed to send\n";
                    }

                    // 시리얼 전송(옵션)
                    if (serialFd >= 0) {
                        if (!SerialWriteLine(serialFd, payload)) {
                            cerr << "[SERIAL] write failed\n";
                        }
                    }

                    lastX = x; lastY = y;
                }
            }
        }

        // ===== 시각화 =====
        if (!headless) {
            Mat vis = frameDet.clone();
            line(vis, Point(xL, 0), Point(xL, DET_H - 1), Scalar(255, 0, 0), 2);
            line(vis, Point(xR, 0), Point(xR, DET_H - 1), Scalar(255, 0, 0), 2);

            if (qrFound && quadDet.size() == 4) {
                DrawQuad(vis, quadDet, Scalar(0, 255, 0));
                putText(vis, "QR FOUND", Point(15, 35), FONT_HERSHEY_SIMPLEX, 1.0, Scalar(0, 255, 0), 2);
            }
            else {
                putText(vis, "QR NOT FOUND", Point(15, 35), FONT_HERSHEY_SIMPLEX, 1.0, Scalar(0, 0, 255), 2);
            }

            if (!decodedRaw.empty()) {
                string show = decodedRaw;
                if ((int)show.size() > 40) show = show.substr(0, 40) + "...";
                putText(vis, "Decoded: " + show, Point(15, DET_H - 20),
                    FONT_HERSHEY_SIMPLEX, 0.65, Scalar(0, 255, 0), 2);
            }
            else {
                putText(vis, "Decoded: (none)", Point(15, DET_H - 20),
                    FONT_HERSHEY_SIMPLEX, 0.65, Scalar(0, 0, 255), 2);
            }

            imshow("OPENCV_VIEW", vis);
            int key = waitKey(1);
            if (key == 27 || key == 'q' || key == 'Q') break;
        }
    }

    if (serialFd >= 0) close(serialFd);
    return 0;
}

*/