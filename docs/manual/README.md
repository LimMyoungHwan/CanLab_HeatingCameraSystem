# HeatingCameraSystem 매뉴얼

운영자·개발자용 통합 매뉴얼. 모든 길은 여기서 시작.

## 목차

| 번호 | 매뉴얼 | 무엇을 |
|---|---|---|
| 00 | [개요](./00-overview.md) | 시스템 구조, 컴포넌트, NATS 토픽 맵 |
| 01 | [설치](./01-installation.md) | NATS / Master / Agent 설치 및 검증 |
| 02 | [설정](./02-configuration.md) | `hardware.json` / `agent.json` 필드 레퍼런스, SimulationMode, 매핑 규칙, 셔터 프로토콜, LiteDB |
| 03 | [사용법](./03-usage.md) | Master UI, Recipe 워크플로, Agent 운영, 시뮬, History, 트러블슈팅 |

## 5분 Quick Start (시뮬레이션)

하드웨어 없이 전체 시스템을 한번 돌려본다.

```powershell
# 1. NATS 서버 (Docker)
docker compose -f docs/deployment/docker-compose.yml up -d

# 2. 빌드 + 자동 E2E (Agent 2대 + Driver + 4단계 Recipe 검증까지)
dotnet build
./docs/deployment/run-e2e-simulation.ps1

# 기대 결과:
# [E2E] *** PASS ***  → exit 0
# %TEMP%\HCS_E2E\Agent\ImageStorage_0,1\capture_*.jpg 4개 생성
```

성공하면 NATS / Master 로직 / Agent / 캡처 전체 흐름이 정상이라는 뜻. 이후 Master GUI 까지 시뮬로 보고 싶다면 [03-usage.md §4.2](./03-usage.md#42-master-gui-로-시뮬) 참고.

## 5분 Quick Start (실 하드웨어)

PLC + 카메라 1대 + NATS 가 준비된 환경.

```powershell
# 1. NATS 서버 기동
docker compose -f docs/deployment/docker-compose.yml up -d

# 2. Master 배포 (개발 PC 에서 publish 후 운영 PC 로 복사)
dotnet publish HeatingCameraSystem.Master -c Release -o publish\Master
# publish\Master\ 폴더를 운영 PC 로 복사 → HeatingCameraSystem.Master.exe 실행

# 3. Agent 배포 (Agent PC)
dotnet publish HeatingCameraSystem.Agent -c Release -o publish\Agent
# publish\Agent\ 복사 → HeatingCameraSystem.Agent.exe 실행

# 4. hardware.json (Master PC %LOCALAPPDATA%) 의 PLC IP / NATS URL 수정 → Master 재시작
# 5. agent.json (Agent exe 폴더) 의 AgentId / NatsUrl / CameraIndex 수정 → Agent 재시작
# 6. Master Dashboard 에서 Agent 녹색 점 확인 → Recipe 선택 → START
```

상세 단계와 검증 체크리스트는 [01-installation.md §7](./01-installation.md#7-설치-검증-체크리스트).

## 관련 자료

- 저장소 루트 [README.md](../../README.md) — 프로젝트 소개
- 샘플 설정 파일 [`docs/samples/`](../samples/)
- 시뮬레이션 전체 가이드 [`docs/deployment/simulation-mode.md`](../deployment/simulation-mode.md)
- 자동 E2E 러너 [`docs/deployment/run-e2e-simulation.ps1`](../deployment/run-e2e-simulation.ps1)
- PDCA 기능별 문서 [`docs/01-plan/`](../01-plan/), [`02-design/`](../02-design/), [`03-analysis/`](../03-analysis/), [`04-report/`](../04-report/)

## 문서 이력

| 버전 | 일자 | 비고 |
|---|---|---|
| 1.0 | 2026-06-20 | 최초 작성 (개요/설치/설정/사용법 4종) |
