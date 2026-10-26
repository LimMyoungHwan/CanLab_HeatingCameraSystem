# 인계 — 2026-09-07 (생산 저장 회차 분리)

기준 커밋: `dd65e19` (origin/master와 동일, 작업 트리 clean)
build 0 errors / tests 444 passed

## 이 세션에서 한 것

### 1. 재실행 시 `.raw` 덮어쓰기 수정 (핵심)
증상: 같은 레시피 2회 실행 → 두 번째가 첫 번째를 덮어씀.
원인: 저장 경로에 실행 회차 요소가 없고 파일명이 매번 `_000`부터 시작.
- `RecipeEngine.NextRunProductNumber()` 신설 — 실행 시작 시 1회 계산
- `CaptureCommandMessage.ProductNumber`에 회차를 실어 보냄 → **Agent 코드 변경 0줄**
- 1회차 `{센서}_{제품}`, 2회차 `{센서}_{제품}_1`, 3회차 `_2`
- 제품번호 비면 회차만 반환 → `{센서}`, `{센서}_1`
- `ProductionRunDialog`의 제품번호 필수 검증 제거 (번역 키 `Dialog_ProductionRun_NeedProduct` 미사용 상태로 남음)

### 2. ShotCount 고정값 제거
`CaptureNamingRule.ShotCount()` + `RoomShotCount=10` / `BlackBodyShotCount=100` 삭제.
`RecipeEngine.cs:880`에서 항상 `step.ShotCount` 사용.
루트 `AGENTS.md`에는 아직 "장수는 하드웨어 계약"으로 적혀 있음 — 문서와 코드가 갈림.

### 3. BIAS 기본값 Core로 통합
`CameraBiasDefaults` (Core/Models/CameraControlMessages.cs) — Low 8500 / Mid 5000 / High 5000.
AgentUI 하드코딩 3곳 교체. 레시피 편집기에서 BIAS LOW/MID/HIGH 선택 시 자동 입력.

### 4. 캡처 계획 미리보기
`RecipeStepModel.CapturePlanSummary` + XAML 한 줄.
`→ 30분 동안 1분 간격으로 30장씩 30회 · 총 900장` + 경고 2종.
라벨: 촬영 장수→1회당 장수, 간격→반복 간격, 전체 시간→반복 지속 시간.

## 검증 방법 (카메라·PLC 없이 가능)
`RecipeEngineTests.ExecuteRecipeAsync_SameRecipeTwice_KeepsBothRunsInSeparateProductFolders`
실제 `RecipeEngine` + 실제 `ProductionCaptureSink`, NATS 자리에만 Agent 대역 mock.
넘버링을 되돌리면 이 테스트가 실패함(확인 완료) — 헛돌지 않는다.

**시뮬레이터로는 `.raw` 경로 검증 불가.** `HeatingCameraSystem.Simulator`에 `ProductionCaptureSink` 참조가 0건이라 저장 트리를 아예 안 지난다.

## 미해결 / 다음 확인

1. **후처리 툴 호환 (전달 전 확인 필요)**
   `_1` 접미사가 붙은 `{센서}_{제품}_1` 폴더를 고객사 툴이 읽는지 미검증.
   툴이 `{센서}_{제품}` 형식을 파싱하면 깨진다. 깨지면 대안은 `{SaveRoot}/run_{x}/` 로 한 계층 위에 얹기(계약 서브트리 무변경).

2. **회차 판정이 robocopy에 의존**
   `NextRunProductNumber`는 Master UNC를 훑는다. Agent가 아직 전송 못 했으면 2회차도 무접미사로 나가 덮어쓴다.
   `ProductionCaptureSink.PendingFiles`로 로컬 잔여를 확인하는 가드가 없음.

3. **제품번호 유/무 실행을 한 SaveRoot에 섞으면 회차 공유** (`ponytail:` 주석으로 표시됨)

4. **장시간 사용 시 느려짐 (이전 세션에서 이월, 미해결)**
   - 카메라 1대에서도 발생 → 대수 스케일 문제 아님, 시간 경과형
   - 트렌드 샘플(60건 상한) / 알람(100건 상한)은 무죄로 확인됨
   - 남은 용의자: `SubscribeLiveFramesAsync`의 `BitmapSource` `Freeze()` 여부 / `LoadCameraFeeds()` 호출 빈도(하트비트마다 재구성 의심) / 이벤트 핸들러 미해제(`LivePreviewColorMode.Changed`, `PlcStatus.Updated`, `EmergencyStopChanged`) / LiteDB 증가 / `ThermalNucCorrector.Apply` LOH 600KB
   - 앞의 3개는 코드 읽기만으로 판별 가능
   - 물어볼 것: Master인가 AgentUI인가 / 재시작하면 회복되나 / 몇 분쯤부터

## 참고 (변경 없음, 오해 방지용)
- `.raw`는 NUC 미보정 원본. `.y16`/미리보기만 소프트웨어 NUC 적용.
- NUC 기준은 `CameraPanelViewModel.RunNucAsync`에서만 생성 — 스텝 op이 `nuc`이거나 수동 버튼일 때만. 촬영이 NUC를 유발하지 않는다.
- 규칙 저장 중에는 `.y16`과 캡처 결과를 배치당 1건만 발행 (`PendingCapture.ExpectedResults`는 찍을 장수가 아님).

## 상태
사용자가 위 내용으로 고객 안내 메일 발송 완료. 내일 사용자 피드백 수신 예정.
