# Handoff 계획 — Master UI 재구조화 + 온도 램프 (다음 세션 개발)

전제: **계획만 확정. 개발은 다음 세션.** 사용자 결정 5건 반영. **F3는 연기.**
관련 세션: dashboard 통합 재설계 직후(DashboardViewModel telemetry + DashboardView.xaml 재설계 완료, 미커밋).

## 확정 결정 (사용자)
1. Dashboard **Mode1 유휴 시 = 빈 화면**. 레시피(테스트) 실행 중에만 촬영 카메라 표시.
2. 수동조작 카메라 = **모든 기능 + RUN/STOP** (셔터/캡처/RUN·STOP/NUC/정보/설정저장 원격제어).
3. 맵핑 = 신규 레시피부터 **빈 맵핑**, 전역 맵핑 **마이그레이션 없음**. + **레시피 복사/삭제/추가** 기능.
4. 온도 램프 = **PLC설정(수동) + 레시피(자동) 둘 다**.
5. **F3(Devices+Serial→Agent설정) 연기** — AgentManager 레인. 전체 완료 후 남은작업으로 접근법 재검토.

## 이번 배치 범위: F1 · F2 · F4 · F5

---

### F1 — Dashboard 모드 재정의 + 영속화
현재: `DashboardViewModel._mode2~5Assignments` = 인메모리 `List<CameraNode?>`(재시작 소실). Mode1 = 전체 자동순환(8/page).
목표:
- **Mode1**: 레시피 실행 중 `SelectedRecipe.Steps[progress.CurrentStep].CameraIndex` → 활성 CAM 라이브 단일/강조. **유휴 시 빈 화면**(자동순환 폐지).
- **Mode2~5**: 드래그 배치 영속. 신규 `IDashboardLayoutRepository` + `LiteDbDashboardLayoutRepository`(컬렉션 `dashboard_layout`, mode→슬롯별 `{agentId, cameraIndex}`). 시작 시 로드 → 하트비트로 카메라 등장 시 슬롯 재바인딩.
파일: `DashboardViewModel.cs`, 신규 repo 2개(Core 인터페이스 + Master 구현), `AppServices.cs`(등록), RecipeEngine 진행 or SelectedRecipe+CurrentStep 조합으로 활성 카메라 해석.
검증: 드래그→재시작→배치 유지(수동 QA) · 실행 중 Mode1 활성카메라 전환 · 유휴 Mode1 빈 화면.

### F2 — 라이브영상 → 수동조작 통합 + 카메라 원격 전체제어 ⚠️확장·동시편집
현재: `LiveViewModel`(NATS 라이브프레임→타일). `ManualControlViewModel`(PLC/서보/흑체 수동, 카메라 없음). 카메라 기능(RUN/STOP/셔터/캡처/NUC/정보/설정저장)은 **Agent PC의 `CameraPanelViewModel`이 로컬 소유** — Master엔 원격 채널 없음(캡처·라이브만 NATS 존재).
목표: 수동조작에 라이브 + **원격 전체제어**. 카메라가 Agent 소유이므로 **신규 NATS 카메라 제어 프로토콜 필요**:
- 신규 `CameraControlMessages`(AgentConfigMessages 패턴 미러, additive): `CameraControlMessage{AgentId, CameraIndex, Op}` + `CameraControlAck`. Op = run/stop/shutterOpen/shutterClose/capture/nuc/saveConfig/refreshInfo. 토픽 `master.cmd.camera.{AgentId}` / `agent.ack.camera.{AgentId}`.
- `INatsCommunicationService`+구현+`FakeNats`(테스트 스텁) pub/sub 추가.
- AgentUI: `CameraNatsConnector`/`App.xaml.cs` 구독 → 해당 `CameraPanelViewModel` 커맨드로 라우팅.
- Master 수동조작: 카메라 목록 + 라이브 타일(기존 SubscribeLiveFrame) + 제어 버튼(위 Op) + ack 표시.
- **주의**: Master 로컬 `ISerialShutterController`(셔터, cameraIndex=식별자)와 Agent 카메라 셔터(`ICameraSerialClient.SetShutterAsync`) 이중 존재 → 실셔터 하드웨어 위치 개발 시 확인 후 셔터 경로 결정.
파일: 신규 `Core/Models/CameraControlMessages.cs`, `INatsCommunicationService`+`NatsCommunicationService`+`FakeNats`(2곳), AgentUI `CameraNatsConnector`/`App.xaml.cs`, `ManualControlViewModel.cs`/`ManualControlView.xaml`(라이브+제어), `MainViewModel`(라이브영상 nav 제거), `LiveViewModel` 병합/삭제.
⚠️ `ManualControlViewModel`·`LiveViewModel`·`CameraPanelViewModel` 다른 세션 편집 중 → 개발 전 재-Read.
검증: 수동조작 라이브 표시 + 각 Op 실 AgentUI 왕복 + ack(임시 테스트).

