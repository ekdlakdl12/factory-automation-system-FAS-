#include <iostream>
#include <opencv2/opencv.hpp>
#include <vector>

#include "csv_store.h"
#include "vision_common.h"
#include "color_mask.h"
#include "scale_calib.h"
#include "box_measure.h"

using namespace std;
using namespace cv;

int main()
{
    // ===== 사용자 설정 =====
    // 캡처카드 장치 인덱스(0,1,2...로 바꿔가며 확인)
    int deviceIndex = 1;

    string scaleYamlPath = "scale.yaml";     // 실행 폴더에 저장/로드
    string csvPath = "measurements.csv";     // 실행 폴더에 누적 저장(Append)

    // ROI (원본 프레임 기준)
    Rect roi(720, 300, 500, 500);

    // HSV 범위: 브라운(노랑~갈색 계열) + 흰색(저채도/고명도)
    HsvRange range;
    range.brownL = Scalar(5, 60, 60);
    range.brownU = Scalar(40, 255, 255);

    range.whiteL = Scalar(0, 0, 160);
    range.whiteU = Scalar(180, 40, 255);

    // 스케일 캘리브레이션 시 실제 거리(mm)
    double realMmForScale = 50.0;
    // =======================

    // 0) 캡처카드 오픈
    VideoCapture cap(deviceIndex, CAP_DSHOW); // Windows면 CAP_DSHOW가 안정적인 경우가 많음
    if (!cap.isOpened()) {
        cerr << "VideoCapture open failed. deviceIndex=" << deviceIndex << "\n";
        cerr << "Try deviceIndex=0/1/2... or remove CAP_DSHOW.\n";
        return -1;
    }

    // (선택) 캡처 해상도/프레임 설정 - 필요하면 조정
    cap.set(CAP_PROP_FRAME_WIDTH, 1920);
    cap.set(CAP_PROP_FRAME_HEIGHT, 1080);
    cap.set(CAP_PROP_FPS, 30);

    // 1) 스케일 로드 시도 (첫 프레임 크기/ROI와 비교하려고 루프 밖에서 먼저 읽기)
    Mat frame;
    cap >> frame;
    if (frame.empty()) {
        cerr << "First frame is empty.\n";
        return -1;
    }

    if (frame.rows > frame.cols) rotate(frame, frame, ROTATE_90_CLOCKWISE);
    ShowFit("frame", frame); // 전체 프레임 표시
    cout << "mean(frame)=" << mean(frame) << "\n";
    // ROI 클램프(프레임 기준)
    Rect roiClamped = ClampROI(roi, frame.size());
    if (roiClamped.width <= 0 || roiClamped.height <= 0) {
        cerr << "ROI out of range\n";
        return -1;
    }

    // 스케일 로드

    ScaleInfo scale;
    bool hasScale = LoadScaleYaml(scaleYamlPath, scale);

    if (hasScale) {
        if (scale.imageSize != frame.size() || scale.roi != roiClamped) {
            cout << "[Warn] Loaded scale but image/ROI differs.\n";
            cout << "       saved imageSize=" << scale.imageSize << " saved roi=" << scale.roi << "\n";
            cout << "       current imageSize=" << frame.size() << " current roi=" << roiClamped << "\n";
            cout << "       Recommend recalibration (press C).\n";
        }
        cout << "[Loaded] mmPerPx=" << scale.mmPerPx << " (realMm=" << scale.realMm << ")\n";
    }
    else {
        cout << "[Info] scale.yaml not found. Need calibration.\n";
    }

    // 2) 캘리브레이션(원하실 때만)
    Mat cropped0 = frame(roiClamped).clone();
    cout << "Keys: C=recalibrate scale, ESC=exit, AnyKey=continue\n";
    ShowFit("cropped", cropped0);
    int key = waitKey(0);
    if (key == 27) return 0;

    if (!hasScale || key == 'c' || key == 'C') {
        double mmPerPx = PickScaleMmPerPx(cropped0, realMmForScale, "scale_pick");
        if (mmPerPx <= 0.0) {
            cerr << "Scale pick canceled/failed.\n";
            return -1;
        }

        scale.mmPerPx = mmPerPx;
        scale.realMm = realMmForScale;
        scale.imageSize = frame.size();
        scale.roi = roiClamped;
        scale.note = "manual 2-point scale";

        if (!SaveScaleYaml(scaleYamlPath, scale)) {
            cerr << "Failed to save yaml: " << scaleYamlPath << "\n";
            return -1;
        }

        cout << "[Saved] " << scaleYamlPath << " mmPerPx=" << scale.mmPerPx << "\n";
        destroyWindow("scale_pick");
    }

    destroyWindow("cropped");

    // 3) 실시간 처리 루프
    while (true)
    {
        cap >> frame;
        if (frame.empty()) {
            cerr << "Frame empty. break.\n";
            break;
        }

        if (frame.rows > frame.cols) rotate(frame, frame, ROTATE_90_CLOCKWISE);

        // ROI는 프레임 크기가 바뀔 수 있으니 매 프레임 클램프
        Rect r = ClampROI(roi, frame.size());
        if (r.width <= 0 || r.height <= 0) continue;

        Mat cropped = frame(r).clone();

        // ==========================
        // 여기부터 "측정 시간" 재기 시작
        // ==========================
        int64 t0 = getTickCount();

        // 마스크 생성
        Mat mask = MakeMaskHSV(cropped, range, 3, 1.0, 5, 1, 3);
		
        double whiteRatio = CalcWhiteRatio(mask);

        // 박스 측정
        BoxMeasure m = MeasureLargestBoxFromMask(mask, 1000.0);

        int64 t1 = getTickCount();
        double elapsedMs = (t1 - t0) * 1000.0 / getTickFrequency();

        Mat vis = cropped.clone();

        if (m.ok) {
            double wMm = m.wPx * scale.mmPerPx;
            double hMm = m.hPx * scale.mmPerPx;

            DrawRotatedRect(vis, m.rr, Scalar(0, 255, 0), 2);
            putText(vis, format("W=%.2fmm H=%.2fmm (%.2fms) WR=%.3f", wMm, hMm, elapsedMs, whiteRatio),
                Point(20, 40), FONT_HERSHEY_SIMPLEX, 0.9, Scalar(0, 255, 0), 2);

            // CSV 누적 저장(원하시면 "m.ok일 때만" 저장)
            AppendMeasureCSV(csvPath, elapsedMs, wMm, hMm);
        }
        else {
            putText(vis, format("No object (%.2fms) WR=%.3f", elapsedMs, whiteRatio),
                Point(20, 40), FONT_HERSHEY_SIMPLEX, 0.9, Scalar(0, 0, 255), 2);
        }

        ShowFit("mask", mask);
        ShowFit("result", vis);

        // 키 처리: ESC 종료, C 재캘리브레이션
        int k = waitKey(1);
        if (k == 27) break;
        if (k == 'c' || k == 'C') {
            // 현재 프레임에서 다시 캘리브레이션
            double mmPerPx = PickScaleMmPerPx(cropped, realMmForScale, "scale_pick");
            if (mmPerPx > 0.0) {
                scale.mmPerPx = mmPerPx;
                scale.realMm = realMmForScale;
                scale.imageSize = frame.size();
                scale.roi = r;
                scale.note = "manual 2-point scale (recalib)";

                SaveScaleYaml(scaleYamlPath, scale);
                cout << "[Recalib Saved] mmPerPx=" << scale.mmPerPx << "\n";
            }
            destroyWindow("scale_pick");
        }
    }

    return 0;
}
