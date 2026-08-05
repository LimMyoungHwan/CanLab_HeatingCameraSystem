# 7. Agent Manager(자동 발견·승인)

PC당 1개 운영자 세션 콘솔 앱(로그온 예약작업 HCS-Manager)으로 USB 카메라를 WMI로 자동 발견한다. 카메라 런타임은 별도 로그온 예약작업(HCS-AgentUI)으로 뜬 **AgentUI** 프로세스가 소유하며, Manager는 Master의 승인 상태에 따라 NATS로 해당 AgentUI에 카메라 런타임 로드/언로드(runtimeLoad/runtimeUnload) 명령만 보낸다. Manager는 프로세스를 죽이지 않으므로 한 카메라를 거부·비활성해도 나머지 카메라는 유지된다. 수동 Agent 기동(3.5)과는 혼용하지 않는다.


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
| manager-settings.json | PCId / NatsUrl / SimulateEnumeration / SimulateAgentMode / LogRetentionDays / WarnAlertEnabled / InstallRoot / AgentExePath / AgentUiExePath | Manager 시작 설정(4.7 참고). 시작 시 1회 로드 |
| manager-state.json | Cameras[](HardwareId/AgentId/Alias/OpenCvIndex/IsApproved/IsDisabled …) | 카메라 승인·상태. `ManagerStateStore`가 관리(직접 편집 금지, 장치 관리 화면에서 조작) |


## 7.3 카메라 승인 운영

1. Agent PC에 USB 카메라 연결 → Manager가 자동 발견
2. Master의 **장치 관리(Devices) 화면**에 미승인(IsApproved=False)으로 표시
3. 카메라 선택 → (선택)Alias 입력 → '승인'
4. Manager가 (필요 시)AgentId 부여, `IsApproved=true`로 저장하고 AgentUI에 runtimeLoad 전송 → 인벤토리 재발행
5. 승인·로드 희망 상태이고 최근 15초 이내 AgentUI 하트비트가 오면 장치 화면에 실행 중(초록)으로 표시

| 버튼 | 동작 |
| --- | --- |
| 승인 | IsApproved=true·IsDisabled=false 저장 후 AgentUI에 runtimeLoad 전송 |
| 거부 | IsApproved=false 저장 후 runtimeUnload 전송(프로세스는 종료하지 않으며 등록은 남아 재승인 가능) |
| 이름 저장 | Alias를 상태파일+LiteDB에 저장(레시피 CameraAlias 매칭) |
| 시리얼 전송 | 승인 카메라에 시리얼 설정 포워딩(적용 ACK 확인 UI는 없음) |
| 로그 가져오기 | Agent 로그를 gzip으로 요청·표시(최대 5MB, 30초 초과 시 응답 없음) |

> 📷 **[그림 10] 장치 관리(Devices) — 승인**
> - **캡처 대상:** Master의 장치 관리(Devices) 화면(카메라 목록 + 승인/거부 + 시리얼 설정)
> - **화면/상태:** 미승인 카메라가 목록에 있는 상태
> - **표시 영역:** 창 가운데

> **[참고]** Manager·AgentUI 프로세스가 실패하면 각자의 로그온 예약작업(HCS-Manager·HCS-AgentUI)이 1분 간격으로 최대 3회 재시작한다. 카메라별 지수 백오프나 영구 드롭 로직은 없다. 비활성(IsDisabled) 카메라는 다시 '승인'하면 runtimeLoad가 재전송된다.