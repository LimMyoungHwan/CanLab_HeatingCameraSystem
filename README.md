# HeatingCameraSystem

열화상 카메라 모니터링 시스템 — WPF Master PC ↔ NATS ↔ 다수의 Agent (카메라 PC)

> 📘 **운영자/개발자 매뉴얼**: [`docs/manual/`](docs/manual/README.md) — 개요 / 설치 / 설정 / 사용법 통합본. 처음이라면 [5분 Quick Start](docs/manual/README.md#5분-quick-start-시뮬레이션) 부터.

## 아키텍처

```
┌─────────────────────────┐        NATS        ┌─────────────────────┐
│     Master PC (WPF)     │◄──────────────────►│   Agent PC #1       │
│                         │                    │   USB-C Camera #1   │
│  Dashboard              │  capture cmd ►     │   OpenCV + Serial   │
│  Recipe Editor          │  ◄ capture result  ├─────────────────────┤
│  Camera Mapping         │  ◄ heartbeat       │   Agent PC #2       │
│  Serial Settings        │  config ►          │   USB-C Camera #2   │
│  History Logs           │  ◄ config ACK      └─────────────────────┘
│                         │
│  PLC (Modbus TCP) ──────┤
│  LiteDB (data.db)       │
└─────────────────────────┘
```

## 프로젝트 구조

```
HeatingCameraSystem/
├── HeatingCameraSystem.Core/        # 인터페이스 + 모델 + 설정 (.NET 8)
├── HeatingCameraSystem.Protocols/   # FluentModbus, NATS.Net, System.IO.Ports
├── HeatingCameraSystem.Master/      # WPF 운영자 UI (.NET 8-windows)
├── HeatingCameraSystem.Agent/       # 카메라 PC 콘솔 앱 (.NET 8)
├── HeatingCameraSystem.Tests/       # xUnit + Moq (.NET 8-windows)
└── docs/                            # PDCA 문서 + 배포 가이드
```

## 빌드 및 실행

### 사전 요구사항

- .NET 8 SDK
- NATS 서버 (Docker 권장)
- Windows 10/11 (WPF)

### 빌드

```powershell
dotnet build
```

### 테스트

```powershell
dotnet test --no-build
```

### NATS 서버 실행

```powershell
docker compose -f docs/deployment/docker-compose.yml up -d
```

### Master 실행

```powershell
dotnet run --project HeatingCameraSystem.Master
```

### Agent 실행

```powershell
# 기본 설정 (agent.json 자동 생성)
dotnet run --project HeatingCameraSystem.Agent

# 인수 오버라이드
dotnet run --project HeatingCameraSystem.Agent -- Agent_Bay1 nats://192.168.1.10:4222
```

## 설정 파일

| 파일 | 위치 | 비고 |
|------|------|------|
| `hardware.json` | `%LOCALAPPDATA%\HeatingCameraSystem\` | PLC, NATS, Serial 설정 |
| `data.db` | `%LOCALAPPDATA%\HeatingCameraSystem\` | LiteDB (레시피, 매핑, 이력) |
| `agent.json` | Agent exe 폴더 | AgentId, CameraIndex, NATS URL |

설정 파일이 없으면 기본값으로 자동 생성됩니다.

## NATS 토픽

```
master.cmd.capture.{AgentId}          Master → Agent (캡처 명령)
master.cmd.capture.all                Master → 전체 Agent (브로드캐스트)
master.config.serial.{AgentId}        Master → Agent (시리얼 설정)
agent.result.capture.{AgentId}        Agent → Master (캡처 결과)
agent.status.{AgentId}                Agent → Master (하트비트 5초)
agent.config.serial.ack.{AgentId}     Agent → Master (설정 적용 ACK)
```

## 배포 가이드

➡ 통합 매뉴얼로 이전: [`docs/manual/`](docs/manual/README.md)

- [00 — 시스템 개요](docs/manual/00-overview.md)
- [01 — 설치](docs/manual/01-installation.md)
- [02 — 설정](docs/manual/02-configuration.md)
- [03 — 사용법](docs/manual/03-usage.md)
- [시뮬레이션 모드 전체 가이드](docs/deployment/simulation-mode.md)
- [NATS docker-compose](docs/deployment/docker-compose.yml)

## 라이선스

Private — Canlab 내부용
