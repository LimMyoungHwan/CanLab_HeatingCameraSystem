You are an expert technical-documentation reviewer in a multi-AI review council.
You review ONE chapter of a Korean operations manual for "HeatingCameraSystem" — a thermal-camera chamber system (WPF Master PC + NATS message bus + camera Agent PCs + LS XGT FEnet PLC + serial shutter + recipe-driven automatic capture).

TARGET READER OF THIS CHAPTER: 설치·설정·유지보수 관리자
CHAPTER ID: 04-설정-파일-레퍼런스   ROUND: 1

TASK — critically review the chapter markdown under "=== CHAPTER CONTENT ===" for:
1. 정확성 — procedures, terminology, field names correct & self-consistent
2. 완결성 — missing steps, missing warnings, unclear points, gaps
3. 명확성 — readability for the target reader
4. 구성/일관성 — structure, heading flow, wording consistency
5. 이미지 자리(📷 [그림 N] blocks) — each placement useful & clearly described?

OUTPUT — write your review in KOREAN, EXACTLY this structure:

# 04-설정-파일-레퍼런스 — {AI_NAME} 검토 (Round 1)

## 종합 평가
- 수정 필요도: 상 / 중 / 하   (택1)
- 한 줄 요약: ...

## 수정 필요 항목
1. [위치/섹션] 문제: ... -> 제안: ...
2. ...
(문제 없으면 "없음")

## 누락/추가 제안
- ...   (없으면 "없음")

## 이미지 자리 검토
- [그림 N] 적절/부적절 — 사유

## (선택) 수정 제안 전문
(수정량 많을 때만, 수정 반영한 챕터 markdown 전문)

RULES:
- 반드시 실제 챕터 내용에 근거. 없는 기능/화면 지어내지 말 것.
- 이미 좋으면 솔직히 "하"로, 항목 최소화.
- 구체적·실행가능·간결. 한국어.
- {AI_NAME} 은 본인 이름(codex/kimi/agy/claude)으로 대체.


=== CHAPTER CONTENT (review target) ===

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
| Plc / Nats / Serial / BlackBody / RecipeEngine | 각 섹션 | 아래 4.3~4.6 |

> **[참고]** 구 매뉴얼과 달리 캡처 보관일은 코드 하드코딩이 아니라 hardware.json의 DataRetentionDays로 조정한다. BlackBody(SR-800N 직접제어)·CameraPairings 섹션도 현재 버전에서 추가되었다.


## 4.3 hardware.json — PLC 주요 필드

| 필드 | 기본값 | 의미 |
| --- | --- | --- |
| IpAddress / Port / StationNo | 192.168.1.2 / 2004 / 0 | PLC 연결(XGT FEnet) |
| CpuSeries / UseHexBitIndex | XGB / true | CPU 계열, 비트 인덱스 16진(비트 오독 시 반전) |
| TempPv/TempSv/TempTarget | D100/D102/D112 | 챔버 온도 현재/제어/최종목표(×10) |
| HumPv/HumSv | D130/D131 | 챔버 습도(×10) |
| Bb1Pv/Bb1Sv, Bb2Pv/Bb2Sv | D140/142, D150/152 | 흑체1/2 PV·SV(×100) |
| BitTempStart/Stop | M10/M11 | 온도제어 시작/정지 |
| ServoXPos/YPos, XBusy/YBusy | D2540/D2640, D2520.0/D2620.0 | 서보 위치·구동 비트 |
| ServoPointMoveBase / XBase / YBase | P601 / D3010 / D3012 | 포인트 이동 트리거·좌표 워드 |
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
| BlackBody.Units[] | COM4/COM5 | 유닛별 연결(Serial 115200 8N1 / Ip) |
| RecipeEngine.TemperatureTolerance | 0.5 | 챔버·흑체 안정화 허용오차(℃) |
| RecipeEngine.CaptureResultTimeoutSeconds | 30 | 캡처 결과 대기 제한시간(초) |
| RecipeEngine.RampStepIntervalSeconds | 60 | 온도 램프 SV 갱신 간격(초) |

> **[참고]** 현재 버전은 위 RecipeEngine 3개 값을 모두 hardware.json에서 읽어 실제로 적용한다(구 매뉴얼의 '하드코딩/미사용' 설명은 더 이상 유효하지 않음). RampStepIntervalSeconds는 온도 램프 기능과 함께 추가되었다.


## 4.5 agent.json

| 필드 | 기본값 | 의미 |
| --- | --- | --- |
| AgentId | 머신명 | 토픽 라우팅 키(권장 Agent_<숫자>) |
| CameraIndex | 0 | OpenCV 카메라 인덱스 + 토픽 매칭 키 |
| NatsUrl | nats://127.0.0.1:4222 | NATS 주소 |
| StoragePath | ImageStorage | 캡처 저장 폴더(상대=exe 기준) |
| HeartbeatIntervalSeconds | 5 | 상태 발행 주기 |
| SimulationMode | false | true 시 합성 이미지 |


## 4.6 카메라 ↔ Agent ↔ NATS 매핑(중요)

레시피 스텝의 CameraIndex는 master.cmd.capture.Agent_{CameraIndex} 토픽으로 발행된다. 즉 수동 Agent의 AgentId는 Agent_<CameraIndex> 형태여야 명령을 받는다. Manager가 {PCId}_{해시8} 형식 AgentId를 부여한 경우에는 스텝의 CameraAlias에 해당 카메라 Alias를 적어 DB 조회로 라우팅한다.

| 스텝 CameraIndex | 발행 토픽 | 받아야 할 AgentId |
| --- | --- | --- |
| 0 | master.cmd.capture.Agent_0 | Agent_0 |
| 1 | master.cmd.capture.Agent_1 | Agent_1 |
