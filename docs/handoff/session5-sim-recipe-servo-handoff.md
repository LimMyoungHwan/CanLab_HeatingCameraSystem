# Handoff — 시뮬레이터 강화 + 레시피 편집기 + 서보/수동조작 + PLC에러/UI (세션6 준비)

> 작성: 2026-07-30. 다음 세션이 이 문서만 읽고 이어서 진행. 구현은 다음 세션.
> 사용자 지시: "이 내용들 정리해서 계획 세우고 다음 세션에서 진행하자."

## 전제 / 현재 상태
- 세션5는 계획만. 코드 미구현.
- **세션4 코드작업(운영자모드 + C1 OnExit + C2 NATS)은 워크트리 미커밋** — 커밋 승인 대기 중. 원자커밋 4개 계획은 session4 핸드오프 참조.
- 검증 환경: 로컬 NATS 4222 실행 중. Simulator `--plc-only`(PLC/흑체, FEnet 2004) + AgentUI(실카메라)로 Master 테스트 가능.
- 빌드/테스트 베이스: `dotnet build HeatingCameraSystem.slnx`=0/0, `dotnet test`=212.

---

## A. PLC 시뮬레이터 강화 (HeatingCameraSystem.Simulator)

### A1. Master가 쓴 setpoint가 시뮬 PLC에 반영 안됨 [근본원인 확인 필요]
- **동작 확인**: `FEnetPlcSimulator.cs` write→`_memory` 저장됨 ✓. `PlcDynamicsEngine.Tick()`(:47)이 `StepScaled(TempPv, TempSv, rate)`(:52)로 **PV를 SV로 램프**. 즉 Master가 **`_plc.TempSv`에 써야** PV가 따라감.
- **근본원인 후보**: `InitializeDefaults`(:66)에 온도 레지스터 **3개** — `TempPv`/`TempSv`/`TempTarget`. RecipeEngine이 자체 온도 램프(AGENTS.md)를 하며 setpoint를 `TempTarget`(또는 제어워드)에 쓰는데 dynamics는 `TempSv`만 읽으면 PV 불변.
- **다음 세션 확인**: `PlcXgtClient`의 온도/습도 setpoint write 대상 토큰(`HardwareSettings` PlcSettings TempSv vs TempTarget) → 정합. 수정안: (a) dynamics가 Master가 실제 쓰는 토큰으로 램프, 또는 (b) sim이 TempTarget→TempSv 미러.
- 파일: `Simulator/Plc/PlcDynamicsEngine.cs:47-88`, `Simulator/Plc/FEnetPlcSimulator.cs:66-83`, `Core/Config/HardwareSettings.cs`(PlcSettings temp/hum 토큰).

### A2. 흑체 연동 안됨
- **흑체는 이미 시뮬됨**: `PlcDynamicsEngine.Tick():55-56` Bb1Pv←Bb1Sv, Bb2Pv←Bb2Sv (×100 스케일). InitializeDefaults Bb1/Bb2=25.
- **근본원인 (그라운딩)**: `PlcStatusSnapshot`은 이미 **BlackBody1/2 Pv/Sv 필드 보유**(PlcModels.cs) — Master는 흑체를 읽음. 따라서 "연동 안됨"의 실제 원인 = **sim의 Bb1Sv setpoint 정합**(A1과 동일: Master가 흑체 목표를 쓰는 레지스터 ≠ dynamics가 램프하는 Bb1Sv). 표시는 StatusMonitorView 별도 흑체 섹션엔 있으나 좌하단 장비패널엔 없음(→F1).
- **다음 세션**: A1과 동일 setpoint 토큰 정합(흑체 목표 write 토큰=Bb1Sv 확인). Master 읽기는 이미 됨.

### A3. 시뮬레이션 강화 (jog 등)
- **dynamics는 point move만 모델링**(`DetectPointMoves:90`→`ServoPointMoveBase` 펄스→`CompleteMoveAsync:103` X/YPos를 point 좌표로). **jog 비트(BitJogX/Y±)는 전혀 안 봄** → 시뮬에선 jog로 위치 안 변함(D1의 sim측 원인).
- **다음 세션**: dynamics에 jog 처리 추가 — jog 비트 ON 동안 해당 축 `ServoXPos`/`ServoYPos`를 램프(속도=설정), OFF면 정지. `DynamicsSettings`에 jog 속도 추가 고려.

---

## B. 레시피 편집기 (HeatingCameraSystem.Master)

### B1. 새 레시피 이름 편집 가능
- 현재 `RecipeEditorViewModel.AddRecipe:106-113` → Name="New Recipe" 고정 저장.
- 요구: 버튼 클릭 시 기본 "new Recipe" 넣되 **이름 TextBox 포커스 + 전체선택** → 사용자가 덮어쓰거나 유지.
- **접근**: AddRecipe 후 name TextBox(`RecipeEditorView.xaml:78`)에 Focus + SelectAll. WPF는 코드비하인드/behavior로 `textBox.Focus(); textBox.SelectAll();`. 새 레시피 저장은 이름 확정(엔터/포커스아웃) 시점으로 미룰지 검토.

