# Handoff — 운영자 모드 전환 + 잔여 작업 (세션4 준비)

> 작성: 2026-07-30. 다음 세션이 이 문서만 읽고 이어서 진행.

## 이번 세션 완료 (커밋됨)

- **S5** live-capture proof — `4d4d7e1`. E2EDriver `--live-capture`(라이브 + 동시 NATS 캡처 + radiometric `.y16` 충실도 + heartbeat). **실카메라 PASS**(Agent_1, max=10478).
- **S7** Manager 재정의 — `39ed6a5`. `AgentSupervisor`: 카메라당 프로세스 spawn 폐지 → NATS `runtimeLoad`/`runtimeUnload`로 단일 AgentUI 카메라별 제어. `IsRunning`=loaded ∧ heartbeat-fresh. `NoteHeartbeat`가 disabled 재조정. public 시그니처 보존(3-arg ctor 유지, +4 테스트). 서비스는 AgentUI spawn 안 함(session-0/UVC), `StopAsync`도 언로드 안 함(standalone 우선).
- **S8** — `39ed6a5`. `ManagerE2EDriver`=in-process `FakeAgentUiRuntime`(WPF/Agent.exe 없음, CI-green + S7 실증). AgentUI `--headless`. `install-agentui-task.ps1`(로그온 예약작업+자동로그인 문서). 콘솔 Agent=진단 폴백(문서화).
- **시리얼 페어링(장치명 기반)** — 기존 구현 확인(stale, `3093c36`). `UsbParentId`(장치 개체 identity)+`DeviceName`(FriendlyName) 매칭, S/N은 상태표시용만. 실HW 검증 r150→COM7 / r200→COM8.
- 검증: 빌드 0/0, 테스트 203, S7 실 NATS E2E PASS(1대 Disable→그 카메라만 not-running).

## ★ 최우선: 운영자 모드 전환 — 서비스(session-0) 제거 [확정]

결정(사용자 확정): **HCS-Manager를 Windows Service(session-0)에서 제거하고, 운영자 로그인 세션의 일반 앱으로 실행**(로그온 시 AgentUI와 함께 기동). session-0 / CreateProcessAsUser / 서비스용 자동로그인 복잡성 전부 소멸.

**먼저 읽을 것:** 이건 **호스팅/배포 변경**이지 S7 supervisor 로직 변경이 아니다. `AgentSupervisor`의 monitor-not-spawn + NATS `runtimeLoad`/`runtimeUnload`는 그대로 유효 — AgentUI는 여전히 자기 로그온 예약작업으로 기동하고 Manager는 spawn 안 함(UVC 단일 핸들 유지). `--headless`와 자동로그인 문서도 유지(무인 운영자 PC는 여전히 로그인 필요).

**변경점 (next session 실행):**
1. `HeatingCameraSystem.AgentManager/Program.cs`: `builder.Services.AddWindowsService(...)` 제거 → 일반 console host(`Host.CreateApplicationBuilder` + `AddHostedService<ManagerWorker>` 유지, Ctrl+C/종료까지 실행). `[assembly: SupportedOSPlatform("windows")]` 유지(WMI).
2. `docs/deployment/install.ps1`: `sc.exe create/description/failure` 서비스 등록 블록 제거. 대신 Manager를 로그온 예약작업으로(신규 `install-manager-task.ps1`, 또는 `install-agentui-task.ps1`에 Manager도 추가/단일 런처). 디렉터리·settings·방화벽은 유지.
3. `docs/deployment/deployment-guide.md`: 토폴로지 갱신 — Manager는 session-0 서비스 아님, Manager+AgentUI 둘 다 운영자 세션 로그온 기동.
4. 검증: 빌드 0/0 + 203 테스트 유지 + `run-manager-e2e.ps1` E2E PASS(로직 무변경이라 그대로 통과해야 함). Manager를 콘솔로 직접 띄워 승인/인벤토리/heartbeat 동작 확인.

## 남은 작업 (우선순위)

