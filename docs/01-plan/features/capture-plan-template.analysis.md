# 촬영 계획의 연결형 스텝 템플릿 검토

> 2026-10-21 · §2–4는 당시 원본 복사본 조사 결과. 이후 사용자 확정에 따른 구현 계약은 §5 및 [연결형 계획 명세](capture-plan-matrix.plan.md) 참조. 원본 런타임 파일은 변경하지 않음.

## 1. 결론

**고정 패턴 대신 기존 스텝 레시피에 명시적 슬롯을 두고 연결한다.** 사용자 결정은 **카메라 한 대용 레시피 전체를 선택 카메라마다 그대로 반복 + RecipeId live link**다. 별도 조건 준비/역할 패턴 분할·승인 스냅샷은 사용하지 않는다.

- 조사 당시 활성 저장 레시피 **18개 모두 `Plan == null`**, 당시 고정 변환기 수용 **0/18(0%)**. 촬영이 있는 12개만 보아도 **0/12**였다.
- 생산형 9개는 `T → BB → 첫 위치 → 30분 대기 → [BIAS] → Capture → 다른 위치/Capture`다. 이제 이 **전체 순서를 카메라마다 반복**하므로 30분 대기/BIAS도 원본에 있는 위치에서 카메라마다 수행한다.
- Cold→Hot→Room 또는 LN30의 Cold→Room→Hot 순서는 템플릿의 원본 스텝 순서로 유지한다.
- 실제 대상은 대부분 **명시적 AgentId + CameraIndex=0**이다. 새 identity는 heartbeat의 실제 조합을 쓰며 논리 1..64/숫자 접미사 추정을 없앴다.
- 자료만으로 불명확했던 반복 범위와 연결/재개 정책은 사용자가 확정했다. 원본을 자동 분해하거나 스텝을 dedup하지 않는다.

## 2. 조사 범위와 원본 보호

| 항목 | 확인 결과 |
|---|---|
| 현재 활성 저장소 | `AppServices.cs:92-104`에서 `FileRecipeRepository(dir)`를 `RecipeRepo`에 할당. LiteDB는 1회 이관 원천이며 현재 레시피 저장소가 아님 |
| 활성 JSON | `%LOCALAPPDATA%\HeatingCameraSystem\recipe\{Id}.json` **18개** |
| 자동 백업 | 같은 루트의 `recipe bak` **613개**. 현 목록의 별도 레시피로 중복 계산하지 않음 |
| LiteDB | `data.db` **21,635,072바이트**, 동반 `data-log.db` **2,416,640바이트**를 임시 폴더로 복사. 해당 이름의 journal 파일은 없었음 |
| 복사본 조회 | LiteDB 5.0.21 `ConnectionString.ReadOnly=true`. `recipes` 컬렉션 **0건**. `capture_plan_runs` 등은 존재 |
| 내보내기 | 저장소에서 CSV/recipe 이름의 JSON을 검색. 가져오기/내보내기 코드에는 고정 `InitialDirectory`가 없음. 활성 레시피가 가리키는 `C:\work\20260930, 20261001, 20261002, 20261012, 20261014, 20261015` 경로는 현 머신에 없음. `E:\tmp`에서 **`Test1.csv` 1개** 발견 |
| CSV 내용 | `Test1`, Plan 없음, **7스텝**. 현재 JSON Test1과 같은 동작·대상·장수. 아래 Test1 행 참조. 독립적인 신규 레시피로 중복 계산하지 않음 |
| 실행한 조사 | 임시 .NET 8 콘솔이 현재 Core 소스를 별도 프로젝트에 포함하여 JSON 복사본 631개 역직렬화, 현 `CapturePlanConverter.ConvertToPlanCopy` 호출. 제품 프로젝트 빌드/테스트나 장치 실행은 하지 않음 |

원본 DB는 읽기 공유(FileShare.ReadWrite/Delete)로 **복사만** 했으며 원본 LiteDB 연결·체크포인트·마이그레이션·저장소 초기화를 실행하지 않았다. 실행 중인 앱을 중지하지 않았다. DB와 로그의 순차 복사는 트랜잭션 스냅샷은 아니므로 실행 이력의 순간적 일관성까지 보증하지 않는다. 레시피 판정의 주 근거는 활성 JSON 복사본이다. 전체 디스크 검색은 하지 않았다.

## 3. 활성 레시피 전수 목록

표기: `T` 챔버 ℃, `BBa/b` 흑체 1/2 ℃, `(set)` 안정 대기 없음, `M(Pn)` Point 이동, `M(x,y)` 좌표 이동, `W` 시간, `Cn` 장수, `F` 팬 Hz. 모든 행의 **Plan은 null**이다.