### B2. 파일 기반 영속 (recipe/ + recipe bak/)
- 현재 LiteDB(`LiteDbRecipeRepository.cs:10-37` collection "recipes", `AppServices.cs:56` data.db).
- 요구: 로컬 프로그램 폴더 `recipe/` 폴더에 레시피 저장(파일당 1레시피, JSON 권장). 수정 시 이전본을 `recipe bak/`에 **`{레시피이름}_{yyyyMMdd_HHmmss}`** 붙여 백업(원복용).
- **접근**: 신규 `FileRecipeRepository : IRecipeRepository`(GetAll=폴더 스캔, Save=파일쓰기+기존본 bak 복사, Delete=파일삭제). `AppServices`에서 LiteDb→File로 교체. 기존 data.db 레시피 마이그레이션 1회 스크립트/기동시 이전 고려.

### B3. (B2에 포함) 백업/원복

### B4. 레시피 스텝 삭제 + 수정
- **삭제는 이미 존재**: `RecipeEditorViewModel.DeleteStep:161-168` + `RecipeEditorView.xaml:231` 버튼. 사용자가 못 찾았을 수 있음 → UI 노출 확인.
- **수정 신규**: 스텝 인라인 편집 또는 편집 다이얼로그. 현재 스텝 필드(PositionX/Y, TargetBlackBodyTemperature, TargetChamber… 등 `RecipeModels.cs:23-46`) 편집 바인딩 추가.

---

## C/D. 서보 / 수동조작 (Master + Protocols)

### 공통: 절대/상대 이동을 point1 경유로 통일 (C1/D2/D3)
- 현재 `PlcXgtClient.MoveToCoordinateAsync:141-146`: X/Y→`ServoPointXBase`/`YBase` 쓰고 `ServoPointMoveBase`(P601) 펄스. **P접두 비트 100ms-off는 이미 존재**(`WriteBitAsync:347-353`, `PulseHoldMs=100` HardwareSettings:98).
- **요구 변경**: 절대이동 = point1의 x,y에 값 쓰기 → **1초 지연** → point1 이동 명령. 즉 좌표 write와 move 펄스 사이 `await Task.Delay(1000)` 추가. (현재는 즉시 펄스.)
- 상대이동(`ManualControlViewModel.MoveRelative:244-256`): 현재위치+offset 계산은 이미 함 → 동일 point1 write+1초+move 경로 사용하도록 통일.
- **다음 세션**: MoveToCoordinateAsync에 1초 지연 삽입(또는 새 오버로드). 절대·상대 모두 이 경로.

### C3. x/y 소수점 1자리 입력
- PositionX/Y는 float(`RecipeModels.cs:40-41`), AbsoluteTargetX/Y·RelativeStepX/Y도 float. UI 바인딩에 `StringFormat=F1` + 입력 검증. PLC 워드 스케일(×10 등) 확인.

### C4. 카메라 맵핑 완전 제거
- 제거 대상: `RecipeEditorViewModel.cs:29-45`(MappingSlot/Camera models), `:73-74`, `:266-285`(Assign/Unassign), `RecipeEditorView.xaml:270-325`(맵핑 Expander), `RecipeModels.Recipe.Mappings` 필드, `CameraMappingConfig.cs`, `RecipeEngine.cs:188-204 ResolveAgentIdAsync`(Mappings 사용) → **RecipeStep.CameraIndex 직접 사용**으로 변경. 관련 테스트 갱신.

### C5. 현재좌표 자동기입 버튼 (X/Y 분리)
- 현재 `RecipeEditorViewModel.UseCurrentXyAsync:476-487`가 ServoXPosition/YPosition을 SelectedStep.PositionX/Y에 **동시** 기입. `ReadStatusAsync` 사용.
- 요구: **X축/Y축 각각 별도 버튼** → `UseCurrentX`/`UseCurrentY` 커맨드로 분할. 스텝 수정 영역(우측 현재 x/y 옆)에 버튼 2개.

### D1. Y축 조그 버그 [3중 확인]
- 코드경로 대칭(`JogAsync:119`→`JogBit:250` X=P745/746, Y=P725/726).
- 원인 후보: (a) **Y비트 P725/P726 placeholder**(HardwareSettings:113-114 "문서 미기재") — 실 주소 확인, (b) **시뮬에 jog 없음**(A3), (c) **`ManualControlView.xaml` Y-jog 버튼 CommandParameter(axis=Y) 오배선** 확인.
- **다음 세션**: 셋 다 점검. 실HW+sim 양쪽에서 Y-jog 동작 확인.

---

## E. PLC 에러 처리

