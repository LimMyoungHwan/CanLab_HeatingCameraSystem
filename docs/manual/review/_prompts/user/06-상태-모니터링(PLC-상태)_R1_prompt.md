You are an expert technical-documentation reviewer in a multi-AI review council.
You review ONE chapter of a Korean operations manual for "HeatingCameraSystem" — a thermal-camera chamber system (WPF Master PC + NATS message bus + camera Agent PCs + LS XGT FEnet PLC + serial shutter + recipe-driven automatic capture).

TARGET READER OF THIS CHAPTER: 일상 운전자 (Master 화면으로 매일 촬영·모니터링)
CHAPTER ID: 06-상태-모니터링(PLC-상태)   ROUND: 1

TASK — critically review the chapter markdown under "=== CHAPTER CONTENT ===" for:
1. 정확성 — procedures, terminology, field names correct & self-consistent
2. 완결성 — missing steps, missing warnings, unclear points, gaps
3. 명확성 — readability for the target reader
4. 구성/일관성 — structure, heading flow, wording consistency
5. 이미지 자리(📷 [그림 N] blocks) — each placement useful & clearly described?

OUTPUT — write your review in KOREAN, EXACTLY this structure:

# 06-상태-모니터링(PLC-상태) — {AI_NAME} 검토 (Round 1)

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

# 6. 상태 모니터링(PLC 상태)

PLC의 전체 상태를 읽기 전용으로 약 1초 주기로 보여주는 화면이다. 값을 바꾸지는 않는다.

> 📷 **[그림 15] PLC 상태 화면**
> - **캡처 대상:** PLC 상태 화면 전체(상단 요약 바 + 각 카드)
> - **화면/상태:** PLC 연결된 상태(값이 표시됨)
> - **표시 영역:** 창 전체

- 상단 요약: PLC 연결, 챔버 온도(현재/목표), 챔버 습도(현재/목표), 진행 상태(스텝/진행바), 서보 포인트(POINT·X/Y)
- 챔버 환경 / 흑체 기준 온도 / 서보·직교로봇 / 진행·기타 카드
- PLC 알람 카드: 활성 오류는 빨간색으로 표시
- 하단: 입력 P000~ · 출력 P020~ 표시등, 장비 상태 표시등

> 📷 **[그림 16] PLC 입출력·장비 램프**
> - **캡처 대상:** PLC 상태 화면 하단의 입력/출력/장비 상태 표시등 영역
> - **화면/상태:** 정상 표시 상태
> - **표시 영역:** 창 아래쪽
