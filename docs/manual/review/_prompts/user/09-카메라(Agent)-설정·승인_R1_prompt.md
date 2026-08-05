You are an expert technical-documentation reviewer in a multi-AI review council.
You review ONE chapter of a Korean operations manual for "HeatingCameraSystem" — a thermal-camera chamber system (WPF Master PC + NATS message bus + camera Agent PCs + LS XGT FEnet PLC + serial shutter + recipe-driven automatic capture).

TARGET READER OF THIS CHAPTER: 일상 운전자 (Master 화면으로 매일 촬영·모니터링)
CHAPTER ID: 09-카메라(Agent)-설정·승인   ROUND: 1

TASK — critically review the chapter markdown under "=== CHAPTER CONTENT ===" for:
1. 정확성 — procedures, terminology, field names correct & self-consistent
2. 완결성 — missing steps, missing warnings, unclear points, gaps
3. 명확성 — readability for the target reader
4. 구성/일관성 — structure, heading flow, wording consistency
5. 이미지 자리(📷 [그림 N] blocks) — each placement useful & clearly described?

OUTPUT — write your review in KOREAN, EXACTLY this structure:

# 09-카메라(Agent)-설정·승인 — {AI_NAME} 검토 (Round 1)

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

# 9. 카메라(Agent) 설정·승인

'Agent 설정' 화면은 두 개의 탭으로 구성된다. 카메라 PC(Agent) 원격 설정과, 새 카메라 승인·관리다. 상세한 관리는 운영자 매뉴얼을 따르고, 여기서는 운전자가 마주치는 기본만 다룬다.

- 탭1 '에이전트 원격설정': 온라인 Agent 선택→조회 후 NATS/저장경로/하트비트/캡처 포맷·장수·카메라 목록을 원격으로 설정·전송
- 탭2 '디바이스 관리': 발견된 카메라 목록에서 미승인 카메라를 선택해 승인/거부/이름 저장, 시리얼 설정 전송, 로그 가져오기

> 📷 **[그림 22] Agent 설정 — 에이전트 원격설정**
> - **캡처 대상:** Agent 설정 탭1(온라인 Agent 선택 + 설정 폼 + 카메라 목록)
> - **화면/상태:** Agent 1대를 조회해 값이 채워진 상태
> - **표시 영역:** 창 가운데

> 📷 **[그림 23] Agent 설정 — 디바이스 관리**
> - **캡처 대상:** Agent 설정 탭2(발견 카메라 목록 + 승인/거부/이름저장/시리얼 설정)
> - **화면/상태:** 카메라 1대 이상 표시된 상태
> - **표시 영역:** 창 가운데

> **[참고]** 새 카메라를 붙였는데 촬영이 안 되면, 이 '디바이스 관리' 탭에서 해당 카메라가 승인(Approved)되어 있는지 먼저 확인한다. 자동 발견·승인은 운영자 매뉴얼 7장 참조.
