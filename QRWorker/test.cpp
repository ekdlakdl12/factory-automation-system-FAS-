#include <opencv2/opencv.hpp>
#include <iostream>
#include <vector>

using namespace std;
using namespace cv;

int main(int argc, char** argv)
{
    // 사용법:
    // qr.exe input.jpg
    // 또는 argv 없으면 기본 "input.jpg"
    string imgPath = R"(C:\\Users\\dbsdm\\Desktop\\testimg\\qrtest3.jpg)";
    

    Mat img = imread(imgPath, IMREAD_COLOR_BGR);
    if (img.empty())
    {
        cerr << "이미지 로드 실패: " << imgPath << "\n";
        return 1;
    }


    Mat si;
    resize(img, si, Size(), 0.3, 0.3, INTER_LINEAR);

    
	cvtColor(img, img, COLOR_BGR2GRAY);

	GaussianBlur(img, img, Size(3, 3), 3);

	threshold(img, img, 100, 255, THRESH_BINARY);

	

    

    // 화면 출력
    imshow("QR Result (overlay.jpg saved)", img);
    waitKey(0);

    return 0;
}
