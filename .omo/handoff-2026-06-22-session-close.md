HANDOFF CONTEXT — 2026-06-22 세션 종료
=========================================

USER REQUESTS (AS-IS)
---------------------
- "이전 세션 작업 이어서" → HeatingCameraSystem agent-manager 잔여 작업 진행
- 매뉴얼 Manager 섹션 보강 (B) → SC-12 E2E (A) → 시리얼 마이그레이션 + /pdca archive
- "다음 세션에서 이어서 할수 있도록 현재 모든 상태 저장하고 커밋하고 serena 업데이트"

GOAL
----
agent-manager PDCA 후속 작업(매뉴얼/SC-12/GAP-6) 완료 + PDCA 아카이브. 다음 세션은 실HW E2E 또는 신규 PDCA 사이클로 진행 가능.

CURRENT STATE
-------------
- 빌드: 10 projects, 0 errors / 0 warnings (.NET 8 타겟, dotnet 10 SDK)
- 테스트: 61/61 통과 (59 + 신규 AgentSupervisorSim 2)
- origin/master 동기 (0 ahead) — 오늘 5커밋 전부 push 완료
- 미커밋: .bkit/runtime·audit, .omo/run-continuation (세션 추적 파일만) → 이 핸드오프 커밋에 포함 예정
- GitHub: https://github.com/LimMyoungHwan/CanLab_HeatingCameraSystem

오늘 커밋 (2026-06-22, 최신순)
-----------------------------
- e68b300 docs(archive): agent-manager PDCA 문서 아카이브 (2026-06)
- 0aeb829 feat(devices): GAP-6 DevicesView 시리얼 설정 UI (Manager SetSerial)
- a16ddb9 feat(agent-manager): SC-12 승인 루프 E2E 드라이버 + sim spawn 결함 수정
- 9feb873 docs(manual): Agent Manager 섹션 추가 — 설치 §8 / 설정 §9 / 사용법 §8

WORK COMPLETED (이번 세션)
--------------------------
1. 매뉴얼 Manager 섹션 (9feb873)
   - 01-installation §8: HCS-Manager Windows Service 설치 (install.ps1, win-x64 빌드, 검증, 제거)
   - 02-configuration §9: manager-settings.json(7필드) + manager-state.json(CameraEntry 10필드) + 재시작 정책
   - 03-usage §8: Devices 탭 카메라 승인 워크플로 + 트러블슈팅, 관련문서 §9 리넘밍
   - README/00-overview 목차·참조 + 문서이력 v1.1, stale 테스트 수 38→59 정정

2. SC-12 Manager 승인 루프 E2E (a16ddb9)
   - 신규 프로젝트 HeatingCameraSystem.ManagerE2EDriver (net8.0/win-x64, Core+Protocols+AgentManager 참조)
     · AgentManager(SimulationMode) in-process 호스팅 → Fake 2대 발견 → inventory → Approve
       → AgentId 부여 → 승인 재발행 → manager-state.json 영속 검증. exit 0/1/2/3
   - 버그 발견·수정: AgentSupervisor.IsRunning/Kill 이 sim 미시작 Process 의 HasExited 에서
     InvalidOperationException → fire-and-forget 로 삼켜져 승인 인벤토리 미발행.
     → IsRunning/Kill 에 InvalidOperationException 가드 추가 (최소 수정, 리팩터링 아님)
   - 회귀 테스트 2건 (AgentSupervisorSimTests): sim Spawn 후 IsRunning/Kill no-throw
   - docs/deployment/run-manager-e2e.ps1 래퍼 (실NATS 위 end-to-end PASS 확인)
   - 캡처 roundtrip 은 기존 E2EDriver 담당 — 중복 없음

3. GAP-6 시리얼 마이그레이션 (0aeb829)
   - DevicesViewModel: 시리얼 draft 5필드(Port/Baud/DataBits/Parity/StopBits)
     + SetSerialCommand(승인된 카메라만) → CameraSerialSettings JSON → ManagerCommandOp.SetSerial
   - DevicesView.xaml: 우측 패널 시리얼 폼 + 전송 버튼 (SettingsView 필드 스타일 미러링)
   - SettingsView 는 legacy 유지 (삭제 안 함) — 수동 Agent(Agent_{idx}) 배포 시리얼 경로로 공존
   - 매뉴얼 03 §8.3: 시리얼 전송 액션 + 2종 경로(Manager vs 수동) 설명

