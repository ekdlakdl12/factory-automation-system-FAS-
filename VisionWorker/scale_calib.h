#pragma once
#include <opencv2/opencv.hpp>
#include <string>

using namespace std;
using namespace cv;

struct ScaleInfo
{
    double mmPerPx = -1.0;
    double realMm = 0.0;        // 클릭한 두 점 사이 실제 길이(mm) (예: 50)
    Size imageSize;             // (ROI 적용 전) 원본/캡처 프레임 크기
    Rect roi;                   // ROI
    string note;                // 옵션 메모
};

double PickScaleMmPerPx(const Mat& viewBgr, double realMm, const string& winName = "scale_pick");

// YAML 저장/로드
bool SaveScaleYaml(const string& yamlPath, const ScaleInfo& s);
bool LoadScaleYaml(const string& yamlPath, ScaleInfo& s);
#pragma once
