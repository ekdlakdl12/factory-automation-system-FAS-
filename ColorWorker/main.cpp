#include <opencv2/opencv.hpp>
#include <iostream>
#include <vector>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <cstdio>
#include <cctype>
#include <filesystem>
#include <cerrno>
#include <chrono>
#include <thread>
#include <cstdlib>
#include <winsock2.h>
#include <ws2tcpip.h>

#pragma comment(lib, "ws2_32.lib")

using namespace cv;
using namespace std;

// =====================
// USER CONFIG
// =====================
static int  DEVICE_INDEX = 1;
static bool USE_DSHOW = true;
static int  CAM_W = 1280;
static int  CAM_H = 720;

// Modbus TCP Configuration
static const char* PLC_IP = "192.168.0.202";
static const int   PLC_PORT = 502;
static const int COIL_TRIGGER = 100;
static const int COIL_RED = 101;
static const int COIL_GREEN = 102;
static const int COIL_BLUE = 103;
static const int COIL_NONE = 104;

// Project paths
static const string CAPTURE_DIR = "./captures";
static const string HISTORY_JSON = "./color_history.json";
static const string CURRENT_JSON = "./color_current.json";

// Object ROI detection tuning
static double MIN_CONTOUR_AREA = 2000.0;
static int ROI_PAD = 10;

// Color decision tuning
static int MIN_COLOR_PIXELS = 100;
static double MIN_COLOR_RATIO = 0.01;

// =====================
// Modbus TCP Communication
// =====================
class ModbusTcpClient {
private:
    SOCKET sock;
    bool connected;
    int transactionId;

public:
    ModbusTcpClient() : sock(INVALID_SOCKET), connected(false), transactionId(0) {
        WSADATA wsaData;
        if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0) {
            cerr << "[MODBUS] WSAStartup failed\n";
        }
    }

    ~ModbusTcpClient() {
        Disconnect();
        WSACleanup();
    }

    bool Connect(const char* ip, int port) {
        if (connected) return true;

        sock = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
        if (sock == INVALID_SOCKET) {
            cerr << "[MODBUS] socket creation failed\n";
            return false;
        }

        sockaddr_in serverAddr{};
        serverAddr.sin_family = AF_INET;
        serverAddr.sin_port = htons(port);

        if (inet_pton(AF_INET, ip, &serverAddr.sin_addr) <= 0) {
            cerr << "[MODBUS] Invalid IP address\n";
            closesocket(sock);
            return false;
        }

        if (::connect(sock, (struct sockaddr*)&serverAddr, sizeof(serverAddr)) == SOCKET_ERROR) {
            cerr << "[MODBUS] Connection failed to " << ip << ":" << port << "\n";
            closesocket(sock);
            sock = INVALID_SOCKET;
            return false;
        }

        connected = true;
        cout << "[MODBUS] Connected to " << ip << ":" << port << "\n";
        return true;
    }

    void Disconnect() {
        if (sock != INVALID_SOCKET) {
            closesocket(sock);
            sock = INVALID_SOCKET;
        }
        connected = false;
    }

    bool IsConnected() const {
        return connected;
    }

    bool ReadCoil(int coilAddress, bool& outValue) {
        if (!connected) {
            if (!Connect(PLC_IP, PLC_PORT)) {
                return false;
            }
        }

        transactionId++;
        unsigned char request[12];
        int idx = 0;

        request[idx++] = (transactionId >> 8) & 0xFF;
        request[idx++] = transactionId & 0xFF;
        request[idx++] = 0x00;
        request[idx++] = 0x00;
        request[idx++] = 0x00;
        request[idx++] = 0x06;

        request[idx++] = 0x01;
        request[idx++] = 0x01;
        request[idx++] = (coilAddress >> 8) & 0xFF;
        request[idx++] = coilAddress & 0xFF;
        request[idx++] = 0x00;
        request[idx++] = 0x01;

        if (send(sock, (const char*)request, idx, 0) == SOCKET_ERROR) {
            cerr << "[MODBUS] Send failed\n";
            Disconnect();
            return false;
        }

        unsigned char response[20];
        int recvLen = recv(sock, (char*)response, sizeof(response), 0);
        if (recvLen <= 0) {
            cerr << "[MODBUS] Receive failed\n";
            Disconnect();
            return false;
        }

        if (recvLen >= 9) {
            outValue = (response[7] & 0x01) != 0;
            return true;
        }

        return false;
    }

    bool WriteCoil(int coilAddress, bool value) {
        if (!connected) {
            if (!Connect(PLC_IP, PLC_PORT)) {
                return false;
            }
        }

        transactionId++;
        unsigned char request[12];
        int idx = 0;

        request[idx++] = (transactionId >> 8) & 0xFF;
        request[idx++] = transactionId & 0xFF;
        request[idx++] = 0x00;
        request[idx++] = 0x00;
        request[idx++] = 0x00;
        request[idx++] = 0x06;

        request[idx++] = 0x01;
        request[idx++] = 0x05;
        request[idx++] = (coilAddress >> 8) & 0xFF;
        request[idx++] = coilAddress & 0xFF;
        request[idx++] = value ? 0xFF : 0x00;
        request[idx++] = 0x00;

        if (send(sock, (const char*)request, idx, 0) == SOCKET_ERROR) {
            cerr << "[MODBUS] Send failed\n";
            Disconnect();
            return false;
        }

        unsigned char response[12];
        int recvLen = recv(sock, (char*)response, sizeof(response), 0);
        if (recvLen <= 0) {
            cerr << "[MODBUS] Receive failed\n";
            Disconnect();
            return false;
        }

        cout << "[MODBUS] Write Coil " << coilAddress << " = " << (value ? "ON" : "OFF") << "\n";
        return true;
    }
};

