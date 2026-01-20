#pragma once
#include <string>

bool AppendMeasureCSV(
    const std::string& csvPath,
    double elapsedMs,
    double wMm,
    double hMm
);
