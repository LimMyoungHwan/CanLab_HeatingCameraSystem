# 07-Agent-Manager(자동-발견·승인) — agy 검토 (Round 1)

## 종합 평가
- 수정 필요도: 중
- 한 줄 요약: 설치 스크립트의 빌드 산출물 복사 단계 및 manager-settings.json의 필수 필드(AgentUiExePath)를 보완하고, Master 디바이스 관리 화면의 시리얼 설정 패널·로그 뷰어·상태 표시줄 등 상세 UI 구성을 정확히 반영해야 합니다.

## 수정 필요 항목
1. `[7.1 설치]` **빌드 결과물 복사 명령 및 관리자 권한 안내 누락**:
   - 문제: 매뉴얼의 설치 스크립트에 `publish\Manager` 빌드 산출물을 실제 설치 경로(`C:\HeatingCameraSystem\Manager`)로 복사하는 과정이 누락되어 있어 스크립트 실행 시 실행 파일 불일치 오류가 발생하며, 스크립트 실행에 필요한 관리자 권한 안내가 생략되어 있습니다.
   - 제안: `Copy-Item` 복사 명령어를 예시 코드 블록에 명시하고, PowerShell 관리자 권한 실행 및 무인 접속 PC를 위한 Autologon(자동 로그인) 설정 안내를 추가할 것.

2. `[7.2 설정 파일 - manager-settings.json]` **필수 설정 필드 누락 및 필드명 불일치**:
   - 문제: Manager가 주 카메라 런타임으로 관리하는 `AgentUiExePath` 필드와 로그 보존 기간(`LogRetentionDays`), 경고 알림(`WarnAlertEnabled`) 필드가 누락되어 있으며, 시뮬레이션 관련 필드명이 실제 JSON 필드명(`SimulateEnumeration`, `SimulateAgentMode`)과 다릅니다.
   - 제안: `AgentUiExePath`를 포함하여 실제 `manager-settings.json`의 주요 필드 및 명확한 시뮬레이션 옵션 명칭을 표에 반영할 것.

3. `[7.3 카메라 승인 운영]` **상세 UI 구성 요소(시리얼 설정 패널, 로그 뷰어, 상태 표시줄) 기술 부족**:
   - 문제: 7.3절 버튼 표에는 '시리얼 전송', '로그 가져오기'가 간략히 기술되어 있으나, 실제 디바이스 관리 탭 오른쪽 패널의 시리얼 통신 파라미터 입력 패널(Port, Baud, Data Bits, Parity, Stop Bits), 하단 NDJSON Log Viewer 콘솔, 화면 최하단 상태 표시줄(Status Bar)에 대한 구체적인 설명이 부족합니다.
   - 제안: 디바이스 관리 탭의 액션 패널 구조와 시리얼 설정, Log Viewer, 상태 표시줄의 동작 방식을 상세히 기술하여 작업자의 UI 이해도를 높일 것.

## 누락/추가 제안
- **무인 자동 로그인(Autologon) 유의사항 추가**: Agent Manager는 작업자 로그온 세션(`-AtLogOn`) 기반 예약작업 앱이므로, 미상주 PC의 경우 Windows 부팅 시 자동 로그인이 수행되도록 Sysinternals Autologon 또는 `netplwiz` 설정이 필수적이라는 [참고/주의] 안내 추가.
- **AgentUI 런타임 우선순위 명시**: Manager가 자동 발견된 카메라의 기본 런타임으로 `AgentUiExePath`(WPF AgentUI)를 우선 기동·감독하며, 콘솔 앱 Agent(`AgentExePath`)는 진단 및 폴백 용도임을 명시.
- **경고 메시지(Alert Box) 및 상태 표시줄 안내**: 카메라 운용 중 이상 발생 시 오른쪽 패널 상단에 표시되는 붉은색 경고 상자(Alert) 및 화면 하단 상태 표시줄(Status Bar)에 대한 설명 보완.

## 이미지 자리 검토
- **[그림 10] 적절 (개선 권장)** — Master의 Agent 설정 메뉴 내 '디바이스 관리' 탭 캡처 자리로 적절함. 캡처 시 왼쪽 디바이스 목록 DataGrid(Status, PCId, Alias, AgentId, HardwareId, IsApproved, OpenCvIndex)와 오른쪽 상세/액션 패널(승인/거부, 시리얼 설정, Log Viewer) 및 하단 상태 표시줄이 명확히 보이도록 캡처 범위를 지정할 것을 권장.

## (선택) 수정 제안 전문

# 7. Agent Manager(자동 발견·승인)

PC당 1개 운영자 세션 콘솔 앱(로그온 예약작업 HCS-Manager)으로 USB 카메라를 WMI로 자동 발견하고, Master PC의 'Agent 설정' 화면 중 **'디바이스 관리(Device Management)' 탭**에서 승인하면 Agent/AgentUI를 자동 기동·감독한다. 수동 Agent 기동(3.5절)과는 택일하여 운영한다.


## 7.1 설치

> ⚠️ **주의:** 아래 설치 스크립트는 **관리자 권한으로 실행된 PowerShell** 창에서 진행해야 합니다.

