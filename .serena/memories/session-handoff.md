# Session Handoff (2026-07-23 세션2) — 라이브 색상/NUC/데드픽셀 + Master NATS 스트리밍

Full doc: `docs/handoff/session2-live-color-nuc-nats-handoff.md` (read first).
Prev: `docs/handoff/blackbody-camera-features-handoff.md`.

## Done this session (커밋됨)
빌드 0/0, 테스트 101/101, 실HW QA. 영구 테스트코드 미작성(사용자 지시).

1. **흰화면 수정**: `CameraPanelViewModel.StartLiveAsync`(RUN+셔터열기, 시작 자동)/`StopLiveAsync`(셔터닫기+STOP, 종료 자동). App.xaml.cs 배선. (기본 셔터닫힘+비RUN이라 UVC에 실장면 없던 게 원인.)
2. **iron 컬러**: `ThermalColorizer`(Protocols) = plateau AGC(파이썬 `two_point_viewer.thresh_plateau_hist_eq`, bin clip 100)+iron 팔레트 LUT→BGR. AgentUI converter가 사용.
3. **NUC 1점 FFC + 데드픽셀**: `ThermalNucCorrector`(카메라별 공유). offset(셔터 플랫 평균)+데드픽셀(5x5 로컬중앙값 아웃라이어 max(50,5·std)→이웃평균 대체). NUC 버튼(RunNucAsync). 표시+NATS 둘 다 같은 corrector로 적용.
4. **Master NATS 라이브(사용자 지정 구조 B)**: `LiveFrameMessage` + `agent.live.*` + `CameraNatsConnector.LiveStreamLoopAsync`(~10fps NUC적용 컬러JPEG 발행) + Master `LiveViewModel`/`LiveView`/nav "라이브 영상". 단일 PC 충돌無(AgentUI만 카메라 오픈).
5. **카메라 취득 = 파이썬 일치**: AISEN `main.py`도 VideoCapture(DSHOW)+FourCC Y16+ConvertRgb=0만 → `CltcThermalFrameSource`와 동일.

## 다음 세션 (열린 항목)
- **A. 2점 NUC(gain)** — two_point_viewer 전체 방식. cold/hot 블랙바디 2점 캘리브 필요. **실 BB 필요**(지금 `FakeBlackBodyController`, SimulationMode). gain·offset 맵 캘리브 워크플로 → ThermalNucCorrector에 gain 추가.
- **B. 시리얼 자동 페어링**(원래 #4): 장치관리자 실제 장치명(`CLTC_T_VGA_G2_S_r150`/`r200`) 기반 카메라↔COM 자동 매칭. Master `CameraComPairingService`+`WmiCameraEnumerator`/`WmiUsbSerialEnumerator`(USB parentId 매칭) 재사용/이식 검토.
- C. (선택) Lynred CSV 데드픽셀 병합, NETD 등 two_point_viewer 평가지표.

## 환경/설정
- AgentUI/Master 종료. NATS 가동중. `agentui.json` SimulationMode=false(실카메라 Agent_1/COM7·Agent_2/COM8, Tiff16). `hardware.json` SimulationMode=true(가짜 PLC/BB) — 실 PLC/BB 연결시 false.
- 참고 파이썬: `참고/AISEN_CODE/`, `참고/FPV_code/`(main/serial/two_point_viewer). 커밋 제외.
- untracked 참고자료: `docs/EnetClient*`, `docs/PC통신라이브러리예제*`, `.council/` — 커밋 제외.

## Env — WPF QA
windows-mcp `State-Tool(use_vision=true)` DPI-aware 좌표+스샷, `Click-Tool loc=[physX,physY]`. 카메라 시리얼 COM7/COM8. **주의**: 파일탐색기 등이 포커스 가로채면 State-Tool이 그 앱 요소만 반환 → AppActivate 후 재캡처. 데드픽셀 같은 비UI 로직은 임시 xUnit 검증(실행 후 삭제)이 UI자동화보다 확실.

## 테스트 플래키 (기존)
`LiveRunning_SnapshotTee` / `HandleCapture_UnknownCamera` 가끔 실패 = LiteDB 정적 BsonMapper xUnit 병렬 초기화 경합. 격리 재실행 통과. 코드 변경 무관.
