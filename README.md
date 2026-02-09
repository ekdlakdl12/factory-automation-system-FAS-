# 🏭 Smart Factory Automation System (FAS)

<p align="center">
  <img src="https://github.com/user-attachments/assets/f189626e-73ce-4a86-95f0-37e4bd61387a" alt="Project Logo" width="600">
</p>

---

## 📌 Project Overview
본 프로젝트는 **공정 자동화 및 실시간 데이터 통합 모니터링 시스템** 구축을 목표로 합니다. PLC 기반의 하드웨어 제어 데이터와 OpenCV 비전 검사 데이터를 **MariaDB**로 집약하여, **C# WPF** 환경에서 실시간으로 시각화하는 스마트 팩토리 표준 모델을 구현했습니다.

- **기간**: 2026.01 ~ (3주 통합 일정)
- **핵심 기술 스택**: 
  - **Environment**: Factory I/O, Arduino IDE
  - **Control**: PLC (LS IS), Arduino (ESP32/Uno)
  - **Language**: C# (.NET 8.0), Python (OpenCV)
  - **Database**: MariaDB 10.11 (Architecture 설계 및 CRUD 구현)
  - **Framework**: WPF (MVVM Pattern)

---

## 👥 Team Members & Roles

| 담당자 | 역할 | 핵심 업무 및 기여도 |
| :--- | :---: | :--- |
| **김진우 (팀장)** | **AMR & Total Process** | 아두이노 온습도/모터 제어, 전체 프로세스 관리 및 통신 검증 |
| **최준영** | **DB & Backend** | **MariaDB 아키텍처 설계, 백엔드 통신 모듈 개발, 데이터 적재 및 API 구현** |
| **양서하** | **WPF UI/UX** | 실시간 모니터링 화면 개발 및 MVVM 기반 데이터 바인딩 설계 |
| **곽태린·김준형** | **PLC & Factory I/O** | PLC IO 리스트 정의, Factory I/O 시뮬레이션 및 시퀀스 로직 개발 |
| **윤은식** | **Computer Vision** | OpenCV 기반 치수/모양 판별 및 색상 구별 로직 연동 |

---

## 🏗️ System Architecture (최준영 담당 파트 상세)

백엔드 및 데이터베이스 설계자로서 하드웨어 데이터와 UI 사이의 **데이터 정합성**을 확보하는 핵심 로직을 구축했습니다.

### 🗄️ Database Design & Management
- **데이터 모델링**: 공정 내 입출고, 불량률, 센서 로그를 관리하는 MariaDB 아키텍처 설계.
- **IO 매핑 데이터**: `IO List_260202`를 기반으로 PLC 어드레스(%MX)와 DB 필드 간의 1:1 매핑 테이블 관리.
- **트랜잭션 최적화**: 대량의 로그 발생 시 시스템 부하를 최소화하기 위한 비동기 데이터 적재 로직 구현.

### 📡 Backend Communication
- **데이터 파이프라인**: PLC 및 비전 검사 결과 데이터를 수신하여 실시간으로 DB에 반영하는 통신 모듈 개발.
- **API 개발**: WPF 클라이언트 대시보드에서 생산 통계 및 로그를 실시간 조회할 수 있는 인터페이스 제공.

---

## 🗓️ Roadmap (3-Week Integration)

### **Week 1: 기반 구축 (Base Building)**
- [PLC] 주소 정의(%MX) 및 Factory I/O 라인 레이아웃 설계
- [AMR] 아두이노 기반 온습도 센서 제어 기초 구현
- [DB/BE] **MariaDB 테이블 스키마 설계 및 초기 CRUD 검증**

### **Week 2: 기능 구현 (Core Development)**
- [PLC] 시퀀스 로직 및 설비 간 인터락 구현
- [Vision] 색상 구별 기능 및 결과 출력 데이터 표준화
- [DB/BE] **하드웨어 데이터 수신 모듈 개발 및 실시간 바인딩 최적화**

### **Week 3: 통합 및 시연 (Integration & Launch)**
- [Integration] PLC-UI-DB-AMR 간 실시간 데이터 통신 통합 연동
- [Optimization] 통신 예외 처리 및 전체 프로세스 안정화 테스트
- [Launch] 시나리오 기반 최종 통합 테스트 및 시연

---

## ⚙️ Environment Settings
- **Database**: MariaDB 10.11+ (Port: 33060/3306)
- **Backend Framework**: .NET 8.0 (C#)
- **Control Interface**: Modbus TCP/IP (PLC Connection)

---

## 📂 Key Deliverables
- [IO List (Excel/CSV)](https://github.com/) - 하드웨어 어드레스 매핑 리스트
- [System Presentation (PPTX)](https://github.com/) - 프로젝트 통합 프로세스 및 결과 보고서