```powershell
# 1. Agent Manager 빌드
dotnet publish HeatingCameraSystem.AgentManager -c Release -r win-x64 --self-contained false -o publish\Manager

# 2. 디렉터리 생성 및 설정 파일 준비 (-NatsUrl 지정)
.\docs\deployment\install.ps1 -NatsUrl nats://192.168.1.10:4222

# 3. 빌드 산출물을 설치 디렉터리로 복사
Copy-Item -Path publish\Manager\* -Destination C:\HeatingCameraSystem\Manager -Recurse -Force

# 4. AgentUI / Agent 빌드 산출물 복사 (C:\HeatingCameraSystem\AgentUI, C:\HeatingCameraSystem\Agent)

# 5. 운영자 로그인 세션 자동 시작 예약작업 등록
.\docs\deployment\install-manager-task.ps1 -InstallRoot C:\HeatingCameraSystem

# 6. 예약작업 즉시 시작 (또는 사용자 로그온 시 자동 실행)
Start-ScheduledTask -TaskName HCS-Manager
```

> **[참고 - 무인 자동 실행 PC 설정]**  
> Agent Manager는 운영자 대화형 로그인 세션(`-AtLogOn`)에서 실행되는 콘솔 앱입니다. 상주 작업자가 없는 PC의 경우 Windows 부팅 후 자동으로 로그인되도록 **Sysinternals Autologon** 또는 `netplwiz`를 설정해야 예약작업이 정상 작동합니다.


## 7.2 설정 파일

| 파일 | 주요 필드 | 의미 |
| --- | --- | --- |
| manager-settings.json | PCId / NatsUrl / InstallRoot / AgentUiExePath / AgentExePath / SimulateEnumeration / SimulateAgentMode / LogRetentionDays / WarnAlertEnabled | Manager 서비스 설정. AgentUiExePath(기본 카메라 런타임) 및 AgentExePath(폴백 콘솔) 경로에 빌드 파일 복사 필요 |
| manager-state.json | Cameras[](HardwareId / AgentId / Alias / IsApproved / IsDisabled / OpenCvIndex) | 카메라 등록 및 관리 상태 (직접 편집 금지, Master의 디바이스 관리 탭에서 자동 관리) |


## 7.3 카메라 승인 및 운영

Master PC의 **'Agent 설정' -> '디바이스 관리(Device Management)' 탭**에서 자동 발견된 카메라를 관리한다.

### 운영 순서
1. Agent PC에 USB 카메라 연결 → Manager가 WMI를 통해 자동으로 디바이스 탐지
2. Master의 디바이스 관리 탭 목록에 미승인 상태(`IsApproved=False`)로 등록되어 표시
3. 카메라 선택 → Alias(별칭) 입력 → **[승인]** 버튼 클릭
4. Manager가 `AgentId` 부여 후 `AgentUI`(또는 Agent) 기동 → 인벤토리 재발행(`IsApproved=True`)
5. Master 대시보드에 초록색 통신 점 표시 → 촬영 준비 완료

### 디바이스 관리 탭 주요 액션 및 버튼

| 구분 | 버튼 / 패널 명칭 | 동작 및 기능 설명 |
| --- | --- | --- |
| 디바이스 제어 | 승인 (Approve) | `IsApproved=true`로 변경하고 Agent/AgentUI 프로세스 기동 (영구 드롭 카메라 재기동 시에도 사용) |
| 디바이스 제어 | 거부 (Reject) | `IsApproved=false`로 변경하고 실행 중인 Agent 프로세스 종료 |
| 디바이스 제어 | 이름 저장 (Save Name) | 입력한 Alias(별칭)를 Manager 상태 파일 및 Master LiteDB에 저장 (레시피 CameraAlias 매칭 시 사용) |
| 디바이스 제어 | 로그 가져오기 (Get Logs) | 해당 Agent의 NDJSON 실행 로그를 수집하여 하단 로그 뷰어에 표시 |
| 시리얼 설정 | 시리얼 설정 패널 | Port(COM), Baud Rate, Data Bits, Parity, Stop Bits 설정 입력 |
| 시리얼 설정 | 시리얼 전송 (Send Serial) | 설정한 시리얼 셔터 통신 파라미터를 선택된 승인 카메라 Agent로 포워딩 |
| 모니터링 | 경고 메세지 (Alert Box) | 카메라 실행 및 통신 이상 발생 시 오른쪽 패널 상단에 붉은색 상자로 에러 메시지 표시 |
| 모니터링 | 로그 뷰어 (Log Viewer) | [로그 가져오기] 실행 시 수집된 NDJSON 로그 텍스트 표시 |
| 모니터링 | 상태 표시줄 (Status Bar) | 화면 최하단에서 승인/거부, 시리얼 전송, 로그 수집 등의 처리 결과 실시간 안내 |

> 📷 **[그림 10] 디바이스 관리 — 승인**
> - **캡처 대상:** Agent 설정 화면의 '디바이스 관리' 탭 (디바이스 목록 DataGrid + 오른쪽 상세/액션 패널 + 하단 상태 표시줄)
> - **화면/상태:** 미승인 카메라 선택 후 승인 처리 및 시리얼 설정이 활성화된 상태
> - **표시 영역:** 창 전체

> **[참고 - 자동 재시작 및 영구 드롭]**  
> Agent 프로세스가 예기치 않게 종료되면 Manager가 지수 백오프(1초 → 2초 → 5초 → 15초 → 60초) 간격으로 재시작을 시도합니다. 단 연속 5회 이상 재시작에 실패하면 영구 드롭(`IsDisabled=true`) 처리되어 자동 재시작이 중지됩니다. 원인을 해결한 후 Master 화면에서 다시 **[승인]** 버튼을 누르면 정상 재기동됩니다.
