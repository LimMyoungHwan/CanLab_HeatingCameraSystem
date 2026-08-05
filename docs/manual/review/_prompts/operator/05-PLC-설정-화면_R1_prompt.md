You are an expert technical-documentation reviewer in a multi-AI review council.
You review ONE chapter of a Korean operations manual for "HeatingCameraSystem" — a thermal-camera chamber system (WPF Master PC + NATS message bus + camera Agent PCs + LS XGT FEnet PLC + serial shutter + recipe-driven automatic capture).

TARGET READER OF THIS CHAPTER: 설치·설정·유지보수 관리자
CHAPTER ID: 05-PLC-설정-화면   ROUND: 1

TASK — critically review the chapter markdown under "=== CHAPTER CONTENT ===" for:
1. 정확성 — procedures, terminology, field names correct & self-consistent
2. 완결성 — missing steps, missing warnings, unclear points, gaps
3. 명확성 — readability for the target reader
4. 구성/일관성 — structure, heading flow, wording consistency
5. 이미지 자리(📷 [그림 N] blocks) — each placement useful & clearly described?

OUTPUT — write your review in KOREAN, EXACTLY this structure:

# 05-PLC-설정-화면 — {AI_NAME} 검토 (Round 1)

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

# 5. PLC 설정 화면

관리자가 PLC·흑체 연결과 한계값, 서보 포인트 좌표를 조정하는 화면이다.

> 📷 **[그림 7] PLC 설정 화면 전체**
> - **캡처 대상:** PLC 설정 화면 전체(연결/흑체/관리자/포인트 카드)
> - **화면/상태:** 값이 로드된 상태
> - **표시 영역:** 창 전체

- PLC 연결: IP 주소·포트·국번(Station) → 저장
- 흑체 연결 설정: 직접 제어 사용 체크, 흑체1/2 연결 방식(Serial: COM/Baud, IP: 주소/포트) → 저장
- 관리자 설정: 과열 상한·상온/2차 냉동기 경계·냉동기 딜레이·바이패스 경계·MFC 최소/최대 출력·페어글라스 경계 → 불러오기/저장
- 서보 포인트 좌표(20): 포인트별 X/Y → 불러오기/저장

> 📷 **[그림 8] PLC 설정 — 관리자 설정 카드**
> - **캡처 대상:** 관리자 설정 카드(과열 상한 등 한계값 입력)
> - **화면/상태:** 값이 입력된 상태
> - **표시 영역:** 창 가운데

> 📷 **[그림 9] PLC 설정 — 서보 포인트 좌표**
> - **캡처 대상:** 서보 포인트 좌표(20) 카드
> - **화면/상태:** 포인트 좌표가 채워진 상태
> - **표시 영역:** 창 오른쪽
