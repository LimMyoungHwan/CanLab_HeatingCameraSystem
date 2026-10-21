# 연결형 촬영 계획 — 실행/저장 계약

> 2026-10-21 · 기존 고정 매트릭스 모델을 대체하는 단일 Plan 모델.
> 실자료 조사: [capture-plan-template.analysis.md](capture-plan-template.analysis.md).

## 1. 사용자 결정

| 항목 | 확정 계약 |
|---|---|
| 반복 범위 | 템플릿은 **카메라 한 대용 스텝 레시피**. 선택 카메라마다 **레시피 전체**를 저장된 순서 그대로 반복 |
| 반복되는 동작 | 챔버/BB/Wait/BIAS/NUC/이동/Capture 모두 원본대로 반복. 자동 dedup/병합/준비 절차 추출 없음 |
| 연결 | 원본 RecipeId **live link**. 원본 저장은 연결된 모든 계획의 다음 컴파일에 즉시 반영. 승인 스냅샷 없음 |
| 실행 고정 | 시작 시 현재 템플릿과 위치표를 한 번 읽어 실행 복사본을 컴파일. 실행 중 템플릿 편집은 진행 중 스텝 목록을 바꾸지 않음 |
| 카메라 | 실제 heartbeat의 **AgentId + Agent 내부 CameraIndex(0 포함)**. AgentId 접미사 추정/1..64 가상 목록 금지 |
| 보드 셀 | `(entryKey, 실제 카메라 identity)`. 역할은 셀 아래 각 카메라 명령의 상태에 표시 |
| 재개 | 현재 live 템플릿/위치로 재컴파일. Done 셀은 유지, 남은/실패 카메라는 **레시피 전체를 처음부터** 실행 |

## 2. 모델

`Recipe.Plan == null`은 기존 스텝 편집·실행을 유지한다. `CapturePlan.Entries`는 순서가 있는 목록이며 항목은 다음만 저장한다.

- 안정 `Key` (추가 시 발급, 정렬/편집 시 유지)
- `TemplateRecipeId` (Plan=null 레시피만 연결, 중첩 계획 금지)
- 선택 `Cameras` (실제 AgentId/CameraIndex, 선택 순서 유지)

실행 순서는 **항목 → 선택 카메라 → 템플릿 스텝**이다. 조건·역할별 별도 패턴/고정 Hot→Cold→Room 순서/BB ensure 모델은 없다. 같은 템플릿을 여러 항목에서 연결할 수 있다.

### 템플릿 슬롯

| 속성 | 적용 종류 | 계획에서만 수행하는 치환 |
|---|---|---|
| `RecipeStep.UseCellCamera` (기본 false) | CameraCommand | AgentId/CameraIndex만 셀 identity로 교체. CameraAlias는 실행 복사본에서 제거. 대상의 **대역/역할·명령·장수·BIAS 값은 원본 유지** |
| `RecipeStep.CellPositionRole` (기본 null) | MotorMove | Hot/Cold/Room 중 지정 역할의 공유 위치로 Point/XY 치환. 나머지 이동 옵션 유지 |

원본 targets/Point/XY는 **독립 실행 기본값**으로 남는다. 모터 카드에서 슬롯을 처음 켜면 뒤에 나오는 첫 Capture의 선택 대상 역할로 초기화하며, 없으면 Cold를 표시하여 운영자가 명시적으로 변경할 수 있다. 슬롯을 끄면 고정 이동이다. 카메라 카드에는 작은 ‘계획 슬롯: 셀 카메라’ 체크를 표시한다. 슬롯 지정은 원본 레시피를 저장해야 live 연결에 반영된다. CSV/JSON/LiteDB 및 편집 복사에서 두 슬롯을 보존한다.

## 3. 실제 카메라와 위치표

