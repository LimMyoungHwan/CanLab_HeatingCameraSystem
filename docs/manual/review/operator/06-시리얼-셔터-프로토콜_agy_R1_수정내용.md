# 06-시리얼-셔터-프로토콜 — agy 검토 (Round 1)

## 종합 평가
- 수정 필요도: 상
- 한 줄 요약: 7바이트 raw binary 셔터 프레임 명세는 정확하나, 타겟 독자(설치·설정·유지보수 관리자)에게 필수적인 `hardware.json` 시리얼 통신 설정, 타임아웃/재연결 메커니즘, 시뮬레이션 모드 안내가 누락되어 보완이 필요합니다.

## 수정 필요 항목
1. [전체 섹션 구성] 문제: 현재 챕터 내용이 5줄의 단순 메모 수준으로 작성되어 설치·유지보수 관리자가 현장에서 통신을 설정하거나 장애를 조치하기에 설명과 구성이 크게 부족합니다. -> 제안: 통신 개요, hardware.json 포트 설정, 바이트 프로토콜 명세, 운용 및 유지보수/프로토콜 변경 절차로 단원을 체계적으로 재구성해야 합니다.
2. [cameraIndex 인자 설명] 문제: `cameraIndex`가 버퍼에 포함되지 않는다는 설명이 단문 형태로만 기재되어 오해를 유발할 수 있습니다. -> 제안: `ISerialShutterController` 인터페이스 호출 시 전달되는 `cameraIndex`는 상위 서비스(Master/Agent)의 카메라 식별용 인자이며, 컨트롤러 내부 시리얼 포트 송신 프레임에는 제어 바이트만 포함되는 구조적 이유를 명확히 설명해야 합니다.
3. [소스 수정 및 재배포 가이드] 문제: "Protocols/SerialShutterController.cs의 _openBuffer/_closeBuffer 수정 후 재배포" 문장이 관리자 매뉴얼에 부적합하게 단순 개발자 메모식으로 기술되어 있습니다. -> 제안: 이종 셔터 모델 변경 시 소스 코드 내 프로토콜 버퍼 수정 위치와 소스 재빌드/배포 절차를 유지보수 관리자 관점의 단계별 가이드로 다듬어야 합니다.

## 누락/추가 제안
- **시리얼 통신 설정(`SerialSettings`) 가이드 누락**: 셔터 제어를 위해 `%LOCALAPPDATA%\HeatingCameraSystem\hardware.json` 파일의 `Serial` 섹션(`PortName`: 기본값 `COM3`, `BaudRate`: `9600`, `DataBits`: `8`, `Parity`: `None`, `StopBits`: `One`)을 실제 카메라 가상 COM 포트에 맞게 설정해야 하는 필수 절차가 누락되어 있습니다.
- **타임아웃 및 재연결 동작 메커니즘 누락**: 쓰기 타임아웃(WriteTimeout = 2000ms) 및 PLC/Serial 연결 상태를 30초 간격으로 자동 점검하고 재연결하는 `ConnectionMonitorService`의 동작 특성 설명이 추가되어야 합니다.
- **시뮬레이션 모드(`SimulationMode`) 안내 누락**: 실제 카메라/셔터 하드웨어가 없는 테스트 환경에서 `hardware.json`의 `SimulationMode: true` 설정을 통해 셔터를 가상(Fake) 객체로 동작시킬 수 있다는 유지보수용 설정 안내가 필요합니다.

## 이미지 자리 검토
- [그림 6.1] 추가 제안 (부적절/누락) — 현재 본문에 이미지 위치 지정(📷)이 완전히 누락되어 있습니다. 셔터 가상 시리얼 포트 확인(장치 관리자) 및 `hardware.json` 시리얼 설정 항목을 안내하는 `📷 [그림 6.1] 시리얼 셔터 통신 포트 설정 및 장치 관리자 확인` 블록을 새로 배치하는 것이 적절합니다.

## (선택) 수정 제안 전문

