You are an expert technical-documentation reviewer in a multi-AI review council.
You review ONE chapter of a Korean operations manual for "HeatingCameraSystem" — a thermal-camera chamber system (WPF Master PC + NATS message bus + camera Agent PCs + LS XGT FEnet PLC + serial shutter + recipe-driven automatic capture).

TARGET READER OF THIS CHAPTER: 설치·설정·유지보수 관리자
CHAPTER ID: 11-부록-—-런타임-파일   ROUND: 1

TASK — critically review the chapter markdown under "=== CHAPTER CONTENT ===" for:
1. 정확성 — procedures, terminology, field names correct & self-consistent
2. 완결성 — missing steps, missing warnings, unclear points, gaps
3. 명확성 — readability for the target reader
4. 구성/일관성 — structure, heading flow, wording consistency
5. 이미지 자리(📷 [그림 N] blocks) — each placement useful & clearly described?

OUTPUT — write your review in KOREAN, EXACTLY this structure:

# 11-부록-—-런타임-파일 — {AI_NAME} 검토 (Round 1)

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

# 11. 부록 — 런타임 파일

| 파일 | 위치 | 비고 |
| --- | --- | --- |
| hardware.json | %LOCALAPPDATA%\HeatingCameraSystem\ | Master 설정 |
| data.db | %LOCALAPPDATA%\HeatingCameraSystem\ | LiteDB 이력 |
| recipe\*.json | %LOCALAPPDATA%\HeatingCameraSystem\recipe\ | 레시피 |
| agent.json | Agent exe 폴더 | Agent 설정 |
| 캡처 이미지 | Agent StoragePath / Master ImageCache | Agent 로컬 + Master 캐시 |
| manager-settings/state.json | C:\HeatingCameraSystem\Manager\ | Manager 설정·상태 |
| Agent 로그 | C:\HeatingCameraSystem\logs\{AgentId}\ | NDJSON, 7일 보관 |
