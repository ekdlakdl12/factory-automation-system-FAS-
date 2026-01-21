#include <opencv2/opencv.hpp>
#include <opencv2/core.hpp>
#include <iostream>
#include <vector>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <cstdio>
#include <cctype>

using namespace cv;
using namespace std;

static bool WriteTextFile(const string& path, const string& text) {
    ofstream ofs(path, ios::out | ios::trunc);
    if (!ofs.is_open()) return false;
    ofs << text;
    ofs.close();
    return true;
}

static bool EnsureJsonArrayFile(const string& path) {
    ifstream ifs(path);
    if (ifs.is_open()) return true; // 이미 있음
    return WriteTextFile(path, "[]\n");
}

static bool AppendJsonArray(const string& path, const string& recordJson) {
    // 1) 기존 파일 읽기(없으면 []로 시작)
    string content;
    {
        ifstream ifs(path, ios::in);
        if (ifs.is_open()) {
            ostringstream ss;
            ss << ifs.rdbuf();
            content = ss.str();
            ifs.close();
        }
        else {
            content = "[]";
        }
    }

    auto rtrim = [&](string& s) {
        while (!s.empty() && isspace((unsigned char)s.back())) s.pop_back();
        };
    auto ltrim = [&](string& s) {
        size_t i = 0;
        while (i < s.size() && isspace((unsigned char)s[i])) i++;
        s.erase(0, i);
        };

    rtrim(content);
    ltrim(content);
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

    string tmpPath = path + ".tmp";
    {
        ofstream ofs(tmpPath, ios::out | ios::trunc);
        if (!ofs.is_open()) return false;
        ofs << out;
        ofs.close();
    }

    std::remove(path.c_str());
    if (std::rename(tmpPath.c_str(), path.c_str()) != 0) {
        std::remove(tmpPath.c_str());
        return false;
    }
    return true;
}

