# AGENTS.md — HeatingCameraSystem

## 프로젝트 개요

열화상 카메라 모니터링 시스템. WPF Master PC ↔ NATS ↔ 다수의 Agent (카메라 PC) 구조.

```
Core (.NET 8)          ← 인터페이스 + 모델 + 설정만. 외부 의존성 없음.
Protocols (.NET 8)     ← Core 구현체. FluentModbus, NATS.Net, System.IO.Ports.
Master (.NET 8-windows)← WPF 운영자 UI. AppServices 정적 서비스 로케이터.
Agent (.NET 8)         ← 카메라 PC 콘솔 앱. OpenCvSharp4 + NATS.
Tests (.NET 8-windows) ← xUnit + Moq. 4개 프로젝트 모두 참조.
```

## 빌드 / 테스트 명령

```powershell
dotnet build                                             # 솔루션 전체 빌드
dotnet test --no-build                                   # 테스트 (현재 22개)
dotnet run --project HeatingCameraSystem.Master          # WPF Master 실행
dotnet run --project HeatingCameraSystem.Agent           # Agent 실행 (agent.json 기준)
dotnet run --project HeatingCameraSystem.Agent -- Bay1 nats://192.168.1.10:4222  # 인수 오버라이드
```

테스트 프로젝트가 `net8.0-windows` 타겟인 이유: Master(WPF) 프로젝트를 직접 참조하기 때문.

## 런타임 설정 파일 (저장소 외부)

| 파일 | 위치 | 비고 |
|---|---|---|
| `hardware.json` | `%LOCALAPPDATA%\HeatingCameraSystem\` | 최초 실행 시 자동 생성 |
| `data.db` | `%LOCALAPPDATA%\HeatingCameraSystem\` | LiteDB |
| `agent.json` | `<Agent exe 폴더>\` | 최초 실행 시 자동 생성 |
| 캡처 이미지 | `<Agent exe 폴더>\ImageStorage\` | agent.json `StoragePath`로 변경 가능 |

설정 파일이 없으면 기본값으로 자동 생성됨. 편집 후 재시작 필요.

## 아키텍처 핵심

### 서비스 초기화
`AppServices.Initialize()` (정적 서비스 로케이터) → `App.xaml.cs`에서 호출.  
DI 컨테이너 없음. 서비스 추가 시 `AppServices.cs`에 프로퍼티 + 초기화 코드 추가.

### NATS 토픽 규칙
```
master.cmd.capture.{AgentId}    ← Master → 특정 Agent (캡처 명령)
master.cmd.capture.all          ← Master → 전체 Agent (브로드캐스트)
agent.result.capture.{AgentId} ← Agent → Master (캡처 결과)
agent.status.{AgentId}         ← Agent → Master (하트비트, 5초 간격)
```

### 연결 재시도
- **NATS**: `NATS.Net` 라이브러리 내부 자동 재연결. `ConnectionMonitorService` 대상 아님.
- **PLC / Serial**: `ConnectionMonitorService`가 30초 간격으로 점검 + 재연결.

### 시리얼 셔터 프로토콜
raw binary 전송. ASCII 문자열 명령 아님.

```csharp
// 셔터 열기
byte[] _openBuffer  = { 0x04, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
// 셔터 닫기
byte[] _closeBuffer = { 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
// 포트에 쓰기: _port.Write(buffer, 0, buffer.Length)
```

`ISerialShutterController.OpenShutterAsync(int cameraIndex)` / `CloseShutterAsync(int cameraIndex)`의  
`cameraIndex`는 **식별자 전용** — 바이트 버퍼에는 사용하지 않음.  
`GetShutterStateAsync`는 하드웨어 조회 불가 → 소프트웨어 상태 캐시(`bool _isOpen`) 반환.

### Agent ↔ 카메라 매핑
`RecipeStep.CameraIndex` → NATS 대상 `Agent_{CameraIndex}`.  
Agent `agent.json`의 `AgentId`와 `CameraIndex`가 일치해야 함.

## 알려진 플레이스홀더

- `hardware.json` PLC Modbus 레지스터 주소: A&D PLC 실제 명세 확인 후 운영자가 직접 수정.
- `SerialSettings` 기본값(`COM3`, `9600 8N1`): 실제 카메라 가상 포트 설정에 맞게 수정 필요.

## 코드 규칙

- `Nullable=enable` + `ImplicitUsings=enable` — 전 프로젝트 공통.
- nullable 경고 억제(`!`, `#pragma warning disable`) 금지. 원인 수정.
- `as any` / `@ts-ignore` 상당 패턴 없음 (C# 프로젝트).
- 버그 수정 시 리팩터링 금지. 최소 변경만.

## 기술 부채 (건드리지 말 것 — 명시적 요청 시에만)

- `App.xaml.cs OnExit`: `.GetAwaiter().GetResult()` — 종료 블로킹 가능성 있음.
- `NatsCommunicationService` 구독 `Task.Run` 루프: 오류 복구 없음.
