You are an expert technical-documentation reviewer in a multi-AI review council.
You review ONE chapter of a Korean operations manual for "HeatingCameraSystem" — a thermal-camera chamber system (WPF Master PC + NATS message bus + camera Agent PCs + LS XGT FEnet PLC + serial shutter + recipe-driven automatic capture).

TARGET READER OF THIS CHAPTER: 설치·설정·유지보수 관리자
CHAPTER ID: 10-트러블슈팅   ROUND: 1

TASK — critically review the chapter markdown under "=== CHAPTER CONTENT ===" for:
1. 정확성 — procedures, terminology, field names correct & self-consistent
2. 완결성 — missing steps, missing warnings, unclear points, gaps
3. 명확성 — readability for the target reader
4. 구성/일관성 — structure, heading flow, wording consistency
5. 이미지 자리(📷 [그림 N] blocks) — each placement useful & clearly described?

OUTPUT — write your review in KOREAN, EXACTLY this structure:

# 10-트러블슈팅 — {AI_NAME} 검토 (Round 1)

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

# 10. 트러블슈팅

| 증상 | 점검 |
| --- | --- |
| Agent 초록 점 안 뜸 | Agent 콘솔 'Connected to NATS' 여부, NATS URL/방화벽 |
| 캡처 타임아웃 | 스텝 CameraIndex ↔ AgentId 매칭(4.6). 예: CameraIndex=1 → Agent_1 |
| 캡처 실패 | Agent 콘솔 로그, frame.Empty()면 카메라 드라이버/USB/점유 문제 |
| PLC 온습도 0 고정 | hardware.json Plc.IpAddress/Port, Test-NetConnection <ip> -Port 2004 |
| 값이 이상한 숫자 | D/M/P 토큰·UseHexBitIndex 확인, 온도는 ÷10 스케일 |
| 셔터 미응답 | PortName이 실제 COM과 일치? 포트 점유? _openBuffer/_closeBuffer 프로토콜 |
| 레시피 '챔버 안정화' 멈춤 | 목표 온도 도달 실패(히터/습도제어). Tolerance 안에 못 들면 대기 → STOP 후 점검 |
| Devices에 카메라 안 뜸 | HCS-Manager 예약작업 Running?, NATS 동일 서버?, manager-state.json 엔트리 |
| 승인했는데 Agent 안 뜸 | AgentExePath에 Agent.exe 존재?, logs\{AgentId} 로그, SimulationMode면 spawn 안 함 |
| data.db 열 수 없음 | Master 중복 실행 여부, *.db-log 있으면 자동 복구 |
