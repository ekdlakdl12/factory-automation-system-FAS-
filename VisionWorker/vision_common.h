
#pragma once
#include <opencv2/opencv.hpp>
#include <string>
#include <iostream>

using namespace std;
using namespace cv;

Rect ClampROI(const Rect& r, const Size& sz);
double CalcWhiteRatio(const Mat& binaryMask); // CV_8UC1

// 화면에 맞춰 축소해서 보여주기 (디버그 편의)
void ShowFit(const string& name, const Mat& src, int maxW = 1920, int maxH = 1080);
