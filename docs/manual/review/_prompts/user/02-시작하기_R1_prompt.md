You are an expert technical-documentation reviewer in a multi-AI review council.
You review ONE chapter of a Korean operations manual for "HeatingCameraSystem" — a thermal-camera chamber system (WPF Master PC + NATS message bus + camera Agent PCs + LS XGT FEnet PLC + serial shutter + recipe-driven automatic capture).

TARGET READER OF THIS CHAPTER: 일상 운전자 (Master 화면으로 매일 촬영·모니터링)
CHAPTER ID: 02-시작하기   ROUND: 1

TASK — critically review the chapter markdown under "=== CHAPTER CONTENT ===" for:
1. 정확성 — procedures, terminology, field names correct & self-consistent
2. 완결성 — missing steps, missing warnings, unclear points, gaps
3. 명확성 — readability for the target reader
4. 구성/일관성 — structure, heading flow, wording consistency
5. 이미지 자리(📷 [그림 N] blocks) — each placement useful & clearly described?

OUTPUT — write your review in KOREAN, EXACTLY this structure:

# 02-시작하기 — {AI_NAME} 검토 (Round 1)

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

# 2. 시작하기


## 2.1 프로그램 실행과 첫 화면

운영 PC 바탕화면(또는 시작 폴더)의 실행 아이콘으로 Master를 실행한다. 실행하면 아래와 같은 메인 창이 열리고, 자동으로 NATS·PLC 연결을 시도한다(연결에 실패해도 화면은 정상적으로 뜨며 알람으로 알려준다).

> 📷 **[그림 2] Master 메인 창(최초 실행)**
> - **캡처 대상:** 프로그램 실행 직후의 전체 창
> - **화면/상태:** 대시보드가 기본으로 열린 상태. 좌측 메뉴 전체가 보이도록 캡처
> - **표시 영역:** 창 전체


## 2.2 화면 구성 — 좌측 메뉴

왼쪽 세로 메뉴에서 화면을 전환한다. 맨 위 언어 선택(한국어/English) 아래로 7개의 화면이 순서대로 배치된다.

| 순서 | 메뉴 | 무엇을 하는 화면 |
| --- | --- | --- |
| 1 | 대시보드 | 챔버·흑체·서보·장비 상태와 카메라 영상, 레시피 실행을 한 화면에서 |
| 2 | 레시피 편집기 | 촬영 순서표(레시피) 작성·수정, 스텝별 좌표/온도/카메라 지정 |
| 3 | 이력 조회 | 촬영 이력·챔버 이력·알람 이력 조회, CSV 내보내기 |
| 4 | Agent 설정 | 카메라 PC 원격 설정(탭1) / 카메라 승인·관리(탭2) |
| 5 | PLC 상태 | PLC의 온습도·서보·입출력·알람을 읽기 전용으로 모니터링 |
| 6 | PLC 설정 | PLC 연결, 흑체 연결, 관리자 한계값, 서보 포인트 좌표 설정 |
| 7 | 수동 조작 | 챔버·서보·흑체·장비·카메라를 직접 수동으로 조작 |

메뉴 아래쪽에는 화면과 무관하게 항상 표시되는 공용 버튼과 알람 영역이 있다.

- PLC 부저 OFF / PLC 에러 리셋 / PLC 원점 복귀 — 자주 쓰는 PLC 즉시 명령
- PLC 연결 표시등 — 초록(연결)/빨강(끊김)
- 알람 목록 — 실시간 알람이 최신순으로 쌓임. 등급 필터(전체/정보/경고/오류), 각 알람 옆 ✕로 개별 삭제
- 비상정지 — 하단의 큰 빨간 버튼

> 📷 **[그림 3] 좌측 메뉴 패널**
> - **캡처 대상:** 좌측 세로 메뉴 전체(언어 선택 + 7개 메뉴 + 하단 공용 버튼 + 알람 목록 + 비상정지)
> - **화면/상태:** 알람이 1건 이상 표시된 상태면 더 좋음
> - **표시 영역:** 창 왼쪽 세로 영역


## 2.3 정상 상태 확인

- 좌측 하단(또는 상단) PLC 표시등이 초록이면 PLC 연결 정상
- 대시보드 우측 카메라/Agent 패널에 카메라가 초록 점으로 표시되면 해당 카메라 준비 완료(5초 이내 하트비트)
- 회색 점은 응답 없음(15초 이상) — 해당 Agent PC/카메라 확인 필요
