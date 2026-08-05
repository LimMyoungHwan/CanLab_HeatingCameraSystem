You are an expert technical-documentation reviewer in a multi-AI review council.
You review ONE chapter of a Korean operations manual for "HeatingCameraSystem" — a thermal-camera chamber system (WPF Master PC + NATS message bus + camera Agent PCs + LS XGT FEnet PLC + serial shutter + recipe-driven automatic capture).

TARGET READER OF THIS CHAPTER: 일상 운전자 (Master 화면으로 매일 촬영·모니터링)
CHAPTER ID: 08-알람-확인·해제   ROUND: 1

TASK — critically review the chapter markdown under "=== CHAPTER CONTENT ===" for:
1. 정확성 — procedures, terminology, field names correct & self-consistent
2. 완결성 — missing steps, missing warnings, unclear points, gaps
3. 명확성 — readability for the target reader
4. 구성/일관성 — structure, heading flow, wording consistency
5. 이미지 자리(📷 [그림 N] blocks) — each placement useful & clearly described?

OUTPUT — write your review in KOREAN, EXACTLY this structure:

# 08-알람-확인·해제 — {AI_NAME} 검토 (Round 1)

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

# 8. 알람 확인·해제

- 좌측 알람 목록에 실시간 알람이 최신순으로 쌓임(정보/경고/오류 등급)
- 등급 필터(전체/정보/경고/오류)로 걸러 보기
- 각 알람 옆 ✕로 개별 삭제
- PLC 부저 OFF — 경보 부저 끄기
- PLC 에러 리셋 — PLC 에러 해제
- 지난 알람은 이력 조회의 알람 이력 탭에서 기간·등급으로 다시 조회

> 📷 **[그림 21] 알람 패널·공용 버튼**
> - **캡처 대상:** 좌측 알람 목록과 PLC 부저 OFF/에러 리셋/원점 복귀 버튼
> - **화면/상태:** 알람이 표시된 상태
> - **표시 영역:** 창 왼쪽
