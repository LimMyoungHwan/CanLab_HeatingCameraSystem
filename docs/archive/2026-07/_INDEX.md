# Archive Index — 2026-07

완료된 PDCA 사이클의 문서를 보관한다. 활성 작업은 `docs/01-plan` ~ `docs/04-report` 에 있다.

| Feature | 완료일 | Match Rate | 문서 |
|---|---|:---:|---|
| **camera-model-select** | 2026-07-08 | 100% | [prd](./camera-model-select/camera-model-select.prd.md) · [plan](./camera-model-select/camera-model-select.plan.md) · [design](./camera-model-select/camera-model-select.design.md) · [analysis](./camera-model-select/camera-model-select.analysis.md) · [report](./camera-model-select/camera-model-select.report.md) |

## camera-model-select 요약

| 항목 | 값 |
|---|---|
| 기능 | 카메라 모델별 해상도 파일 기반 설정 — `agent.json.CameraModel` + `CameraModels\{모델}.json` |
| 아키텍처 | 파일 기반 (Master UI/LiteDB 없음) — 신규 모델 = JSON 파일 하나 |
| FR | 6/6 |
| SC | 5/5 (100%) |
| 테스트 | 69/69 통과 (기존 64 + 신규 5) |
| E2E | PASS, 회귀 없음 |
| 후속 (별도 진행) | 카메라 실물 입고 후 해상도 실측 검증, 셔터 프로토콜/BaudRate 검증 (다음 주) |
