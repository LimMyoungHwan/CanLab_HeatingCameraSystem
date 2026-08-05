# AGENTS.md — HeatingCameraSystem

## 프로젝트 개요

열화상 카메라 모니터링 시스템. WPF Master PC ↔ NATS ↔ 다수의 Agent (카메라 PC) 구조.

```
Core (.NET 8)              ← 인터페이스 + 모델 + 설정만. 외부 의존성 없음.
Protocols (.NET 8)         ← Core 구현체. XGT FEnet, NATS, Serial, 카메라 프로토콜.
Master (.NET 8-windows)    ← WPF 운영자 UI. AppServices 정적 서비스 로케이터.
Agent (.NET 8)             ← 카메라 PC 콘솔 앱. OpenCvSharp4 + NATS.
AgentUI (.NET 8-windows)   ← 카메라 런타임 WPF UI.
AgentManager (.NET 8)      ← Agent 승인·감독 호스트.
Simulator (.NET 8)         ← XGT FEnet + NATS 외부 시뮬레이터.
E2EDriver (.NET 8)         ← 외부 시뮬레이터 E2E 드라이버.
ManagerE2EDriver (.NET 8)  ← AgentManager E2E 드라이버.
Tests (.NET 8-windows)     ← xUnit + Moq 통합 테스트 프로젝트.
```

## 빌드 / 테스트 명령

```powershell
dotnet build                                             # 솔루션 전체 빌드
dotnet test --no-build                                   # 테스트 (현재 254개)
dotnet run --project HeatingCameraSystem.Master          # WPF Master 실행
dotnet run --project HeatingCameraSystem.Agent           # Agent 실행 (agent.json 기준)
dotnet run --project HeatingCameraSystem.Agent -- Bay1 nats://192.168.1.10:4222  # 인수 오버라이드
```

테스트 프로젝트가 `net8.0-windows` 타겟인 이유: Master(WPF) 프로젝트를 직접 참조하기 때문.

테스트는 `HeatingCameraSystem.Tests/TestAssembly.cs`의 설정에 따라 전역 병렬 실행을 끈다. 정적
`AppServices`와 WPF 상태를 공유하므로 테스트를 병렬화하지 말고, 외부 NATS/PLC가 필요한 테스트는
시뮬레이터 또는 별도 실행 스크립트의 전제조건을 확인한다. 테스트 출력에서 Master 리소스를 읽는
경우 `HeatingCameraSystem.Tests.csproj`의 `Resources/Lang` 복사 설정을 유지한다.

프로젝트별 세부 규칙:
- `HeatingCameraSystem.Master/AGENTS.md` — WPF 시작·서비스 로케이터·ViewModel 규칙.
- `HeatingCameraSystem.Protocols/AGENTS.md` — XGT·시리얼 셔터·NATS 구현 규칙.

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

### PLC 프로토콜 (LS XGT FEnet)

Modbus → **XGT 전용 프로토콜**(TCP 2004)로 변경됨. 구현: `PlcXgtClient` (VagabondK.Protocols.LSElectric).
- 논리 디바이스 토큰(`D100`, `M10`, `P000`, `D2520.0`)을 `PlcSettings`에 저장 → VagabondK `DeviceVariable`로 변환.
- 비트-오브-워드(`D2520.0`)는 워드 읽기+마스크(쓰기는 read-modify-write). 순수 비트(`M10`/`P000`)는 직접.
- CPU=**XGB**(XBC-DN64H) 확인 → `UseHexBitIndex=true` 기본. 비트 오독 시 반전. 위치결정: XBF-PD02A(X/Y 2축).
- 전체 상태 일괄: `IPlcController.ReadStatusAsync()` → `PlcStatusSnapshot` (Master 상태 화면 1초 폴링).
- 온도 램프: `Recipe.TemperatureRampMinutes`(분) — RecipeEngine이 현재온도→타겟을 선형 스텝(히터 급출력 방지).

### Agent ↔ 카메라 매핑
`RecipeStep.CameraIndex` → NATS 대상 `Agent_{CameraIndex}`.  
Agent `agent.json`의 `AgentId`와 `CameraIndex`가 일치해야 함.

## 알려진 플레이스홀더

- `hardware.json` PLC 디바이스 주소(D/M/P): A&D PLC 실제 명세 확인 후 운영자가 직접 수정. CPU=XGB(XBC-DN64H) 확인 → `UseHexBitIndex=true` 기본.
- `ServoSpeedPercent`(D2560), `BitEmergencyStop`(M2000), Y축 JOG 비트(P725/P726): 문서 미기재 임의값 — 실제 비트 확인 후 교체.
- `SerialSettings` 기본값(`COM3`, `9600 8N1`): 실제 카메라 가상 포트 설정에 맞게 수정 필요.
- `ServoPointYBase`(D3012): 절대좌표 이동(`MoveToCoordinateAsync`)의 Y 목표 워드. 이동 트리거는 `ServoPointMoveBase`(P601) 재사용 — 실제 주소 하드웨어 확인 후 교체.

## 코드 규칙

- `Nullable=enable` + `ImplicitUsings=enable` — 전 프로젝트 공통.
- nullable 경고 억제(`!`, `#pragma warning disable`) 금지. 원인 수정.
- `as any` / `@ts-ignore` 상당 패턴 없음 (C# 프로젝트).
- 버그 수정 시 리팩터링 금지. 최소 변경만.

## 기술 부채 (건드리지 말 것 — 명시적 요청 시에만)

- `App.xaml.cs OnExit`: `.GetAwaiter().GetResult()` — 종료 블로킹 가능성 있음.
- `NatsCommunicationService` 구독 `Task.Run` 루프: 오류 복구 없음.
