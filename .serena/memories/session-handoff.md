# Session Handoff — 2026-07-08 갱신

## 최신 현황 (2026-07-08)
- `camera-model-select` PDCA 전 사이클 완료 (PM→Plan→Design→Do→Check→Report→Archive), Match Rate 100%
  → 최종 구조: 파일 기반 (Master UI/LiteDB 폐기) — `agent.json.CameraModel` + `CameraModels\{모델}.json`
  → 신규 파일: `Core/Models/CameraModelSpec.cs`, `Agent/CameraModels/{AISEN,FPV}.json`, `Tests/CameraModelSpecTests.cs`
  → `docs/archive/2026-07/camera-model-select/` 로 이동
  → 빌드 0/0, 테스트 69/69(기존64+신규5), E2E 회귀 없음(exit 0)
  → **미완**: 카메라 실물로 해상도 실적용 검증 — 다음 주 입고 예정
- 셔터 프로토콜(`04 00 01...` vs `43 4C 30 01...`)/BaudRate(9600 vs 115200) 검증도 카메라 입고 후 진행 (`mem:camera-connection-verification`)
- **미커밋**: 이번 세션 변경사항 아직 git add/commit 안 됨 — 사용자 요청 시 커밋/푸시

---

# Session Handoff — 2026-06-29 갱신 (이전)

## 최신 현황 (2026-06-29)
- SC-12 범위 2 PDCA 전 사이클 완료 (PM→Plan→Design→Do→Check→Report→Archive), Match Rate 100%
  → `docs/archive/2026-06/sc-12-scope2/` 로 이동, `_INDEX.md` 갱신
- 신규 기능 `camera-model-select` PRD 작성 완료 (`docs/00-pm/camera-model-select.prd.md`)
  → `참고/AISEN_CODE`(CBS014d 640x480) vs `참고/FPV_code`(FPV024d 320x240) 벤더 코드 분석 기반
  → 스코프: 해상도만 포함, bias/레지스터값(TINT/CINT/GSK/GFID) 제외 — **사용자 확인 대기 중**
  → 확인되면 Plan/Design 진행
- 카메라 실물 연결 검증 항목 2건 BLOCKED (물리 하드웨어 필요, `mem:camera-connection-verification` 참조)

---

# Session Handoff — 2026-06-23 종료 (이전)

## 현황
- SC-12 범위 2 (SimulationMode 분리 + 캡처 Roundtrip E2E) PDCA Do 완료
- 구현 커밋: `877a7e2`, 본 핸드오프: `b272e6b`
- origin/master push 완료

## 완료
- PRD/Plan/Design/Do 모두 완료 (Check/Report/Archive는 다음 세션)
- 9 projects 빌드 0 errors/0 warnings, 테스트 64/64 통과
- 3가지 핵심 설계 결정 (1-B/2-B/3-A), args 인덱스 버그 수정

## 다음 세션 할 일
1. NATS 기동 후 E2E 실행 (docker compose up -d → build Agent → run ManagerE2EDriver)
2. 전 구간 통과 시 Check/Report/Archive PDCA 마무리
3. 이후 #18(열화상/RGB 자동판별), #14(Recipe 소요시간 추정), NATS 인증 등 후보

## 중요 맥락
- SimulationMode 흔적: HardwareSettings.cs(AppServices), AgentConfig.cs(Agent/Program.cs)는 별개
- ManagerE2EDriver E2E는 NATS 서버 필요 (nats://127.0.0.1:4222)
- 자세한 맥락: `.omo/handoff-2026-06-23-session-close.md` 참조