- `AgentDirectory.Note`는 heartbeat마다 AgentId/CameraIndex 조합의 last-seen을 갱신한다. 15초(기존 threshold) 안의 실제 카메라만 새 선택 후보가 된다. 같은 Agent의 여러 CameraIndex는 별개다.
- 이미 저장된 카메라는 현재 오프라인이어도 선택 목록과 보드에 **오프라인**으로 남긴다. 사용자가 해제하지 않은 대상을 자동으로 삭제하지 않는다. 오프라인 실행 시 기존 엔진 preflight/건너뜀 처리를 유지한다.
- `camera_positions`의 새 키는 실제 camera identity × **Hot/Cold/Room 역할**이다. Room도 반드시 명시적 슬롯 위치가 있어야 한다. 물리 흑체 유닛 번호를 위치 키로 사용하지 않는다.
- Point는 2 이상이며 **Point 1은 좌표 이동 예약**이다. 저장된 Point→Coordinate 변경은 거절하고, 번호 변경/삭제 후 신규 좌표 등록은 가능하다. 저장소 인스턴스가 둘이어도 같은 DB 락으로 보호한다.
- 이전 위치 행은 원본 레시피의 동일 이동 위치/대상 CameraIndex/다음 Capture 역할에서 **실제 identity와 역할이 유일하게 확인될 때만** 재키잉한다. AgentId 숫자 접미사를 추측하지 않는다. 모호하거나 근거 없는 행은 원본을 남기고 현재 위치표에서 제외하며 UI에 건수와 재등록 안내를 표시한다. 새로 티칭한 행을 이관 행으로 덮지 않는다.

## 4. 컴파일과 번호

- 템플릿/카메라/항목/슬롯/스텝 식별자·위치를 실행 전에 검증한다. 없는 템플릿, 중첩 계획, 중복 키/카메라/StepId, 미해결 역할 위치, 잘못된 Point는 실행 불가다. 템플릿의 CameraCommand는 한 카메라용이어야 한다.
- 모든 생성 스텝에 같은 카메라 셀의 PlanCellKey를 붙인다. 기존 스텝 종류를 새 엔진 명령으로 바꾸지 않는다.
- 결정적 StepId는 SHA-256의 앞 16바이트 Guid로, `capture-plan-template-v2 + planRecipeId + entryKey + cameraIdentity + templateStepId`에서 생성한다. 문자열은 길이 접두로 경계를 구분한다. 순서 인덱스/시각/임의 새 Guid는 사용하지 않는다.
- 템플릿 StepId는 수정/정렬에서 유지하고 신규·복제 스텝은 새 ID를 받는다. 다른 카메라 추가/삭제/정렬은 살아 있는 스텝 ID를 바꾸지 않는다.
- 저장 때 실패해도 Plan/원인을 저장하는 **초안 저장**을 허용하고 Steps는 비운다. Dashboard는 매 시작 현재 템플릿·위치로 재컴파일하며 저장된 생성 Steps를 신뢰하지 않는다. 부분 재개 스텝으로 원본 계획을 덮어쓰지 않는다.

### 여러 Capture의 생산 shot 번호

`CaptureNamingRule`의 폴더/접두사/확장자 규칙은 그대로다. 같은 실제 카메라의 같은 챔버 **목표 온도 코드**·대역·역할 폴더에 쓰는 Capture들은 항목 경계와 관계없이 누적 shot 수를 `ShotNumberOffset`에 배정한다. 원본 템플릿 offset은 계획에서 재계산한다.

- 다음 offset = 이전 offset + `ShotCount × CaptureRepeatCount`.
- 엔진의 반복 회차는 `offset + round × ShotCount`; Agent 저장 index는 여기에 local index를 더한다.
- 서로 다른 실제 카메라는 별도 번호열. 온도는 실측값이 아니라 직전 ChamberControl 목표를 기존 규칙으로 스냅한다.
- 챔버 스텝이 없는 촬영 전용 템플릿도 기존 엔진의 런타임 목표 fallback을 유지한다. 컴파일 시 첫 목표를 알 수 없는 번호열은 온도 코드 간에도 보수적으로 번호를 예약하여 충돌을 피한다(다른 폴더에 번호 간격이 생길 수 있음).
- 제외 셀은 이번 부분 실행의 번호를 소비하지 않는다. 재개/재촬영은 기존 제품 회차 폴더 정책을 사용한다. 옛 폴더 병합/삭제/잔여 파일 청소를 임의로 추가하지 않는다.

## 5. 영수증/재개/변경 경고

`capture_plan_runs` v2 영수증은 셀마다 **모든 필수 스텝 ID와 결과**, 카메라 identity, 항목 표시명, 실패 이유를 저장한다. 카메라 명령은 대역이 아닌 실제 원본 역할/명령별 하위 상태를 표시한다.

