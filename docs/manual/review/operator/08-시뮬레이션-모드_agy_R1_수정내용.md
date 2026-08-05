# 08-시뮬레이션-모드 — agy 검토 (Round 1)

## 종합 평가
- 수정 필요도: 상
- 한 줄 요약: 4가지 시나리오 구분 표와 E2E 스크립트 경로만 나열되어 있어, 관리자가 내부/외부 시뮬레이션을 직접 설정·실행·제어하기 위한 구체적 절차, JSON 설정 예시, 콘솔 명령, 설정 백업 주의사항 및 제약 사항이 전면 누락되어 보완이 시급함.

## 수정 필요 항목
1. [전체 구조] 문제: 본문이 단 18줄(표 1개, 코드 블록 1개, 그림 자리 1개)로 지나치게 간소화되어 설치·설정·유지보수 관리자 대상의 매뉴얼로서 실용성이 부족함 -> 제안: 내부 `SimulationMode`와 외부 `Simulator` 프로세스의 아키텍처 차이, 파일별 JSON 설정 방법, 콘솔 인터랙티브 제어 명령, E2E 스크립트 실행 파라미터 등을 포함한 체계적 절차 문서로 확장.
2. [시나리오 표 및 설정 정보] 문제: `hardware.json SimulationMode=false` 수준으로만 기재되어 실제 파일 경로(`%LOCALAPPDATA%\HeatingCameraSystem\hardware.json`)와 정확한 JSON 속성 필드 구성을 알 수 없음 -> 제안: Master 및 Agent의 실제 JSON 설정 예시와 경로 정보 명시.
3. [E2E 스크립트 안내] 문제: 스크립트 파일 경로만 코드 블록에 표기되어 필수 전제조건(NATS 서버 실행 여부, Docker 연동 등)과 파라미터 사용법(`-StartNatsDocker`)이 누락됨 -> 제안: 스크립트별 개별 역할, 실행 명령 파라미터, 통과 메시지 판정 기준 추가.

## 누락/추가 제안
- **내부 SimulationMode vs 외부 Simulator 개념 구분 누락**: 프로세스 내부 Fake 구현체(FakePlcController 등) 사용 방식과 실제 네트워크 프로토콜 경계(XGT FEnet TCP 2004, NATS)를 에뮬레이션하는 외부 Simulator 프로세스 방식의 차이 명시.
- **외부 Simulator 인터랙티브 콘솔 명령 누락**: Simulator 실행 중 사용 가능한 동적 장애 주입/상태 변경 명령 (`status`, `plc online/offline`, `plc fault`, `camera <AgentId> online/fault/offline`) 안내 추가.
- **운영 환경 설정 백업/복원 가이드 누락**: 외부 Simulator 연동을 위해 Master의 `hardware.json`을 수정할 때 기존 운영 설정을 백업하고 원복하는 방법 및 PowerShell 예시 안내 추가.
- **시뮬레이션 모드의 한계/제약 사항 누락**: 시리얼 셔터 COM 포트, DirectShow/USB 가상 카메라 디바이스, AgentUI 실시간 영상 경로 미에뮬레이션 등 범위 밖 항목 명시.

## 이미지 자리 검토
- **[그림 11] 시뮬레이션 E2E 결과 (적절)**: E2E 테스트 스크립트 수행 후 최종 `*** PASS ***` 출력 메시지가 콘솔에 나타난 화면으로, 통과 여부 확인용으로 적절함.
- **[추가 제안] [그림 12] 외부 시뮬레이터 콘솔 제어 및 Master 연동 화면 (적절)**: 외부 Simulator 콘솔에서 `plc offline` 또는 `camera Agent_1 fault` 명령 입력 시 Master UI의 통신 상태 변화를 보여주는 캡처 화면 추가 권장.

---

## (선택) 수정 제안 전문

# 8. 시뮬레이션 모드

실제 PLC, 시리얼 셔터, 열화상 카메라 하드웨어 없이 개발·검증·유지보수를 수행하기 위해 **내부 SimulationMode**와 **외부 Simulator**의 두 가지 시뮬레이션 방식을 제공합니다.