대상 약호: **A1**=`DESKTOP-3T03TKP_Agent_1`, **A5**=`DESKTOP-3T03TKP_Agent_5`, **F3**=`FOXSTARSOFTPC_Agent_3`. `0/A1`은 저장된 대상 CameraIndex=0/AgentId=A1을 뜻하며, 번호를 1로 추정·수정했다는 뜻이 아니다. 각 행의 명령 번호는 1부터 시작한다.

| 이름 | 스텝 | 전체 동작 순서 | CameraCommand별 대상: 스텝 번호 → 장치·대역·역할 |
|---|---:|---|---|
| LN30 | 10 | `T-30 BB70/10 M(P7) W30m BIAS_LOW C100 M(P8) C100 M(P9) C100` | #5 BIAS/#6 C: `0/A5 Low Cold`; #8 C: `0/A5 Low Room`; #10 C: `0/A5 Low Hot` |
| LN10 | 9 | `T-10 BB70/10(set) M(P7) W30m C100 M(P8) C100 M(P9) C100` | #5/#7/#9 C: 각각 `0/A5 Low Cold/Hot/Room` |
| LP10 | 9 | `T10 BB70/10 M(P7) W30m C100 M(P8) C100 M(P9) C100` | #5/#7/#9 C: 각각 `0/A1 Low Cold/Hot/Room` |
| MP10 | 10 | `T10 BB80/20 M(P12) W30m BIAS_MID C100 M(P13) C100 M(P14) C100` | #5 BIAS/#6 C: `0/A1 Mid Cold`; #8/#10 C: `0/A1 Mid Hot/Room` |
| MP25 | 9 | `T25 BB80/20 M(P12) W30m C100 M(P13) C100 M(P14) C100` | #5/#7/#9 C: 각각 `0/A1 Mid Cold/Hot/Room` |
| MP40 | 9 | `T40 BB80/20 M(P12) W30m C100 M(P13) C100 M(P14) C100` | #5/#7/#9 C: 각각 `0/A1 Mid Cold/Hot/Room` |
| RP40 | 10 | `T40 BB100/40 M(P12) W30m BIAS_HIGH C100 M(P13) C100 M(P14) C100` | #5 BIAS/#6 C: `0/A1 High Cold`; #8/#10 C: `0/A1 High Hot/Room` |
| RP50 | 9 | `T50 BB100/40 M(P12) W30m C100 M(P13) C100 M(P14) C100` | #5/#7/#9 C: 각각 `0/A1 High Cold/Hot/Room` |
| RP60 | 9 | `T60 BB100/40 M(P12) W30m C100 M(P13) C100 M(P14) C100` | #5/#7/#9 C: 각각 `0/A1 High Cold/Hot/Room` |
| capture | 10 | `M(P12) W2s BIAS_MID C100 M(P13) W2s C100 M(P14) W2s C10` | #3 BIAS/#4 C: `0/A1 Mid Cold`; #7/#10 C: `0/A1 Mid Hot/Room` |
| RP10S_10C_10B | 3 | `T50 BB70/80 M(P8)` | 없음 |
| RP10S_10C_10B_Capture | 3 | `M(P8) W3m C100` | #3 C: `0/A5 High Cold` |
| BiasSerch | 1 | `BIAS_MID` | #1 BIAS: `0/A1 Mid Cold` |
| BlackBody | 1 | `BB10/15` | 없음 |
| WaitTest | 2 | `T30 W30m` | 없음 |
| 60Receipe | 1 | `T60` | 없음 |
| Test1 | 7 | `T50 M(P1) BB70/30(set) C5 F30 M(0,0) T25` | #4 C: `1/F3`, **대역/역할 모두 null** |
| camera | 1 | `C2` | #1 C: `1/F3 Mid Room` |

### 대상·반복·촬영 파라미터