// =====================
// File helpers
// =====================
static bool WriteTextFileAtomic(const string& path, const string& text)
{
    string tmp = path + ".tmp";
    {
        ofstream ofs(tmp, ios::out | ios::trunc);
        if (!ofs.is_open()) return false;
        ofs << text;
        ofs.close();
    }
    ::remove(path.c_str());
    if (::rename(tmp.c_str(), path.c_str()) != 0) {
        ofstream ofs(path, ios::out | ios::trunc);
        if (!ofs.is_open()) { ::remove(tmp.c_str()); return false; }
        ofs << text;
        ofs.close();
        ::remove(tmp.c_str());
        return true;
    }
    return true;
}

static bool EnsureJsonArrayFile(const string& path)
{
    ifstream ifs(path);
    if (ifs.is_open()) return true;
    return WriteTextFileAtomic(path, "[]\n");
}

static bool AppendJsonArray(const string& path, const string& recordJson)
{
    string content;
    {
        ifstream ifs(path, ios::in);
        if (ifs.is_open()) {
            ostringstream ss;
            ss << ifs.rdbuf();
            content = ss.str();
            ifs.close();
        }
        else content = "[]";
    }

    auto rtrim = [&](string& s) { while (!s.empty() && isspace((unsigned char)s.back())) s.pop_back(); };
    auto ltrim = [&](string& s) { size_t i = 0; while (i < s.size() && isspace((unsigned char)s[i])) i++; s.erase(0, i); };

    rtrim(content); ltrim(content);
    if (content.empty()) content = "[]";
    if (content.front() != '[' || content.back() != ']') content = "[]";

    bool hasAny = false;
    for (size_t i = 1; i + 1 < content.size(); i++) {
        if (content[i] == '{') { hasAny = true; break; }
    }

    string out;
    if (!hasAny) {
        out = "[\n" + recordJson + "\n]\n";
    }
    else {
        out = content.substr(0, content.size() - 1);
        rtrim(out);
        if (!out.empty() && out.back() != '[') out += ",";
        out += "\n" + recordJson + "\n]\n";
    }

    return WriteTextFileAtomic(path, out);
}

static string NowTimeString()
{
    using namespace chrono;

    auto now = system_clock::now();
    auto ms = duration_cast<milliseconds>(now.time_since_epoch()) % 1000;

    time_t t = system_clock::to_time_t(now);
    tm lt{};

#ifdef _WIN32
    localtime_s(&lt, &t);
#else
    lt = *localtime(&t);
#endif

    ostringstream ss;
    ss << put_time(&lt, "%Y-%m-%d %H:%M:%S")
        << "." << setw(3) << setfill('0') << (int)ms.count();
    return ss.str();
}

static inline string NowFileStamp()
{
    using clock = chrono::system_clock;
    auto now = clock::now();
    auto ms = chrono::duration_cast<chrono::milliseconds>(now.time_since_epoch()) % 1000;

    time_t tt = clock::to_time_t(now);
    tm tmLocal{};
#ifdef _WIN32
    localtime_s(&tmLocal, &tt);
#else
    localtime_r(&tt, &tmLocal);
#endif

    ostringstream ss;
    ss << put_time(&tmLocal, "%Y%m%d_%H%M%S")
        << '_' << setw(3) << setfill('0') << (int)ms.count();
    return ss.str();
}

// =====================
// HSV thresholds
// =====================
struct HsvRange {
    Scalar L;
    Scalar U;
};

