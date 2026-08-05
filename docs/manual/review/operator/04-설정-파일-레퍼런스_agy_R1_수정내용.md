# 04-설정-파일-레퍼런스 — agy 검토 (Round 1)

## 종합 평가
- 수정 필요도: 중
- 한 줄 요약: 설정 파일의 위치와 핵심 필드 구조가 잘 명시되어 있으나, agent.json의 누락 필드(`CameraModel`, `LogPath`) 보완 및 hardware.json의 주요 제어 파라미터(PLC 펄스/이동 지연, SR-800N 타임아웃/통신방식) 추가가 필요합니다.

## 수정 필요 항목
1. [4.3 hardware.json — PLC 주요 필드] 문제: P 영역 원터치 비트의 펄스 유지 시간(`PulseHoldMs`), 좌표 이동 전 래치 지연(`CoordinateMoveDelayMs`), 에러 리셋/부저 OFF 비트 등 유지보수 및 캘리브레이션 시 필수적인 PLC 제어 필드가 표에서 누락됨. -> 제안: 표에 `PulseHoldMs`, `CoordinateMoveDelayMs`, `BitErrorReset`, `BitBuzzerOff` 항목을 추가하여 캘리브레이션 및 이상 대치 관련 설정을 안내하도록 보완.
2. [4.4 hardware.json — BlackBody / RecipeEngine] 문제: BlackBody(SR-800N 직접 제어) 설정 시 유닛별 통신 방식(RS-232 Serial vs TCP IP), 타임아웃(`ReadTimeoutMs`), 메시지 간격(`InterMessageDelayMs`), 시뮬레이션 램프 속도(`SimulatedRampCelsiusPerSecond`) 등 상세 파라미터 설명이 부족함. -> 제안: BlackBody 유닛 세부 필드 및 통신 타임아웃 파라미터를 명시하는 내용 추가.
3. [4.5 agent.json] 문제: Agent 설정(`AgentConfig`)의 `CameraModel`(카메라 모델 기반 해상도 스펙 로드) 및 `LogPath`(로그 파일 저장 경로) 필드가 표에서 누락됨. -> 제안: `LogPath`와 `CameraModel` 필드를 표에 추가하고 null/지정 시의 동작(CameraModels\{CameraModel}.json 참조)을 명시.

## 누락/추가 제안
- `4.2 hardware.json — 최상위` 또는 세부 섹션에 `NatsSettings`(`Url`) 및 `SerialSettings`(`PortName`, `BaudRate` 등 시리얼 셔터 제어 설정) 섹션의 기본 설명 보완 제안.
- `4.1 파일 위치` 및 `state.json` 설명에 AgentManager의 주요 설정(`SimulateEnumeration`, `SimulateAgentMode`, `AgentExePath` 등)에 대한 보안/운용 관점의 설명을 추가하면 관리자 레퍼런스로서 완결성이 향상됨.

## 이미지 자리 검토
- 본 챕터에는 `📷 [그림 N]` 블록이 포함되어 있지 않습니다. 텍스트 설정 레퍼런스 특성상 텍스트 표와 주의/참고 콜아웃 위주의 구성이 타당하며, 이미지 추가 없이도 정보 전달이 명확합니다.

## (선택) 수정 제안 전문

