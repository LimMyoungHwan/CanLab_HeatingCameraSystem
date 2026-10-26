# 인계 — 2026-09-28 (흑체 PLC 분리 / raw 번들 / CSV 실물 확인)

`39fb3ce` 위에 **2커밋**. build 0/0 · tests **490 passed** (491 → 미러링 전용 테스트 1건 삭제)

- `70df78b` fix(blackbody): 흑체 PV/SV를 PLC 미러링에서 떼어내고 판독 실패를 화면에 노출
- `7d83507` fix(plc): 에러 리셋 비트 주소를 P525에서 P252로 정정 (운영자 실기 확인분)

---

## 1. 오진 정정 기록 — "PLC 미러링이 안 된다"의 진짜 구조

운영자 보고: 흑체 PV/SV가 화면에서 계속 같은 값. `BlackBody.Enabled`는 **true**였고 수동조작
화면에서 직접 재판독하면 값이 정상. 그래서 "PLC 흑체 주소가 틀렸다"로 판단하고 PLC 경유를 전부 제거.

**코드 구조상 미러링 실패만으로는 값이 고정될 수 없었다.** `MergeCachedBlackBody`가 직접 판독값을
PLC 레지스터 값보다 우선시키므로, 미러링이 100% 실패해도 화면에는 직접 판독값이 떴어야 한다.
값이 고정이었다는 건 **`PollBlackBodyAsync`의 SR 판독 자체가 실패하고 있었고**, `_bbXPv`가 null로
남아 merge가 스킵되어 **PLC 레지스터 값(틀린 주소의 워드)이 그대로 화면에 뜨고 있었다**는 뜻이다.

→ 근본 원인(SR 판독이 poll 안에서 실패하는 이유)은 **아직 확정되지 않았다.** 다만 PLC 레지스터
폴백을 없앴으므로 이제 같은 상황이 오면 0.0℃ + 빨간 경고로 **드러난다.** 배포 후 경고가 뜨는지가
이 세션 작업의 실질적 검증이다. 경고가 뜨면 `BlackBodyStatusMessage` 내용이 원인을 말해준다.

교훈: 값이 "그럴듯하게 고정"이면 폴백 경로를 먼저 의심한다. 폴백이 있으면 1차 소스의 실패가
증상으로 드러나지 않는다.

## 2. 흑체를 PLC에서 완전히 떼어냈다

흑체는 `SrBlackBodyController`(RS-232/UDP VIP 프레임) 직접-제어 장비다. PLC 경유 경로를 전부 제거.

| 위치 | 변경 |
|---|---|
| `PlcStatusService.PollBlackBodyAsync` | PLC 미러링 쓰기 블록 삭제 |
| `PlcXgtClient.ReadStatusAsync` | `s.BlackBody*` 4줄 + `scalarWordTokens`의 `Bb1Pv/Bb1Sv/Bb2Pv/Bb2Sv` 삭제 |
| `PlcXgtClient` | `WriteBlackBodyTemperaturesAsync` 삭제 |
| `IPlcController` / `FakePlcController` | 같은 메서드 선언·구현 삭제 |
| `SrBlackBodyController` | `SetTemperatureAsync`의 PLC 병행 SV 쓰기, `_plc` 필드·생성자 인수 삭제 |
| `AppServices.CreateBlackBodyController` | `plc` 인수 삭제 (호출자·테스트 3곳 갱신) |

### 되돌리지 말 것

1. **`MergeCachedBlackBody`가 흑체 값의 유일한 공급원이다.** PLC 레지스터 폴백을 다시 넣으면
   1번의 "그럴듯한 고정" 증상이 그대로 돌아온다.
2. **판독 실패 시 `_bbXPv`를 지우지 않는다** → 마지막 정상값이 남고 `BlackBodyXFaulted`로 구분.
   한 번도 판독 못 했으면 0이 뜨고, 그건 경고로 덮는다.
3. `IPlcController.SetBlackBodyTemperatureAsync` / `GetCurrentBlackBodyTemperatureAsync`는
   **남겼다** — `PlcBlackBodyAdapter`(PLC 경유 대체 컨트롤러) 전용이고 외부 시뮬레이터 테스트가
   덮는다. 운영 경로가 아니다. `IPlcController.cs:47-48`에 주석으로 못 박아 뒀다.
4. `BlackBody.Enabled` 게이트(`AppServices.cs:147`)는 **남겼다.** 유일한 사용처이고 false면
   흑체 폴링 자체가 안 돈다 — 이제 0 + 경고로 드러나므로 조용한 함정은 아니다. 다만 이 설정은
   PLC 설정 화면 체크박스로 운영자가 끌 수 있다는 걸 기억할 것.

### 부수 이점

- `BlackBodyUpdated`가 `WriteGapMs`(100ms × 유닛수)를 기다리지 않는다.
- `ReadStatusAsync` 판독 토큰 4개 감소.

## 3. 판독 실패 화면 노출 (신규)