### F4 — 카메라 맵핑 → 레시피 내장 (레시피별 맵핑) ⚠️최대변경·동시편집
현재: 맵핑 **전역**(`LiteDbCameraMappingRepository` 단일 "current" 문서, `List<CameraMappingConfig>{SlotId="P01"~, CameraId}`). `RecipeEngine.LoadPositionAgentMapAsync`가 전역 맵핑으로 position→agent 해석.
목표: 레시피별 맵핑.
- `Recipe`(Core/Models/RecipeModels.cs)에 `List<CameraMappingConfig> Mappings` 추가.
- RecipeEditor에 맵핑 UI 흡수(`CameraMappingView` 로직 이식), Camera Mapping nav 제거.
- `RecipeEngine`: 전역 repo 대신 `recipe.Mappings` 사용(`LoadPositionAgentMapAsync` 시그니처 변경 → recipe 인자).
- **마이그레이션 없음**: 신규 레시피 빈 맵핑, 기존 레시피도 빈 맵핑(전역 폐기). LiteDbCameraMappingRepository는 폐기 or 미사용.
- **레시피 복사** 신규 커맨드(steps+mappings 딥카피, 새 Id/이름). 삭제/추가 기존 존재(`DeleteRecipe`/`AddRecipe`).
파일: `RecipeModels.cs`(Recipe+Mappings), `RecipeEditorViewModel`/`RecipeEditorView`(맵핑 탭 흡수 + Copy 커맨드), `RecipeEngine.cs`(맵핑 소스 전환 + 테스트 갱신 `RecipeEngineTests`/`SimulationTests`), `MainViewModel`(nav 제거), `CameraMappingViewModel`(이식 후 정리).
⚠️ `RecipeEditorViewModel`·`CameraMappingViewModel`·`CameraPanelViewModel`·`LiteDbCameraMappingRepository` 다른 세션 편집 중.
검증: 레시피별 상이 맵핑 저장/로드 + 실행 시 `recipe.Mappings`로 캡처 타겟 해석(테스트) + 복사 동작.

### F5 — 온도 램프 (100% 출력 챔버, PID 대체) — PLC설정 + 레시피
핵심: 선형 승온(현재보다 타겟 10도↑, 10분 설정 → 분당 1도씩, 마지막 타겟 도달)은 **이미 `RecipeEngine.RampTemperatureAsync`에 구현**(현재→타겟 N분, `SetTargetTemperatureAsync` 분할, `RecipeEngineSettings.RampStepIntervalSeconds`). 단 (a) UI 편집 불가(RecipeModel에 필드 없음), (b) PLC설정 수동 램프 없음(ApplyTemperature=즉시 점프).
목표:
- 선형 승온 로직 → 공용 `TemperatureRampController`(현재·타겟·분·스텝간격 → SV 스텝, 취소 지원) 추출. RecipeEngine 재사용.
- **PLC설정**: 타겟온도 + 도달시간(분) + 램프 시작/중지 버튼(수동 승온).
- **Recipe**: `RecipeModel`에 `RampMinutes` 필드 + 에디터 UI(레시피별 자동 램프). `Recipe.TemperatureRampMinutes` ↔ 매핑.
파일: 신규 `TemperatureRampController`(Master.Services or Protocols), `RecipeEngine.cs`(헬퍼화), `PlcControlSettingsViewModel`/`PlcControlSettingsView`(램프 UI), `RecipeModels`/`RecipeEditorViewModel`/`RecipeEditorView`(RampMinutes).
검증: 램프 단위테스트(현재10→타겟20, 10분 → 분당 선형 SV) + PLC설정 수동 램프 실동작.

---

## 실행 순서 (파)
- **Wave A (독립·저위험):** F5 온도램프(공용헬퍼+PLC+레시피) · F1 Dashboard(영속화+Mode1).
- **Wave B (동시편집 정리 후):** F2 라이브→수동조작(신규 NATS 카메라제어) · F4 맵핑→레시피.
- 각 파 완료: `dotnet build` 0/0 · `dotnet test` green · 수동 QA(디스플레이).

## 최우선 리스크 — 동시 편집
다른 세션이 F2·F4 관련 파일 7개 동시 편집 중: `LiveViewModel`·`ManualControlViewModel`·`RecipeEditorViewModel`·`CameraMappingViewModel`·`CameraPanelViewModel`·`SrProtocol`·`LiteDbCameraMappingRepository`. **개발 전 필수**: (1) 해당 파일 재-Read/재-index, (2) 그 세션 작업 완료·머지 확인, (3) 충돌 조율. F2는 신규 NATS 프로토콜이라 Core+Master+AgentUI 삼면 수정.

## 연기됨 (F3, 추후)
Devices + Serial Settings → Agent설정 중첩(Agent설정 ⊃ 디바이스설정 ⊃ 시리얼셋팅). Devices는 AgentManager 레인(핸드오프 회피 대상). **전체 완료 후 남은작업으로 접근법 재검토.**

## 내비게이션 변화
```
현재(11): Dashboard · 라이브영상 · Recipe Editor · Camera Mapping · History · Serial Settings · Devices · Agent설정 · PLC상태 · PLC설정 · 수동조작
이번배치후(9): Dashboard · Recipe Editor⊕맵핑 · History · Serial Settings · Devices · Agent설정 · PLC상태 · PLC설정⊕램프 · 수동조작⊕카메라(라이브+원격제어)
  제거: 라이브영상(→수동조작) · Camera Mapping(→Recipe)
F3 완료 후(7): Serial Settings·Devices → Agent설정 흡수
```