## 8.1 시뮬레이션 시나리오 구분

검증하려는 범위와 하드웨어 구비 여부에 따라 아래 4가지 시나리오 중 적절한 모드를 선택합니다.

| 시나리오 | 설정 방식 | 검증 범위 및 용도 |
| --- | --- | --- |
| **운영 (실HW)** | `hardware.json` `SimulationMode=false`<br>`agent.json` `SimulationMode=false` | 실제 PLC, 시리얼 셔터, 열화상 카메라 전체 연동 운영 환경 |
| **전체 시뮬레이션** | `hardware.json` `SimulationMode=true`<br>`agent.json` `SimulationMode=true` | 하드웨어 없이 Master와 Agent 내부 Fake 객체로 전체 레시피 흐름 검증 |
| **외부 Simulator** | Master/Driver `SimulationMode=false`<br>`HeatingCameraSystem.Simulator` 프로세스 별도 실행 | XGT FEnet(TCP 2004) 및 NATS 네트워크 프로토콜 경계 검증 |
| **하이브리드 모드** | `hardware.json` `SimulationMode=true`<br>`agent.json` `SimulationMode=false` (웹캠/실카메라) | PLC/셔터 없이 실제 카메라 캡처 및 영상 스트리밍 동작 확인 |

---

## 8.2 내부 SimulationMode 설정

앱 내부에서 하드웨어 드라이버 대신 Fake 구현체(`FakePlcController`, `FakeSerialShutterController`, `FakeCameraCaptureService`)를 바인딩하는 방식입니다.

### 1. Master PC 내부 시뮬레이션 설정
`%LOCALAPPDATA%\HeatingCameraSystem\hardware.json` 파일의 `SimulationMode`를 `true`로 설정합니다.

```json
{
  "SimulationMode": true,
  "Nats": {
    "Url": "nats://127.0.0.1:4222"
  }
}
```

### 2. Agent PC 내부 시뮬레이션 설정
Agent 실행 폴더의 `agent.json` 내 `SimulationMode`를 `true`로 설정하거나 CLI 실행 인수로 시뮬레이션 모드를 지정합니다.

```powershell
# CLI 인수를 통한 Agent 내부 시뮬레이션 실행 예시
dotnet run --project HeatingCameraSystem.Agent -- Agent_0 nats://127.0.0.1:4222 0 ImageStorage_0 true
dotnet run --project HeatingCameraSystem.Agent -- Agent_1 nats://127.0.0.1:4222 1 ImageStorage_1 true
```

---

## 8.3 외부 Simulator 연동 및 제어

외부 Simulator(`HeatingCameraSystem.Simulator`)는 Master PC나 E2EDriver를 **실제 하드웨어 모드(`SimulationMode=false`)**로 두고, 별도 프로세스가 XGT FEnet TCP 서버(기본 2004 포트) 및 NATS 메시지를 에뮬레이션하는 방식입니다.

### 1. Master PC 설정 및 운영 환경 백업
외부 Simulator와 연동하려면 Master의 `hardware.json`을 아래와 같이 맞춥니다. 설정 변경 전 반드시 기존 운영 설정을 백업합니다.

```powershell
# 기존 운영 설정 백업
$configDir = Join-Path $env:LOCALAPPDATA "HeatingCameraSystem"
Copy-Item "$configDir\hardware.json" "$configDir\hardware.before-simulator.json"
```

`hardware.json` 설정 예시:
```json
{
  "SimulationMode": false,
  "Plc": {
    "IpAddress": "127.0.0.1",
    "Port": 2004,
    "CpuSeries": "XGB",
    "UseHexBitIndex": true
  },
  "Nats": {
    "Url": "nats://127.0.0.1:4222"
  }
}
```

### 2. 외부 Simulator 실행
PowerShell 스크립트를 이용하여 외부 시뮬레이터 프로세스를 실행합니다.

```powershell
./docs/deployment/start-external-simulator.ps1
```
*최초 실행 시 `HeatingCameraSystem.Simulator/simulator.example.json`을 기반으로 `simulator.json`이 자동 생성됩니다.*

