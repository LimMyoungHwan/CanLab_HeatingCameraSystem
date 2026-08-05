You are an expert technical-documentation reviewer in a multi-AI review council.
You review ONE chapter of a Korean operations manual for "HeatingCameraSystem" — a thermal-camera chamber system (WPF Master PC + NATS message bus + camera Agent PCs + LS XGT FEnet PLC + serial shutter + recipe-driven automatic capture).

TARGET READER OF THIS CHAPTER: 설치·설정·유지보수 관리자
CHAPTER ID: 02-시스템-아키텍처   ROUND: 1

TASK — critically review the chapter markdown under "=== CHAPTER CONTENT ===" for:
1. 정확성 — procedures, terminology, field names correct & self-consistent
2. 완결성 — missing steps, missing warnings, unclear points, gaps
3. 명확성 — readability for the target reader
4. 구성/일관성 — structure, heading flow, wording consistency
5. 이미지 자리(📷 [그림 N] blocks) — each placement useful & clearly described?

OUTPUT — write your review in KOREAN, EXACTLY this structure:

# 02-시스템-아키텍처 — {AI_NAME} 검토 (Round 1)

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

# 2. 시스템 아키텍처


## 2.1 구성

| 구성요소 | 타겟 | 역할 |
| --- | --- | --- |
| Core | .NET 8 | 인터페이스·모델·설정. 외부 의존성 없음 |
| Protocols | .NET 8 | XGT FEnet(VagabondK)·NATS.Net·시리얼·카메라 구현체, Fake 포함 |
| Master | .NET 8-windows | WPF 운영자 UI, AppServices 정적 서비스 로케이터 |
| Agent | .NET 8 | 카메라 PC 콘솔 앱(OpenCvSharp + NATS) |
| AgentUI | .NET 8-windows | 카메라 런타임 WPF UI |
| AgentManager | .NET 8(win-x64) | USB 카메라 자동발견 + Agent 승인·감독(로그온 예약작업) |
| Simulator / E2EDriver / ManagerE2EDriver | .NET 8 | 외부 시뮬레이터·E2E 드라이버 |

> 📷 **[그림 1] 네트워크·설비 구성도**
> - **캡처 대상:** Master/NATS/Agent PC·PLC·챔버의 실제 네트워크 및 설비 배치도
> - **화면/상태:** 설비 문서의 구성도 또는 현장 배치 사진


## 2.2 NATS 토픽

| 토픽 | 방향 | 내용 |
| --- | --- | --- |
| master.cmd.capture.{AgentId} | Master→Agent | 특정 카메라 캡처 명령 |
| master.cmd.capture.all | Master→전체 | 전체 캡처 브로드캐스트 |
| master.cmd.camera.{AgentId} | Master→Agent | 카메라 제어(RUN/STOP/셔터/캡처/NUC 등) |
| master.config.serial.{AgentId} | Master→Agent | 시리얼 설정 전송 |
| agent.result.capture.{AgentId} | Agent→Master | 캡처 결과(성공여부+경로+이미지바이트) |
| agent.status.{AgentId} | Agent→Master | 하트비트(기본 5초) |
| agent-mgr.inventory.{PCId} | Manager→Master | 카메라 목록·상태 |
| server.cmd.mgr.{PCId} | Master→Manager | 승인/거부/이름/시리얼/재시작/비활성 |

AgentId 형식: 수동 방식은 Agent_{CameraIndex}, Manager 방식은 {PCId}_{HardwareId해시8}. 레시피 스텝에서 CameraAlias를 쓰면 Alias→DB조회→AgentId로 변환되고, 없으면 Agent_{CameraIndex}로 폴백한다.
