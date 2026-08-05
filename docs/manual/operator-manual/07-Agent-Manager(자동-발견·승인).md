# 7. Agent Manager(자동 발견·승인)

PC당 1개 운영자 세션 콘솔 앱(로그온 예약작업 HCS-Manager)으로 USB 카메라를 WMI로 자동 발견하고, Master에서 승인하면 Agent를 자동 기동·감독한다. 수동 Agent 기동(3.5)과는 택일이다.


## 7.1 설치

```
dotnet publish HeatingCameraSystem.AgentManager -c Release -r win-x64 --self-contained false -o publish\Manager
.\docs\deployment\install.ps1 -NatsUrl nats://192.168.1.10:4222
.\docs\deployment\install-manager-task.ps1 -InstallRoot C:\HeatingCameraSystem
Start-ScheduledTask -TaskName HCS-Manager
```


## 7.2 설정 파일

| 파일 | 주요 필드 | 의미 |
| --- | --- | --- |
| manager-settings.json | PCId / NatsUrl / SimulationMode / InstallRoot / AgentExePath | 서비스 설정. AgentExePath 경로에 Agent 빌드 필요 |
| manager-state.json | Cameras[](HardwareId/AgentId/Alias/IsApproved/IsDisabled) | 카메라 등록 상태(직접 편집 금지, Devices 탭에서 관리) |


## 7.3 카메라 승인 운영

1. Agent PC에 USB 카메라 연결 → Manager가 자동 발견
2. Agent 설정 화면의 디바이스 관리 탭에 미승인(Approved=False)으로 표시
3. 카메라 선택 → (선택)Alias 입력 → '승인'
4. Manager가 AgentId 부여 + Agent 기동 → 인벤토리 재발행(Approved=True)
5. 대시보드에 초록 점 → 촬영 준비 완료

| 버튼 | 동작 |
| --- | --- |
| 승인 | IsApproved=true, Agent 기동(영구드롭 재기동에도 사용) |
| 거부 | IsApproved=false + Agent 프로세스 종료 |
| 이름 저장 | Alias를 상태파일+LiteDB에 저장(레시피 CameraAlias 매칭) |
| 시리얼 전송 | 승인 카메라에 시리얼 설정 포워딩 |
| 로그 가져오기 | Agent NDJSON 로그를 gzip으로 요청·표시 |

> 📷 **[그림 10] 디바이스 관리 — 승인**
> - **캡처 대상:** Agent 설정 화면의 디바이스 관리 탭(카메라 목록 + 승인/거부 + 시리얼 설정)
> - **화면/상태:** 미승인 카메라가 목록에 있는 상태
> - **표시 영역:** 창 가운데

> **[참고]** Agent 프로세스가 반복 크래시하면 지수 백오프(1→2→5→15→60초)로 재시작하고 5회 초과 시 영구 드롭(IsDisabled=true)된다. 원인 해결 후 다시 '승인'하면 재기동된다.
