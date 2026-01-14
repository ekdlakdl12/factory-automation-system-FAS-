#include <iostream>
#include <opencv2/opencv.hpp>
#include <vector>

using namespace std;
using namespace cv;

int main()
{
    string path = R"(C:\Users\dbsdm\Desktop\testimg\BoxTestImg.jpg)";
    Mat img = imread(path, IMREAD_COLOR);
    Mat hsv, mask;

    if (img.empty())
    {
        cerr << "Could not open or find the image!" << endl;
        return -1;
    }

    // cvtColor(img, hsv, COLOR_BGR2HSV); // 색상기반 마스크(나중에 사용할 예정)
    cvtColor(img, mask, COLOR_BGR2GRAY); // 측정용 마스크, 그레이스케일 변환

    // 블러 적용
    GaussianBlur(mask, mask, Size(5, 5), 0);

    // Canny(mask, mask, 200, 255);

    // 이진화 (0 or 255)
    threshold(mask, mask, 220, 255, THRESH_BINARY);

    bitwise_not(mask, mask); // 흰색 배경 / 검정 물체로 반전 필요하면 사용
    // 모폴로지
    Mat kernel = getStructuringElement(MORPH_RECT, Size(3, 3));
    morphologyEx(mask, mask, MORPH_OPEN, kernel);
    // morphologyEx(mask, mask, MORPH_GRADIENT, kernel);


	// findContours는 입력 이미지를 내부적으로 바꿔버릴 수 있으니 clone 사용
    Mat contourInput = mask.clone();

    // 외곽 경계선 추출
    vector<vector<Point>> contours;  // 컨투어 하나 = Point들의 배열, 전체 = 그 배열들의 배열
    vector<Vec4i> hierarchy;        // 컨투어 계층 구조(부모/자식 관계). 필요 없으면 안 써도 됩니다.

    
    // 컨투어 추출
    // mode: RETR_EXTERNAL = "최외곽만"
    // method: CHAIN_APPROX_SIMPLE = 직선 구간 중복 점들을 압축(가볍고 일반적)
    findContours(contourInput, contours, hierarchy, RETR_EXTERNAL, CHAIN_APPROX_SIMPLE);

    if (contours.empty()) {
        cerr << "No contours found.\n";
        return -1;
    }
    
    Mat contourCanvas = Mat::zeros(mask.size(), CV_8UC1);

    // 모든 컨투어를 흰색(255) 선으로 그리기 (두께 2)
    drawContours(contourCanvas, contours, -1, Scalar(255), 1, LINE_8);

    // 확인
    imshow("Contours (BW)", contourCanvas);
    // imshow("hsv window", hsv); // 색상기반 마스크 확인용
    //imshow("Mask window", contours);

    waitKey(0);
    return 0;
}
