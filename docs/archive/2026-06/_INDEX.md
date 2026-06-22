# Archive Index — 2026-06

완료된 PDCA 사이클의 문서를 보관한다. 활성 작업은 `docs/01-plan` ~ `docs/04-report` 에 있다.

| Feature | 완료일 | Match Rate | 문서 |
|---|---|:---:|---|
| **agent-manager** | 2026-06-21 | 96% | [plan](./agent-manager/agent-manager.plan.md) · [design](./agent-manager/agent-manager.design.md) · [analysis](./agent-manager/agent-manager.analysis.md) · [report](./agent-manager/agent-manager.report.md) |

## agent-manager 요약

| 항목 | 값 |
|---|---|
| 기능 | PC당 Agent Manager (Windows Service) — WMI 카메라 자동 발견 + Agent supervisor + NDJSON 로그 |
| 아키텍처 | Option C: Distributed Supervisor |
| FR | 20/20 |
| Decision Compliance | 8/8 (100%) |
| 후속 (archive 후 별도 진행) | SC-12 승인 루프 E2E (`0aeb829` 이전 `a16ddb9`), GAP-6 DevicesView 시리얼 UI (`0aeb829`), 매뉴얼 Manager 섹션 (`9feb873`) |
