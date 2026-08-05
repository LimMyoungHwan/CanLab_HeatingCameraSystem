You are an expert technical-documentation reviewer in a multi-AI review council.
You review ONE chapter of a Korean operations manual for "HeatingCameraSystem" — a thermal-camera chamber system (WPF Master PC + NATS message bus + camera Agent PCs + LS XGT FEnet PLC + serial shutter + recipe-driven automatic capture).

TARGET READER OF THIS CHAPTER: 일상 운전자 (Master 화면으로 매일 촬영·모니터링)
CHAPTER ID: 11-자주-겪는-문제(사용자용)   ROUND: 1

TASK — critically review the chapter markdown under "=== CHAPTER CONTENT ===" for:
1. 정확성 — procedures, terminology, field names correct & self-consistent
2. 완결성 — missing steps, missing warnings, unclear points, gaps
3. 명확성 — readability for the target reader
4. 구성/일관성 — structure, heading flow, wording consistency
5. 이미지 자리(📷 [그림 N] blocks) — each placement useful & clearly described?

OUTPUT — write your review in KOREAN, EXACTLY this structure:

# 11-자주-겪는-문제(사용자용) — {AI_NAME} 검토 (Round 1)

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

# 11. 자주 겪는 문제(사용자용)

| 증상 | 확인 사항 |
| --- | --- |
| 대시보드에 카메라 초록 점이 안 뜸 | 카메라 PC가 켜져 있는지, 네트워크 연결, 잠시 후에도 회색이면 운영자에게 문의 |
| 촬영이 제한시간 초과(캡처 타임아웃) | 레시피 스텝의 카메라 지정과 실제 카메라 매칭 확인, 대상 카메라 점 색 확인 |
| 온도/습도가 0으로 고정 | PLC 연결 표시등 확인(빨강이면 PLC/네트워크 문제 → 운영자 문의) |
| 레시피가 '챔버 안정화'에서 멈춤 | 챔버가 목표 온도에 도달 못함(히터/습도제어 문제). STOP 후 설비 점검 |
| 셔터/카메라가 반응 없음 | 수동 조작의 카메라 제어에서 RUN/셔터 동작 확인, 안 되면 운영자 문의 |
| 알람이 계속 뜸 | 등급 필터로 오류만 보고, 부저 OFF/에러 리셋 후에도 재발하면 원인(설비/연결) 점검 |