### 3. Simulator 콘솔 동적 제어 명령
Simulator가 실행 중인 대화형 콘솔에서 아래 명령어를 입력하여 장애 상황 및 하드웨어 상태 변화를 실시간으로 주입할 수 있습니다.

| 명령어 | 설명 | 예시 |
| --- | --- | --- |
| `status` | 현재 PLC 및 카메라 Agent 에뮬레이션 상태 출력 | `status` |
| `plc online` / `plc offline` | PLC FEnet 통신 연결/끊김 상태 토글 | `plc offline` |
| `plc fault <0-19> on\|off` | 특정 PLC 고장 비트 발생/해제 | `plc fault 0 on` |
| `camera <AgentId> online\|fault\|offline` | 특정 카메라 Agent의 상태 강제 변경 | `camera Agent_1 offline` |
| `quit` | 시뮬레이터 종료 | `quit` |

---

## 8.4 자동화 E2E 검증 스크립트

저장소에는 시뮬레이션 모드를 활용하여 전체 시스템 동작을 자동으로 검증하는 PowerShell 스크립트 3종이 제공됩니다.

### 1. 내부 Fake E2E 검증 (`run-e2e-simulation.ps1`)
NATS 컨테이너가 실행 중인 상태에서 내부 Fake 구현체를 기반으로 Master/Agent E2E 흐름을 검증합니다.

```powershell
# Docker로 NATS 실행 후 스크립트 수행
docker compose -f docs/deployment/docker-compose.yml up -d
./docs/deployment/run-e2e-simulation.ps1
```

### 2. 외부 Simulator E2E 검증 (`run-external-simulator-e2e.ps1`)
외부 Simulator 프로세스를 자식 프로세스로 자동 띄우고 `E2EDriver`를 통해 XGT FEnet 프로토콜 및 레시피 제어 전체 과정을 검증합니다. NATS Docker 자동 실행 옵션을 지원합니다.

```powershell
# Docker NATS 자동 기동 옵션 포함 실행
./docs/deployment/run-external-simulator-e2e.ps1 -StartNatsDocker
```

### 3. Manager 승인 루프 E2E 검증 (`run-manager-e2e.ps1`)
AgentManager의 신규 Agent 승인 및 상태 감독 흐름을 자동 검증합니다.

```powershell
./docs/deployment/run-manager-e2e.ps1
```

### E2E 테스트 성공 판정 화면
스크립트 실행 완료 시 콘솔 마지막 라인에 `*** PASS ***`가 출력되어야 정상적으로 검증이 완료된 것입니다.

> 📷 **[그림 11] 시뮬레이션 E2E 결과**
> - **캡처 대상:** E2E 러너 실행 후 `*** PASS ***` 및 캡처 수량(`Captures received: 4 / 4`)이 출력된 콘솔 화면
> - **화면/상태:** `run-external-simulator-e2e.ps1` 또는 `run-e2e-simulation.ps1` 통과 콘솔 화면

---

## 8.5 시뮬레이션 모드의 제약 사항 (범위 밖)

시뮬레이션 모드를 사용할 때 다음 항목은 에뮬레이션되지 않으므로 주의가 필요합니다.

1. **시리얼 셔터 COM 포트 미에뮬레이션**: 가상 시리얼 포트(COM) 수준의 바이트 응답은 에뮬레이션되지 않으며 내부 Fake 객체로 처리됩니다.
2. **가상 카메라 장치 미지원**: DirectShow/USB 가상 카메라 디바이스가 생성되지 않으며, 테스트용 샘플 이미지/더미 프레임이 사용됩니다.
3. **AgentUI 실시간 카메라 뷰 경로 제한**: AgentUI의 실시간 실카메라 영상 스트리밍 경로는 외부 Simulator에서 검증되지 않습니다.
4. **실제 흑체(Blackbody) 통신 프로토콜**: 흑체 장비 직접 통신 프로토콜 대신 PLC 레지스터 워드를 경유하여 에뮬레이션됩니다.
