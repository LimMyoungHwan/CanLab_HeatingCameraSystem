# 02-시스템-아키텍처 — claude 검토 (Round 1)

## 종합 평가
- 수정 필요도: 중
- 한 줄 요약: 구성 요소 표와 AgentId 규칙은 코드와 정확히 일치하나, NATS 토픽 표가 실제 구현 대비 상당수 누락되어 유지보수 담당자가 트러블슈팅 시 참고하기엔 불완전함.

## 수정 필요 항목
1. [2.2 NATS 토픽] 문제: 코드(`NatsCommunicationService.cs`)에는 `agent.live.{AgentId}`, `master.config.agent.get/set.{AgentId}`, `agent.config.agent.ack/snapshot.{AgentId}`, `agent.config.serial.ack.{AgentId}`, `agent.ack.camera.{AgentId}`, `agent-mgr.log.alert/dump.{PCId}`, `server.req.log.{PCId}` 등 실제 사용 중인 토픽이 다수 있으나 표에는 8개만 실려 있음. -> 제안: "설치·유지보수 관리자용 핵심 토픽만 발췌"라는 문구를 표 상단에 명시하거나, 나머지 토픽을 표에 추가(또는 04장 설정 파일 레퍼런스로 교차 참조 링크 추가).
2. [2.1 구성 표] 문제: 타겟 표기가 `.NET 8-windows`(Master, AgentUI) vs `.NET 8(win-x64)`(AgentManager)로 형식이 불일치. -> 제안: 동일한 표기 규칙으로 통일(예: `.NET 8-windows (win-x64)`).

## 누락/추가 제안
- NATS 토픽 표에 "용도"뿐 아니라 페이로드 대략적 크기/빈도(특히 `agent.result.capture`의 이미지 바이트 포함 여부로 인한 대역폭 이슈)를 한 줄 언급하면 설치 담당자가 네트워크 대역폭 산정 시 도움됨.
- AgentId 규칙 설명 문장이 한 문장에 정보 밀도가 높음. "수동 방식", "Manager 방식", "레시피 CameraAlias 변환 규칙"을 항목별로 나눠 bullet로 표기하면 가독성 개선.

## 이미지 자리 검토
- [그림 1] 적절 — 네트워크·설비 구성도는 2.1 구성 표 직후 배치되어 전체 아키텍처를 시각적으로 보완하는 위치로 타당함. 캡처 대상·화면 설명도 구체적.

## (선택) 수정 제안 전문
(수정량이 표 일부 항목 정정 수준이라 전문 생략)