- 활성 **CameraCommand 38개 = Capture 33 + BIAS 5**. **각 명령 대상은 정확히 1개**, 빈 targets/다중 대상은 없다. CameraAlias도 모두 null이다.
- A1/A5 명령 **36개**는 step.CameraIndex와 target.CameraIndex가 **모두 0**이다. F3 명령 2개는 둘 다 **1**. `ActiveTargetsAsync`는 targets가 존재하면 이 목록을 그대로 사용하고, `ResolveTargetAgentIdAsync`는 명시적 AgentId를 우선한다. `_Agent_5` 접미사만 보고 CameraIndex=5라고 단정하면 안 된다.
- 저장된 명령 대상의 **서로 다른 AgentId 3개**, CameraIndex 값은 `{0,1}`. 각 활성 레시피 안의 명령 대상 장치는 1개뿐이다. 실제 물리 카메라 수/온라인 상태는 이 정적 자료만으로 확정하지 않는다.
- 생산형 9개는 **각 3위치/3역할, 장치 1개**. 전체 활성 이동은 **Point 7종(P1/7/8/9/12/13/14) + 좌표 1종(0,0)**. 같은 P8도 LN30에서는 Room, LN10에서는 Hot이다. 자동 공유 위치표 등록은 위험하며 기존처럼 레시피 로컬 override를 우선해야 한다.
- 생산형 9개는 **W30m가 첫 이동 후 한 번만** 나온다. BIAS는 그중 LN30/MP10/RP40에서만 첫 Cold 촬영 전에 한 번; 나머지 6개에는 없다. 모든 위치마다 BIAS를 하지는 않는다.
- `capture`는 **위치마다 W2s**, BIAS는 첫 Cold에서만 한 번. `RP10S_10C_10B_Capture`는 유일한 위치에서 W3m. `WaitTest`는 촬영 없는 대기, `BiasSerch`는 독립 BIAS다. **단일 전역 ‘대기/BIAS 옵션’으로 표현할 자료가 아니다.**
- 활성 및 조사한 백업에서 **NUC 명령은 발견되지 않았다.** ‘NUC는 필요 없다’는 결론은 아니다.
- 생산형 9개는 Capture당 **100장으로 균일**. `capture`는 **100/100/10장으로 역할별 다름**, Test1은 5장, camera는 2장. 모든 활성 Capture는 interval=0/duration=0, `WaitForCaptureResult=true`, BiasTargetLevel=0이다. BIAS 목표는 LN30/MP10/RP40 **8500**, capture/BiasSerch **5000**이다.

### 백업에서 추가로 확인된 내용

- 613개는 25개 이름 그룹의 과거 저장본이다. **612개 스텝형의 변환 성공도 0/612**. 나머지 1개는 `새 촬영 계획_20261021_105206.json`: **Plan 존재, Steps=0, 셀 목록 비어 있는 초안**이며 활성 목록에는 없다. 이미 계획이므로 변환기 수용률의 분모에서 제외한다.
- 백업 분포: RP10S_10C_10B_Capture 262, RP10S_10C_10B 166, LN30 37, capture 32, 새 레시피 26, LN10 19, BiasSerch 12, LP10 8, N30 7, BlackBody/Test1 각 6, RP10S_10C_10B Capture 5, RP40/WaitTest 각 4, MP10/RP50 각 3, 60Receipe/New Recipe/RP60/테스트1 각 2, MP25/RP10S_10C_10B (복사)/camera/새 레시피 1/새 촬영 계획 각 1.
- **빈 CameraTargets가 있는 백업은 5개**다. `BiasSerch_20260930_165145.json`, `_165256.json`은 #1 `bias`, step.CameraIndex=**1**. `RP10S_10C_10B_Capture_20261014_134411.json`, `_134418.json`, `_134435.json`은 #2 Capture100, step.CameraIndex=**0**. 이 경우 엔진은 빈 목록을 단일 fallback target으로 만들고 **step.CameraIndex 1/0을 그대로 사용**한다. 대역/역할도 자동 복구되지 않는다. 활성 자료와 혼동하지 않는다.

## 4. 조사 당시 고정 패턴 변환 실패 (구형 모델)

복사본에 당시 변환기를 직접 호출한 **첫 거절 지점**이다. 아래는 구형 모델의 조사 기록이며, 현재 구현은 이 변환기를 제거하고 템플릿 연결로 대체했다.

| 대상 | 첫 거절 | 그 뒤에도 남는 제약 |
|---|---|---|
| LN30/LP10/MP10/MP25/MP40/RP40/RP50/RP60 (8개) | #4 Wait | 일부 BIAS, 대상 index=0, Cold 우선/임의 역할 순서 |
| LN10 | #2 BB가 set-only | Wait, index=0, Cold 우선 |
| capture/RP10S_10C_10B_Capture/BiasSerch/BlackBody/camera (5개) | #1이 Chamber가 아님 | 독립 재사용 절차/촬영. capture는 Wait·BIAS·장수 차이도 존재 |
| WaitTest | #2가 Move가 아님 | 촬영 셀 자체가 없음 |
| 60Receipe | 촬영 셀 없음 | 챔버 운전 전용 |
| Test1 및 내보낸 Test1.csv | #2 Point 1 예약 규칙 | Move 뒤 BB, set-only, 대역/역할 누락, 셀 사이 팬·챔버 동작 |
| RP10S_10C_10B | 마지막 Move 뒤 Capture 없음 | 준비 전용 절차 |

