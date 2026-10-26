# 인계 — 2026-09-11 (시뮬레이션 모드 / 목표온도 폴더명 / JPEG 포맷)

기준 커밋: `901ffc7` (origin/master와 동일, 작업 트리 clean)
build 0 errors / 0 warnings · tests 452 passed (445 → +7)

운영자 요청 3건을 한 커밋으로 처리. 이메일용 수정내역은 사용자에게 이미 전달함.

---

## 왜 3건을 묶었나

폴더명을 레시피 목표 온도에서 가져오게 되면서 **PLC 없이도 저장 규칙이 실제와 똑같이** 나온다.
그게 시뮬레이션 모드의 쓸모를 결정한다. 두 건은 분리 불가능하게 얽혀 있다.

파일도 겹친다(`RecipeEngine.cs`, `DialogService.cs`, lang 2개, `AGENTS.md`) — 3커밋으로 쪼개려면
hunk 단위 스테이징이 필요해서 포기했다. 직전 커밋 `6d2b55d`도 4건을 묶은 선례가 있다.

---

## 1. 시뮬레이션 모드 장비별 분리

### 설정 (`hardware.json`)
```jsonc
"SimulationMode": true,                                    // 마스터 스위치
"Simulate": { "Plc": true, "BlackBody": true, "Camera": false }
```
`Simulate` 절이 없는 기존 json은 세 항목 전부 기본값 true → **예전 동작(전부 가짜) 그대로**.
마이그레이션 불필요.

### 판정 지점 — 한 곳
`AppServices.IsSimulated(settings, s => s.Plc)` — 마스터 스위치 && 개별 선택.
`AppServices.DescribeSimulated(settings)` — 배너·로그용 장비명 문자열.

### 스왑 지점 (`AppServices.Initialize`)
| 플래그 | 교체 대상 |
|---|---|
| `Simulate.Plc` | `PlcController` → `FakePlcController`, `ConnectionMonitor.Start()` 생략 |
| `Simulate.Camera` | `CameraSerialClientFactory` / `LiveThermalCamera` / `CameraPairingService` 3종 묶음 |
| `Simulate.BlackBody` | `CreateBlackBodyController` → `FakeBlackBodyController` |

**"fake 챔버"는 별도 항목으로 못 만든다.** 챔버 온습도·서보는 전용 컨트롤러가 없고
`IPlcController`의 메서드다 → `Simulate.Plc`에 포함, 라벨에 범위 명시.

**Agent PC 카메라는 여기 없다.** NATS 너머라 Master가 못 바꾼다 — `agent.json` 소관,
기존 AgentSettings 화면에서 별도 관리.

### UI
- `PlcControlSettingsView.xaml` **첫 카드**에 시뮬레이션 박스 (마스터 체크 + 하위 3개, 마스터 끄면 하위 `IsEnabled=false`)
- `ApplySimulationCommand` → `RecipeEngine.IsRunning`이면 거부 → 저장 → `IDialogService.ConfirmRestart` → `App.RestartRequested = true` → `Shutdown()`
- `MainWindow.xaml` 최상단 Row 0 (2열 span) 배너, `MainViewModel.IsSimulationMode` / `SimulationTargetsText` 바인딩

### 건드리면 깨지는 것 ⚠️
`App.OnExit`의 재실행은 **`base.OnExit(e)` 다음, 메서드 맨 끝**이어야 한다.
그 전에 `Process.Start`를 하면 구 프로세스가 `AppServices.DisposeAsync()` 5초 동기 대기에
걸려 있는 동안 새 프로세스가 떠서 **둘이 동시에 `data.db`를 열어 LiteDB 잠금 예외**로 죽는다.
위로 옮기지 말 것. (AGENTS.md에도 적어둠)

### 신설된 API
- `HardwareSettings.Simulate` (`SimulationTargets` 클래스)
- `AppServices.IsSimulated` / `DescribeSimulated`
- `RecipeEngine.IsRunning` (volatile bool, `ExecuteRecipeAsync` try/finally)
- `IDialogService.ConfirmRestart(string)` — **default interface method, 기본 false**.
  덕분에 기존 Moq 테스트가 하나도 안 깨졌고 headless에서 재시작이 안 일어난다.
- `App.RestartRequested` (static bool)

### 제거
`RecipeEditorViewModel.IsSequentialMode` + `SetCaptureMode` 커맨드 — 저장도 안 되고
`RecipeEngine`이 읽지도 않고 XAML에 바인딩도 없던 완전한 死코드였다.

---

## 2. 조건 폴더 온도: PV → 최종 타겟

### 우선순위 (`ResolveProductionNamingAsync`)
```
① _lastChamberTargetC          직전 ChamberControl 스텝의 목표  ← PLC 없어도 정확
② ReadChamberTargetFromPlcAsync()  ReadStatusAsync().TargetTemperature
③ GetCurrentTemperatureAsync()     PV, ②가 예외일 때만
```

**`TempSv`가 아니라 `TempTarget`이다.** PLC 온도 워드가 3개:
- `TempPv`(D100) 실측 — `GetCurrentTemperatureAsync`
- `TempSv`(D102) 제어값 — `SetControlTemperatureAsync`. **램프 중 중간값**이라 쓰면 안 됨
- `TempTarget` 최종 목표 — `ReadStatusAsync().TargetTemperature`, `SetTargetTemperatureAsync`

`_lastChamberTargetC`는 필드다(`_runProductNumber`와 같은 패턴). 캡처 스텝과 챔버 스텝이
분리돼 있어 캡처 시점에 파라미터로 못 받는다. `ExecuteRecipeAsync` 시작 시 null로 리셋.

