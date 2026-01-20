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
    string imgPath = R"(C:\Users\dbsdm\Desktop\testimg\box34.JPG)";
    string scaleYamlPath = "scale.yaml";     // 실행 폴더에 저장/로드
    string csvPath = "measurements.csv";     // 실행 폴더에 누적 저장(Append)

    // ROI (원본 이미지 기준)
    Rect roi(780, 180, 850, 1010);

    // HSV 범위: 브라운(노랑~갈색 계열) + 흰색(저채도/고명도)
    HsvRange range;
    range.brownL = Scalar(5, 60, 60);
    range.brownU = Scalar(40, 255, 255);

    range.whiteL = Scalar(0, 0, 160);
    range.whiteU = Scalar(180, 40, 255);

    // 스케일 캘리브레이션 시 실제 거리(mm)
    double realMmForScale = 60.0; // 5cm면 50.0
    // =======================

    Mat img = imread(imgPath, IMREAD_COLOR);
    if (img.empty()) {
        cerr << "imread failed. path=" << imgPath << "\n";
        return -1;
    }

    if (img.rows > img.cols) rotate(img, img, ROTATE_90_CLOCKWISE);

    roi = ClampROI(roi, img.size());
    if (roi.width <= 0 || roi.height <= 0) {
        cerr << "ROI out of range\n";
        return -1;
    }

    Mat cropped = img(roi).clone();

    // 1) 스케일 로드 시도
    ScaleInfo scale;
    bool hasScale = LoadScaleYaml(scaleYamlPath, scale);

    if (hasScale) {
        if (scale.imageSize != img.size() || scale.roi != roi) {
            cout << "[Warn] Loaded scale but image/ROI differs.\n";
            cout << "       saved imageSize=" << scale.imageSize << " saved roi=" << scale.roi << "\n";
            cout << "       current imageSize=" << img.size() << " current roi=" << roi << "\n";
            cout << "       Recommend recalibration (press C).\n";
        }
        cout << "[Loaded] mmPerPx=" << scale.mmPerPx << " (realMm=" << scale.realMm << ")\n";
    }
    else {
        cout << "[Info] scale.yaml not found. Need calibration.\n";
    }

    // 2) 스케일 없으면(또는 사용자가 재측정 원하면) 클릭 후 저장
    cout << "Keys: C=recalibrate scale, ESC=exit, AnyKey=continue\n";
    ShowFit("cropped", cropped);
    int key = waitKey(0);
    if (key == 27) return 0;

    if (!hasScale || key == 'c' || key == 'C') {
        double mmPerPx = PickScaleMmPerPx(cropped, realMmForScale, "scale_pick");
        if (mmPerPx <= 0.0) {
            cerr << "Scale pick canceled/failed.\n";
            return -1;
        }

        scale.mmPerPx = mmPerPx;
        scale.realMm = realMmForScale;
        scale.imageSize = img.size();
        scale.roi = roi;
        scale.note = "manual 2-point scale";

        if (!SaveScaleYaml(scaleYamlPath, scale)) {
            cerr << "Failed to save yaml: " << scaleYamlPath << "\n";
            return -1;
        }

        cout << "[Saved] " << scaleYamlPath << " mmPerPx=" << scale.mmPerPx << "\n";
        destroyWindow("scale_pick");
    }

    // ==========================
    // 여기부터 "측정 시간" 재기 시작
    // ==========================
    int64 t0 = getTickCount();

    // 3) 마스크 생성
    Mat mask = MakeMaskHSV(cropped, range, 3, 1.0, 5, 1, 3);

    double whiteRatio = CalcWhiteRatio(mask);
    cout << "whiteRatio=" << whiteRatio << "\n";

    // 4) 박스 측정
    BoxMeasure m = MeasureLargestBoxFromMask(mask, 1000.0);
    if (!m.ok) {
        int64 tFail = getTickCount();
        double elapsedFailMs = (tFail - t0) * 1000.0 / getTickFrequency();
        cerr << "No valid object found. elapsed_ms=" << elapsedFailMs << "\n";

        ShowFit("mask", mask);
        waitKey(0);
        return -1;
    }

    double wMm = m.wPx * scale.mmPerPx;
    double hMm = m.hPx * scale.mmPerPx;

    int64 t1 = getTickCount();
    double elapsedMs = (t1 - t0) * 1000.0 / getTickFrequency();

    cout << "W_px=" << m.wPx << " H_px=" << m.hPx << "\n";
    cout << "W_mm=" << wMm << " H_mm=" << hMm << "\n";
    cout << "elapsed_ms=" << elapsedMs << "\n";

    // 4.5) CSV 누적 저장 (파일 없으면 헤더 생성, 있으면 append)
    if (!AppendMeasureCSV(csvPath, elapsedMs, wMm, hMm)) {
        cerr << "[Warn] CSV save failed: " << csvPath << "\n";
    }
    else {
        cout << "[CSV] appended -> " << csvPath << "\n";
    }

    // 5) 시각화
    Mat vis = cropped.clone();
    DrawRotatedRect(vis, m.rr, Scalar(0, 255, 0), 2);
    putText(vis, format("W=%.2fmm H=%.2fmm (%.2fms)", wMm, hMm, elapsedMs),
        Point(20, 40), FONT_HERSHEY_SIMPLEX, 0.9, Scalar(0, 255, 0), 2);

    ShowFit("mask", mask);
    ShowFit("result", vis);

    waitKey(0);
    return 0;
}
