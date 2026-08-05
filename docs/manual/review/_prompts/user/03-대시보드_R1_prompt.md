You are an expert technical-documentation reviewer in a multi-AI review council.
You review ONE chapter of a Korean operations manual for "HeatingCameraSystem" — a thermal-camera chamber system (WPF Master PC + NATS message bus + camera Agent PCs + LS XGT FEnet PLC + serial shutter + recipe-driven automatic capture).

TARGET READER OF THIS CHAPTER: 일상 운전자 (Master 화면으로 매일 촬영·모니터링)
CHAPTER ID: 03-대시보드   ROUND: 1

TASK — critically review the chapter markdown under "=== CHAPTER CONTENT ===" for:
1. 정확성 — procedures, terminology, field names correct & self-consistent
2. 완결성 — missing steps, missing warnings, unclear points, gaps
3. 명확성 — readability for the target reader
4. 구성/일관성 — structure, heading flow, wording consistency
5. 이미지 자리(📷 [그림 N] blocks) — each placement useful & clearly described?

OUTPUT — write your review in KOREAN, EXACTLY this structure:

# 03-대시보드 — {AI_NAME} 검토 (Round 1)

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

# 3. 대시보드

대시보드는 운전 중 가장 많이 보는 화면이다. 좌측 텔레메트리 카드, 가운데 카메라 영상, 우측 카메라/레시피 패널로 구성된다.

> 📷 **[그림 4] 대시보드 전체**
> - **캡처 대상:** 대시보드 화면 전체
> - **화면/상태:** 레시피 실행 중이면 진행바가 보여 더 좋음
> - **표시 영역:** 창 전체


## 3.1 상단 상태 요약(HUD)

- PLC 연결 표시등 + 메시지
- AGENTS — 현재 온라인 카메라(Agent) 수
- POINT — 현재 서보 포인트 번호
- ALARMS — 현재 알람 건수
- 비상정지가 걸리면 상단에 'EMERGENCY STOP ACTIVE · 비상정지 동작 중' 배너 표시


## 3.2 텔레메트리 카드(좌측)

| 카드 | 표시 내용 |
| --- | --- |
| CHAMBER(챔버) | 현재/목표 온도(℃), 현재/목표 습도(%RH), 온·습도 추세 그래프 |
| BLACKBODY(흑체) | 흑체1/2 현재값(PV)·목표값(SV) |
| MOTION(서보) | X/Y 위치(mm), X/Y BUSY·원점 표시등, X/Y 에러코드 |
| PROGRAM(진행) | 현재/전체 스텝, 팬 속도(Hz), 가스 유속 |
| EQUIPMENT(장비) | 히터·냉동기1/2/상온·바이패스·도어램프·페어글라스·MCF·블로워1/2 표시등 |


## 3.3 카메라 영상(가운데)

- 보기 모드 버튼 Mode 1(Auto 8)/Mode 2(8)/Mode 3(4)/Mode 4(2)/Mode 5(1) — 한 화면에 표시할 카메라 수
- 컬러맵 선택(컬러/그레이스케일)
- Mode 2~5에서는 우측 카메라 목록을 영상 칸으로 끌어다 놓아 배치, 우클릭으로 비움
- 각 칸에 카메라 번호·실시간 영상·측정 온도(℃) 표시, 신호 없으면 'No Signal'

> 📷 **[그림 5] 카메라 영상 영역**
> - **캡처 대상:** 가운데 카메라 피드 그리드와 상단 Mode 1~5 버튼, 컬러맵 선택
> - **화면/상태:** 카메라가 1대 이상 영상을 표시 중인 상태
> - **표시 영역:** 창 가운데


## 3.4 레시피 실행 패널(우측)

1. RECIPE 드롭다운에서 실행할 레시피 선택(목록이 비면 새로고침 버튼)
2. START 버튼으로 실행 시작
3. 진행바(0~100%)와 단계 문구로 진행 확인
4. 중간 중단은 STOP 버튼(현재 단계 정리 후 안전 정지)

> 📷 **[그림 6] 레시피 실행 패널**
> - **캡처 대상:** 우측 RECIPE 선택 콤보 + START/STOP + 진행바 + 단계 문구
> - **화면/상태:** 레시피 실행 중(진행바가 차 있는 상태) 권장
> - **표시 영역:** 창 오른쪽 하단