int main() {
    int deviceIndex = 1;

    double realMm = 50.0;
    double mmPerPx = 0.0;
    string yamlPath = "scale.yaml";

    string jsonPath = "result.json";     // 누적 저장(배열)
    string statusPath = "status.json";   // 상태/에러 기록(디버그용)
    int labelCounter = 0;

    const int avgN = 5;
    double sumW = 0.0, sumH = 0.0, sumMs = 0.0;
    int collected = 0;

    bool inCollect = false;
    bool inCooldown = false;
    int presentStreak = 0;
    int absentStreak = 0;
    const int presentNeed = 10;
    const int absentNeed = 3;

    

    // 결과 파일은 무조건 존재하게
    EnsureJsonArrayFile(jsonPath);

    // ===== YAML 로드 (필수) =====
    {
        FileStorage fs(yamlPath, FileStorage::READ);
        if (!fs.isOpened()) {
            WriteTextFile(statusPath,
                "{\n"
                "  \"ok\": false,\n"
                "  \"reason\": \"scale.yaml not found\",\n"
                "  \"hint\": \"Put scale.yaml in the same folder as the exe working directory\"\n"
                "}\n");
            return -1;
        }

        double loadedMmPerPx = 0.0;
        fs["mmPerPx"] >> loadedMmPerPx;

        double loadedRealMm = 0.0;
        if (!fs["realMm"].empty()) fs["realMm"] >> loadedRealMm;

        fs.release();

        if (loadedMmPerPx <= 0.0) {
            WriteTextFile(statusPath,
                "{\n"
                "  \"ok\": false,\n"
                "  \"reason\": \"mmPerPx invalid in scale.yaml\"\n"
                "}\n");
            return -1;
        }

        mmPerPx = loadedMmPerPx;
        if (loadedRealMm > 0.0) realMm = loadedRealMm;
    }

    VideoCapture cap(deviceIndex, CAP_DSHOW);
    if (!cap.isOpened()) {
        WriteTextFile(statusPath,
            "{\n"
            "  \"ok\": false,\n"
            "  \"reason\": \"camera open failed\",\n"
            "  \"hint\": \"Try deviceIndex 0/1/2 or remove CAP_DSHOW\"\n"
            "}\n");
        return -1;
    }

    cap.set(CAP_PROP_FOURCC, VideoWriter::fourcc('M', 'J', 'P', 'G'));
    cap.set(CAP_PROP_FRAME_WIDTH, 1920);
    cap.set(CAP_PROP_FRAME_HEIGHT, 1080);

    

    Mat frame;
    Rect roi(695, 300, 500, 500);

    // 프레임이 안 들어오는 상황 방지(2초 타임아웃)
    int64 startTicks = getTickCount();
    double freq = getTickFrequency();

    int invalidRoiStreak = 0;

    for (;;) {
        cap >> frame;

        if (frame.empty()) {
            double sec = (getTickCount() - startTicks) / freq;
            if (sec > 2.0) {
                WriteTextFile(statusPath,
                    "{\n"
                    "  \"ok\": false,\n"
                    "  \"reason\": \"frame empty timeout\",\n"
                    "  \"hint\": \"Capture card may not be delivering frames yet\"\n"
                    "}\n");
                return -1;
            }
            continue;
        }

        // 프레임 들어오기 시작하면 타이머 리셋
        startTicks = getTickCount();

        Rect r = roi & Rect(0, 0, frame.cols, frame.rows);
        if (r.width <= 0 || r.height <= 0) {
            invalidRoiStreak++;
            if (invalidRoiStreak > 60) { // 약 2초(30fps 가정) 동안 계속 invalid
                ostringstream ss;
                ss << "{\n"
                    << "  \"ok\": false,\n"
                    << "  \"reason\": \"ROI out of range\",\n"
                    << "  \"frameW\": " << frame.cols << ",\n"
                    << "  \"frameH\": " << frame.rows << ",\n"
                    << "  \"roi\": [" << roi.x << "," << roi.y << "," << roi.width << "," << roi.height << "]\n"
                    << "}\n";
                WriteTextFile(statusPath, ss.str());
                return -1;
            }
            continue;
        }
        invalidRoiStreak = 0;

        int64 t0 = getTickCount();

        Mat roiFrame = frame(r);

        Mat hsv;
        cvtColor(roiFrame, hsv, COLOR_BGR2HSV);

        Scalar lower(15, 70, 70);
        Scalar upper(50, 180, 255);

        Mat mask;
        inRange(hsv, lower, upper, mask);

        Mat blurred;
        GaussianBlur(mask, blurred, Size(3, 3), 0);
        threshold(blurred, blurred, 150, 255, THRESH_BINARY);

        Mat kernel = getStructuringElement(MORPH_RECT, Size(5, 5));
        morphologyEx(blurred, blurred, MORPH_OPEN, kernel, Point(-1, -1), 1);
        morphologyEx(blurred, blurred, MORPH_CLOSE, kernel, Point(-1, -1), 1);

        vector<vector<Point>> contours;
        vector<Vec4i> hierarchy;
        findContours(blurred, contours, hierarchy, RETR_EXTERNAL, CHAIN_APPROX_SIMPLE);

        bool detected = false;
        double wOut = 0.0, hOut = 0.0;

        int best = -1;
        double bestArea = 0.0;

        

        // RotatedRect rr는 탐지됐을 때만 유효하므로 바깥에서 선언
        RotatedRect rr;

        if (!contours.empty()) {
            for (int i = 0; i < (int)contours.size(); i++) {
                double a = contourArea(contours[i]);
                if (a < 2000) continue;
                if (a > bestArea) {
                    bestArea = a;
                    best = i;
                }
            }

            if (best >= 0) {
                rr = minAreaRect(contours[best]);
                float wPx = rr.size.width;
                float hPx = rr.size.height;
                if (wPx < hPx) swap(wPx, hPx);

                detected = true;
                wOut = (double)wPx * mmPerPx;
                hOut = (double)hPx * mmPerPx;

                
            }
        }

        int64 t1 = getTickCount();
        double elapsedMs = (t1 - t0) * 1000.0 / freq;

        if (detected) {
            presentStreak++;
            absentStreak = 0;
        }
        else {
            absentStreak++;
            presentStreak = 0;
        }

       

        // 쿨다운 로직: 쿨다운 중엔 측정/누적을 하지 않되, 화면은 계속 갱신되게 "continue"는 하지 않음
        if (inCooldown) {
            if (absentStreak >= absentNeed) inCooldown = false;
        }
        else {
            if (!inCollect) {
                if (presentStreak >= presentNeed) {
                    inCollect = true;
                    collected = 0;
                    sumW = sumH = sumMs = 0.0;
                }
            }

            if (inCollect) {
                if (detected) {
                    sumW += wOut;
                    sumH += hOut;
                    sumMs += elapsedMs;
                    collected++;

                    if (collected >= avgN) {
                        double avgW = sumW / collected;
                        double avgH = sumH / collected;
                        double avgMs = sumMs / collected;

                        labelCounter++;

                        ostringstream rec;
                        rec << "  {\n";
                        rec << "    \"label\": " << labelCounter << ",\n";
                        rec << "    \"x\": " << fixed << setprecision(3) << avgW << ",\n";
                        rec << "    \"y\": " << fixed << setprecision(3) << avgH << ",\n";
                        rec << "    \"ms\": " << fixed << setprecision(3) << avgMs << "\n";
                        rec << "  }";

                        bool ok = AppendJsonArray(jsonPath, rec.str());

                        // [PRINT] 저장된 값 콘솔 출력 (나중에 출력 없애려면 이 블록 통째로 삭제/주석)
                        cout << "[SAVED] "
                            << "ok=" << (ok ? "true" : "false")
                            << " label=" << labelCounter
                            << " x=" << fixed << setprecision(3) << avgW
                            << " y=" << fixed << setprecision(3) << avgH
                            << " ms=" << fixed << setprecision(3) << avgMs
                            << "\n";

                        // 상태 파일에 마지막 결과 기록(성공/실패 확인용)
                        {
                            ostringstream ss;
                            ss << "{\n"
                                << "  \"ok\": " << (ok ? "true" : "false") << ",\n"
                                << "  \"last_label\": " << labelCounter << ",\n"
                                << "  \"last_x\": " << fixed << setprecision(3) << avgW << ",\n"
                                << "  \"last_y\": " << fixed << setprecision(3) << avgH << ",\n"
                                << "  \"last_ms\": " << fixed << setprecision(3) << avgMs << "\n"
                                << "}\n";
                            WriteTextFile(statusPath, ss.str());
                        }

                        

                        inCollect = false;
                        inCooldown = true;

                        collected = 0;
                        sumW = sumH = sumMs = 0.0;
                    }
                }
            }
        }

        

        
    }

    return 0;
}