- 첫 셀 동작부터 Running. **모든 스텝**의 Succeeded가 확인되어야 Done이다. 첫 Capture 성공/성공 이력 한 건으로 완료를 판단하지 않는다. BIAS/NUC/후속 Wait/이동 실패나 건너뜀도 Done이 아니다.
- fork Capture는 엔진이 결과를 확정한 후 성공 이벤트를 내는 기존 계약을 따른다. 셀 경계를 넘어 미완료 동작을 남기는 작성은 피해야 한다.
- 중단된 Running 시도는 시작 시 Interrupted로 복구하고 진행 셀은 Failed, 미실행 셀은 Pending으로 남긴다.
- 재개는 최신 root의 영수증을 셀 키로 합성하여 Done 제외, 실패만 재촬영은 Failed만 선택한다. 각 시도는 새 RunId와 root/parent 체인을 갖는다.
- 런은 사용 템플릿의 내용 fingerprint를 저장한다. 이후 live 템플릿이 변경되면 보드와 재개 확인창에 **‘템플릿이 이전 시도 이후 변경됨’**을 표시한다. 현재 템플릿으로 재컴파일하되 이미 Done인 셀은 이전 결과 그대로 남는다는 점도 명시한다. 동일 셀/StepId가 동일 동작을 보증한다고 주장하지 않는다.
- 재개는 선택 카메라 확장을 맨 처음부터 재실행하므로 원본에 들어 있는 챔버/BB/Wait/BIAS 전제조건도 반복한다. 별도 준비 스텝을 삽입하거나 dedup하지 않는다.

## 6. 연결 관리와 구형 데이터

- 스텝 편집기는 이 레시피를 사용하는 **계획 이름 목록**을 보여 준다. 사용 중 템플릿 삭제는 ViewModel과 저장소 모두 거절하며 계획 이름을 안내한다. 먼저 계획에서 다른 템플릿으로 바꾸거나 항목/계획을 삭제해야 한다.
- ‘계획으로 정확 변환’은 제거한다. **‘이 레시피로 계획 만들기’**는 원본 ID를 연결한 초안을 만들 뿐 슬롯/카메라/순서를 추정하지 않는다.
- `Entries`가 없는 이전 고정 Plan은 **‘이전 형식 계획입니다. 다시 만들어 주세요.’**로 읽고 캐시 Steps를 메모리에서 비워 실행을 막는다. 자동 변환/낡은 Steps 폴백은 하지 않는다. 원본 파일은 로드만으로 저장/변경하지 않는다.
- 이전 `capture_plan_runs`는 삭제하지 않고 v2 실행 체인에서 제외한다. 이전 영수증의 필드 누락으로 앱이 중단되거나 Done이 잘못 승계되지 않게 한다.

## 7. 검증 계약

- 실자료형 MP10(`T10 BB80/20 M W30m BIAS C100 M C100 M C100`) × 실제 카메라 2대의 정확 순서/대상/위치/offset.
- 다중 Capture/반복 회차/여러 항목 간 같은 생산 폴더 번호 누적, 결정적 ID, live 원본 편집 반영, 원본/실행 복사본 분리.
- 사용 중 삭제 거절, 온라인 후보만 표시, 저장 오프라인 대상 유지, Point 보호, 위치 이관/미해결 안내, 초안 저장, 구형 계획/영수증 안전 처리.
- 일부 Capture 실패 뒤 셀 전체 재개, 변경 경고, 모든 스텝 성공 후에만 Done.
- 실제 WPF 계획 편집기/보드/레거시 슬롯 카드의 headless STA PNG와 binding warning 0. 임시 저장소만 사용하며 사용자 런타임 파일에 접근/수정하지 않는다.

## 8. 수행한 검증 (2026-10-21)

- `dotnet build`: 오류 0 / 경고 0.
- `dotnet test --no-build`: **613 통과 / 실패 0 / 건너뜀 0**. 교체 케이스/그룹별 수와 일반 안전 회귀 복구는 [테스트 감사](capture-plan-template-test-audit.md) 참조.
- Headless STA 렌더 4장, binding warning **0**. underscore 포함 실제 AgentId의 정확한 TextBlock 표시, CAM-00 형식, 공통 운영자 명령 라벨, 30분 대기 및 빈 카메라 선택 안내를 검증했다.
- 출력: `%TEMP%\HeatingCameraSystem-template-plan-renders\01-template-plan-editor.png`, `02-template-plan-board.png`, `03-legacy-template-slots.png`, `04-legacy-empty-camera.png`. 같은 폴더의 `binding-warnings.txt`/`render-result.txt`/`template-review.trx`가 증거다.
- 렌더/단위 테스트는 임시 파일 저장소·메모리 LiteDB/가짜 PLC·NATS만 사용했으며 임시 runtime 디렉터리는 삭제했다. 사용자 런타임 데이터·기존 백업은 수정하지 않았다.