따라서 실패는 사용자 레시피가 잘못됐다기보다 **정확 변환기의 표현 범위와 자유 스텝 레시피가 다르기 때문**이다. 변환을 위해 Wait/BIAS를 삭제하거나 순서를 바꾸거나 CameraIndex를 추정해서는 안 된다.

## 5. 사용자 확정과 구현 계약

| 쟁점 | 확정 |
|---|---|
| 반복 단위 | **템플릿 레시피 전체 × 선택 실제 카메라**. 챔버/BB/대기/BIAS/NUC/이동/Capture의 원본 순서/횟수 유지, dedup 없음 |
| 계획 | 순서 있는 `{entryKey, templateRecipeId, cameras}` 목록. 셀은 `(entry, 실제 카메라)` |
| 슬롯 | MotorMove `CellPositionRole`=Hot/Cold/Room 위치, CameraCommand `UseCellCamera`=셀 Agent/번호. 템플릿 대역/역할·나머지 값은 유지하며 독립 실행 기본값을 지우지 않음 |
| 연결 | RecipeId **live link**, 승인 사본 없음. 시작 때 현재 원본/위치로 컴파일한 실행 목록만 해당 런에 고정 |
| 재개 | 현재 템플릿으로 재컴파일, Done 유지, 선택 카메라의 레시피 전체를 처음부터. fingerprint 변경은 보드/확인창에 **템플릿이 이전 시도 이후 변경됨** 경고 |
| 카메라 | AgentDirectory heartbeat의 실제 AgentId+CameraIndex(0 포함). 신규 후보 온라인만, 저장된 오프라인 선택은 표시/보존 |
| 위치 | 실제 identity×역할 공유표. Point 1 예약/Point→Coordinate 잠금 유지. 근거로 유일하게 매핑되는 구형 행만 이관, 나머지는 보존·보고 |
| 영수증 | 모든 필수 스텝 성공 뒤만 Done. 명령/역할별 하위 결과 저장. 일부 Capture 실패 시 카메라 전체 재실행 |
| 번호 | 같은 카메라/온도코드/대역/역할 폴더에 누적 총 장수(반복 포함)를 ShotNumberOffset으로 배정. 기존 명명 함수 유지 |
| 삭제 | 사용 계획 이름 목록 제공, 참조 중 템플릿 삭제 거절 |
| 구형 계획 | **이전 형식 계획입니다. 다시 만들어 주세요.**로 실행 불가. 자동 변환 없음. 구형 실행 영수증은 삭제하지 않고 새 체인에서 안전 제외 |

정확한 저장/컴파일/재개/UI/검증 계약은 [capture-plan-matrix.plan.md](capture-plan-matrix.plan.md)에 단일 명세로 정리했다. 본 분석의 초기 제안이었던 조건 준비 분리·승인 스냅샷·구형 계획 자동 전환은 채택하지 않았다.

## 6. 근거 및 임시 자료 정리

- 저장/이관: `HeatingCameraSystem.Master/Services/AppServices.cs`, `FileRecipeRepository.cs`, `LiteDbCameraPositionRepository.cs`.
- 모델/생성: `HeatingCameraSystem.Core/Models/RecipeModels.cs`, `CapturePlanModels.cs`, `CapturePlanConverter.cs`, `CapturePlanCompiler.cs`.
- 완료/재개: `HeatingCameraSystem.Master/Services/CapturePlanRunService.cs`, `LiteDbCapturePlanRunRepository.cs`. 현재 구현은 셀 전체의 필수 스텝 영수증을 집계한다.
- 대상/BIAS/명명: `AgentDirectory.cs`, `RecipeEngine.cs`, `CaptureNamingRule.cs`. 여러 Capture/반복의 누적 offset은 기존 파일명 규칙을 유지한다.
- 현재 파일 번호/회차 계약: `capture-plan-matrix.plan.md` §4.
- 임시 조사 디렉터리: `%TEMP%\HCS-recipe-template-research-20261021`. 콘솔 소스/프로젝트, bin/obj, `results.json`, DB/log 복사본, 활성 JSON 18개/백업 JSON 613개 복사본을 조사 완료 후 모두 삭제했다. 원본 데이터·원본 백업·`E:\tmp\Test1.csv`는 보존했다.
