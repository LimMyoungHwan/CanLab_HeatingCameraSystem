# HeatingCameraSystem — 프로젝트 개요

## 정체성
열화상 카메라 **모니터링 + 레시피 기반 촬영** 시스템 (데스크톱, C# WPF 예정).
- ⚠️ **캘리브레이션 폐기됨** — 기존 implementation_plan.md의 캘리브레이션 시퀀스(5.3)는 더 이상 사용 안 함.
- 레시피 기반: 모터가 블랙바디를 카메라 위치로 이동 → 환경(온습도) 타겟 도달 대기 → 블랙바디 타겟온도 도달 대기 → 해당 카메라 영상 취득 → 다음 스텝 반복.

## 현재 단계
**요구사항 정의 + UI 시안** 단계. 실제 코드(src/) 아직 없음. 구현 착수 전.

## 하드웨어/구조 (확정)
- 열화상 카메라 **64대**, 여러 Agent PC에 분산 연결 (USB-C: UVC 영상 + 가상 시리얼).
- **Master-Agent 분산 구조 유지** (여러 PC에 카메라 분산 확률 높음).
- A&D PLC (Modbus TCP) — Master만 통신. 챔버(온습도) + 서보(블랙바디 위치 이동) 제어.
- 챔버 1대. 블랙바디 2개 = **촬영용 1 + 카메라 웜업용 1**.
- 통신: Modbus TCP(PLC), gRPC(Master↔Agent), Serial(카메라).

## 산출물 (커밋됨, root-commit 19bafd9)
- `docs/ui_requirements.md` — UI 요구사항 정의서 (정본).
- `docs/implementation_plan.md` — 초기 계획서 (단 캘리브레이션 부분은 폐기, 분산구조/Modbus 등은 유효).
- `docs/mockups/` — Stitch 생성 4개 화면 시안 HTML.
- `docs/260522 회의록_V_01 .pdf` — 회의록 (UI엔 미반영).

## Git
- root-commit `19bafd9`. master 브랜치. user.name/email은 임시(Sisyphus/sisyphus@local)로 커밋함 — 추후 사용자 git config 설정 필요.
- 주의: `.bkit/runtime/*` 런타임 파일이 커밋에 포함됨 (필요시 .gitignore 추가 고려).

## 미정 (1개)
- 이미지 저장 포맷 (raw 열데이터 / 16bit TIFF / 온도맵) — 사용자가 나중에 선택.