```markdown
# 4. 설정 파일 레퍼런스


## 4.1 파일 위치

| 파일 | 위치 | 주체 | 자동생성 |
| --- | --- | --- | --- |
| hardware.json | %LOCALAPPDATA%\HeatingCameraSystem\ | Master | 예 |
| data.db | %LOCALAPPDATA%\HeatingCameraSystem\ | Master(LiteDB) | 예(빈 상태) |
| recipe/*.json | %LOCALAPPDATA%\HeatingCameraSystem\recipe\ | Master | 예(레시피 저장) |
| ImageCache | %LOCALAPPDATA%\HeatingCameraSystem\ImageCache\ | Master | 예 |
| agent.json | Agent exe 폴더 | Agent | 예 |
| manager-settings/state.json | C:\HeatingCameraSystem\Manager\ | AgentManager | 예 |

> **[참고]** 레시피는 예전 LiteDB 컬렉션이 아니라 recipe 폴더의 개별 JSON 파일로 저장된다(구버전 DB가 있으면 최초 1회 자동 마이그레이션). data.db에는 촬영 이력·챔버 이력·알람 이력·대시보드 레이아웃·카메라 시리얼/디바이스 정보가 저장된다.


## 4.2 hardware.json — 최상위

| 필드 | 기본값 | 의미 |
| --- | --- | --- |
| SimulationMode | false | true 시 PLC/셔터를 Fake로 대체(카메라 시뮬은 agent.json) |
| DataRetentionDays | 30 | 촬영 이미지·이력 보관 일수(0=정리 안 함) |
| CameraPairings | [] | 카메라-COM 페어링 목록 |
| Plc / Nats / Serial / BlackBody / RecipeEngine | 각 섹션 | 아래 4.3~4.6 참고 |

> **[참고]** 구 매뉴얼과 달리 캡처 보관일은 코드 하드코딩이 아니라 hardware.json의 DataRetentionDays로 조정한다. BlackBody(SR-800N 직접제어)·CameraPairings 섹션도 현재 버전에서 추가되었다.
> Nats 섹션(`Url`: nats://127.0.0.1:4222)은 메세지 버스 연결 주소를, Serial 섹션(`PortName`: COM3, `BaudRate`: 9600 등)은 카메라 시리얼 셔터 포트 통신 속성을 설정한다.


## 4.3 hardware.json — PLC 주요 필드

| 필드 | 기본값 | 의미 |
| --- | --- | --- |
| IpAddress / Port / StationNo | 192.168.1.2 / 2004 / 0 | PLC 연결(XGT FEnet) |
| CpuSeries / UseHexBitIndex | XGB / true | CPU 계열, 비트 인덱스 16진(비트 오독 시 반전) |
| TempPv/TempSv/TempTarget | D100/D102/D112 | 챔버 온도 현재/제어/최종목표(×10) |
| HumPv/HumSv | D130/D131 | 챔버 습도(×10) |
| Bb1Pv/Bb1Sv, Bb2Pv/Bb2Sv | D140/142, D150/152 | 흑체1/2 PV·SV(×100) |
| BitTempStart/Stop | M10/M11 | 온도제어 시작/정지 |
| BitErrorReset / BitBuzzerOff | P525 / P250 | 에러 리셋 / 부저 OFF (모멘터리 비트) |
| ServoXPos/YPos, XBusy/YBusy | D2540/D2640, D2520.0/D2620.0 | 서보 위치·구동 비트 |
| ServoPointMoveBase / XBase / YBase | P601 / D3010 / D3012 | 포인트 이동 트리거·좌표 워드 |
| PulseHoldMs | 100 | 원터치 비트 ON 유지 시간(ms) |
| CoordinateMoveDelayMs | 1000 | 좌표 직접 이동 시 목표 워드 쓰기 후 트리거 전 지연(ms) |
| BitJogX± / BitJogY± | P745/746 / P725/726 | 조그 비트(Y축은 placeholder) |
| Eq* (냉동기/블로워/칠러/도어락/조명/페어글라스) | M502~506, P410/411, D280.0, P370 | 장비 원터치 비트 |
| Admin* (과열상한/경계/딜레이/MFC) | D4004, D19xx, D78 | 관리자 한계값(×10) |

스케일: 온·습도 쓰기 value×10 → short, 읽기 short/10. 흑체는 ×100.

> **[주의]** ServoSpeedPercent(D2560), BitEmergencyStop(M2000), Y축 조그(P725/726), ServoPointYBase(D3012)는 소스 주석에 placeholder로 표시된 임의값이다. 실제 비트/워드 확인 후 교체한다.


## 4.4 hardware.json — BlackBody / RecipeEngine

| 필드 | 기본값 | 의미 |
| --- | --- | --- |
| BlackBody.Enabled | false | true 시 PLC 경유 대신 SR-800N 직접제어 |
| BlackBody.Simulated | false | 물리 장비 없이 인메모리 SR-800N 구동 |
| BlackBody.SimulatedRampCelsiusPerSecond | 5.0 | 시뮬레이션 구동 시 목표 온도 수렴 속도(℃/s) |
| BlackBody.ReadTimeoutMs / InterMessageDelayMs | 1500 / 50 | SR-800N 응답 대기 제한시간 및 메시지 간 안전 간격(ms) |
| BlackBody.Units[] | COM4/COM5 | 유닛별 연결(ConnectionType: Serial=115200 8N1 / Ip=TCP) |
| RecipeEngine.TemperatureTolerance | 0.5 | 챔버·흑체 안정화 허용오차(℃) |
| RecipeEngine.CaptureResultTimeoutSeconds | 30 | 캡처 결과 대기 제한시간(초) |
| RecipeEngine.RampStepIntervalSeconds | 60 | 온도 램프 SV 갱신 간격(초) |

> **[참고]** 현재 버전은 위 RecipeEngine 3개 값을 모두 hardware.json에서 읽어 실제로 적용한다(구 매뉴얼의 '하드코딩/미사용' 설명은 더 이상 유효하지 않음). RampStepIntervalSeconds는 온도 램프 기능과 함께 추가되었다.


## 4.5 agent.json

| 필드 | 기본값 | 의미 |
| --- | --- | --- |
| AgentId | 머신명 | 토픽 라우팅 키(권장 Agent_<숫자>) |
| CameraIndex | 0 | OpenCV 카메라 인덱스 + 토픽 매칭 키 |
| CameraModel | null | 지정 시 CameraModels\{CameraModel}.json 읽어 해상도 적용 (null시 기본 해상도) |
| NatsUrl | nats://127.0.0.1:4222 | NATS 주소 |
| StoragePath | ImageStorage | 캡처 저장 폴더(상대=exe 기준) |
| LogPath | "" | 로그 파일 저장 경로 (미지정 시 기본 경로) |
| HeartbeatIntervalSeconds | 5 | 상태 발행 주기 |
| SimulationMode | false | true 시 합성 이미지 |


## 4.6 카메라 ↔ Agent ↔ NATS 매핑(중요)

레시피 스텝의 CameraIndex는 master.cmd.capture.Agent_{CameraIndex} 토픽으로 발행된다. 즉 수동 Agent의 AgentId는 Agent_<CameraIndex> 형태여야 명령을 받는다. Manager가 {PCId}_{해시8} 형식 AgentId를 부여한 경우에는 스텝의 CameraAlias에 해당 카메라 Alias를 적어 DB 조회로 라우팅한다.

| 스텝 CameraIndex | 발행 토픽 | 받아야 할 AgentId |
| --- | --- | --- |
| 0 | master.cmd.capture.Agent_0 | Agent_0 |
| 1 | master.cmd.capture.Agent_1 | Agent_1 |
```
