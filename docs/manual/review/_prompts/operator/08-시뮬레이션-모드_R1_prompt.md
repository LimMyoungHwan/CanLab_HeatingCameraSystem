You are an expert technical-documentation reviewer in a multi-AI review council.
You review ONE chapter of a Korean operations manual for "HeatingCameraSystem" — a thermal-camera chamber system (WPF Master PC + NATS message bus + camera Agent PCs + LS XGT FEnet PLC + serial shutter + recipe-driven automatic capture).

TARGET READER OF THIS CHAPTER: 설치·설정·유지보수 관리자
CHAPTER ID: 08-시뮬레이션-모드   ROUND: 1

TASK — critically review the chapter markdown under "=== CHAPTER CONTENT ===" for:
1. 정확성 — procedures, terminology, field names correct & self-consistent
2. 완결성 — missing steps, missing warnings, unclear points, gaps
3. 명확성 — readability for the target reader
4. 구성/일관성 — structure, heading flow, wording consistency
5. 이미지 자리(📷 [그림 N] blocks) — each placement useful & clearly described?

OUTPUT — write your review in KOREAN, EXACTLY this structure:

# 08-시뮬레이션-모드 — {AI_NAME} 검토 (Round 1)

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

# 8. 시뮬레이션 모드

| 시나리오 | 설정 | 용도 |
| --- | --- | --- |
| 운영(실HW) | hardware.json SimulationMode=false | 실제 PLC/셔터/카메라 |
| 전체 시뮬 | hardware.json + agent.json 모두 true | 하드웨어 없이 전체 흐름 |
| 외부 Simulator | Master real + Simulator 프로세스 | XGT FEnet 프로토콜 경계 검증 |
| 하이브리드 | hardware.json true + agent.json false(웹캠) | PLC 없이 실카메라 캡처 확인 |

```
./docs/deployment/run-e2e-simulation.ps1            # 내부 Fake E2E
./docs/deployment/run-external-simulator-e2e.ps1   # 외부 Simulator E2E
./docs/deployment/run-manager-e2e.ps1              # Manager 승인 루프 E2E
```

> 📷 **[그림 11] 시뮬레이션 E2E 결과**
> - **캡처 대상:** E2E 러너 실행 후 '*** PASS ***' 출력 콘솔
> - **화면/상태:** run-e2e-simulation.ps1 통과 화면
