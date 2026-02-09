# 🏭 Smart Factory Automation System (FAS)

<p align="center">
  <img src="https://github.com/user-attachments/assets/f189626e-73ce-4a86-95f0-37e4bd61387a" alt="Project Logo" width="600">
</p>

---

## 📌 Project Overview
본 프로젝트는 **공정 자동화 및 실시간 데이터 통합 모니터링 시스템** 구축을 목표로 합니다. PLC 기반의 하드웨어 제어 데이터와 OpenCV 비전 검사 데이터를 **MariaDB**로 집약하여, **C# WPF** 환경에서 실시간으로 시각화하는 스마트 팩토리 표준 모델을 구현했습니다.

- **기간**: 2026.01.13 ~ 2026.02.09 (3주 통합 일정)
- **핵심 기술 스택**: 
  - **Environment**: Factory I/O, Arduino IDE
  - **Control**: PLC (LS IS), Arduino (ESP32/Uno)
  - **Language**: C# (.NET 8.0), C++ (Arduino), Python (OpenCV)
  - **Database**: MariaDB 10.11 (Architecture 설계 및 CRUD 구현)
  - **Framework**: WPF (MVVM Pattern)

---

## 👥 Team Members & Roles

| 담당자 | 역할 | 핵심 업무 및 기여도 |
| :--- | :---: | :--- |
| **김진우 (팀장)** | **AMR & Total Process** | PLC 및 하드웨어 전반, 아두이노 온습도/모터 제어, RC카 구현, 전체 프로세스 관리 및 통신 검증 |
| **최준영** | **DB & Backend** | MariaDB 아키텍처 설계, 백엔드 통신 모듈 개발, 데이터 적재 및 API 구현, 라즈베리파이 |
| **양서하** | **WPF UI/UX** | 실시간 모니터링 화면 개발 및 MVVM 기반 데이터 바인딩 설계 |
| **곽태린·김준형** | **PLC & Factory I/O** | PLC IO 리스트 정의, Factory I/O 시뮬레이션 및 시퀀스 로직 개발 |
| **윤은식** | **Computer Vision** | OpenCV 기반 치수/모양 판별 및 색상 구별 로직 연동 |

---

## 🏗️ System Architecture (파트별 상세 구현)

본 시스템은 하드웨어 제어부, 검사부, 통합 관리부가 유기적으로 연결된 **4-Tier 아키텍처**로 설계되었습니다.

### 🦾 PLC & Factory I/O (곽태린, 김준형)
- **공정 시퀀스 최적화**: `IO List_260202`를 기반으로 총 150여 개의 입출력 어드레스를 매핑하여 정밀 제어 로직 구현.
- **안전 인터락(Interlock)**: 설비 간 충돌 방지 및 비상 정지 시나리오를 반영하여 공정 안정성 확보.
- **디지털 트윈**: Factory I/O를 활용해 가상 환경에서 실제 산업 현장과 동일한 라인 시뮬레이션 구축.

### 🤖 AMR & Hardware Control (김진우)
- **IoT 환경 센싱**: Arduino(ESP32)를 활용해 공정 내 온습도 데이터를 실시간 수집 및 서버 전송.
- **모빌리티 제어**: 물류 이동을 위한 RC카 기반 AMR 시스템 구축 및 작업 상태(Idle/Running) 출력.
- **통신 검증**: 하드웨어 단의 데이터를 백엔드 서버로 유실 없이 전송하기 위한 핸드셰이킹 로직 적용.

### 👁️ Computer Vision (윤은식)
- **품질 검사 자동화**: OpenCV 기반 알고리즘을 통해 제품의 외형 치수 측정 및 모양 판별(Shape Detection).
- **지능형 분류**: 색상 추출 로직을 적용하여 양품과 불량품을 실시간 구별하고 결과를 PLC로 피드백.
- **데이터 표준화**: 검사 결과를 백엔드에서 즉시 처리할 수 있도록 JSON 포맷의 데이터 스트림 생성.

### 🗄️ Database & Backend (최준영)
- **데이터 모델링**: 공정 내 입출고, 불량률, 센서 로그를 관리하는 MariaDB 아키텍처 설계.
- **데이터 정합성**: PLC 어드레스(%MX)와 DB 필드 간의 1:1 매핑 테이블 관리.
- **비동기 트랜잭션**: 대량의 로그 발생 시 시스템 부하를 최소화하기 위한 비동기 데이터 적재 로직 개발.

### 💻 WPF UI/UX (양서하)
- **실시간 대시보드**: MVVM 패턴을 적용하여 DB의 생산 데이터를 실시간 차트 및 로그에 바인딩.
- **흐름 시각화**: 전체 공정 흐름도를 UI에 구현하여 실시간 설비 가동 상태 및 위치 정보 가시화.
- **사용자 관리**: 생산 통계 검색 필터 및 이상 발생 알림(Alarm) 시스템 구축.

---

## 🗓️ Roadmap (3-Week Integration)

- **Week 1: 기반 구축** - PLC 주소 정의, 아두이노 기초 제어 및 DB 스키마 설계.
- **Week 2: 기능 구현** - 공정 시퀀스 로직 완성, 비전 검사 알고리즘 및 데이터 연동 모듈 개발.
- **Week 3: 통합 및 시연** - 전체 공정(PLC-UI-DB-AMR) 통합 연동 및 최종 안정화 테스트.

---

---

## 🖥 실행 화면

<div align="center">
  <video src="" width="100%" controls autoplay muted loop>
    브라우저가 비디오 태그를 지원하지 않습니다.
  </video>


  
  <p><i> PLC 팩토리IO C# WPF 연동 제어 시연 영상 </i></p>
</div>

---

---

## ERD
<img width="511" height="360" alt="2026_1_19_ERD" src="https://github.com/user-attachments/assets/a4f4610a-4bf3-4a13-83ee-601eef9c4526" />

## ⬇️ 프로젝트 발표 자료
[PPT](https://docs.google.com/presentation/d/1RCwlPaEpzzQsnMo9Wbf_0YfH3aT70IDSX2ZANdCwv0I/edit?slide=id.p1#slide=id.p1)  

## ⬇️  PLC 레더 주소 엑셀
[PLC 레더](https://github.com/user-attachments/files/25178307/IO.List_260202.xlsx)  

## ⬇️  OpenCV 파일
[OpenCV 파일](https://drive.google.com/file/d/1qpcM1e98IfPNa7O2w33_IxLAAgXCMBUQ/view?usp=sharing)

---


## ⚙️ Environment Settings
- **Database**: MariaDB 10.11+ (Port: 33060/3306)
- **Backend Framework**: .NET 8.0 (C#)
- **Control Interface**: Modbus TCP/IP (PLC Connection)