4. PDCA 아카이브 (e68b300)
   - docs/{01-plan,02-design,03-analysis,04-report}/.../agent-manager.*.md 4종
     → docs/archive/2026-06/agent-manager/ (git mv, 히스토리 보존)
   - 상호참조 링크 형제 경로로 수정(6건) + docs/archive/2026-06/_INDEX.md 생성
   - bkit MCP state 미추적 기능이라 파일 이동만 (status 갱신 N/A)

KEY FILES (이번 세션 신규/수정)
--------------------------------
- HeatingCameraSystem.ManagerE2EDriver/Program.cs — SC-12 E2E 드라이버 (2 NATS 커넥션: manager+driver)
- HeatingCameraSystem.ManagerE2EDriver/*.csproj — net8.0/win-x64, Logging.Abstractions
- HeatingCameraSystem.AgentManager/Services/AgentSupervisor.cs — IsRunning/Kill 가드 (sim Process)
- HeatingCameraSystem.Tests/AgentManagerTests.cs — AgentSupervisorSimTests 2건
- HeatingCameraSystem.Master/ViewModels/DevicesViewModel.cs — SetSerialCommand + 시리얼 draft
- HeatingCameraSystem.Master/Views/DevicesView.xaml — 시리얼 폼 섹션
- docs/deployment/run-manager-e2e.ps1 — SC-12 래퍼
- docs/manual/{00,01,02,03}*.md — Manager 섹션
- docs/archive/2026-06/agent-manager/ — 아카이브된 PDCA 4종 + _INDEX.md
- HeatingCameraSystem.slnx — ManagerE2EDriver 등록

IMPORTANT DECISIONS
-------------------
- SC-12 범위 1 채택: 승인 루프 E2E만(캡처 X). 풀 캡처 roundtrip 은 SimulationMode 단일 플래그가
  열거/spawn-skip/Agent모드 3개를 동시 제어해 코드상 불가 → 범위 2(플래그 분리)는 미진행.
- GAP-6 옵션 A 채택: DevicesView 시리얼 추가 + SettingsView legacy 유지(삭제 X).
  이유: SettingsView 가 수동 Agent 배포의 유일 시리얼 UI라 삭제 시 회귀.
- 매뉴얼 stale 38→59 정정 (agent-manager 17테스트 추가분 반영).

EXPLICIT CONSTRAINTS
--------------------
- CAVEMAN MODE 유지 (불필요한 군더더기 제거)
- Nullable 경고 억제 금지, 버그 수정 시 리팩터링 금지(최소 변경)
- PowerShell (no &&, no tail — `;`, `Select-Object -Last N`)
- rtk wrapper for dotnet, .slnx 솔루션 포맷
- 프로젝트 타겟: Core/Protocols/Agent/E2EDriver net8.0, Master/Tests net8.0-windows,
  AgentManager/ManagerE2EDriver net8.0 win-x64
- 주석: 꼭 필요할 때만 (hook 이 신규 주석 감지 시 정당화 요구)
- git stderr 빨간 출력은 PowerShell 가 진행메시지를 stderr 로 받아서임 — 정상 push

PENDING / NEXT CANDIDATES
-------------------------
- SC-01~03, SC-06: 실HW E2E (USB 카메라 + Windows Service 필요 — 자동화 불가)
- SC-12 범위 2: 풀 캡처 roundtrip (SimulationMode 플래그 분리, 프로덕션 수정 2~3배)
- 신규 PDCA 사이클 후보: #18 열화상/RGB 자동 판별, #14 Recipe 단계 소요시간 추정(실PLC 후), NATS 인증
- 기술부채(건드리지 말 것): App.xaml.cs OnExit GetAwaiter().GetResult(),
  NatsCommunicationService Task.Run 구독 루프 오류복구 없음, Streaming CameraStatus 전환 미구현

TECH NOTES
----------
- Manager AgentId = {PCId}_{SHA256(HardwareId)[0:8] 소문자}
- NATS 토픽 11개 (기존 6 + Manager 5): agent-mgr.inventory.{PCId}, server.cmd.mgr.{PCId},
  agent-mgr.log.alert.{PCId}, server.req.log.{PCId}, agent-mgr.log.dump.{PCId}
- 시리얼: 7바이트 raw binary, open[2]=0x01 / close[2]=0x00, cameraIndex 는 식별자 전용
- DevicesView 노출 버튼: 승인/거부/이름저장/로그가져오기/시리얼전송 (Restart/Disable Op 은 핸들러만)
- 실 NATS nats://127.0.0.1:4222 구동 중이어야 SC-12/E2E 검증 가능 (Docker)
