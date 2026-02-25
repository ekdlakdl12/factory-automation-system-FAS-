/*
    ============================================================
    [Raspberry Pi A] QR 인식 → "x,y" 시리얼 전송 전용 프로그램
    ============================================================

    ✅ 역할(최종 확정)
    - Raspberry Pi A: 카메라로 QR 코드를 탐지/디코드한다.
    - QR 내부 문자열이 "x,y" 형태일 때만 추출한다.
    - 추출한 좌표를 Raspberry Pi B로 시리얼로 전송한다.
      전송 포맷은 반드시 아래와 같이 "한 줄"이다.

        "x,y\n"

    - 네트워크/HTTP/curl 등은 사용하지 않는다.
    - Raspberry Pi B는 이 값을 받아 qr.json(최신화/덮어쓰기)만 담당한다.
      이후 작업은 다른 팀원이 B의 qr.json을 읽어서 진행한다.

    ✅ 옵션
    - 화면 출력(윈도우) 켜고/끄기 가능 (--headless)
    - 카메라 입력 경로 선택
        * CSI 카메라(libcamera + GStreamer) 경로: 기본값
        * USB 카메라(V4L2) 경로: --v4l2 옵션으로 전환
    - flip(좌우/상하 반전) 설정 가능
    - 게이트(탐지 영역 제한) 범위 조절 가능

    ✅ 빌드 예시
    g++ A_qr_to_serial_commented.cpp -o A_qr_to_serial `pkg-config --cflags --libs opencv4`

    ✅ 실행 예시
    ./A_qr_to_serial --serial /dev/serial0 --baud 115200
    ./A_qr_to_serial --headless --serial /dev/serial0 --baud 115200
    ./A_qr_to_serial --v4l2 --dev /dev/video0 --serial /dev/serial0 --baud 115200

    !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!  재부팅시 자동 실행하게 끔 설정 완료  !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
*/


// 윈도우 OS 실행 X
// 라즈베리파이 전용


// 만약 코드 수정이 필요하다면 ----->  /home/allday-project/qr_xy/main.cpp 경로에 있는 main.cpp 파일을 수정하면 됨
// 수정 방법은 VSCODE에서 SSH로 라즈베리파이에 접속한 후 main.cpp 파일을 열어서 수정하면 됨








#include <opencv2/opencv.hpp>
#include <opencv2/objdetect.hpp>
#include <opencv2/core/utils/logger.hpp>

#include <fcntl.h>
#include <unistd.h>
#include <termios.h>

#include <iostream>
#include <vector>
#include <algorithm>
#include <cmath>
#include <string>
#include <cctype>
#include <climits>
#include <thread>
#include <chrono>
/*
using namespace std;
using namespace cv;

// ============================================================
// 1) 카메라 해상도 설정
// ============================================================
// CAP_* : 실제 QR 디코드(정확도)용 원본 프레임 크기
static const int CAP_W = 1280;
static const int CAP_H = 720;

// DET_* : QR "탐지(detect)" 속도를 위해 다운스케일한 프레임 크기
// detect는 빠른 대신 대략적이므로 작은 사이즈로 돌린 뒤,
// 디코드는 CAP 원본에서 워핑하여 수행한다.
static const int DET_W = 640;
static const int DET_H = 360;

// ============================================================
// 2) "게이트(gate)" 설정
// ============================================================
// 컨베이어/카메라 환경에서 QR이 화면 전체에 나타나지 않고
// 보통 중앙/특정 구간을 지나기 때문에 탐지 범위를 줄이면 안정성이 올라간다.
// gateLX~gateRX는 DET_W 기준 비율(0.0~1.0)로 가로 영역을 제한한다.
static const double DEFAULT_GATE_LX = 0.02;
static const double DEFAULT_GATE_RX = 0.98;

// ============================================================
// 3) QR 디코드 튜닝 파라미터
// ============================================================
// QR_PAD_PX : 워핑 시 주변 여백을 주어 디코드 안정성을 올림
static const int QR_PAD_PX = 40;

// QR_DECODE_EVERY_N : 매 프레임 디코드하면 느리므로 N프레임마다 디코드
static const int QR_DECODE_EVERY_N = 2;

// QR_MIN_SIZE_DET : DET 프레임에서 QR이 너무 작으면 디코드 시도 자체를 하지 않음
static const int QR_MIN_SIZE_DET = 70;

// UPSCALE_TO : 워핑된 QR 이미지가 너무 작으면 확대해서 디코드 안정성 향상
static const int UPSCALE_TO = 500;

// clamp helper: 범위를 벗어난 값을 강제로 끼워 넣기
static inline int clampi(int v, int lo, int hi) { return max(lo, min(hi, v)); }

// ============================================================
// 4) 시리얼 통신(POSIX) 유틸 함수들
// ============================================================

/*
    BaudToSpeed:
    - 사람이 쓰는 baud 숫자를 termios 상수(B115200 등)로 변환한다.
*/
/*
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

/*
    SerialOpen:
    - dev: "/dev/serial0" 또는 "/dev/ttyUSB0" 등
    - baud: 115200 등
    - 반환: 성공 시 fd(파일 디스크립터), 실패 시 -1

    세팅 내용(중요):
    - Raw 모드: cfmakeraw
    - 8N1 (8bit, No parity, 1 stop bit)
    - HW/SW flow control OFF (CRTSCTS, IXON/IXOFF off)
*/

