# 02-시스템-아키텍처 — agy 검토 (Round 1)

## 종합 평가
- 수정 필요도: 중
- 한 줄 요약: 필수 모듈 구성 및 NATS 토픽 명세가 명확히 작성되어 있으나, 유지보수 관리자가 전체 통신 및 데이터 흐름을 직관적으로 파악할 수 있도록 하드웨어 연동 및 동작 방식 설명 보완이 필요합니다.

## 수정 필요 항목
1. [2.1 구성] 문제: 모듈별 소프트웨어 역할(Core, Protocols, Master 등)은 잘 설명되어 있으나, 각 모듈이 물리적 하드웨어(LS XGT PLC, USB 카메라, 시리얼 셔터 등)와 어떻게 통신/연결되는지 표기되어 있지 않아 타겟 독자(설치·유지보수 관리자) 관점에서 연결 구조 이해가 다소 어렵습니다. -> 제안: 구성요소 표의 역할 열에 연동 프로토콜/하드웨어 정보를 추가 명시.
2. [2.2 NATS 토픽] 문제: NATS 토픽 목록에 하트비트(5초 간격) 외 다른 토픽들의 발송/수신 트래픽 성격(이벤트 기반, 요청-응답 등)이 기재되어 있지 않습니다. -> 제안: 토픽 표에 '작동 방식' 열을 추가하여 유지보수 및 네트워크 모니터링 시 트래픽 발생 시점을 명확히 구별할 수 있게 함.

## 누락/추가 제안
- **통신 및 제어 흐름 개요 추가**: '2.1 구성'과 '2.2 NATS 토픽' 사이에 Master-PLC 통신(TCP 2004 FEnet)과 Master-NATS-Agent 통신 구조 간의 전체 제어/데이터 흐름에 대한 짧은 요약 설명을 추가하면 유지보수 시 장애 지점(Troubleshooting Point) 파악에 크게 도움이 됩니다.

## 이미지 자리 검토
- **[그림 1] 네트워크·설비 구성도**: 적절 — Master PC, NATS 메시지 버스, Agent PC, PLC, 챔버 카메라 및 셔터 간의 전체 물리/논리 네트워크 배치를 한눈에 파악할 수 있어 시스템 아키텍처 이해에 반드시 필요한 필수 시각 자료임.

## (선택) 수정 제안 전문

```markdown
# 2. 시스템 아키텍처


## 2.1 구성

| 구성요소 | 타겟 | 역할 및 연동 하드웨어/프로토콜 |
| --- | --- | --- |
| Core | .NET 8 | 인터페이스·모델·설정 (외부 의존성 없는 순수 라이브러리) |
| Protocols | .NET 8 | XGT FEnet(VagabondK, TCP 2004)·NATS.Net·시리얼 셔터·카메라 구현체 (Fake 포함) |
| Master | .NET 8-windows | WPF 운영자 UI, AppServices 정적 서비스 로케이터 (PLC 통신 및 레시피 제어 총괄) |
| Agent | .NET 8 | 카메라 PC 콘솔 앱 (OpenCvSharp + NATS), USB 카메라 캡처 및 시리얼 셔터 직접 제어 |
| AgentUI | .NET 8-windows | 카메라 런타임 WPF UI |
| AgentManager | .NET 8(win-x64) | USB 카메라 자동발견 + Agent 승인·감독 (로그온 예약작업 기반 관리 호스트) |
| Simulator / E2EDriver / ManagerE2EDriver | .NET 8 | 외부 시뮬레이터 및 E2E 테스트 드라이버 모듈 |

> 📷 **[그림 1] 네트워크·설비 구성도**
> - **캡처 대상:** Master/NATS/Agent PC·PLC·챔버의 실제 네트워크 및 설비 배치도
> - **화면/상태:** 설비 문서의 구성도 또는 현장 배치 사진


## 2.2 NATS 토픽

| 토픽 | 방향 | 내용 | 작동 방식 |
| --- | --- | --- | --- |
| master.cmd.capture.{AgentId} | Master→Agent | 특정 카메라 캡처 명령 | 레시피 스텝 / 운영자 개별 캡처 요청 시 |
| master.cmd.capture.all | Master→전체 | 전체 캡처 브로드캐스트 | 전체 카메라 동시 캡처 이벤트 발생 시 |
| master.cmd.camera.{AgentId} | Master→Agent | 카메라 제어(RUN/STOP/셔터/캡처/NUC 등) | 운영자 제어 명령 전송 시 |
| master.config.serial.{AgentId} | Master→Agent | 시리얼 설정 전송 | Agent 최초 접속 또는 설정 변경 시 |
| agent.result.capture.{AgentId} | Agent→Master | 캡처 결과(성공여부+경로+이미지바이트) | Agent 캡처 완료 즉시 발행 |
| agent.status.{AgentId} | Agent→Master | 하트비트 | 기본 5초 간격 주기적 발행 |
| agent-mgr.inventory.{PCId} | Manager→Master | 카메라 목록·상태 | 카메라 연결 상태 변경 및 주기 보고 |
| server.cmd.mgr.{PCId} | Master→Manager | 승인/거부/이름/시리얼/재시작/비활성 | Master 운영자의 승인/관리 명령 전송 시 |

AgentId 형식: 수동 방식은 Agent_{CameraIndex}, Manager 방식은 {PCId}_{HardwareId해시8}. 레시피 스텝에서 CameraAlias를 쓰면 Alias→DB조회→AgentId로 변환되고, 없으면 Agent_{CameraIndex}로 폴백한다.
```
