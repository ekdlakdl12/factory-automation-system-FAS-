#include "csv_store.h"

#include <fstream>
#include <filesystem>
#include <iomanip>

using namespace std;

static int GetNextIdFromCSV(const string& csvPath)
{
    if (!filesystem::exists(csvPath)) return 1;

    ifstream in(csvPath);
    if (!in.is_open()) return 1;

    string line;
    int lastId = 0;

    // 첫 줄(헤더) 버리기
    if (!getline(in, line)) return 1;

    // 마지막 데이터 라인의 id를 읽기 (간단 버전: 끝까지 스캔)
    while (getline(in, line)) {
        if (line.empty()) continue;

        // line: id,elapsed_ms,w_mm,h_mm
        // id만 파싱
        size_t pos = line.find(',');
        if (pos == string::npos) continue;

        try {
            int id = stoi(line.substr(0, pos));
            if (id > lastId) lastId = id;
        }
        catch (...) {
            // 깨진 라인 무시
        }
    }
    return lastId + 1;
}

bool AppendMeasureCSV(const string& csvPath, double elapsedMs, double wMm, double hMm)
{
    bool exists = filesystem::exists(csvPath);

    // id는 파일 마지막 id + 1
    int nextId = GetNextIdFromCSV(csvPath);

    ofstream out(csvPath, ios::app);
    if (!out.is_open()) return false;

    // 파일이 처음이면 헤더부터
    if (!exists) {
        out << "id,elapsed_ms,w_mm,h_mm\n";
    }

    out << nextId << ","
        << fixed << setprecision(3) << elapsedMs << ","
        << fixed << setprecision(3) << wMm << ","
        << fixed << setprecision(3) << hMm << "\n";

    return true;
}