### E1. PLC 에러 감지 → 전 PLC 장비 제어 정지 + 화면 즉시 표시
- 현재: 에러 올라와도 계속 정상동작(정지 훅 없음).
- **재사용 가능**: `IPlcController.TriggerEmergencyStopAsync`(BitEmergencyStop write) 존재(세션4 확인). "전 PLC 제어 정지"에 확장/재사용.
- **접근**: 상태 폴링(~1s, `ReadStatusAsync`)에서 `PlcStatusSnapshot`의 에러/폴트 비트 감지 → (1) 모든 PLC 출력/제어 정지(EmergencyStop 또는 개별 정지), (2) UI 알람/배너로 즉시 표시(기존 unified session alarm feed 연계). 정지 후 자동 재개 금지(운영자 해제까지).
- **그라운딩**: `PlcStatusSnapshot.ErrorBits`(PlcModels.cs:95, M4001~M4020) **존재**. `DashboardViewModel.ApplyStatus:474-475`가 `IsEmergencyStop = ErrorBits[0]` 설정 — **감지+표시는 하나 stop-all 훅 없음**. `StatusMonitorViewModel:59` Errors→PlcDeviceCatalog.ErrorNames. **재사용** `PlcXgtClient.TriggerEmergencyStopAsync:225`. → 훅 위치 = DashboardViewModel 폴링(ApplyStatus)에서 ErrorBits 감지 시 stop-all 호출 + 알람 배너. 파일: Core/Models/PlcModels.cs:95, Master/ViewModels/DashboardViewModel.cs:474-475, Protocols/PlcXgtClient.cs:225.

---

## F. UI / 상태 / 이력

### F1. 좌하단 장비 상태에 흑체 상태 표시
- **그라운딩**: `PlcStatusSnapshot`은 BlackBody1/2 Pv/Sv 보유. `StatusMonitorView.xaml:75-91`에 흑체 **별도 섹션** 있으나 **좌하단 장비상태 패널(:151-167 Heater/Cooler 등)엔 없음** → 장비패널에 흑체 행 추가. 파일: Master/Views/StatusMonitorView.xaml:151-167, Master/ViewModels/StatusMonitorViewModel.cs.

### F2. 이력조회에 챔버 이력 조회
- 현재 History는 캡처/카메라 이력만(`HistoryViewModel` + HistoryRepo, `AppServices.HistoryRepo`).
- **그라운딩**: `HistoryViewModel.LoadPage:112-115`가 `AppServices.HistoryRepo.QueryAsync`(캡처만), `HistoryLogItem:17-33`(CameraId/Temp/Humidity/Thumbnail). **챔버 이력 store 없음** → 신규 챔버 이력 repo(온도/습도/흑체 시계열) + 기록지점=상태폴링 + History 뷰 챔버 탭/필터 추가. 파일: Master/ViewModels/HistoryViewModel.cs:112-115, Master/Views/HistoryView.xaml.

### F3. 이력조회 시작/종료 시간 콤보박스 흰바탕+흰글씨 → 글씨 안 보임
- **그라운딩 — 콤보박스가 아니라 DatePicker**: `HistoryView.xaml:146,152` 시작/종료 **DatePicker**. dark 리소스(Background=BgSurfaceContainerLowest/Foreground=TextOnSurface) 지정했으나 **WPF 기본 DatePickerTextBox chrome이 light라 Background 무시 → 글씨 안 보임**. 수정: DatePicker(내부 DatePickerTextBox 포함) dark 스타일 추가 — App.xaml:73-147 ComboBox 스타일 + InputBg/InputFg 참고. 파일: Master/Views/HistoryView.xaml:146,152, Master/App.xaml.

---

## 빌드 / 실행
```powershell
dotnet build HeatingCameraSystem.slnx --nologo    # 0/0
dotnet test  HeatingCameraSystem.slnx --nologo    # 212 (세션4 반영분)
# 시뮬 검증 환경:
#   Simulator --plc-only:  HeatingCameraSystem.Simulator.exe "<repo>\HeatingCameraSystem.Simulator\simulator.json" --plc-only
#   AgentUI(실카메라):     HeatingCameraSystem.AgentUI.exe   (agentui.json SimulationMode=false)
#   Master: hardware.json SimulationMode=false, Nats=nats://127.0.0.1:4222, Plc=127.0.0.1:2004
```

## 참고 파일 요약
- 시뮬: `Simulator/Plc/{PlcDynamicsEngine,FEnetPlcSimulator}.cs`, `Simulator/Config/SimulatorSettings.cs`, `simulator.json`.
- 레시피: `Master/ViewModels/RecipeEditorViewModel.cs`, `Master/Views/RecipeEditorView.xaml(.cs)`, `Master/Services/{LiteDbRecipeRepository,RecipeEngine,AppServices}.cs`, `Core/Models/RecipeModels.cs`, `Core/Models/CameraMappingConfig.cs`, `Core/Interfaces/IRecipeRepository.cs`.
- 서보: `Master/ViewModels/ManualControlViewModel.cs`, `Protocols/PlcXgtClient.cs`, `Core/Config/HardwareSettings.cs`, `Core/Interfaces/IPlcController.cs`.
- 에러/UI: `Protocols/PlcXgtClient.cs`(ReadStatusAsync), `Core/Models/PlcStatusSnapshot`, Dashboard/장비상태 패널, `Master/Views/HistoryView.xaml` + `Master/ViewModels/HistoryViewModel.cs`.
