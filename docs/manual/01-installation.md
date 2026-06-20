# 01 — 설치 매뉴얼

> Master 1 대 + Agent N 대 + NATS 1 대 구성. 시뮬레이션만 돌릴 거면 PLC/카메라/시리얼 없이도 가능.

전체 그림과 토픽 맵은 [00-overview.md](./00-overview.md) 참조.

## 1. 사전 요구사항

| 컴포넌트 | 필수 | 비고 |
|---|---|---|
| Windows 10/11 (64-bit) | ✅ | Master 는 WPF 라 Windows 전용. Agent 도 OpenCvSharp 윈도우 런타임 사용 |
| .NET 8 SDK (개발 PC) 또는 Runtime (운영 PC) | ✅ | Master = Desktop Runtime, Agent = Runtime |
| NATS 서버 | ✅ | Docker 권장 (§2) |
| PLC (Modbus TCP) | ⛔ (시뮬 시) | A&D PLC 등. IP + 포트 502 |
| 카메라 + USB-C 가상 시리얼 | ⛔ (시뮬 시) | 일반 USB 웹캠으로도 동작 (`CameraIndex=0`) |
| 네트워크 | ✅ | NATS 4222/tcp, PLC 502/tcp 접근 가능 |

설치 도구:
```powershell
winget install Microsoft.DotNet.SDK.8                 # 개발 PC
winget install Microsoft.DotNet.DesktopRuntime.8      # Master 운영 PC
winget install Microsoft.DotNet.Runtime.8             # Agent 운영 PC
```

## 2. NATS 서버 설치

### 2-1. Docker (권장)

저장소 루트에서:
```powershell
docker compose -f docs/deployment/docker-compose.yml up -d
docker ps | Select-String nats     # 컨테이너 살아있는지 확인
```

기본 노출 포트:
- `4222/tcp` — 클라이언트 연결
- `8222/tcp` — 모니터링 (`http://localhost:8222/varz` 로 상태 확인)

종료: `docker compose -f docs/deployment/docker-compose.yml down`

### 2-2. 바이너리 (Docker 불가 환경)

