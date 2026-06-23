HANDOFF CONTEXT — 2026-06-23 세션 종료
=========================================

USER REQUESTS (AS-IS)
---------------------
- "sc-12 범위 2 진행해줘. 계획을 세워서 꼼꼼히 진행해줘." → PDCA 전 사이클 (PM→Plan→Design→Do)
- 기본 추천안(1-B/2-B/3-A) + 주석 추가 요청
- 현재 상태 저장·커밋·푸시·Serena 업데이트 요청

GOAL
----
SC-12 범위 2(SimulationMode 플래그 분리 + 캡처 Roundtrip E2E) PDCA Do 완료.
다음 세션은 NATS 켜고 E2E 실행 → Check/Analyze → Report → Archive 순으로 진행 가능.

CURRENT STATE
-------------
- 빌드: 9 projects, 0 errors / 0 warnings (dotnet 10 SDK, .NET 8 타겟)
- 테스트: 64/64 통과 (기존 61 + 신규 3)
- origin/master 동기 (0 ahead) — 오늘 커밋 2개 push 완료
- GitHub: https://github.com/LimMyoungHwan/CanLab_HeatingCameraSystem

오늘 커밋 (2026-06-23, 최신순)
-------------------------------
- 877a7e2 feat(sc-12-scope2): SimulationMode 플래그 분리 + 캡처 Roundtrip E2E
- 957c77b docs(sc-12-scope2): PRD + Plan + Design 작성

PDCA 현황 (sc-12-scope2)
-------------------------
- [PM]     ✅ docs/00-pm/sc-12-scope2.prd.md
- [Plan]   ✅ docs/01-plan/features/sc-12-scope2.plan.md
- [Design] ✅ docs/02-design/features/sc-12-scope2.design.md — Option B 채택
- [Do]     ✅ 구현 완료 (877a7e2)
- [Check]  ⏳ 정적 분석 완료(96%), 런타임 E2E 미실행 (NATS 필요)
- [Report] ⏳
- [Archive]⏳

이번 세션 완료 작업 (sc-12-scope2)
------------------------------------
1. PRD 작성 (docs/00-pm/sc-12-scope2.prd.md)
   - SimulationMode 단일 플래그 문제 정의, FR-01~06, 성공 기준 5개

2. Plan 작성 (docs/01-plan/features/sc-12-scope2.plan.md)
   - Option B(SimulationMode 완전 제거) 확정
   - 성공 기준 SC-01~05, 구현 순서 6단계

3. Design 작성 (docs/02-design/features/sc-12-scope2.design.md)
   - 3가지 아키텍처 옵션 비교, Option B 선택
   - Module Map 5개, 주석 컨벤션([SC-12 범위 2] Design Ref: §N)

4. Do 구현 (877a7e2)
   a. ManagerSettings.cs — SimulationMode 제거, SimulateEnumeration + SimulateAgentMode 추가
   b. AgentManager/Program.cs — SimulateEnumeration으로 교체
   c. AgentSupervisor.cs:
      - spawn 스킵 조건: `SimulationMode` 제거 → `!File.Exists(AgentExePath)` 만
      - args 인덱스 버그 수정: [4]=SimulateAgentMode, [5]=logPath (Agent/Program.cs 기준)
   d. ManagerE2EDriver/Program.cs:
      - SimulationMode=true → SimulateEnumeration=true + SimulateAgentMode=true
      - FindAgentExe(): 솔루션 루트에서 Debug/Release 순으로 Agent.exe 자동 탐지
      - 범위 2 추가: 하트비트 대기(SubscribeAgentStatusAsync) → 캡처 cmd 발행 → 결과 수집 → 검증
   e. AgentManagerTests.cs:
      - AgentSupervisorSimTests: SimulationMode → 두 플래그 + 주석 추가
      - ManagerSettingsFlagTests 3건 신규: DefaultFalse / JsonRoundTrip / CanBeSetIndependently

KEY FILES (이번 세션 수정)
---------------------------
- HeatingCameraSystem.AgentManager/Config/ManagerSettings.cs
- HeatingCameraSystem.AgentManager/Program.cs
- HeatingCameraSystem.AgentManager/Services/AgentSupervisor.cs
- HeatingCameraSystem.ManagerE2EDriver/Program.cs
- HeatingCameraSystem.Tests/AgentManagerTests.cs
- docs/00-pm/sc-12-scope2.prd.md
- docs/01-plan/features/sc-12-scope2.plan.md
- docs/02-design/features/sc-12-scope2.design.md

IMPORTANT DECISIONS
-------------------
- Option B 채택: SimulationMode 완전 제거 → SimulateEnumeration + SimulateAgentMode
  이유: 단일 책임, JSON 호환(기존 필드 무시됨), 코드 명확성
- Agent args 인덱스 버그 수정 포함: [4]=SimulateAgentMode (Agent/Program.cs 기준)
  이전 코드는 [4]=logPath [5]=SimulationMode 로 순서가 맞지 않았음
- FindAgentExe(): 5레벨 상위(솔루션 루트)에서 Debug→Release 순 탐색
- 캡처 결과 timeout: 30초 (별도 TimeSpan, 승인 타임아웃과 분리)
- 모든 코드 변경에 [SC-12 범위 2] 주석 추가 (사용자 요청)

EXPLICIT CONSTRAINTS (다음 세션도 유지)
----------------------------------------
- Nullable 경고 억제 금지, 버그 수정 시 리팩터링 금지(최소 변경)
- PowerShell (no &&/tail), rtk wrapper, .slnx 포맷
- 타겟: Core/Protocols/Agent/E2EDriver=net8.0, Master/Tests=net8.0-windows,
  AgentManager/ManagerE2EDriver=net8.0 win-x64
- 주석: 사용자가 이해하기 쉽도록 항상 추가 (이번 세션부터 적용)

PENDING / NEXT (다음 세션)
--------------------------
[즉시 가능]
- NATS 기동 후 E2E 실행:
    docker compose -f docs/deployment/docker-compose.yml up -d
    dotnet build HeatingCameraSystem.Agent    ← exe 먼저 빌드
    dotnet run --project HeatingCameraSystem.ManagerE2EDriver
  → exit 0 확인 → SC-01/SC-02 체크 완료

[E2E PASS 후]
- /pdca analyze sc-12-scope2  → Check 문서 작성 (docs/03-analysis/)
- /pdca report  sc-12-scope2  → Report 작성 (docs/04-report/)
- /pdca archive sc-12-scope2  → Archive (docs/archive/2026-06/)

[기타 후보]
- 실HW E2E (SC-01~03, SC-06 — USB 카메라 + Windows Service)
- 신규 PDCA: #18 열화상/RGB 자동판별, #14 Recipe 소요시간 추정, NATS 인증

TECH NOTES
----------
- SimulationMode 흔적이 남은 곳:
  · HardwareSettings.cs(Core) — PLC/Serial 시뮬용, 별개 플래그. 건드리지 않음.
  · AgentConfig.cs(Core)     — Agent 자체 설정(agent.json), 별개. 건드리지 않음.
  · AppServices.cs(Master)   — HardwareSettings.SimulationMode 사용. 건드리지 않음.
  · Agent/Program.cs         — AgentConfig.SimulationMode. args[4]로 수신. 건드리지 않음.
- ManagerE2EDriver 범위 2: AgentExePath 없으면 captureRoundtrip=false → 범위 1만 실행
- 기존 ManagerStateStore, InventoryPublisher, ManagerCommandHandler 변경 없음
- FakeCameraCaptureService: OpenCV로 실제 640×480 JPEG 생성 → ImageBytes 비어있지 않음