### 램프 사용 중지
`RecipeEngine.cs` ChamberControl case에서 `ApplyChamberTemperatureAsync(..., 0, ...)` — **리터럴 0**.
`recipe.TemperatureRampMinutes`를 안 쓴다. 기존 레시피에 남은 값(예: 15분)도 무시된다 —
UI만 숨기면 화면에 안 보이는데 히터가 15분에 걸쳐 올라가는 유령 버그가 된다.

`TemperatureRampController`, `Recipe.TemperatureRampMinutes`, `SetControlTemperatureAsync`
전부 **남아 있다**. 되살리려면 그 `0`을 `recipe.TemperatureRampMinutes`로 되돌리면 끝.
`RampAsync:41`의 `SetTargetTemperatureAsync(target)`는 램프와 무관하게 항상 호출된다.

에디터 UI: `RecipeEditorView.xaml` 램프 열 → `ColumnDefinition Width="0"` + `Visibility="Collapsed"`.

### 깨졌다가 고친 기존 테스트
`ExecuteRecipeAsync_SameRecipeTwice_...`, `..._WithoutRecordingConditions_...` 둘 다
챔버 스텝이 없어 PV(25/24.8)에 의존해 `RPP25`를 기대했다.
목 `ReadStatusAsync`에 `TargetTemperature = 25.0f` 추가로 해결 — 새 계약을 표현하도록 고친 것.

---

## 3. 저장 포맷 런 단위 선택

### 데이터 흐름
```
ProductionRunDialog (라디오)
  → ProductionRunInput.SaveFormat
  → Recipe.SaveFormat            (레시피 파일에 저장됨)
  → CaptureCommandMessage.SaveFormat   (NATS 계약)
  → ProductionCaptureSink.WriteShot 분기
```

`ProductionCaptureFormat { Raw, Jpeg }` — `Core/Models/CaptureImageFormat.cs`에 같이 넣음.
**기존 `CaptureImageFormat`(Y16Raw/Tiff16)과 다른 개념이다.** 저건 레거시 `.y16` 경로 전용.

`CaptureNamingRule.FileName(prefix, index, format = Raw)` — 선택적 파라미터라 기존 호출부 무변경.
`CaptureNamingRule.Extension(format)` 신설.

JPEG는 `ThermalPreviewEncoder.EncodeJpeg` (8비트 grayscale, min/max 정규화).
**FPA 원시값을 싣지 않는다** — 8비트라 의미가 없다.

### 건드리면 깨지는 것 ⚠️
확장자를 추가하면 `ProductionCaptureSink`의 **두 곳을 함께** 고쳐야 한다:
1. `SyncFolderAsync`의 robocopy 패턴 `"*.raw *.jpg *.json"`
2. `PendingFiles`의 `IsCaptureFile` 판정

하나만 고치면 전송 실패가 "완료"로 보고된다 (직전 커밋 `6d2b55d`에서 고쳤던 것과 동일한 함정).
`PendingFiles`가 `.raw`만 세고 있어서 JPEG 런이 이 버그를 되살릴 뻔했음 — 같이 고쳤다.

---

## 다음 세션 확인 목록

### 실기 확인 필요 (코드로 검증 불가)
1. **자동 재시작** — `Environment.ProcessPath` 기준. `dotnet run`으로는 `dotnet.exe`가 떠서
   정상 동작 안 함. **게시된 exe로 테스트해야 한다.**
2. **배너 아이콘** `Kind="FlaskOutline"` (Material.Icons.WPF) 렌더링 — 빌드는 통과했으나 시각 확인 안 함
3. **JPEG 육안 품질** — 현재 흑백. 컬러(iron colormap)가 나으면
   `ProductionCaptureSink.WriteShot`의 `EncodeJpeg` → `EncodeColorJpeg` **한 줄 교체**
4. `TempTarget` 워드 주소 — 챔버 스텝 없이 수동 운전 중 촬영할 때만 타는 경로 (폴백 ②)

### 미해결 / 이월
- JPEG 런에서도 cold 조건이면 `bias.json`을 쓴다 (역할 기반이라). 의도한 건지 미확인 — 무해해 보임
- `Dialog_ProductionRun_NeedProduct` 번역 키 여전히 미사용 (이전 세션 이월)
- `AGENTS.md`의 "장수는 하드웨어 계약" 문구 vs 코드(항상 `step.ShotCount`) 불일치 (이전 세션 이월)
- `App.xaml.cs OnExit`의 `.GetAwaiter().GetResult()` 동기 대기 — 기술 부채로 등록됨.
  재시작 기능이 이 경로를 자주 타게 됐으니 **여기서 멈추면 좀비 프로세스**가 남는다. 관찰 필요

---

## 사용자 결정 사항 (되묻지 말 것)

- 폴더 온도는 **최종 타겟값**. 중간 제어(램프)는 설비에서 안 쓰기로 확정
- 램프는 **메서드 유지, UI만 숨김**. 타겟 쓰기는 반드시 살아있어야 함
- JPEG는 **재촬영** 방식 (변환 아님). raw로 캘리브레이션 → 결과 확인용으로 다시 촬영
- 시뮬레이션 저장물에 `_SIM` 접미사 **넣지 않음**
- 시뮬레이션 설정은 **팝업 없이** PLC 설정화면 안에서, 메시지만 표시
- 재부팅 = **프로그램 재시작** (PC 재부팅 아님)