struct ColorThresholds {
    HsvRange R1{ Scalar(0,   60,  40),  Scalar(12,  255, 255) };
    HsvRange R2{ Scalar(168, 60,  40),  Scalar(179, 255, 255) };
    HsvRange G{ Scalar(30,  40,  40),  Scalar(95,  255, 255) };
    HsvRange B{ Scalar(85,  40,  40),  Scalar(140, 255, 255) };
};

static void BuildMasksRGB(const Mat& hsv, Mat& maskR, Mat& maskG, Mat& maskB, const ColorThresholds& th)
{
    Mat rA, rB;
    inRange(hsv, th.R1.L, th.R1.U, rA);
    inRange(hsv, th.R2.L, th.R2.U, rB);
    maskR = rA | rB;

    inRange(hsv, th.G.L, th.G.U, maskG);
    inRange(hsv, th.B.L, th.B.U, maskB);

    Mat k = getStructuringElement(MORPH_RECT, Size(5, 5));
    morphologyEx(maskR, maskR, MORPH_OPEN, k, Point(-1, -1), 1);
    morphologyEx(maskR, maskR, MORPH_CLOSE, k, Point(-1, -1), 2);

    morphologyEx(maskG, maskG, MORPH_OPEN, k, Point(-1, -1), 1);
    morphologyEx(maskG, maskG, MORPH_CLOSE, k, Point(-1, -1), 2);

    morphologyEx(maskB, maskB, MORPH_OPEN, k, Point(-1, -1), 1);
    morphologyEx(maskB, maskB, MORPH_CLOSE, k, Point(-1, -1), 2);
}

static int CountMaskPixels(const Mat& mask) { return countNonZero(mask); }

static int NormalizePixelValue(int pixelCount, int roiPixels)
{
    if (roiPixels <= 0) return 1;
    double ratio = (double)pixelCount / (double)roiPixels;
    int normalized = (int)(ratio * 255.0) + 1;
    if (normalized > 256) normalized = 256;
    if (normalized < 1) normalized = 1;
    return normalized;
}

// =====================
// Object ROI by contour
// =====================
static bool FindObjectROI(const Mat& bgr, Rect& outRoi)
{
    Mat gray;
    cvtColor(bgr, gray, COLOR_BGR2GRAY);
    GaussianBlur(gray, gray, Size(5, 5), 0);

    Mat edges;
    Canny(gray, edges, 50, 150);

    Mat k = getStructuringElement(MORPH_RECT, Size(5, 5));
    morphologyEx(edges, edges, MORPH_CLOSE, k, Point(-1, -1), 2);
    morphologyEx(edges, edges, MORPH_DILATE, k, Point(-1, -1), 1);

    vector<vector<Point>> contours;
    findContours(edges, contours, RETR_EXTERNAL, CHAIN_APPROX_SIMPLE);

    int best = -1;
    double bestArea = 0.0;
    for (int i = 0; i < (int)contours.size(); i++) {
        double a = contourArea(contours[i]);
        if (a > bestArea) { bestArea = a; best = i; }
    }

    if (best < 0) return false;
    if (bestArea < MIN_CONTOUR_AREA) return false;

    Rect br = boundingRect(contours[best]);

    br.x -= ROI_PAD; br.y -= ROI_PAD;
    br.width += ROI_PAD * 2; br.height += ROI_PAD * 2;

    Rect imgRect(0, 0, bgr.cols, bgr.rows);
    br = br & imgRect;
    if (br.width <= 0 || br.height <= 0) return false;

    outRoi = br;
    return true;
}

