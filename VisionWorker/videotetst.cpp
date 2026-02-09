#include <iostream>
#include <opencv2/opencv.hpp>

using namespace std;
using namespace cv;

int main() {


	VideoCapture cap(2, CAP_DSHOW);

	while (true) {
		Mat vis;
		cap >> vis;
		imshow("VIEW", vis);
		int key = waitKey(1);
		if (key == 27) {
			cout << "[EXIT] ESC pressed\n";
			break;
		}
	}

	
	
}