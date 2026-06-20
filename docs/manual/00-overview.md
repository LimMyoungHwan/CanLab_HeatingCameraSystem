# 00 — 시스템 개요

> HeatingCameraSystem 의 구조·구성요소·통신 흐름을 한 장에 정리. 매뉴얼 1~3 의 공통 컨텍스트.

## 1. 무엇을 하는 시스템인가

열·습도 챔버 안에서 다수의 (열화상) 카메라를 자동으로 운용해 **레시피 단위로 캡처를 진행**하고, 모든 캡처를 PLC 환경값(온도·습도)과 함께 이력 DB 에 적재한다.

- Master 1 대 — WPF 운영자 GUI, PLC/시리얼 셔터를 직접 제어, 캡처 결과 이력 저장
- Agent N 대 — 콘솔 앱, 카메라 1대를 담당, NATS 로 명령 수신·결과 송신
- NATS — 비동기 메시지 브로커 (Master ↔ Agent 분리)

하드웨어가 없을 때는 모든 컴포넌트를 시뮬레이션 모드로 돌릴 수 있다 (매뉴얼 03).

## 2. 아키텍처

```
┌───────────────────────────────┐          ┌──────────────────────┐
│        Master PC (WPF)        │          │      Agent PC #N     │
│                               │          │  USB-C Camera #N     │
│  Dashboard / Recipe Editor    │ capture  │  OpenCV + Serial     │
│  Camera Mapping / History     │  cmd  ►  │                      │
│  Serial Settings              │ ◄ result │  Heartbeat 5s        │
│                               │ ◄ status │  Serial config ACK   │
│  ┌─ PLC (Modbus TCP) ──────┐  │          └──────────────────────┘
│  │  챔버 온/습도, 서보, BB │  │                  ▲
│  └─────────────────────────┘  │                  │
│  ┌─ Serial Shutter (COM) ──┐  │           NATS Subjects
│  │  7-byte open/close      │  │           (다중 Agent 공통)
│  └─────────────────────────┘  │
│  ┌─ LiteDB (data.db) ──────┐  │
│  │  Recipe / Mapping / 이력│  │
│  └─────────────────────────┘  │
└───────────────┬───────────────┘
                │
                ▼
        ┌───────────────┐
        │  NATS Server  │  (Docker 권장, 단일 인스턴스)
        │  4222 / 8222  │
        └───────────────┘
```

## 3. 프로젝트 구성

| 어셈블리 | 타겟 | 역할 |
|---|---|---|
| `HeatingCameraSystem.Core` | `net8.0` | 인터페이스 + 모델 + Config. 외부 의존성 없음 |
| `HeatingCameraSystem.Protocols` | `net8.0` | FluentModbus / NATS.Net / System.IO.Ports 구현체. `Simulation/` 폴더에 Fake 구현 포함 |
| `HeatingCameraSystem.Master` | `net8.0-windows` | WPF GUI. `AppServices` 정적 서비스 로케이터 |
| `HeatingCameraSystem.Agent` | `net8.0` | 카메라 PC 콘솔 앱 |
| `HeatingCameraSystem.Tests` | `net8.0-windows` | xUnit + Moq (38건) |
| `HeatingCameraSystem.E2EDriver` | `net8.0` | 헤드리스 E2E 시뮬 드라이버 |

## 4. NATS 토픽 맵

| Subject | 방향 | Payload |
|---|---|---|
| `master.cmd.capture.{AgentId}` | Master → 특정 Agent | `CaptureCommandMessage` |
| `master.cmd.capture.all` | Master → 전체 Agent | `CaptureCommandMessage` (브로드캐스트) |
| `master.config.serial.{AgentId}` | Master → 특정 Agent | `SerialConfigMessage` |
| `agent.result.capture.{AgentId}` | Agent → Master | `CaptureResultMessage` (이미지 경로 + 성공여부) |
| `agent.status.{AgentId}` | Agent → Master (5초 주기) | `AgentStatusMessage` (CameraStatus enum) |
| `agent.config.serial.ack.{AgentId}` | Agent → Master | `SerialConfigAckMessage` |

`{AgentId}` 의 숫자 부분과 `RecipeStep.CameraIndex` 가 일치해야 라우팅됨. 자세한 매핑 규칙은 매뉴얼 02 §4.

## 5. 데이터 흐름 — 캡처 1회

```
[Operator] Recipe 시작 (Dashboard)
   │
   ▼
[Master.RecipeEngine] 챔버 안정화 (PLC StartChamber + Set T/H + 폴링)
   │
   ├─ 단계마다 ─────────────────────────────────────────────────
   │  서보 이동 (PLC MoveServo + IsServoAtPosition 폴링)
   │  BB 안정화 (PLC SetBlackBodyTemp + 폴링)
   │  ▼
   │  PublishCaptureCommandAsync → NATS master.cmd.capture.Agent_{idx}
   │                                                           │
   │                                                           ▼
   │                                                [Agent] CameraCaptureService
   │                                                  .CaptureFrame() → JPEG
   │                                                           │
   │                                                           ▼
   │                                                  NATS agent.result.capture.{Id}
   │  ▲                                                        │
   │  └─ TaskCompletionSource ◄──────────────────  CaptureResultMessage
   │
   ▼ (모든 단계 후)
[Master.RecipeEngine] StopChamber + 이력 저장 (LiteDb)
[Master.Dashboard] 진행률 100% + "완료"
```

## 6. 런타임 파일

| 파일 | 위치 | 비고 |
|---|---|---|
| `hardware.json` | `%LOCALAPPDATA%\HeatingCameraSystem\` | Master 기동 시 없으면 자동 생성 |
| `data.db` | `%LOCALAPPDATA%\HeatingCameraSystem\` | LiteDB |
| `agent.json` | Agent exe 폴더 | Agent 기동 시 없으면 자동 생성 |
| 캡처 이미지 | Agent exe 폴더 `\ImageStorage\` | `agent.json` `StoragePath` 로 변경 |

## 7. 다음 단계

| 목적 | 매뉴얼 |
|---|---|
| 처음 설치 | [01-installation.md](./01-installation.md) |
| 설정 파일 필드 모두 보기 | [02-configuration.md](./02-configuration.md) |
| Recipe 만들기·실행·이력 보기 | [03-usage.md](./03-usage.md) |
| 5분 만에 한번 돌려보고 싶다 | [README.md](./README.md) Quick Start |
