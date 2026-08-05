# 03-설치 — agy 검토 (Round 1)

## 종합 평가
- 수정 필요도: 중
- 한 줄 요약: 설치 환경 사전 요구사항부터 Master/Agent 설치 및 검증 체크리스트까지 체계적으로 구성되어 있으나, 그림 번호 누락 및 hardware.json/agent.json 핵심 설정 필드 명시 등 완결성 보강이 필요함.

## 수정 필요 항목
1. [전체 / 그림 번호 체계] 문제: 3.3절의 첫 번째 이미지 블록이 `[그림 2]`로 시작하여 `[그림 1]`이 누락되어 있음. -> 제안: 3장 내부 그림 번호를 `[그림 1]`부터 순차 부여하거나 장 번호를 포함한 `[그림 3-1]` ~ `[그림 3-5]` 형식으로 변경.
2. [3.4 Master 설치] 문제: 최초 실행 시 생성되는 `hardware.json`의 수정 지침이 "PLC IP/NATS URL 등"으로 모호함. -> 제안: 관리자가 필수 확인/수정해야 하는 핵심 필드(`NatsUrl`, `PlcSettings.IpAddress`, `SerialSettings.PortName`)를 구체적으로 명시.
3. [3.5 Agent 설치] 문제: 단일 인스턴스 기동 시 생성되는 `agent.json`의 필수 설정 항목 안내가 부족함. -> 제안: 개별 Agent PC별로 변경해야 하는 필드(`AgentId`, `CameraIndex`, `NatsUrl`, `StoragePath`) 설정 가이드 추가.
4. [3.1 사전 요구사항] 문제: NATS 서버 환경이 Docker 권장으로만 설명되어 있음. -> 제안: Docker를 사용하지 않는 Windows 독립 실행 환경을 위해 `nats-server.exe` 단독 실행방식도 비고란에 간략히 언급.

## 누락/추가 제안
- [3.2 방화벽] NATS 모니터링 포트(8222/tcp) 접속 주체(Master PC 웹 브라우저)에 대한 설명 비고 추가.
- [3.5 Agent 설치] CLI 인수 방식과 `agent.json` 파일 설정 방식 간의 우선순위 및 유의사항(CLI 인수 전파 시 agent.json 무시)을 강조 박스로 명확히 표기.