// =====================
// Image save (크롭된 이미지만)
// =====================
static string SaveColorCroppedJpg(
    const Mat& roiBgr,
    int rPix,
    int gPix,
    int bPix,
    int labelNo,
    const string& color
)
{
    std::error_code ec;
    filesystem::create_directories(CAPTURE_DIR, ec);

    ostringstream fn;
    fn << "color_" << NowFileStamp() << "_L" << labelNo << ".jpg";
    filesystem::path outPath = filesystem::path(CAPTURE_DIR) / fn.str();

    Mat canvas = roiBgr.clone();

    int baseLine = 0;
    ostringstream ss_color;
    ss_color << "Color: " << color;
    string colorLabel = ss_color.str();
    Size colorSz = getTextSize(colorLabel, FONT_HERSHEY_SIMPLEX, 0.8, 1, &baseLine);

    ostringstream ss_pix;
    ss_pix << "Pixels(R,G,B)=(" << rPix << "," << gPix << "," << bPix << ")";
    string pixLabel = ss_pix.str();
    Size pixSz = getTextSize(pixLabel, FONT_HERSHEY_SIMPLEX, 0.6, 1, &baseLine);

    Rect bgRect1(10, 10, colorSz.width + 4, colorSz.height + 4);
    rectangle(canvas, bgRect1, Scalar(255, 255, 255), FILLED);
    rectangle(canvas, bgRect1, Scalar(0, 0, 0), 1);

    Rect bgRect2(10, 40, pixSz.width + 4, pixSz.height + 4);
    rectangle(canvas, bgRect2, Scalar(255, 255, 255), FILLED);
    rectangle(canvas, bgRect2, Scalar(0, 0, 0), 1);

    putText(canvas, colorLabel, Point(12, 10 + colorSz.height), FONT_HERSHEY_SIMPLEX, 0.8, Scalar(0, 0, 0), 1);
    putText(canvas, pixLabel, Point(12, 40 + pixSz.height), FONT_HERSHEY_SIMPLEX, 0.6, Scalar(0, 0, 0), 1);

    vector<int> params = { IMWRITE_JPEG_QUALITY, 92 };
    bool ok = imwrite(outPath.string(), canvas, params);
    if (!ok) return "";

    return outPath.generic_string();
}

// =====================
// Color classify on ROI
// =====================
static string ClassifyColorROI(const Mat& roiBgr, const ColorThresholds& th,
    int& outRpix, int& outGpix, int& outBpix)
{
    Mat hsv;
    cvtColor(roiBgr, hsv, COLOR_BGR2HSV);
    GaussianBlur(hsv, hsv, Size(3, 3), 0);

    Mat maskR, maskG, maskB;
    BuildMasksRGB(hsv, maskR, maskG, maskB, th);

    int rawRPix = CountMaskPixels(maskR);
    int rawGPix = CountMaskPixels(maskG);
    int rawBPix = CountMaskPixels(maskB);

    int roiPixels = roiBgr.rows * roiBgr.cols;

    outRpix = NormalizePixelValue(rawRPix, roiPixels);
    outGpix = NormalizePixelValue(rawGPix, roiPixels);
    outBpix = NormalizePixelValue(rawBPix, roiPixels);

    int bestPix = 0;
    string color = "NONE";

    if (rawRPix > bestPix && rawRPix > rawGPix && rawRPix > rawBPix) {
        bestPix = rawRPix;
        color = "RED";
    }
    else if (rawGPix > bestPix && rawGPix > rawRPix && rawGPix > rawBPix) {
        bestPix = rawGPix;
        color = "GREEN";
    }
    else if (rawBPix > bestPix && rawBPix > rawRPix && rawBPix > rawGPix) {
        bestPix = rawBPix;
        color = "BLUE";
    }

    double ratio = (roiPixels > 0) ? (double)bestPix / (double)roiPixels : 0.0;

    if (bestPix < MIN_COLOR_PIXELS || ratio < MIN_COLOR_RATIO) return "NONE";
    return color;
}

