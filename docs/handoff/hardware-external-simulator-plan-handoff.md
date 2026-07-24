# Handoff — 하드웨어 외부 시뮬레이터 계획 완료 (2026-07-24)

> 다음 세션이 이 문서만 읽고 이어서 진행 가능. 이전: `docs/handoff/session2-live-color-nuc-nats-handoff.md`.

## 이번 세션 한 일

### 1. 하드웨어 외부 시뮬레이터 계획 수립
- 사용자 요청: 하드웨어 없이 테스트 가능한 환경 — UI 미완성 기능 보완 + 카메라/챔버/흑체 시뮬레이터
- 결정: **별도 콘솔 프로세스** `HeatingCameraSystem.Simulator` (in-process SimulationMode 확장 아님)
- 5개 병렬 탐색: UI 갭맵, 시뮬레이션 표면, 테스트 인프라, 런타임 설정, 리스크 검토
- Metis 사전분석: 흑체 real-mode 배선 누락 확인, 셔터 E2E 제외, 문서 부실 — 모두 계획에 반영
- VagabondK `FEnetSimulationService` + `TcpChannelProvider` 공식 샘플로 연결 방식 검증 완료

### 2. 계획 문서 완성
- `.omo/drafts/hardware-external-simulator.md` — 승인된 드래프트 (범위/결정/토폴로지)
- `.omo/plans/hardware-external-simulator.md` — 전체 실행 계획
  - 13개 작업 (3개 웨이브) + 4개 최종 검증
  - 의존성 매트릭스, 허용 기준, QA 시나리오, 커밋 전략, 성공 기준
  - Must have 14개 / Must NOT have 10개 명시

## 검증
- 계획만 수립, 구현 없음. 빌드/테스트 영향 없음.
- 기존 작업트리 더티 파일 (`.bkit/`, `.omo/run-continuation/`, `.council/`, `TestResults/`, `docs/EnetClient*`, `docs/PC통신라이브러리예제*`) 건드리지 않음.

## 다음 세션 해야 할 일

### 1순위: 시뮬레이터 구현 시작
- `.omo/plans/hardware-external-simulator.md` 읽고 Wave 1 (T1-T5) 부터 실행
- `/start-work` 명령으로 시작 (또는 고정밀 이중 검토 후 시작)
- Wave 1: 프로젝트/패키지 계약, 설정/상태, 메모리맵, 열 발생, Dashboard 데이터 흐름

### Wave 구성
| Wave | 작업 | 내용 |
|------|------|------|
| 1 (병렬 5) | T1-T5 | 프로젝트/계약/설정/메모리/열발생/Dash 데이터 |
| 2 (병렬 5) | T6-T10 | FEnet 엔드포인트/NATS 에이전트/호스트/흑체배선/Dash XAML |
| 3 (병렬 3) | T11-T13 | E2E 증명/실행스크립트/문서정리 |
| 최종 (병렬 4) | F1-F4 | 계획준수/코드품질/UI QA/범위감사 |

### 중요 결정사항
- 시뮬레이터는 `net8.0` 콘솔, 솔루션에 포함
- PLC = VagabondK `FEnetSimulationService` (커스텀 파서 금지)
- NATS = 기존 메시지/주제만 사용 (시뮬레이터 전용 주제 금지)
- Master는 `SimulationMode=false`로 테스트 (기존 Fake 경로 유지)
- COM/셔터/가상 DirectShow 카메라 제외
- 물리 = 결정론적 가속 (랜덤 없음)
- T9: `AppServices`에서 `FakeBlackBodyController` → `PlcBlackBodyAdapter` 조건부 라우팅 필수

## 참고 파일

| 파일 | 역할 |
|------|------|
| `.omo/plans/hardware-external-simulator.md` | 전체 실행 계획 (13 todo + 4 검증) |
| `.omo/drafts/hardware-external-simulator.md` | 승인된 드래프트 (범위/결정/토폴로지) |
| `HeatingCameraSystem.Master/Services/AppServices.cs:68-93` | T9 수정 대상 (흑체 라우팅) |
| `HeatingCameraSystem.Protocols/PlcXgtClient.cs:32-215` | FEnet 클라이언트 (시뮬레이터가 구현할 계약) |
| `HeatingCameraSystem.Protocols/PlcBlackBodyAdapter.cs:8-39` | 흑체 어댑터 (T9, T11 E2E 대상) |
| `HeatingCameraSystem.Master/ViewModels/DashboardViewModel.cs:136-192` | 라이브 프레임 구독 (T5, T10) |
| `HeatingCameraSystem.Master/Views/DashboardView.xaml:186-239` | 플레이스홀더 XAML (T10 대체) |
| `docs/deployment/simulation-mode.md` | 문서 정리 대상 (T13) |