### A. S7/S8 후속 (선택 — 코드)
- **Option B 엄격 disable**: 현재 A(reconcile)는 AgentUI 재기동 시 disabled 카메라가 ≤1 heartbeat(~5s) 열렸다 재-unload. 엄격=AgentUI가 Manager 제공 disabled-set을 로컬 영속 + 기동 시 필터(카메라 열기 전). → `App.xaml.cs` 기동 루프 + `agentui.json`/별도 캐시 + Manager push 프로토콜.
- **AgentUI 패널 재바인딩**: `runtimeUnload`/`runtimeLoad` 후 UI 갱신(현재 패널 프리즈 — 핸들은 정상 해제/재획득). → `App.cameraControlHandler` + `_mainViewModel.Cameras` dispatcher 갱신.
- **inventory 구분**: Manager 승인했지만 `agentui.json`에 없는 카메라 = "configured, absent" 를 offline과 구분 표시(Oracle 지적). → `InventoryPublisher` / `CameraInventoryItem`.

### B. 실장비 / 운영자 설정 (코드 아님)
- 실 `agentui.json`에 DeviceName 채우기: Agent_1=`CLTC_T_VGA_G2_S_r150`(→COM7), Agent_2=`CLTC_T_VGA_G2_S_r200`(→COM8) → 자동감지 즉시 동작.
- 2점 NUC(gain): 실 블랙바디 필요.
- PLC 실주소 치환: `ServoSpeedPercent`(D2560) / `BitEmergencyStop`(M2000) / Y축 JOG(P725/P726) / `ServoPointYBase`(D3012) 임의값 → 실 A&D 메모리맵. (`hardware.json` + AGENTS.md "알려진 플레이스홀더")

### C. 결함 / 기술부채
- **AgentUI `OnExit` 블로킹**(`.GetAwaiter().GetResult()`): 강제종료 시 셔터 안 닫힘 / COM 잠금 — **이번 세션 실제 겪음**. → `App.xaml.cs OnExit` 타임아웃 + 비동기화. (AGENTS.md 기술부채)
- **NatsCommunicationService 구독 루프**: `RunSubscriptionLoop`의 `Task.Run` 오류복구 없음. (AGENTS.md 기술부채)
- **History `EmergencyStop` 죽은 버튼**: 상태문자열만 — 배선 또는 제거. → ✅ VERIFIED WIRED (2026-07-30, 세션5): 죽은 버튼 아님, 이미 배선됨. `HistoryViewModel.cs:198-217` → `IPlcController.TriggerEmergencyStopAsync` → `PlcXgtClient.cs:225`(BitEmergencyStop write); `ManualControlViewModel.cs:227` 동일 패턴. M2000 주소만 placeholder(하드웨어 B, 범위밖). **코드변경 불필요** — 태스크 종료.

## ⚠️ 물리 확인 (즉시)
- S5 실카메라 실행 후 AgentUI 강제종료 → **Agent_1/Agent_2 셔터가 열린 채일 수 있음**. 평소 AgentUI 정상 종료로 닫아 확인.

## 빌드 / 테스트 / 실행
```powershell
dotnet build HeatingCameraSystem.slnx            # 0/0
dotnet test  HeatingCameraSystem.slnx --nologo   # 203
docs/deployment/run-manager-e2e.ps1              # S7 E2E (실 NATS 필요)
# S5 라이브캡처 (실카메라 + AgentUI + NATS):
#   HeatingCameraSystem.E2EDriver.exe --live-capture nats://127.0.0.1:4222 Agent_1 30
```
> 무장비 로컬 검증: portable nats-server + AgentUI SimulationMode 로 S5/S7 E2E 재현 가능(이번 세션 방식).

## 참고
- 설계 authority: `.council/proposal.md`, `docs/handoff/agent-ui-slave-gui-handoff.md`, `docs/handoff/session3-mapping-serial-deploy-handoff.md`.
- 이번 커밋: `39ed6a5`(S7+S8), `4d4d7e1`(S5). `origin/master` 대비 앞섬(push 미수행).