```markdown
# 6. 시리얼 셔터 프로토콜

본 단원은 열화상 카메라 모듈의 시리얼 셔터(Serial Shutter) 제어 프로토콜 및 통신 설정 방법에 대해 설명합니다.

---

## 6.1 시리얼 통신 설정 (`hardware.json`)

시리얼 셔터는 RS-232 / 가상 COM 포트를 통해 제어됩니다. 설치 및 유지보수 관리자는 카메라 PC의 가상 시리얼 포트 번호와 통신 옵션을 아래 파일에서 설정해야 합니다.

- **설정 파일 경로**: `%LOCALAPPDATA%\HeatingCameraSystem\hardware.json`

```json
{
  "SimulationMode": false,
  "Serial": {
    "PortName": "COM3",
    "BaudRate": 9600,
    "DataBits": 8,
    "Parity": "None",
    "StopBits": "One"
  }
}
```

📷 [그림 6.1] 시리얼 셔터 통신 포트 설정 및 장치 관리자 확인

### 주요 설정 항목
| 항목 | 기본값 | 설명 |
|---|---|---|
| `PortName` | `COM3` | 카메라 셔터가 연결된 시리얼 포트 이름 (장치 관리자에서 확인) |
| `BaudRate` | `9600` | 통신 속도 (bps) |
| `DataBits` | `8` | 데이터 비트 |
| `Parity` | `None` | 패리티 비트 (`None`, `Odd`, `Even`) |
| `StopBits` | `One` | 정지 비트 (`One`, `Two`) |

> [!NOTE]
> - `SimulationMode`를 `true`로 설정하면 물리적 시리얼 포트 연결 없이 셔터 동작을 가상(Fake)으로 시뮬레이션할 수 있습니다.
> - 시리얼 포트 연결 이상 발생 시 `ConnectionMonitorService`가 30초 간격으로 자동 재연결을 시도합니다. 쓰기 타임아웃은 2,000ms로 설정되어 있습니다.

---

## 6.2 셔터 제어 바이너리 프로토콜

시리얼 셔터 제어 명령은 ASCII 문자열 명령이 아닌 **7바이트 고정 raw binary 프레임**을 사용합니다.

### 셔터 제어 바이트 명세
```
열기 (Open) : 0x04 0x00 0x01 0x00 0x00 0x00 0x00
닫기 (Close): 0x04 0x00 0x00 0x00 0x00 0x00 0x00
```
- **프레임 구조**: 3번째 바이트가 셔터 상태를 결정합니다 (`0x01`: 열기 / `0x00`: 닫기).
- **카메라 식별자 인자 (`cameraIndex`)**: 상위 서비스 인터페이스(`OpenShutterAsync(int cameraIndex)`)에서 전달되는 `cameraIndex`는 상위 레이어의 카메라 식별용이며, 바이너리 버퍼 내부에는 포함되지 않고 개별 포트로 전송됩니다.
- **상태 조회 (State Polling)**: 하드웨어 자체의 상태 조회 프로토콜을 지원하지 않으므로, 시스템은 소프트웨어 내부 캐시(`_isOpen`)에 마지막 제어 상태를 저장하여 반환합니다.

---

## 6.3 이종 셔터 모델 변경 가이드 (유지보수)

다른 제어 프로토콜을 사용하는 셔터 모델로 변경할 경우 소스 코드 수정 후 재배포가 필요합니다.

1. `HeatingCameraSystem.Protocols/SerialShutterController.cs` 파일을 열어 전송 버퍼 정의를 수정합니다.
   ```csharp
   // 7바이트 raw binary 프로토콜 버퍼
   private static readonly byte[] _openBuffer  = { 0x04, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
   private static readonly byte[] _closeBuffer = { 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
   ```
2. 솔루션을 재빌드(`dotnet build`)한 뒤 생성된 `HeatingCameraSystem.Protocols.dll` 또는 Executable을 대상 시스템에 재배포합니다.
```
