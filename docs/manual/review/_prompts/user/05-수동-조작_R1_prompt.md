You are an expert technical-documentation reviewer in a multi-AI review council.
You review ONE chapter of a Korean operations manual for "HeatingCameraSystem" — a thermal-camera chamber system (WPF Master PC + NATS message bus + camera Agent PCs + LS XGT FEnet PLC + serial shutter + recipe-driven automatic capture).

TARGET READER OF THIS CHAPTER: 일상 운전자 (Master 화면으로 매일 촬영·모니터링)
CHAPTER ID: 05-수동-조작   ROUND: 1

TASK — critically review the chapter markdown under "=== CHAPTER CONTENT ===" for:
1. 정확성 — procedures, terminology, field names correct & self-consistent
2. 완결성 — missing steps, missing warnings, unclear points, gaps
3. 명확성 — readability for the target reader
4. 구성/일관성 — structure, heading flow, wording consistency
5. 이미지 자리(📷 [그림 N] blocks) — each placement useful & clearly described?

OUTPUT — write your review in KOREAN, EXACTLY this structure:

# 05-수동-조작 — {AI_NAME} 검토 (Round 1)

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

# 5. 수동 조작

레시피 없이 챔버·서보·흑체·장비·카메라를 직접 조작하는 화면이다. 초기 셋업, 점검, 좌표 확인 등에 사용한다.

> 📷 **[그림 10] 수동 조작 전체**
> - **캡처 대상:** 수동 조작 화면 전체
> - **화면/상태:** 모든 카드가 보이도록
> - **표시 영역:** 창 전체


## 5.1 챔버 / 온도 램프

- 챔버: 온도제어 시작/정지, 비상정지, 습도 제어 체크, 목표 온도·목표 습도 입력 후 각각 '적용'
- 온도 램프: 목표 온도·도달시간(분) 입력 후 '램프 시작'/'중지'

> 📷 **[그림 11] 챔버·온도 램프 카드**
> - **캡처 대상:** 수동 조작 좌측 상단의 챔버 카드와 온도 램프 카드
> - **화면/상태:** 정상 표시 상태
> - **표시 영역:** 창 왼쪽 위


## 5.2 모터/팬 · 장비 원터치

- 모터/팬: 서보 속도(1~100%) 적용, 팬 속도(Hz) 적용, 현재 팬 Hz 표시
- 장비 원터치: 1차/2차/상온 냉동기, 블로워1/2, 칠러, 도어락, 조명, 페어글라스 토글

> 📷 **[그림 12] 장비 원터치 카드**
> - **캡처 대상:** 장비 원터치 토글 스위치들이 있는 카드
> - **화면/상태:** 토글 상태가 보이도록
> - **표시 영역:** 창 가운데/우측


## 5.3 서보 조작

- X/Y 위치·BUSY 표시등·현재 포인트 표시
- X축/Y축 JOG(누르는 동안 이동), X 원점/Y 원점
- 절대 위치 이동(X/Y 입력 후 X 이동/Y 이동)
- 상대 위치 이동(스텝값 입력 후 +/-)
- 포인트 이동: 1~20번 포인트 버튼

> 📷 **[그림 13] 서보 조작 카드**
> - **캡처 대상:** 서보 조작 카드(JOG·원점·절대/상대 이동)와 포인트 이동(20버튼)
> - **화면/상태:** 위치값이 표시된 상태
> - **표시 영역:** 창 가운데


## 5.4 흑체 / 카메라 제어

- 흑체: 흑체1/2 목표(℃) 적용, 현재값 표시
- 카메라 제어: 카메라 선택·컬러맵 선택·실시간 영상, RUN/STOP/셔터 열기/셔터 닫기/캡처/NUC/설정 저장/정보 갱신

> 📷 **[그림 14] 카메라 제어 카드**
> - **캡처 대상:** 수동 조작 하단의 카메라 제어 카드(영상 + RUN/STOP/셔터/캡처/NUC 버튼)
> - **화면/상태:** 카메라 영상이 나오는 상태
> - **표시 영역:** 창 아래쪽

> **[주의]** 비상정지 버튼은 즉시 챔버 온도제어를 정지시킨다. 실제 설비에서 이상 상황 시 주저하지 말고 사용한다.
