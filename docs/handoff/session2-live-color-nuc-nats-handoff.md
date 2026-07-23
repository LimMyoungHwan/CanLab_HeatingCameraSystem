# Handoff — 라이브 영상 색상/NUC/데드픽셀 + Master NATS 스트리밍 (2026-07-23 세션2)

> 다음 세션이 이 문서만 읽고 이어서 진행 가능. 이전: `docs/handoff/blackbody-camera-features-handoff.md`.

## 이번 세션 한 일

### 1. AgentUI 라이브 영상 흰화면 → 실영상
- 원인: 카메라 기본 셔터닫힘+비RUN → UVC 스트림에 실 열장면 없음. 시작 시 RUN·셔터열기 자동호출 없었음.
- `CameraPanelViewModel.StartLiveAsync`(RUN+셔터열기, 앱 시작 자동) / `StopLiveAsync`(셔터닫기+STOP, 종료 자동). `App.xaml.cs` 배선.

### 2. 열화상 컬러 (grayscale → false-color)
- **`ThermalColorizer`**(Protocols) = 공유 파이프라인: **plateau 히스토그램 평활화(AGC)** — 파이썬 `two_point_viewer.thresh_plateau_hist_eq` 이식(bin clip=100) → 14bit→8bit → **iron 팔레트 LUT** → BGR24.
- AgentUI `ThermalFrameBitmapSourceConverter`가 이 컬러라이저 사용(WriteableBitmap Bgr24).

### 3. NUC (셔터 기반 1점 FFC) + 데드픽셀 보정
- **`ThermalNucCorrector`**(Protocols, 카메라별 공유):
  - **offset FFC**: 셔터 닫고 평면필드 평균 → `offset[i]=flat[i]-mean` → 라이브에서 `clamp(frame-offset)`. 고정 그라데이션/컬럼 FPN 제거.
  - **데드픽셀**: 플랫에서 5x5 로컬중앙값 대비 아웃라이어 검출(강건 임계 = max(50, 5·std), >5% 시 무시) → 프레임당 이웃 평균 대체.
- NUC 버튼(`RunNucAsync`, `CameraPanelViewModel`): 셔터닫기→12프레임 평균→offset+데드픽셀 검출→셔터열기. 상태에 `데드픽셀 N개` 표시.
- AgentUI 표시 + NATS 스트림 **둘 다** 같은 corrector 인스턴스로 보정.

### 4. Master 라이브 영상 (NATS 스트리밍) — 사용자 지정 구조(B)
- 신규 `LiveFrameMessage` + `agent.live.{AgentId}` 토픽 (INatsCommunicationService/NatsCommunicationService).
- Agent(`CameraNatsConnector.LiveStreamLoopAsync`): ~10fps로 각 카메라 `LatestFrame` → NUC 적용 → **컬러 JPEG**(`ThermalPreviewEncoder.EncodeColorJpeg` → ThermalColorizer) → 발행.
- Master `LiveViewModel`(구독·JPEG 디코드·표시) + `Views/LiveView.xaml` + nav 버튼 "라이브 영상".
- **단일 PC 충돌 없음**: AgentUI만 카메라 오픈, Master는 NATS 수신만.

### 5. 카메라 취득 = 파이썬 방식 확인
- 파이썬 `AISEN_CODE/main.py`도 `VideoCapture(idx, DSHOW)` + FourCC `Y16 ` + `CONVERT_RGB=0`만(FPS/해상도 미설정). 내 `CltcThermalFrameSource`와 **일치**. (FPV_code 변형만 FPS 설정.)

## 검증
- 빌드 **11 projects 0/0** · 테스트 **101/101**
- 실HW QA: AgentUI 흰화면→iron 컬러 확인, NUC 전/후 FPN·그라데이션 제거 확인, Master 라이브 탭 두 카메라 NATS 스트리밍 확인
- 데드픽셀: 임시 단위검증(검출+이웃대체) 통과 후 삭제. (사용자 지시대로 영구 테스트코드 미작성.)
- 참고: 테스트 `LiveRunning_SnapshotTee` / `HandleCapture_UnknownCamera`가 **가끔 실패** = LiteDB 정적 BsonMapper의 xUnit 병렬 초기화 경합(기존 플래키, 격리 재실행 통과). 내 변경 무관.

## ⚠️ 다음 세션 열린 항목

### A. 2점 NUC (gain 보정) — two_point_viewer 전체 방식
- 현재는 **1점(offset)만**. gain 불균일은 **cold/hot 블랙바디 2점 캘리브레이션** 필요(파이썬 `two_point_viewer`: cold/hot 평균→responsivity/offset, NETD).
- **실 블랙바디 필요** — 지금 BB는 `FakeBlackBodyController`(SimulationMode). 실 BB 확보 시: 캘리브 워크플로(cold 캡처/hot 캡처→gain·offset 맵→적용) 구현. `ThermalNucCorrector`에 gain 필드 추가하는 형태.

### B. 카메라 시리얼 자동 페어링 (장치관리자 장치명 기반) [원래 #4]
- 현재 AgentUI는 config `SerialPortName`(COM 수동). 사용자 요청: 장치관리자 실제 장치명(`CLTC_T_VGA_G2_S_r150`/`r200`)으로 카메라↔COM 자동 매칭.
- Master에 이미 `CameraComPairingService` + `WmiCameraEnumerator`/`WmiUsbSerialEnumerator`(USB parentId 매칭) 존재 → AgentUI에 재사용/이식 검토.

### C. (선택) 데드픽셀 Lynred CSV 병합, NETD 등 two_point_viewer 평가지표

## 변경 파일
- 신규: `Protocols/Cameras/ThermalColorizer.cs`, `Protocols/Cameras/ThermalNucCorrector.cs`, `Master/ViewModels/LiveViewModel.cs`, `Master/Views/LiveView.xaml(.cs)`
- 수정: AgentUI(App.xaml.cs, MainWindow.xaml, ThermalFrameBitmapSourceConverter, CameraPanelViewModel) · Core(INatsCommunicationService, NatsMessages) · Protocols(CameraNatsConnector, ThermalPreviewEncoder, NatsCommunicationService) · Master(MainWindow.xaml, MainViewModel)

## 환경/설정
- AgentUI/Master 종료됨. NATS 가동중. `agentui.json` SimulationMode=false(실카메라 Agent_1/COM7·Agent_2/COM8, Tiff16). `hardware.json` SimulationMode=true(가짜 PLC/BB).
- 참고 파이썬: `참고/AISEN_CODE/`, `참고/FPV_code/`(main/serial/two_point_viewer). 커밋 제외.

## 빌드/테스트
```powershell
dotnet build HeatingCameraSystem.slnx     # 0/0
dotnet test HeatingCameraSystem.slnx      # 101/101 (LiteDB 병렬 플래키 시 격리 재실행)
```
