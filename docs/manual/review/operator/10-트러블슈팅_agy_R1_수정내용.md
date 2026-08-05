# 10-트러블슈팅 — agy 검토 (Round 1)

## 종합 평가
- 수정 필요도: 중
- 한 줄 요약: 핵심 현장 장애 증상과 점검 포인트가 표 형태로 잘 요약되어 있으나, 설치·설정·유지보수 관리자가 실제 조치에 활용하기 위한 단계별 구체적 진단 절차, 설정/로그 경로 안내, 참고 이미지 자리가 부족합니다.

## 수정 필요 항목
1. [전체 구조/완결성] 문제: 단순 1개의 매트릭스 표로만 챕터가 구성되어 있어, 장애 발생 시 원인 분석을 위한 구체적인 명령어나 로그 파일 경로, 단계별 조치 흐름이 약술되어 있음 -> 제안: 요약 매트릭스 표 외에 탭/모듈별 상세 진단 절차(NATS, Agent, PLC/XGT FEnet, 시리얼 셔터, DB) 섹션을 구체화.
2. [Agent 초록 점 안 뜸] 문제: Agent 초록 점이 NATS 하트비트(`agent.status.{AgentId}`, 5초 주기) 수신 상태를 기반으로 판단된다는 기술적 설명이 누락되어 있음 -> 제안: 5초 하트비트 메커니즘과 NATS 서버 접속 확인 명령(`Test-NetConnection <NATS_IP> -Port 4222`)을 구체적으로 명시.
3. [PLC 온습도 0 고정 및 값 이상] 문제: `hardware.json` 설정 경로와 스케일링(온도/습도 10으로 나누기, UseHexBitIndex 설정) 조치 방법이 명확하지 않음 -> 제안: `%LOCALAPPDATA%\HeatingCameraSystem\hardware.json` 절대 경로 표기와 함께 XGB(XBC-DN64H) 기준 `UseHexBitIndex=true` 설정 점검 조항 상세 명시.
4. [data.db 열 수 없음] 문제: LiteDB 프로세스 점유(Master 중복 실행) 문제 해결 방법 및 파일 경로 정보 부족 -> 제안: `%LOCALAPPDATA%\HeatingCameraSystem\data.db` 경로 명시, 작업 관리자(Task Manager)를 통한 중복 `HeatingCameraSystem.Master.exe` 종료 및 `*.db-log` 자동 복구 설명 보강.