// =====================
// MAIN
// =====================
int main()
{
    cout << "[CWD] " << filesystem::current_path().string() << "\n";
    cout << "[MODE] RGB Detection + Modbus TCP (Trigger-Based)\n";
    cout << "[MODBUS] PLC IP=" << PLC_IP << " PORT=" << PLC_PORT << "\n";

    std::error_code ec;
    filesystem::create_directories(CAPTURE_DIR, ec);

    if (!EnsureJsonArrayFile(HISTORY_JSON)) {
        cerr << "EnsureJsonArrayFile failed\n";
        return -1;
    }

    VideoCapture cap;
    if (USE_DSHOW) cap.open(DEVICE_INDEX, CAP_DSHOW);
    else cap.open(DEVICE_INDEX);

    if (!cap.isOpened()) {
        cerr << "camera open failed\n";
        return -1;
    }
    cap.set(CAP_PROP_FRAME_WIDTH, CAM_W);
    cap.set(CAP_PROP_FRAME_HEIGHT, CAM_H);

    cout << "[CAMERA] Opened\n";

    ModbusTcpClient modbus;
    ColorThresholds th;
    int captureCounter = 0;

    cout << "[RUN] Waiting for Modbus trigger (Coil " << COIL_TRIGGER << ")...\n";

    bool lastTrigger = false;
    bool modbusConnected = false;
    bool initialStateKnown = false;

    // 프로그램 시작 시에는 절대 촬영 금지.
    // 한 번이라도 PLC와 연결 후 초기 코일 상태를 읽어 lastTrigger만 초기화한다.
    while (true) {
        Mat frame;
        if (!cap.read(frame) || frame.empty()) {
            this_thread::sleep_for(chrono::milliseconds(50));
            continue;
        }

        // 연결이 되어있지 않다면 연결 시도
        if (!modbusConnected) {
            if (modbus.Connect(PLC_IP, PLC_PORT)) {
                modbusConnected = true;
                // 연결 직후 초기 코일 상태 읽기 (저장 동작 불발)
                bool initVal = false;
                if (modbus.ReadCoil(COIL_TRIGGER, initVal)) {
                    lastTrigger = initVal;
                    initialStateKnown = true;
                    cout << "[MODBUS] Initial trigger state read: " << (lastTrigger ? "ON" : "OFF") << "\n";
                } else {
                    // 읽기 실패하면 연결 초기화하고 재시도
                    modbus.Disconnect();
                    modbusConnected = false;
                    initialStateKnown = false;
                    cout << "[MODBUS] Initial read failed, will retry connect...\n";
                    this_thread::sleep_for(chrono::milliseconds(1000));
                    continue;
                }
            } else {
                this_thread::sleep_for(chrono::milliseconds(1000));
                continue;
            }
        }

        // 연결 및 초기 상태가 확보되었을 때만 트리거 감시
        if (modbusConnected && initialStateKnown) {
            bool triggerNow = false;
            if (!modbus.ReadCoil(COIL_TRIGGER, triggerNow)) {
                // 읽기 실패 -> 재연결 플로우
                cout << "[MODBUS] ReadCoil failed, reconnecting...\n";
                modbus.Disconnect();
                modbusConnected = false;
                initialStateKnown = false;
                this_thread::sleep_for(chrono::milliseconds(500));
                continue;
            }

            // OFF -> ON 전환에서만 동작 (최초 실행의 초기 상태는 무시됨)
            if (triggerNow && !lastTrigger) {
                cout << "[TRIGGER] Coil " << COIL_TRIGGER << " OFF->ON detected -> Measuring...\n";

                Rect objRoi;
                bool found = FindObjectROI(frame, objRoi);

                string color = "NONE";
                int rPix = 0, gPix = 0, bPix = 0;
                string imgPath = "";

                if (found) {
                    Mat roiBgr = frame(objRoi).clone();
                    color = ClassifyColorROI(roiBgr, th, rPix, gPix, bPix);

                    // 트리거 받았을 때만 이미지 저장
                    captureCounter++;
                    imgPath = SaveColorCroppedJpg(roiBgr, rPix, gPix, bPix, captureCounter, color);
                }

                string tsStr = NowTimeString();

                // 결과 코일 신호 (필요 시 PLC에 알림)
                int resultCoil = COIL_NONE;
                if (color == "RED") resultCoil = COIL_RED;
                else if (color == "GREEN") resultCoil = COIL_GREEN;
                else if (color == "BLUE") resultCoil = COIL_BLUE;

                // 결과 전송 (성공 여부는 내부에서 로그)
                modbus.WriteCoil(resultCoil, true);

                // 기록 저장 (이미지 경로 포함)
                {
                    ostringstream rec;
                    rec << "  {\n";
                    rec << "    \"time\": \"" << tsStr << "\",\n";
                    rec << "    \"color\": \"" << color << "\",\n";
                    rec << "    \"pix\": {\"r\": " << rPix << ", \"g\": " << gPix << ", \"b\": " << bPix << "},\n";
                    rec << "    \"image\": \"" << imgPath << "\"\n";
                    rec << "  }";
                    AppendJsonArray(HISTORY_JSON, rec.str());
                }

                {
                    ostringstream cur;
                    cur << "{\n";
                    cur << "  \"time\": \"" << tsStr << "\",\n";
                    cur << "  \"color\": \"" << color << "\",\n";
                    cur << "  \"pix\": {\"r\": " << rPix << ", \"g\": " << gPix << ", \"b\": " << bPix << "},\n";
                    cur << "  \"image\": \"" << imgPath << "\"\n";
                    cur << "}\n";
                    WriteTextFileAtomic(CURRENT_JSON, cur.str());
                }

                cout << "[MEASUREMENT] #" << captureCounter
                    << " time=" << tsStr
                    << " color=" << color
                    << " pix(r,g,b)=(" << rPix << "," << gPix << "," << bPix << ")"
                    << " image=" << imgPath
                    << " result_coil=" << resultCoil << "\n";
            }

            // 상태 갱신
            lastTrigger = triggerNow;
        }

        this_thread::sleep_for(chrono::milliseconds(50));
    }

    cap.release();
    modbus.Disconnect();
    cout << "[EXIT] Program closed normally\n";
    return 0;
}