static int SerialOpen(const string& dev, int baud)
{
    int fd = open(dev.c_str(), O_RDWR | O_NOCTTY | O_SYNC);
    if (fd < 0) { perror("open(serial)"); return -1; }

    termios tty{};
    if (tcgetattr(fd, &tty) != 0) { perror("tcgetattr"); close(fd); return -1; }

    // raw 모드(캐노니컬/에코/특수키 처리 등 제거)
    cfmakeraw(&tty);

    // ---- 8N1 강제 ----
    tty.c_cflag &= ~CSIZE;
    tty.c_cflag |= CS8;

    tty.c_cflag &= ~PARENB;   // parity off
    tty.c_cflag &= ~CSTOPB;   // 1 stop bit
    tty.c_cflag &= ~CRTSCTS;  // HW flow off
    tty.c_cflag |= (CLOCAL | CREAD);

    tty.c_iflag &= ~(IXON | IXOFF | IXANY); // SW flow off

    // read timeout 설정(읽기에서 0.1초 대기)
    // A는 송신만 쓰지만, 안정적 동일 세팅 유지
    tty.c_cc[VMIN] = 0;
    tty.c_cc[VTIME] = 1;

    speed_t spd = BaudToSpeed(baud);
    cfsetispeed(&tty, spd);
    cfsetospeed(&tty, spd);

    if (tcsetattr(fd, TCSANOW, &tty) != 0) { perror("tcsetattr"); close(fd); return -1; }

    tcflush(fd, TCIOFLUSH);
    return fd;
}

/*
    SerialWriteLine:
    - line을 시리얼로 보낸다.
    - 반드시 개행('\n')으로 끝나게 해서 B가 "줄 단위"로 파싱하기 쉽게 만든다.
*/

static bool SerialWriteLine(int fd, const string& line)
{
    if (fd < 0) return false;

    // line이 '\n'으로 끝나지 않으면 붙여준다.
    const string out = (!line.empty() && line.back() == '\n') ? line : (line + "\n");

    ssize_t n = write(fd, out.data(), out.size());
    if (n < 0) {
        perror("write(serial)");
        return false;
    }
    return (size_t)n == out.size();
}

// ============================================================
// 5) QR 탐지/디코드 안정성용 보조 함수들
// ============================================================

/*
    ValidateCorners:
    - QRCodeDetector::detect()가 주는 corners(4점)가 유효한지 점검한다.
    - 너무 작은 면적/점 겹침/비정상 좌표이면 false로 처리하여 오탐 방지.
*/
static bool ValidateCorners(const Mat& corners4)
{
    if (corners4.empty() || corners4.total() != 4) return false;

    vector<Point2f> pts(4);
    for (int i = 0; i < 4; i++) pts[i] = corners4.at<Point2f>(i);

    // 좌표 유효성(무한대/NaN) + 점 간 거리 체크
    for (int i = 0; i < 4; i++) {
        if (!isfinite(pts[i].x) || !isfinite(pts[i].y)) return false;
        for (int j = i + 1; j < 4; j++) {
            if (norm(pts[i] - pts[j]) < 1.0) return false; // 거의 같은 점이면 invalid
        }
    }

    // 면적 너무 작으면 QR로 보기 어려움
    double a = fabs(contourArea(pts));
    if (a <= 200.0) return false;

    // convex 여부 체크
    vector<Point> ip(4);
    for (int i = 0; i < 4; i++) ip[i] = Point((int)round(pts[i].x), (int)round(pts[i].y));
    if (!isContourConvex(ip)) return false;

    return true;
}