[nats-server 릴리스](https://github.com/nats-io/nats-server/releases) 에서 `nats-server-vX.Y.Z-windows-amd64.zip` 받아 압축 해제 후:
```powershell
nats-server.exe -p 4222 -m 8222
```

### 2-3. 연결 확인

```powershell
Test-NetConnection -ComputerName 127.0.0.1 -Port 4222 | Select TcpTestSucceeded
# TcpTestSucceeded : True   ← OK
```

## 3. Master 설치

### 3-1. 빌드 (개발 PC)

```powershell
git clone https://github.com/LimMyoungHwan/CanLab_HeatingCameraSystem.git
cd CanLab_HeatingCameraSystem
dotnet publish HeatingCameraSystem.Master -c Release -o publish\Master
```

### 3-2. 배포 (Master 운영 PC)

`publish\Master\` 폴더 전체를 운영 PC 의 `C:\HeatingCameraSystem\Master\` 같은 경로로 복사.

### 3-3. 최초 실행

```powershell
C:\HeatingCameraSystem\Master\HeatingCameraSystem.Master.exe
```

최초 실행 시 자동 생성:
- `%LOCALAPPDATA%\HeatingCameraSystem\hardware.json` — 기본값 (필드 의미는 매뉴얼 02 §2)
- `%LOCALAPPDATA%\HeatingCameraSystem\data.db` — 빈 LiteDB

`hardware.json` 을 실제 환경 값으로 수정 후 Master 재시작. PLC IP / NATS URL 만 맞으면 즉시 동작.

### 3-4. 자동 시작 등록 (선택)

WPF 앱은 Windows 서비스로 등록 불가. 대신 시작 프로그램 폴더에 바로가기:
```powershell
$shell = New-Object -ComObject WScript.Shell
$lnk   = $shell.CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\HCS-Master.lnk")
$lnk.TargetPath = "C:\HeatingCameraSystem\Master\HeatingCameraSystem.Master.exe"
$lnk.Save()
```

## 4. Agent 설치

### 4-1. 빌드

```powershell
dotnet publish HeatingCameraSystem.Agent -c Release -o publish\Agent
```

### 4-2. 배포 — 단일 인스턴스

운영 Agent PC 의 `C:\HeatingCameraSystem\Agent\` 로 복사.

```powershell
C:\HeatingCameraSystem\Agent\HeatingCameraSystem.Agent.exe
```

최초 실행 시 같은 폴더에 `agent.json` 자동 생성. CameraIndex / AgentId 수정 후 재시작.

### 4-3. 배포 — 같은 PC 다중 인스턴스 (테스트·시뮬)

같은 폴더 한 벌만 두고 CLI 인수로 구분:
```
Agent.exe <AgentId> <NatsUrl> [<CameraIndex>] [<StoragePath>] [<SimulationMode>]
```

PowerShell 3 터미널:
```powershell
.\HeatingCameraSystem.Agent.exe Agent_0 nats://127.0.0.1:4222 0 ImageStorage_0 false
.\HeatingCameraSystem.Agent.exe Agent_1 nats://127.0.0.1:4222 1 ImageStorage_1 true
.\HeatingCameraSystem.Agent.exe Agent_2 nats://127.0.0.1:4222 2 ImageStorage_2 true
```

> CLI 인수를 모두 넘기면 `agent.json` 을 생성·읽지 않으므로 동시 기동해도 안전. 파일 기반 단일 인스턴스 모드와 CLI 기반 다중 인스턴스 모드를 명확히 분리한다.

### 4-4. 배포 — 운영 환경 N 대 PC

PC 마다 별도 `agent.json` 두는 게 안전. 각 PC 에 `AgentId`, `CameraIndex`, `NatsUrl` 다르게 설정 후 재시작.

### 4-5. Windows 서비스 등록 (선택)

```powershell
sc.exe create HeatingCameraAgent binPath="C:\HeatingCameraSystem\Agent\HeatingCameraSystem.Agent.exe" start=auto
sc.exe start  HeatingCameraAgent
```

> CLI 인수를 함께 넘기려면 `binPath="\"...\Agent.exe\" Agent_Bay1 nats://10.0.0.5:4222"` 형식. 다중 인스턴스를 서비스로 등록하려면 서비스 이름을 인스턴스마다 다르게 부여.

## 5. 카메라 + 시리얼 셔터 (실 하드웨어용)

USB-C 열화상 카메라 연결 시 두 가지가 자동 잡힘:
1. **USB 비디오 장치** — OpenCvSharp `VideoCapture(cameraIndex)` 가 잡음. 장치 관리자 → 이미징 장치에서 인덱스 확인 (보통 0 또는 1)
2. **가상 시리얼 포트** — 셔터 제어. 장치 관리자 → 포트(COM & LPT) 에서 COM 번호 확인

확인 후 `agent.json` 의 `CameraIndex` 와 Master 의 `hardware.json` Serial 섹션(또는 Master Serial Settings UI 로 원격 송신) 설정.

웹캠으로 동작 검증만 할 거면 `CameraIndex=0` 그대로, 셔터는 시뮬레이션 모드(`SimulationMode=true`)로 건너뜀.

## 6. 방화벽

| 포트 | 방향 | 용도 | 필요 PC |
|---|---|---|---|
| 4222/tcp | 아웃바운드 | NATS 클라이언트 | Master, Agent |
| 4222/tcp | 인바운드 | NATS 서버 수신 | NATS 호스트 PC |
| 8222/tcp | 인바운드 | NATS 모니터링 (선택) | NATS 호스트 PC |
| 502/tcp | 아웃바운드 | PLC Modbus TCP | Master |

PowerShell 예시:
```powershell
# NATS 호스트
New-NetFirewallRule -DisplayName "NATS 4222" -Direction Inbound -LocalPort 4222 -Protocol TCP -Action Allow
```

## 7. 설치 검증 체크리스트

| # | 확인 항목 | 방법 |
|---|---|---|
| 1 | .NET 런타임 | `dotnet --info` 8.x 표시 |
| 2 | NATS 서버 | `Test-NetConnection 127.0.0.1 -Port 4222` → True |
| 3 | Master 기동 | `HeatingCameraSystem.Master.exe` 실행 → Dashboard 창 표시 |
| 4 | `hardware.json` 생성 | `%LOCALAPPDATA%\HeatingCameraSystem\hardware.json` 존재 |
| 5 | Agent 기동 | `HeatingCameraSystem.Agent.exe` 콘솔에 `Connected to NATS` 로그 |
| 6 | Agent → Master 인식 | Master Dashboard 우측 Agent 패널에 녹색 점 (5초 내) |
| 7 | (실 PLC) 온도/습도 표시 | Dashboard 좌상단 숫자 갱신 |
| 8 | (시뮬) 단위·E2E 테스트 | 저장소 루트에서 `dotnet test --no-build` → 38/38 통과 |

8번까지 모두 OK 면 설치 완료. 설정 세부는 [02-configuration.md](./02-configuration.md), 운영은 [03-usage.md](./03-usage.md).