## 누락/추가 제안
- **설정 파일 및 로그 파일 경로 안내 섹션**:
  - Master DB/설정: `%LOCALAPPDATA%\HeatingCameraSystem\data.db`, `hardware.json`
  - Agent 설정 및 이미지 저장 경로: `<Agent exe 폴더>\agent.json`, `ImageStorage\`
  - HCS-Manager 상태 및 로그: `manager-state.json`, `logs\{AgentId}\`
- **시리얼 셔터 하드웨어 제어 특성 명시**:
  - `ISerialShutterController.GetShutterStateAsync`는 하드웨어 직접 조회가 아닌 소프트웨어 상태 캐시(`bool _isOpen`)를 반환하므로, 물리적 셔터 상태와 소프트웨어 상태가 불일치할 경우 셔터 전원 reset 또는 통신 포트 재연결 절차가 필요함을 안내.

## 이미지 자리 검토
- [그림 1] 추가 제안 — Agent 콘솔 정상 연결 로그('Connected to NATS') 및 캡처 성공 화면 (적절: 현장 엔지니어가 콘솔 로그 정상 여부를 시각적으로 비교 가능)
- [그림 2] 추가 제안 — HCS-Manager / Master Devices 탭의 Agent 승인 대기 및 Running 상태 화면 (적절: Agent 승인 절차 미완료 시 조치 가이드용)
- [그림 3] 추가 제안 — PowerShell을 이용한 PLC(Port 2004) 및 NATS(Port 4222) 포트 통신 테스트 화면 (적절: 네트워크 장애 확인 시 진단 툴 사용법 제공)

## (선택) 수정 제안 전문

# 10. 트러블슈팅

## 10.1 트러블슈팅 요약 매트릭스

| 증상 | 점검 대상 | 주요 원인 및 조치 방법 |
| --- | --- | --- |
| Agent 초록 점 안 뜸 | Agent 콘솔, NATS 서버 | Agent 콘솔의 'Connected to NATS' 메시지 확인, NATS URL 접속 가능 여부 및 방화벽 4222 포트 점검 |
| 캡처 타임아웃 | 레시피 스텝 설정 | 스텝의 `CameraIndex`와 NATS 대상 `AgentId` 매칭 확인 (예: `CameraIndex=1` $\rightarrow$ `Agent_1`) |
| 캡처 실패 | 카메라 USB / 드라이버 | Agent 콘솔 로그 확인. OpenCV `frame.Empty()` 발생 시 카메라 드라이버 재설치, USB 케이블 재연결 또는 타 앱의 카메라 점유 해제 |
| PLC 온습도 0 고정 | PLC 통신 설정 / 케이블 | `hardware.json` 내 `Plc.IpAddress` 및 `Port`(2004) 확인. PowerShell `Test-NetConnection <IP> -Port 2004` 점검 |
| 값이 이상한 숫자 | PLC 데이터 타입 / 토큰 | D/M/P 토큰 매핑, `UseHexBitIndex` 확인. (온도/습도는 기본 $\div 10$ 스케일 적용 여부 확인) |
| 셔터 미응답 | COM 포트 / 프로토콜 | `SerialSettings`의 `PortName`이 실제 COM 포트와 일치하는지, 타 프로그램 점유 여부 확인. `_openBuffer`/`_closeBuffer` 프로토콜 통신 점검 |
| 레시피 '챔버 안정화' 멈춤 | 히터/습도 제어기 | 목표 온도/습도 도달 실패. `Tolerance`(허용 오차) 내 진입 불가 시 제어기 작동 점검 후 레시피 STOP 처리 |
| Devices에 카메라 안 뜸 | HCS-Manager / NATS | HCS-Manager 서비스/예약작업 Running 여부 확인, 동일 NATS 서버 연결 여부, `manager-state.json` 엔트리 확인 |
| 승인했는데 Agent 안 뜸 | AgentExePath / 로그 | `AgentExePath`에 `Agent.exe` 파일 존재 여부 점검, `logs\{AgentId}` 로그 확인, `SimulationMode` true 여부 확인 |
| data.db 열 수 없음 | LiteDB 파일 잠금 | Master PC 중복 실행 여부 확인(작업 관리자에서 프로세스 종료). `*.db-log` 파일 존재 시 자동 복구 진행 |

---

## 10.2 시스템 파일 및 로그 위치

유지보수 시 문제 원인을 분석하기 위해 아래 경로의 설정 파일과 로그를 확인하십시오.

* **Master 데이터베이스 및 하드웨어 설정**: `%LOCALAPPDATA%\HeatingCameraSystem\`
  * `data.db`: Master 운영 데이터베이스 (LiteDB, 중복 실행 금지)
  * `hardware.json`: PLC, 시리얼 셔터, NATS 접속 정보 설정
* **Agent 설정 및 저장소**: `<Agent 실행 폴더>\`
  * `agent.json`: Agent ID, CameraIndex, NATS URL 및 StoragePath 설정
  * `ImageStorage\`: 캡처된 열화상 이미지 저장 폴더
* **AgentManager 관리 파일**: `<AgentManager 실행 폴더>\`
  * `manager-state.json`: 등록 및 승인된 Agent 목록 및 상태
  * `logs\{AgentId}\`: 개별 Agent 실행 및 시스템 로그

📷 [그림 1] Agent 콘솔 정상 접속 로그 화면

---

## 10.3 세부 장치별 진단 절차

### 1) NATS 및 Agent 통신 장애 진단
1. Agent PC에서 PowerShell을 열고 NATS 서버 포트(기본 4222) 통신을 점검합니다:
   ```powershell
   Test-NetConnection <NATS_IP> -Port 4222
   ```
2. Agent 콘솔 창에서 `Connected to NATS` 문구가 정상 출력되는지 확인합니다.
3. Master UI의 Devices 탭에서 해당 Agent가 승인(Approved) 상태인지 확인합니다.

📷 [그림 2] Master UI의 Devices 탭 및 Agent 승인 관리 화면

### 2) PLC (LS XGT FEnet) 통신 장애 진단
1. Master PC에서 PLC IP 및 2004 포트(XGT FEnet Dedicated Port) 연결을 확인합니다:
   ```powershell
   Test-NetConnection <PLC_IP> -Port 2004
   ```
2. `%LOCALAPPDATA%\HeatingCameraSystem\hardware.json` 내 토큰 주소(D, M, P) 및 XGB CPU 기준 `UseHexBitIndex=true` 설정 여부를 확인합니다.

📷 [그림 3] PowerShell을 이용한 PLC 2004 포트 네트워크 점검 화면
