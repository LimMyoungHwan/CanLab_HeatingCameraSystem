You are an expert technical-documentation reviewer in a multi-AI review council.
You review ONE chapter of a Korean operations manual for "HeatingCameraSystem" — a thermal-camera chamber system (WPF Master PC + NATS message bus + camera Agent PCs + LS XGT FEnet PLC + serial shutter + recipe-driven automatic capture).

TARGET READER OF THIS CHAPTER: 일상 운전자 (Master 화면으로 매일 촬영·모니터링)
CHAPTER ID: 07-이력-조회   ROUND: 1

TASK — critically review the chapter markdown under "=== CHAPTER CONTENT ===" for:
1. 정확성 — procedures, terminology, field names correct & self-consistent
2. 완결성 — missing steps, missing warnings, unclear points, gaps
3. 명확성 — readability for the target reader
4. 구성/일관성 — structure, heading flow, wording consistency
5. 이미지 자리(📷 [그림 N] blocks) — each placement useful & clearly described?

OUTPUT — write your review in KOREAN, EXACTLY this structure:

# 07-이력-조회 — {AI_NAME} 검토 (Round 1)

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

# 7. 이력 조회

촬영 결과와 챔버 데이터, 알람을 조회하고 CSV로 내보낼 수 있다. 상단 '보기' 탭으로 촬영 이력 / 챔버 이력 / 알람 이력을 전환한다.


## 7.1 촬영 이력

- 필터: 시작/종료 날짜, 카메라 필터 → '조회'
- 표: 타임스탬프, 카메라 ID, 온도(℃), 습도(%RH), 썸네일, '열기'
- 'CSV 내보내기'로 표를 파일로 저장

> 📷 **[그림 17] 이력 조회 — 촬영 이력**
> - **캡처 대상:** 촬영 이력 탭(필터 + 표 + 썸네일)
> - **화면/상태:** 조회 결과가 여러 건 표시된 상태
> - **표시 영역:** 창 가운데


## 7.2 챔버 이력 / 알람 이력

- 챔버 이력: 시간별 온도·습도·흑체1·흑체2 기록
- 알람 이력: 시간·등급·출처·메시지. 알람 이력 탭에서는 '알람 수준' 필터 추가 표시

> 📷 **[그림 18] 이력 조회 — 챔버 이력**
> - **캡처 대상:** 챔버 이력 탭(시간/온도/습도/BB1/BB2 표)
> - **화면/상태:** 조회 결과 표시 상태
> - **표시 영역:** 창 가운데

> 📷 **[그림 19] 이력 조회 — 알람 이력**
> - **캡처 대상:** 알람 이력 탭(시간/등급/출처/메시지 표 + 알람 수준 필터)
> - **화면/상태:** 알람이 조회된 상태
> - **표시 영역:** 창 가운데


## 7.3 이미지 상세 보기

촬영 이력에서 '열기'를 누르면 확대 이미지·열화상 스케일 바·카메라 소스·타임스탬프·온도·습도가 있는 상세 창이 뜬다.

> 📷 **[그림 20] 이미지 상세 보기**
> - **캡처 대상:** 촬영 이미지 상세 모달(확대 이미지 + 열화상 스케일 + 메타정보)
> - **화면/상태:** 이미지 1건을 연 상태
> - **표시 영역:** 화면 중앙 팝업