/*
    OrderQuadTLTRBRBL:
    - QR 4개 코너를 (TL, TR, BR, BL) 순서로 정렬한다.
    - perspective warp가 안정적으로 동작하게 하기 위함.
*/
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

/*
    PointsToRect:
    - 4점 bounding box를 Rect로 변환한다.
    - 이미지 범위를 넘어가지 않도록 clamp 한다.
*/
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

/*
    CLAHE_Gray:
    - 조명 변화가 심할 때 대비 향상(국부 히스토그램 평활화)
*/
static Mat CLAHE_Gray(const Mat& g)
{
    Ptr<CLAHE> c = createCLAHE(2.0, Size(8, 8));
    Mat out; c->apply(g, out);
    return out;
}

/*
    Sharpen:
    - 살짝 샤프닝해서 QR 모서리/패턴이 선명해지도록 함
*/
static Mat Sharpen(const Mat& g)
{
    Mat blur, out;
    GaussianBlur(g, blur, Size(0, 0), 1.0);
    addWeighted(g, 1.30, blur, -0.30, 0, out);
    return out;
}

/*
    WarpWithPadding:
    - 원본(grayFull)에서 QR 사각형 영역을 정면으로 펴(upright) 디코드 안정성 개선
    - QR_PAD_PX 만큼 여백을 주어 코드 경계가 잘리지 않게 함
*/
static bool WarpWithPadding(const Mat& grayFull, const vector<Point2f>& quadFull, Mat& uprightOut)
{
    vector<Point2f> q = OrderQuadTLTRBRBL(quadFull);

    Rect r = PointsToRect(q, grayFull.cols, grayFull.rows);
    int side = max(r.width, r.height);
    side = clampi(side, 200, 900);

    int outSide = side + 2 * QR_PAD_PX;

    // 목적지 사각형(정면) 좌표
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

/*
    DrawQuad:
    - 디버그 시각화용(사각형/코너 점 표시)
*/
static void DrawQuad(Mat& img, const vector<Point2f>& pts, const Scalar& color)
{
    if (pts.size() != 4) return;
    for (int i = 0; i < 4; i++) {
        line(img, pts[i], pts[(i + 1) % 4], color, 2);
        circle(img, pts[i], 4, color, FILLED);
    }
}

/*
    ParseXY_CSV:
    - QR 디코드 결과 문자열이 "x,y"인지 확인하고 숫자로 파싱
    - 예: "100,2" / " 2,133 " 가능
    - 실패하면 false (즉, QR 내용이 원하는 포맷이 아니면 전송하지 않음)
*/
static bool ParseXY_CSV(const string& s, int& x, int& y)
{
    // 공백 제거
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

// ============================================================
// 6) 카메라 오픈 (CSI/libcamera vs USB/V4L2)
// ============================================================

/*
    BuildLibcameraPipeline:
    - Raspberry Pi CSI 카메라는 libcamera 기반으로 GStreamer pipeline을 쓰는 것이 흔함.
    - appsink drop=true, sync=false로 지연을 줄임.
*/
static string BuildLibcameraPipeline(int w, int h, int fps)
{
    return string("libcamerasrc ! ")
        + "video/x-raw,width=" + to_string(w)
        + ",height=" + to_string(h)
        + ",framerate=" + to_string(fps) + "/1 ! "
        + "videoconvert ! video/x-raw,format=BGR ! "
        + "appsink drop=true sync=false";
}

/*
    OpenCamera:
    - preferLibcamera=true  : libcamera(GStreamer) 경로로 열기(기본 권장)
    - preferLibcamera=false : V4L2(/dev/video0) 경로로 열기(USB 카메라용)

    카메라가 완전 흰 화면(노출/연결 문제)만 주는 경우를 2프레임 체크하여 실패 처리.
*/
static bool OpenCamera(VideoCapture& cap, const string& devPath, bool preferLibcamera)
{
    cap.release();

#ifdef _WIN32
    // Windows는 여기 관심 없음(라즈베리파이용이므로)
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

    // 완전 흰 화면 의심(2프레임 연속 평균이 너무 높으면 실패로 간주)
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

// ============================================================
// 7) main: 전체 실행 루프
// ============================================================

int main(int argc, char** argv)
{
    // OpenCV 내부 로그를 에러만 출력하게(불필요한 경고 줄이기)
    cv::utils::logging::setLogLevel(cv::utils::logging::LOG_LEVEL_ERROR);

    // ------------------------------------------------------------
    // [런타임 옵션 기본값]
    // ------------------------------------------------------------
    bool headless = false;            // true면 imshow 창을 띄우지 않음(현장 헤드리스)
    string devPath = "/dev/video0";   // V4L2 카메라 경로(USB 카메라일 때)

    // flip: 카메라가 좌우/상하 반전되어 들어오는 경우 수정용
    // flipCode: 0=상하, 1=좌우, -1=둘다
    bool doFlip = true;
    int flipCode = 1;                // 기본값: 좌우 반전

    // gate 범위
    double gateLX = DEFAULT_GATE_LX;
    double gateRX = DEFAULT_GATE_RX;

    // preferLibcamera=true: CSI 카메라(libcamera+GStreamer) 경로 사용
    bool preferLibcamera = true;

    // 시리얼 설정(기본값: 라즈베리파이 UART)
    string serialDev = "/dev/serial0";  // USB-TTL이면 /dev/ttyUSB0 등으로 변경
    int serialBaud = 115200;
    int serialFd = -1;

    // ------------------------------------------------------------
    // [옵션 파싱]
    // ------------------------------------------------------------
    // 사용 예:
    // --headless
    // --dev /dev/video0
    // --flip 1
    // --no-flip
    // --gate 0.02 0.98
    // --v4l2
    // --serial /dev/serial0
    // --baud 115200
    for (int i = 1; i < argc; i++) {
        string a = argv[i];
        if (a == "--headless") headless = true;
        else if (a == "--dev" && i + 1 < argc) devPath = argv[++i];
        else if (a == "--flip" && i + 1 < argc) { flipCode = stoi(argv[++i]); doFlip = true; }
        else if (a == "--no-flip") doFlip = false;
        else if (a == "--gate" && i + 2 < argc) { gateLX = stod(argv[++i]); gateRX = stod(argv[++i]); }
        else if (a == "--v4l2") preferLibcamera = false; // USB/V4L2로 강제
        else if (a == "--serial" && i + 1 < argc) serialDev = argv[++i];
        else if (a == "--baud" && i + 1 < argc) serialBaud = stoi(argv[++i]);
    }

    // gate 값 유효성 체크
    if (!(0.0 <= gateLX && gateLX < gateRX && gateRX <= 1.0)) {
        cerr << "Invalid gate range.\n";
        return 1;
    }

    // ------------------------------------------------------------
    // [카메라 오픈: 실패 시 재시도]
    // ------------------------------------------------------------
    VideoCapture cap;
    bool opened = false;

    for (int t = 0; t < 5; t++) {
        if (OpenCamera(cap, devPath, preferLibcamera)) { opened = true; break; }
        cerr << "[WARN] camera open failed, retry " << (t + 1) << "/5\n";
        this_thread::sleep_for(chrono::milliseconds(300));
    }

    if (!opened) {
        cerr << "Camera open failed.\n";
        cerr << "CSI(imx219 등) -> 기본(libcamera) 경로 권장\n";
        cerr << "USB 카메라면: --v4l2 --dev /dev/video0\n";
        return -1;
    }

    // ------------------------------------------------------------
    // [시리얼 오픈: 실패하면 프로그램 종료]
    // ------------------------------------------------------------
    serialFd = SerialOpen(serialDev, serialBaud);
    if (serialFd < 0) {
        cerr << "[ERR] Serial open failed: " << serialDev << "\n";
        return -1;
    }
    cerr << "[OK] Serial opened: " << serialDev << " baud=" << serialBaud << "\n";

    // ------------------------------------------------------------
    // [시각화 창 설정: headless가 아니면 창을 띄움]
    // ------------------------------------------------------------
    if (!headless) {
        namedWindow("OPENCV_VIEW", WINDOW_NORMAL);
        resizeWindow("OPENCV_VIEW", 900, 600);
        moveWindow("OPENCV_VIEW", 20, 20);
    }

    // OpenCV QR detector 객체
    QRCodeDetector qrd;

    // frameCount: 디코드를 매 프레임 하지 않고 N프레임마다 하기 위해 사용
    int frameCount = 0;

    // lastX,lastY: 같은 값이 계속 들어오면 시리얼 전송을 반복하지 않기 위함(중복 억제)
    int lastX = INT_MIN, lastY = INT_MIN;

    // empty frame 처리(카메라 glitch 대비)
    int emptyStreak = 0;

    // ------------------------------------------------------------
    // [메인 루프]
    // ------------------------------------------------------------
    while (true) {
        Mat frameCap;

        // 1) 프레임 읽기
        if (!cap.read(frameCap) || frameCap.empty()) {
            emptyStreak++;

            // 연속으로 빈 프레임이 많으면 카메라 재오픈 시도
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

            // 화면 모드라면 디버그 표시
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

        // 2) 필요시 반전 보정
        if (doFlip) flip(frameCap, frameCap, flipCode);

        // 3) 탐지용 프레임으로 다운스케일(속도)
        Mat frameDet;
        resize(frameCap, frameDet, Size(DET_W, DET_H), 0, 0, INTER_LINEAR);

        // 4) grayscale
        Mat grayDet;
        cvtColor(frameDet, grayDet, COLOR_BGR2GRAY);

        // 5) gate 영역 계산 (DET 기준)
        int xL = clampi((int)round(gateLX * DET_W), 0, DET_W - 2);
        int xR = clampi((int)round(gateRX * DET_W), xL + 1, DET_W - 1);

        Rect gateRect(xL, 0, xR - xL, DET_H);
        Mat gateGray = grayDet(gateRect).clone();

        // 6) QR 탐지/디코드 관련 변수
        bool qrFound = false;
        vector<Point2f> quadDet;
        vector<Point2f> quadCap;
        string decodedRaw;

        // --------------------------------------------------------
        // 7) QR detect (빠르게) → 필요 시 decode (느리게)
        // --------------------------------------------------------
        try {
            Mat corners;
            bool ok = qrd.detect(gateGray, corners);

            if (ok && ValidateCorners(corners)) {
                qrFound = true;
                quadDet.resize(4);

                // corners는 gateGray 기준 좌표이므로, 원래 DET 좌표로 되돌리기 위해 x offset을 더한다.
                for (int i = 0; i < 4; i++) {
                    Point2f p = corners.at<Point2f>(i);
                    p.x += (float)gateRect.x;
                    p.y += (float)gateRect.y;
                    quadDet[i] = p;
                }

                // QR 크기 체크(DET 기준)
                Rect detRect = PointsToRect(quadDet, DET_W, DET_H);
                int detSize = min(detRect.width, detRect.height);

                // DET 좌표를 CAP(원본) 좌표로 스케일 변환
                float sx = (float)frameCap.cols / (float)DET_W;
                float sy = (float)frameCap.rows / (float)DET_H;

                quadCap.resize(4);
                for (int i = 0; i < 4; i++) {
                    quadCap[i] = Point2f(quadDet[i].x * sx, quadDet[i].y * sy);
                }

                // N프레임마다 + 충분히 큰 QR일 때만 디코드 시도
                frameCount++;
                if (frameCount % QR_DECODE_EVERY_N == 0 && detSize >= QR_MIN_SIZE_DET) {
                    // 원본 프레임을 gray로 만들고 워핑 후 디코드
                    Mat grayCap;
                    cvtColor(frameCap, grayCap, COLOR_BGR2GRAY);

                    Mat upright;
                    if (WarpWithPadding(grayCap, quadCap, upright)) {
                        // 워핑 결과가 너무 작으면 확대
                        if (upright.cols < UPSCALE_TO) {
                            Mat up2;
                            resize(upright, up2, Size(), 2.0, 2.0, INTER_LINEAR);
                            upright = up2;
                        }

                        // 대비/선명도 보정
                        Mat u1 = CLAHE_Gray(upright);
                        Mat u2 = Sharpen(u1);

                        // 일반 QR 디코드
                        Mat dc, st;
                        string d = qrd.detectAndDecode(u2, dc, st);

                        // curved(곡면) QR을 위한 fallback
                        if (d.empty()) {
                            Mat dc2, st2;
                            d = qrd.detectAndDecodeCurved(u2, dc2, st2);
                        }

                        if (!d.empty()) decodedRaw = d;
                    }
                }
            }
        }
        catch (const cv::Exception&) {
            // OpenCV 내부 예외는 프레임 단위로 무시하고 계속 진행
        }

        // --------------------------------------------------------
        // 8) 디코드 성공 시: "x,y" 형태인지 확인 후 시리얼 전송
        // --------------------------------------------------------
        if (!decodedRaw.empty()) {
            int x = 0, y = 0;

            // QR 내부 문자열이 "x,y" 포맷이면 파싱 성공
            if (ParseXY_CSV(decodedRaw, x, y)) {

                // 중복 억제:
                // 같은 값이 반복되는 경우(같은 QR이 계속 화면에 있을 때),
                // 매 프레임 전송하면 B에서 JSON이 계속 덮어써져 불필요 IO가 증가한다.
                if (x != lastX || y != lastY) {

                    // 시리얼로 보낼 payload는 반드시 "x,y" 문자열
                    string payload = to_string(x) + "," + to_string(y);

                    // 콘솔 로그(팀 디버깅용)
                    cout << "QR: " << decodedRaw << " -> (" << x << ", " << y << ")\n" << flush;

                    // 시리얼 전송: 반드시 payload + "\n"
                    if (!SerialWriteLine(serialFd, payload)) {
                        cerr << "[SERIAL] write failed\n";
                    }
                    else {
                        cout << "[SERIAL] sent: " << payload << "\n" << flush;
                    }

                    // 마지막 전송값 업데이트
                    lastX = x;
                    lastY = y;
                }
            }
            else {
                // QR을 읽긴 했는데 "x,y" 포맷이 아닌 경우
                // 요구사항상 이런 데이터는 B로 보내면 안 되므로 무시한다.
                // 필요하면 아래 로그를 켜서 디버깅 가능:
                // cerr << "[WARN] decoded but not x,y: " << decodedRaw << "\n";
            }
        }

        // --------------------------------------------------------
        // 9) 시각화(옵션): headless가 아니면 화면 표시
        // --------------------------------------------------------
        if (!headless) {
            Mat vis = frameDet.clone();

            // gate 라인 표시(파란색)
            line(vis, Point(xL, 0), Point(xL, DET_H - 1), Scalar(255, 0, 0), 2);
            line(vis, Point(xR, 0), Point(xR, DET_H - 1), Scalar(255, 0, 0), 2);

            // QR 탐지 사각형 표시
            if (qrFound && quadDet.size() == 4) {
                DrawQuad(vis, quadDet, Scalar(0, 255, 0));
                putText(vis, "QR FOUND", Point(15, 35), FONT_HERSHEY_SIMPLEX, 1.0, Scalar(0, 255, 0), 2);
            }
            else {
                putText(vis, "QR NOT FOUND", Point(15, 35), FONT_HERSHEY_SIMPLEX, 1.0, Scalar(0, 0, 255), 2);
            }

            // 디코드 문자열 표시(너무 길면 자르기)
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

            // 화면 출력
            imshow("OPENCV_VIEW", vis);

            // 키 입력: ESC/q/Q로 종료
            int key = waitKey(1);
            if (key == 27 || key == 'q' || key == 'Q') break;
        }
    }

    // ------------------------------------------------------------
    // 10) 종료 처리
    // ------------------------------------------------------------
    if (serialFd >= 0) close(serialFd);
    return 0;
}
