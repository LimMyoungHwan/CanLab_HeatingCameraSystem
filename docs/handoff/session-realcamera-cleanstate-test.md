# Handoff — 다음 세션: 실카메라 클린 상태 전체 재테스트

## 이번 세션 완료 (커밋됨)
UI/카메라 이슈 4건 + 대시보드 알람 피드. 빌드 12/0/0, 테스트 191/191.

1. **④ 프리뷰 노트북캠 버그** — RecipeEditor 프리뷰가 로컬 `VideoCapture`(노트북캠) 대신 Agent NATS `LiveFrameMessage`(실 열화상)를 표시. 로컬 `CltcLiveThermalCamera`/`CameraComPairingService` 경로 제거. 셔터/카메라 버튼은 NATS `CameraControlMessage`로 재배선. FPA 표시는 제거(NATS 소스 없음).
2. **① 맵핑 빈 목록** — `RecipeEditorViewModel`이 빈 `CameraDeviceRepo` 대신 온라인 하트비트 Agent(`OnAgentStatus`)로 `OnlineAgentCameras` + `AvailableMappingCameras` 채움. 스텝 CAMERA 콤보도 온라인 Agent 기준.
3. **② 중복 시리얼 설정 제거** — Agent 설정의 "시리얼 설정" 탭 + `SerialVm` + orphan `SettingsView`/`SettingsViewModel` 삭제. 탭 2개(에이전트 원격설정 · 디바이스 관리)만.
4. **③ 흐린 글씨** — AgentSettingsView/DevicesView `#859493` → `#bac9c9`.
5. **알람 피드(가벼운 A안)** — `AlarmSink`(세션 인메모리 피드). 소스: PLC 에러비트(rising-edge)·PLC 연결끊김/복구·Agent 오프라인·PLC/시리얼 재연결 실패/성공·NATS/PLC/흑체 연결실패·캡처 실패/타임아웃. Dashboard ALARMS 카드 = 피드(시각·심각도·출처·메시지), 헤더 ⚠ALARMS 카운트 칩.

검증은 전부 **시뮬레이터 + FakePlc(SimMode)** 로만 함. 실HW 미검증.

## 다음 세션 목표 = 실카메라 클린 상태 E2E
모든 설정 초기화 → 처음부터 실장비 연결 → 실제 연동 정상 여부 확인.

### 1) 설정 전부 초기화 (삭제 → 기본값 재생성)
```
# Master
del "%LOCALAPPDATA%\HeatingCameraSystem\hardware.json"
del "%LOCALAPPDATA%\HeatingCameraSystem\data.db"
rmdir /s /q "%LOCALAPPDATA%\HeatingCameraSystem\ImageCache"
# AgentUI
del "%LOCALAPPDATA%\HeatingCameraSystem\AgentUI\agentui.json"
# Agent(콘솔) — exe 폴더
del <Agent exe 폴더>\agent.json
# 캡처 이미지
rmdir /s /q <Agent exe 폴더>\ImageStorage
```
삭제하면 최초 실행 시 기본값으로 자동 재생성됨.

### 2) 실장비 구성
- **NATS 브로커** 기동(실 IP 또는 로컬 `127.0.0.1:4222`). `E:\SW\Nats\nats-server.exe` 있음.
- **AgentUI** `agentui.json`: `SimulationMode=false`, 실 CLTC 카메라의 정확한 `OpenCvIndex` + `SerialPortName`(COM) 지정. (이전 하이브리드: Agent_1 idx1/COM7, Agent_2 idx2/COM8)
- **Master** `hardware.json`: `SimulationMode=false`, `Plc.IpAddress`=실 PLC(192.168.1.2:2004), `Serial` 실 COM.

### 3) 검증 체크리스트 (이번 세션 변경분을 실HW로 재확인)
- [ ] Agent 하트비트 → Dashboard `AGENTS N ONLINE` + Mode 타일에 **실 열화상** 렌더.
- [ ] **④** Recipe Editor 프리뷰: 온라인 Agent 선택 → 실 열화상(노트북캠 아님). 셔터/카메라 버튼 NATS 동작.
- [ ] **①** 맵핑 "사용 가능한 카메라"에 온라인 Agent 뜸 → 슬롯 배정 → 레시피 저장/실행.
- [ ] **②** Agent 설정 탭 2개(시리얼 설정 없음). **③** 글씨 가독.
- [ ] **알람** 실 연결 끊김(카메라 USB 뽑기 / PLC 끊기 / Agent 종료)시 피드 + 헤더 카운트 반응.
- [ ] 레시피 실행 E2E: 실 PLC 승온·서보 이동·흑체·실카메라 캡처.

### 참고
- 실카메라 `OpenCvIndex`는 DirectShow 열거 순서라 장치 물리 순서와 다를 수 있음 → agentui.json에서 실측 인덱스로 조정 필요.
- 알람은 세션 인메모리(영속화 없음). 필요시 B안(이력/ack/LiteDB/전역 벨) 별도 요청.
