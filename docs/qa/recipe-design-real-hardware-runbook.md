# Recipe Design — 실장비 QA 런북 (CLTC_T_VGA ×2 + COM7/COM8 + PLC)

SimulationMode QA는 통과했습니다(`recipe-design-simulation-qa.md`). 실제 하드웨어에서만 확정 가능한 항목을 아래 순서로 검증합니다.

## 0. 사전 준비
- CLTC_T_VGA 열화상 카메라 2대 USB 연결 → **장치 관리자 > 카메라**에 `CLTC_T_VGA...` 2개 표시 확인.
- 각 카메라의 USB-시리얼(제어 채널)이 **COM7 / COM8** 로 잡혔는지 확인(장치 관리자 > 포트).
- PLC(XGT FEnet, TCP 2004) 네트워크 도달 확인.
- `docs/deployment/docker-compose.yml`로 NATS 기동(캡처 파이프라인 사용 시).

## 1. 실모드 전환
- `%LOCALAPPDATA%\HeatingCameraSystem\hardware.json` 에서 `"SimulationMode": false` 확인(기본값).
- Master 재시작: `dotnet run --project HeatingCameraSystem.Master`.

## 2. 카메라↔COM 매칭 (S4)
- 레시피 디자인 > **LIVE PREVIEW & MOTION** 패널 > **REFRESH PAIRING**.
- 각 카메라가 `Paired` 상태 + COM + 9자리 시리얼번호(예 `0001XXYYY`)를 표시하는지 확인.
- **미매칭/Ambiguous 시**: `UsbTopology.DeriveContainerId`가 레지스트리 `HKLM\SYSTEM\CurrentControlSet\Enum\{PnPID}\ContainerID`로 물리 장치를 식별함. 두 카메라가 동일 VID/PID여도 ContainerID로 구분됨. ContainerID 부재 시 `NormalizeParent`(`&MI_xx` 제거) 폴백 — 이 경우 수동 override(패널에서 재지정 → hardware.json `CameraPairings`에 영속) 사용.

## 3. 라이브 열화상 프리뷰 (S2)
- 카메라 A 선택 → 실제 열화상 영상이 렌더되는지 확인(검은 화면/BGR 깨짐 아님).
- **깨짐 시 점검**: `CltcLiveThermalCamera`는 `VideoCapture(idx, DSHOW)` + FourCC `"Y16 "`(끝 공백 포함) + `ConvertRgb=0`을 **첫 read 전** 설정. MSMF 백엔드는 Y16 손실 → DSHOW 강제 필수. 프레임은 `CV_16UC1`, 14bit 마스크(`&0x3FFF`).
- 손/차가운 표면을 향하게 해 핫스팟이 영상에서 이동하는지 확인.

## 4. XY 이동 (S3)
- 위치(스텝) 선택 → X/Y 입력 → **GO TO XY**: 서보가 이동하고 하단 `X:/Y:` readback이 목표값으로 갱신되는지 확인.
- **USE CURRENT**: 현재 서보 좌표가 스텝 X/Y로 복사되는지 확인.
- JOG 패드(X±/Y±) 누름/뗌 동작, **HOME** 원점 복귀 확인.
- ⚠️ 플레이스홀더 주소(실비트 확정 필요, `AGENTS.md` 알려진 플레이스홀더):
  - `MoveToCoordinateAsync`: X→`ServoPointXBase`(D3010), Y→`ServoPointYBase`(D3012), 트리거→`ServoPointMoveBase`(P601) **재사용**. 실 위치결정(XBF-PD02A) 좌표/기동 주소 대조 후 hardware.json에서 교체.
  - 도착 판정은 서보 축 **idle**(`ServoXBusy/ServoYBusy` 비구동)로 확인 — 이동 트리거 직후 busy가 늦게 서면 정착 지연 추가 튜닝 필요.

## 5. 시리얼 제어 (S5)
- SHUTTER OPEN/CLOSE, CAMERA START/STOP 동작 확인.
- FPA 온도가 그럴듯한 값으로 표시·갱신되는지 확인.
- CL 프로토콜: 115200 8N1, 7바이트 `[0x43,0x4C,MainId,SubId,RW,0,Data]`. 응답 무반응 시 baud/포트/배선 점검(기존 셔터 프로토콜 `{0x04,..}`/9600은 폐기됨 — 실장비는 CL 방식).

## 6. 레시피 저장/실행 (S1)
- 위치별 [카메라, XY, 챔버온습도, 흑체온도] + 레시피명 입력 → **SAVE RECIPE** → 앱 재시작 → 값 복원 확인(LiteDB `data.db`).
- 레시피 실행 시 RecipeEngine이 스텝별 `MoveToCoordinateAsync(PositionX,PositionY)`로 이동함(포인트 인덱스 아님).

## 확정 후 반영
실 비트/주소 확인되면 `hardware.json`(또는 `docs/samples/hardware.json`)에서 `ServoPointYBase`, `ServoPointMoveBase`, JOG/HOME 비트, 카메라 시리얼 baud 등을 실측값으로 교체하고 `AGENTS.md` 플레이스홀더 항목에서 제거.
