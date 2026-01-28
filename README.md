# 🏭 Smart Factory Automation System (FAS)

<p align="center">
  <img src="https://github.com/user-attachments/assets/f189626e-73ce-4a86-95f0-37e4bd61387a" alt="Project Logo" width="600">
</p>

---

## 📌 Project Overview
본 프로젝트는 **공정 자동화 및 실시간 모니터링 시스템 구축**을 목표로 합니다. 각 파트별 하드웨어 제어와 소프트웨어 통신을 통합하여 스마트 팩토리의 표준 모델을 제시합니다.

- **기간**: 2026.01 ~ (3주 통합 일정)
- **핵심 기술**: PLC, Arduino, OpenCV Vision, MariaDB, C# WPF (MVVM)

---

## 👥 Team Members & Roles
우리 팀은 총 6명으로 구성되어 각자의 전문 분야에서 협업하고 있습니다.

| 담당자 | 역할 | 핵심 업무 |
| :--- | :---: | :--- |
| **김진우 (팀장)** | **AMR & Total Process** | 아두이노 온습도/모터 제어, RC카 구현, 전체 프로세스 관리 및 통신 검증 |
| **최준영** | **DB & Backend** | MariaDB 아키텍처 설계, 백엔드 통신 모듈 개발, 데이터 적재 및 API 구현 |
| **양서하** | **WPF UI/UX** | 실시간 모니터링 화면(물류 흐름, 통계, 로그) 개발 및 MVVM 구조 설계 |
| **곽태린·김준형** | **PLC & Factory I/O** | PLC 메모리 맵 정의, Factory I/O 시뮬레이션 구성 및 시퀀스 로직 개발 |
| **윤은식** | **Computer Vision** | OpenCV 기반 외형 치수/모양 판별, 색상 구별 로직 및 PLC 연동 테스트 |

---

## 🗓️ Roadmap (3-Week Integration)

### **Week 1: 기반 구축 (Base Building)**
- [cite_start]**PLC**: 주소 정의 및 Factory I/O 라인 설계 [cite: 35, 38, 39]
- [cite_start]**AMR**: 아두이노 기반 온습도 센서 및 모터 제어 기초 구현 [cite: 58, 61, 62]
- [cite_start]**Vision**: 치수 측정 및 모양 판별 프로토타입 개발 [cite: 82]
- [cite_start]**UI**: 물류 흐름도 및 통계 영역 레이아웃 설계 [cite: 85]
- [cite_start]**DB**: 테이블 설계 및 초기 CRUD 검증 (불량률, 입출고 등) [cite: 88]

### **Week 2: 기능 구현 (Core Development)**
- [cite_start]**PLC**: 시퀀스 로직 및 인터락 구현 [cite: 46, 47, 48]
- [cite_start]**AMR**: 주행/정지/상태 출력이 가능한 RC카 시스템 완성 [cite: 69, 70, 71]
- [cite_start]**Vision**: 색상 구별 기능 추가 및 결과 출력 표준화 [cite: 83]
- [cite_start]**UI**: MVVM 기반 실시간 데이터 바인딩 적용 [cite: 86]
- [cite_start]**DB/BE**: 로컬 환경 안정화 및 하드웨어 통신 수신 모듈 개발 [cite: 89]

### **Week 3: 통합 및 시연 (Integration & Launch)**
- [cite_start]**전체 공정**: PLC-UI-DB-AMR 간 실시간 데이터 통신 연동 [cite: 51, 74, 87, 90]
- [cite_start]**안정화**: 통신 예외 처리 및 전체 프로세스 최적화 [cite: 79, 81, 87, 90]
- [cite_start]**시연**: 시나리오 기반 최종 통합 테스트 및 시연 진행 [cite: 80, 91]

---

## ⚙️ Environment Settings (for Backend)
- **Database**: MariaDB 10.11+ (Port: 33060/3306)
- **Backend Framework**: .NET 6.0/8.0 (C#)
- **Configuration**: `appsettings.json`을 통한 보안 관리 (Git Ignore 적용됨)

---