`PlcStatusService`가 계산만 하고 아무도 안 쓰던 상태를 화면에 붙였다.

- `DashboardViewModel` / `StatusMonitorViewModel`: `IsBlackBodyFaulted`(= BB1 or BB2 faulted),
  `BlackBodyStatusMessage` 추가. 각 `OnBlackBodyUpdated`에서 대입.
- `DashboardView.xaml` / `StatusMonitorView.xaml`: 흑체 카드 하단 빨간 경고 TextBlock,
  `BooleanToVisibilityConverter`로 실패 시에만. `StatusMonitorView`에는 컨버터 선언도 추가.
- **새 loc 키 없음** — 메시지는 이미 `Dash_Refreshed`/`Dash_ReadFailed`로 번역돼 온다.
- 테스트 추가 안 함: VM 쪽은 `||` 2줄이고 실제 불변식(서비스가 Faulted를 올리는 것)은
  `PlcBlackBodyIsolationTests`가 이미 검증한다.

**아직 per-unit이 아니다.** 어느 흑체가 실패했는지는 메시지 문구로만 알 수 있다. 유닛 수가
1개인 설정에서 흑체2는 폴링되지 않아 `BlackBody2Faulted`가 false로 남고 0.0이 그냥 뜬다 —
불편 보고가 오면 per-unit 플래그로 쪼갠다.

## 4. ViewModel 원복

운영자가 임시로 넣었던 흑체 직접 재판독을 되돌렸다. `.Wait()` 8회가 **UI 스레드**에서 돌고 있었고
`4229732`(PLC 폴링 25초 지연)와 같은 계열의 위험이었다. `using OpenTK.Graphics.ES20;`(IDE가 끼워넣은
무관한 using), 빈 `finally { }`, `Debug.WriteLine` 삼킴도 제거. CS8602 경고 1건도 함께 사라졌다.

`AGENTS.md`의 "화면은 재판독하지 않는다" 규약으로 복귀.

## 5. AgentUI raw 번들 배포

