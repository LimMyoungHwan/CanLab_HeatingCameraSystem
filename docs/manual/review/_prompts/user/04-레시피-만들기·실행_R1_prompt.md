You are an expert technical-documentation reviewer in a multi-AI review council.
You review ONE chapter of a Korean operations manual for "HeatingCameraSystem" — a thermal-camera chamber system (WPF Master PC + NATS message bus + camera Agent PCs + LS XGT FEnet PLC + serial shutter + recipe-driven automatic capture).

TARGET READER OF THIS CHAPTER: 일상 운전자 (Master 화면으로 매일 촬영·모니터링)
CHAPTER ID: 04-레시피-만들기·실행   ROUND: 1

TASK — critically review the chapter markdown under "=== CHAPTER CONTENT ===" for:
1. 정확성 — procedures, terminology, field names correct & self-consistent
2. 완결성 — missing steps, missing warnings, unclear points, gaps
3. 명확성 — readability for the target reader
4. 구성/일관성 — structure, heading flow, wording consistency
5. 이미지 자리(📷 [그림 N] blocks) — each placement useful & clearly described?

OUTPUT — write your review in KOREAN, EXACTLY this structure:

# 04-레시피-만들기·실행 — {AI_NAME} 검토 (Round 1)

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

# 4. 레시피 만들기·실행

레시피는 '어느 좌표에서, 흑체를 몇 도로 맞추고, 어느 카메라로 찍을지'를 스텝 순서로 적어둔 촬영 순서표다. 레시피 편집기에서 만들고, 대시보드에서 실행한다.


## 4.1 레시피 편집기 화면

좌측 레시피 목록 / 가운데 레시피·스텝 설정 / 우측 라이브 프리뷰의 3분할 구성이다.

> 📷 **[그림 7] 레시피 편집기 전체**
> - **캡처 대상:** 레시피 편집기 화면 전체(3분할)
> - **화면/상태:** 레시피 1개를 선택해 스텝이 보이는 상태
> - **표시 영역:** 창 전체


## 4.2 새 레시피 만들기

1. 좌측에서 '새 레시피'로 레시피 생성 후 이름 입력
2. 가운데 상단에 챔버 목표 온도(TARGET CHAMBER TEMP), 온도 램프 도달시간(분), 챔버 목표 습도(TARGET CHAMBER HUMIDITY) 입력
3. '+ ADD NEW RECIPE STEP'으로 스텝 추가
4. 각 스텝에 흑체 기준온도(BLACKBODY REF ℃), X/Y 좌표(mm), 챔버 ℃/%RH, 카메라를 지정
5. 스텝은 드래그로 순서 변경, 각 스텝 우측 X로 삭제
6. 하단 'SAVE RECIPE'로 저장(레시피는 파일로 저장됨)

> **[참고]** 온도 램프 도달시간(분)을 0보다 크게 주면, 현재 챔버 온도에서 목표 온도까지 지정한 시간에 걸쳐 단계적으로(선형) 올린다. 히터가 급격히 출력되는 것을 막는 기능이다. 0이면 목표 온도로 즉시 설정한다.

> 📷 **[그림 8] 레시피 스텝 편집**
> - **캡처 대상:** 가운데 스텝 목록과 스텝 1개의 입력 항목(흑체 기준온도/X·Y/챔버 온습도/카메라)
> - **화면/상태:** 스텝 2개 이상 입력된 상태
> - **표시 영역:** 창 가운데


## 4.3 라이브 프리뷰로 좌표 잡기

우측 'LIVE PREVIEW & MOTION' 패널에서 실제 카메라 영상을 보며 서보를 움직여 좌표를 확인하고 스텝에 반영할 수 있다.

- 온라인 Agent 카메라 선택 → 실시간 영상 표시
- XY TARGET: X/Y 입력 후 GO TO XY로 이동, USE CURRENT X/Y로 현재 위치 값 가져오기, HOME으로 원점 복귀
- SHUTTER & CAMERA: 셔터 열기/닫기, 카메라 시작/정지
- 조그 패드(Y+/X-/X+/Y-)로 미세 이동

> 📷 **[그림 9] 라이브 프리뷰 & 모션**
> - **캡처 대상:** 우측 LIVE PREVIEW & MOTION 패널(영상 + XY TARGET + 셔터/카메라 + 조그 패드)
> - **화면/상태:** 카메라 영상이 나오는 상태
> - **표시 영역:** 창 오른쪽


## 4.4 백업(IMPORT / EXPORT)

- EXPORT — 현재 레시피를 .json 파일로 저장(다른 PC로 옮기거나 백업)
- IMPORT — .json 레시피를 불러와 추가


## 4.5 레시피 실행과 진행 단계

대시보드에서 레시피를 선택하고 START를 누르면 다음 순서로 자동 진행된다.

| 단계 | 내용 |
| --- | --- |
| 챔버 안정화 | 챔버 가동 + 목표 습도 설정 + (도달시간 지정 시) 온도 램프 → 목표 온도 도달까지 대기 |
| 서보 이동 | 스텝의 X/Y 좌표로 서보 이동 후 정지(BUSY 해제) 대기 |
| BB 안정화 | 스텝의 흑체 기준온도로 맞춘 뒤 허용오차 안에 들어올 때까지 대기 |
| 캡처 | 대상 카메라에 촬영 명령 → 결과 이미지 수신(제한시간 내) |
| 완료 | 모든 스텝 종료 후 챔버 정지, '완료' 표시 |

> **[참고]** 캡처가 제한시간(기본 30초, 설정 가능) 안에 오지 않으면 '경고' 알람, 카메라가 실패를 알리면 '오류' 알람이 뜬다. 알람 목록과 이력 조회의 알람 이력에서 확인할 수 있다.