## 이미지 자리 검토
- [그림 2] 적절 — NATS 서버 컨테이너/프로세스 기동 상태 확인용으로 적합함 (단, 번호를 [그림 1]로 변경 필요).
- [그림 3] 적절 — `%LOCALAPPDATA%\HeatingCameraSystem\` 경로 내 `hardware.json` 및 `data.db` 자동 생성 확인에 필수적임 (단, [그림 2]로 변경 필요).
- [그림 4] 적절 — 장치관리자의 이미징 장치 및 COM 포트를 동시 확인하는 캡처로 매우 유용한 구성임 (단, [그림 3]으로 변경 필요).
- [그림 5] 적절 — Agent 기동 성공 로그('Camera ready', 'Connected to NATS') 확인용으로 적합함 (단, [그림 4]로 변경 필요).
- [그림 6] 적절 — Master 대시보드 내 Agent 하트비트 초록 점 인식을 시각적으로 검증하기에 적절함 (단, [그림 5]로 변경 필요).

## (선택) 수정 제안 전문

# 3. 설치


## 3.1 사전 요구사항

| 항목 | 필수 | 비고 |
| --- | --- | --- |
| Windows 10/11 64-bit | 예 | Master(WPF)·Agent(OpenCvSharp) 모두 Windows |
| .NET 8 (SDK/Runtime) | 예 | Master=Desktop Runtime, Agent=Runtime |
| NATS 서버 | 예 | Docker 권장 (또는 nats-server.exe 단독 실행 가능) |
| PLC(LS XGT FEnet) | 시뮬 시 불필요 | XGT 전용 TCP, 기본 포트 2004 |
| 카메라 + USB 가상 시리얼 | 시뮬 시 불필요 | 웹캠으로도 검증 가능 |


## 3.2 방화벽

| 포트 | 방향 | 용도 | 대상 PC |
| --- | --- | --- | --- |
| 4222/tcp | 아웃바운드 | NATS 클라이언트 접속 | Master, Agent |
| 4222/tcp | 인바운드 | NATS 서비스 수신 | NATS 호스트 |
| 8222/tcp | 인바운드 | NATS 모니터링 웹 대시보드(선택) | NATS 호스트 |
| 2004/tcp | 아웃바운드 | LS XGT FEnet 통신 | Master |


## 3.3 NATS 서버

```powershell
docker compose -f docs/deployment/docker-compose.yml up -d
docker ps | Select-String nats
Test-NetConnection -ComputerName 127.0.0.1 -Port 4222 | Select TcpTestSucceeded
```

> 📷 **[그림 1] NATS 서버 기동 확인**
> - **캡처 대상:** docker ps 또는 NATS 콘솔에서 서버가 떠 있는 상태
> - **화면/상태:** NATS 컨테이너/프로세스가 실행 중인 화면


## 3.4 Master 설치

```powershell
dotnet publish HeatingCameraSystem.Master -c Release -o publish\Master
# publish\Master 폴더를 운영 PC로 복사 후 HeatingCameraSystem.Master.exe 실행
```

최초 실행 시 `%LOCALAPPDATA%\HeatingCameraSystem\` 에 `hardware.json`(기본값)과 `data.db`(빈 LiteDB)가 자동 생성된다. 생성된 `hardware.json`을 열어 실제 현장 환경에 맞게 주요 항목(`NatsUrl`, `PlcSettings.IpAddress`, `SerialSettings.PortName` 등)을 수정한 후 앱을 재시작한다.

> 📷 **[그림 2] hardware.json 생성 위치**
> - **캡처 대상:** %LOCALAPPDATA%\HeatingCameraSystem\ 폴더에 hardware.json·data.db가 생성된 탐색기 화면
> - **화면/상태:** 탐색기 주소창에 경로가 보이도록


## 3.5 Agent 설치

```powershell
dotnet publish HeatingCameraSystem.Agent -c Release -o publish\Agent
# 단일 인스턴스: agent.json 자동 생성 후 편집 (AgentId, CameraIndex, NatsUrl, StoragePath 설정)
# 다중 인스턴스(CLI): Agent.exe <AgentId> <NatsUrl> [CameraIndex] [StoragePath] [SimulationMode]
```

> **[참고]** CLI 인수를 지정하여 실행하면 `agent.json`을 읽지 않으므로, 동일한 폴더에서 여러 인스턴스를 동시 기동할 때 유용하다(테스트·시뮬레이션 전용).


## 3.6 카메라·시리얼 셔터 확인

- USB 비디오 장치 → 장치관리자 '이미징 장치'에서 인덱스 확인(보통 0 또는 1) → `agent.json` 또는 CLI의 `CameraIndex`에 반영
- 가상 시리얼 포트(셔터) → 장치관리자 '포트(COM & LPT)'에서 COM 포트 번호 확인 → `hardware.json`의 `SerialSettings.PortName`에 반영

> 📷 **[그림 3] 장치관리자 카메라·COM 확인**
> - **캡처 대상:** 장치관리자에서 이미징 장치와 포트(COM & LPT)가 함께 보이는 화면
> - **화면/상태:** 카메라·COM 포트가 인식된 상태


## 3.7 설치 검증 체크리스트

| # | 확인 | 방법 |
| --- | --- | --- |
| 1 | .NET 런타임 | dotnet --info 8.x |
| 2 | NATS | Test-NetConnection 127.0.0.1 -Port 4222 → True |
| 3 | Master 기동 | 실행 시 대시보드 정상 표시 |
| 4 | hardware.json 생성 | %LOCALAPPDATA%\HeatingCameraSystem\hardware.json 존재 |
| 5 | Agent 기동 | 콘솔에 'Connected to NATS' 및 'Camera ready' 표시 |
| 6 | Master 인식 | 대시보드에 초록 점(5초 내) |
| 7 | (실PLC) 온습도 | 대시보드 값 갱신 확인 |

> 📷 **[그림 4] Agent 콘솔 로그**
> - **캡처 대상:** Agent 콘솔의 'Camera ready' 및 'Connected to NATS' 로그
> - **화면/상태:** Agent 정상 기동 로그

> 📷 **[그림 5] Master Agent 인식**
> - **캡처 대상:** 대시보드 카메라/Agent 패널에 초록 점이 표시된 상태
> - **화면/상태:** Agent 기동 후 5초 이내
> - **표시 영역:** 창 오른쪽