- `참고\BB70_000.raw`(614,400바이트 = 640×480×16bit) → `HeatingCameraSystem.AgentUI\Assets\`로 **복사**
  (참고 폴더 원본도 유지). `참고\` 사본은 여전히 untracked.
- csproj: `<None Update="Assets\BB70_000.raw" CopyToOutputDirectory="PreserveNewest" />`
  (SDK 기본 None 글롭에 이미 있으므로 `Include`가 아니라 `Update`)
- `App.xaml.cs`: 기본 경로를 **`AppContext.BaseDirectory\Assets\BB70_000.raw`**로.
  **`.\`를 쓰지 말 것** — 작업 디렉터리가 exe 폴더가 아니면(바로가기·서비스·스크립트) 깨진다.
  `HCS_SIM_RAW_PATH`는 오버라이드로 유지. `config.SimulationMode` 강제 설정을 없앴으므로
  스위치는 `agent.json` 하나다. 로그도 실제 시뮬레이션일 때만 찍는다(전에는 로그만 거짓이었다).

---

## 6. CSV 실물 확인 결과 — 파서는 정상, 레시피 내용에 문제

운영자 export 파일 `E:\tmp\Test1.csv`(7,522바이트, **2026-09-26 작성** — 이번 세션 export 아님)을
`RecipeCsvSerializer.Deserialize`로 실제 실행해 확인했다(임시 테스트는 삭제).

**직렬화기 정상:**
- 7스텝 전부 파싱, 종류·순서 보존:
  `ChamberControl → MotorMove → BlackBodyControl → CameraCommand → FanControl → MotorMove → ChamberControl`
- 재직렬화 → 재파싱 동일 (왕복 안정)
- `CameraTargets`의 빈 대역/흑체가 **null로 정확히** 파싱됨 (`1:::FOXSTARSOFTPC_Agent_3`
  → `1:null:null:FOXSTARSOFTPC_Agent_3`). enum 기본값으로 밀리지 않는다 (`RecipeCsvSerializer.cs:506-507`).
- `[GUIDE]`·enum 범례·설명 블록 정상 무시, 마커 인식 정상.

### 레시피 내용 문제 — 다음 세션 우선순위

**P1. `SaveRootPath`가 비어 있다 (CSV line 92).**
생산 저장 규칙이 **완전히 꺼진다**(`RecipeEngine.cs:881`). `.raw` 트리가 한 장도 안 생긴다.
게다가 `DescribeMissingProductionTargets`(`:925`)가 **첫 줄에서 `SaveRootPath` 비면 return null**
하므로(`:927`) **아래 P2 경고조차 안 뜬다.** 몇 시간짜리 챔버 시퀀스를 다 돌고 빈 폴더를 확인하는
바로 그 시나리오다 — 이 검사를 만든 이유와 같은 함정이 한 겹 위에 남아 있다.

→ 제안: `SaveRootPath`가 비었는데 `CameraCommand`+`Capture` 스텝이 있으면 실행 전 경고.
`DescribeMissingProductionTargets`에 3줄이면 된다. loc 키 1개 필요.
(참고: 미사용으로 남아 있는 `Dialog_ProductionRun_NeedProduct` 키를 재활용할 수 있을지 확인.)

**P2. `CameraTargets` 대역/흑체가 null (line 101).**
폴더 코드(`{대역코드}{온도코드}`)를 만들 수 없어 생산 규칙에 따른 `.raw` 저장 경로를 구성하지 못한다.
`ResolveProductionNamingAsync:882`가 null을 반환한다는 뜻이며, 별도의 일반 `.y16`·메타데이터 저장까지 모두 없다는 의미는 아니다.
P1이 해결되면 실행 전 `"#4 CAM-01"` 형태로 경고가 뜬다.

**P3. 마지막 `MotorMove`가 `Manual`인데 `TargetPositionIndex=6` (line 103).**
Manual은 X/Y를 쓰므로 **(0,0)으로 이동하고 인덱스 6은 무시된다.** 3번째 줄(line 99)은
`Automatic`+인덱스 1로 제대로 돼 있어 대비된다. **운영자 의도 확인 필요** — 포인트 6이면 `Automatic`.

**P4. `BlackBodyControl`이 `WaitForStabilization=False` + `SoakMinutes=0` (line 100).**
흑체 70℃/30℃ 지시만 하고 승온 중에 다음 스텝으로 넘어간다. 바로 뒤가 캡처(line 101)라
**흑체가 목표 도달 전에 찍는다.** 캘리브레이션용이면 의도 확인 필요.

**P5. `ChamberControl`의 `UseSafetyTemperature=False`, Min/Max=0 (line 98, 104).**
온도 안전 범위 검사가 꺼져 있다. 50℃/25℃ 운전에 가드 없음.

무해 확인분: `CameraOperation`이 모든 행에서 `capture`로 파싱된다(enum 기본값 0). 무관한 스텝에선
`RecipeEngine`이 무시하고 재export 시 다시 빈칸이 되므로 문제 없다.

---

## 7. 실기 확인 (다음 세션 — 오늘 배포분)

1. **흑체 PV/SV가 1초마다 갱신되는지.** 이번 작업의 수용 기준. 실패하면 **빨간 경고가 뜬다** —
   메시지 문구를 받아온다. 경고 없이 0.0이면 유닛 수 설정(`BlackBody.Units`)을 본다.
2. **AgentUI `Assets\BB70_000.raw` 리플레이** (`agent.json`의 `SimulationMode: true`).
3. **팬 스텝이 `D350`에 기대 회전수를 쓰는지** (`PlcStatusSnapshot.FanSpeedHz`로 읽어 확인).
4. **CSV 왕복을 현장 엑셀로** — Export → `CameraCommand` 행 복사·붙여넣고 `CameraTargets`만 수정
   → Import. 단위 테스트는 우리 파서만 검증한다(엑셀이 뭘 뱉는지는 못 본다).
5. **`[GUIDE]`만 보고 스텝을 추가할 수 있는지.** 지난 개정의 수용 기준.
6. **엑셀이 `;`로 저장하는지** (자동 감지는 있다).
7. **`BitErrorReset`(P252) 동작 확인** — 주소만 고쳤고 실동작은 미확인.

**챔버 RUN 램프 펄스는 확인 완료** (운영자 보고, 실 PLC가 펄스 방식).

---

## 8. 미해결 / 이월

- **흑체 판독 per-unit 표시 없음** (3절 참고).
- **BOM 없는 CP949 CSV는 읽기 실패한다.** Export가 항상 BOM을 쓰므로 주경로는 무영향. 증상이
  보고되면 UTF-8 디코딩 실패 시 시스템 코드페이지 재시도 폴백. (지금 안 넣은 이유: 조용한
  오디코딩으로 한글 레시피명이 깨지는 쪽이 더 나쁘다.)
- **`[STEPS]` 34컬럼 가로 스크롤.** 복사·붙여넣기 워크플로에선 `CameraTargets`가 3번째라 무관하지만
  `CaptureJoinTimeoutSeconds`(34번째) 직접 수정은 멀다. 카탈로그 정렬만 고치면 된다(읽기는 이름 기준).
- **AgentUI 로컬 패널이 UYVY에서 흑백으로 보임.**
- **untracked 잔여물**: `참고/util/`(56MB), `참고/BB70_000.raw`, `참고/Y16_표시_파이프라인.pdf`,
  `docs/장비 Check List_260921.xlsx`, `.banyancode/`, `.serena/memories/handoff/`.
- **`Dialog_ProductionRun_NeedProduct` 미사용 loc 키** — P1 경고에 재활용 검토.
- **`App.xaml.cs OnExit`의 `.GetAwaiter().GetResult()`** — 기술 부채, 명시적 요청 시에만.
- **`docs/01-plan/features/` 체크박스** — 전부 실기 검증 항목(7절과 중복). 배포 확인 후 일괄 체크.
