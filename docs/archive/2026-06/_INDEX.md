# Archive Index — 2026-06

완료된 PDCA 사이클의 문서를 보관한다. 활성 작업은 `docs/01-plan` ~ `docs/04-report` 에 있다.

| Feature | 완료일 | Match Rate | 문서 |
|---|---|:---:|---|
| **agent-manager** | 2026-06-21 | 96% | [plan](./agent-manager/agent-manager.plan.md) · [design](./agent-manager/agent-manager.design.md) · [analysis](./agent-manager/agent-manager.analysis.md) · [report](./agent-manager/agent-manager.report.md) |
| **sc-12-scope2** | 2026-06-29 | 100% | [prd](./sc-12-scope2/sc-12-scope2.prd.md) · [plan](./sc-12-scope2/sc-12-scope2.plan.md) · [design](./sc-12-scope2/sc-12-scope2.design.md) · [analysis](./sc-12-scope2/sc-12-scope2.analysis.md) · [report](./sc-12-scope2/sc-12-scope2.report.md) |

## sc-12-scope2 요약

| 항목 | 값 |
|---|---|
| 기능 | SimulationMode 플래그 분리(SimulateEnumeration/SimulateAgentMode) + ManagerE2EDriver 캡처 Roundtrip E2E |
| 아키텍처 | Option B: SimulationMode 완전 제거, 단일 책임 플래그 2개로 대체 |
| FR | 6/6 |
| SC | 5/5 (100%) |
| 테스트 | 64/64 통과 |
| E2E | PASS (범위 1 승인 루프 + 범위 2 캡처 roundtrip, exit 0) |
| 후속 (별도 진행) | camera-model-select (PRD 작성 완료, 사용자 스코프 확인 대기), 카메라 실물 연결 검증(셔터 프로토콜/BaudRate) |

## agent-manager 요약

| 항목 | 값 |
|---|---|
| 기능 | PC당 Agent Manager (Windows Service) — WMI 카메라 자동 발견 + Agent supervisor + NDJSON 로그 |
| 아키텍처 | Option C: Distributed Supervisor |
| FR | 20/20 |
| Decision Compliance | 8/8 (100%) |
| 후속 (archive 후 별도 진행) | SC-12 승인 루프 E2E (`0aeb829` 이전 `a16ddb9`), GAP-6 DevicesView 시리얼 UI (`0aeb829`), 매뉴얼 Manager 섹션 (`9feb873`) |
