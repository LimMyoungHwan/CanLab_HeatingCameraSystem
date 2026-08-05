# HeatingCameraSystem.Protocols

## 범위

Core의 인터페이스와 모델을 실제 하드웨어·네트워크·시리얼 구현으로 연결한다.
새 장치 계약은 먼저 `HeatingCameraSystem.Core`에 정의하고, 구현은 이 프로젝트에 둔다.

## PLC: LS XGT FEnet

- 구현 중심 파일은 `PlcXgtClient.cs`다. Modbus 주소 규칙을 새로 만들지 않는다.
- 설정 토큰은 `D100`, `M10`, `P000`, `D2520.0` 같은 XGT 논리 디바이스 형식이다.
- `D2520.0` 같은 워드 내 비트는 워드 읽기+마스크로 읽고, 쓰기는 read-modify-write를 사용한다.
- 순수 비트 디바이스(`M10`, `P000`)는 직접 접근한다.
- XGB CPU 기본값은 `UseHexBitIndex=true`다. 실제 PLC 명세 확인 없이 주소나 비트 인덱스를 바꾸지 않는다.
- 상태 전체는 `IPlcController.ReadStatusAsync()`의 `PlcStatusSnapshot` 계약을 유지한다.

## 시리얼 셔터

- `SerialShutterController.cs`는 ASCII 명령이 아닌 raw binary를 전송한다.
- 열기: `04 00 01 00 00 00 00`
- 닫기: `04 00 00 00 00 00 00`
- `cameraIndex`는 식별자일 뿐 전송 버퍼에 넣지 않는다.
- 하드웨어 상태 조회가 없으므로 `GetShutterStateAsync`는 소프트웨어 캐시를 반환한다.

## NATS

- `NatsCommunicationService.cs`는 `NATS.Net`을 사용한다.
- NATS 연결 재시도는 라이브러리/서비스의 기존 경로를 따른다. `ConnectionMonitorService`에 NATS 재시도를 추가하지 않는다.
- 토픽 문자열은 루트 `AGENTS.md`의 Master/Agent 계약과 정확히 일치해야 한다.
- 구독 콜백은 예외가 전체 구독 루프를 중단시키지 않는지 확인하고, 변경 시 retry 테스트를 실행한다.

## 구현과 테스트

- 실제 구현과 함께 `Simulation` 아래 Fake 구현의 동작도 유지한다.
- 카메라·PLC·NATS 통합 변경은 `HeatingCameraSystem.Tests`의 Protocols 및 외부 시뮬레이터 테스트를 실행한다.
- VagabondK 타입·메서드 이름을 추측하지 말고 현재 패키지 API와 기존 구현을 기준으로 사용한다.
- 장치 주소, 타임아웃, 재시도 정책 변경은 하드웨어 영향이 있으므로 최소 diff로 검토한다.
