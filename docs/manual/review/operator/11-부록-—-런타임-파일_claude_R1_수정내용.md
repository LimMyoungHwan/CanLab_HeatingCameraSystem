# 11-부록-—-런타임-파일 — claude 검토 (Round 1)

## 종합 평가
- 수정 필요도: 중
- 한 줄 요약: 표 자체는 간결하고 유용하나 "manager-settings/state.json" 행이 실제 두 개 파일(manager-settings.json, manager-state.json)을 뭉뚱그려 표기해 혼동을 주고, 로그 경로가 InstallRoot 기반 가변값임에도 고정 경로처럼 서술돼 있음.

## 수정 필요 항목
1. [표 6행 manager-settings/state.json] 문제: 코드상 `manager-settings.json`(초기 설정)과 `manager-state.json`(런타임 상태, ManagerStateStore가 저장)은 서로 다른 두 개의 파일임. "manager-settings/state.json" 표기는 하나의 파일 또는 하위 폴더처럼 오해될 수 있음 -> 제안: 행을 두 개로 분리 — `manager-settings.json | ...\Manager\ | Manager 초기 설정`, `manager-state.json | ...\Manager\ | Manager 런타임 상태(에이전트 목록 등)`.
2. [표 7행 Agent 로그] 문제: 로그 경로 `C:\HeatingCameraSystem\logs\{AgentId}\`는 AgentManager의 `InstallRoot` 기본값(코드 기본값 `C:\HeatingCameraSystem`)에서 파생되며, 설치 시 변경 가능한 값. 표에는 고정 경로처럼 제시돼 설치 경로를 바꾼 환경에서 오해 소지 -> 제안: 비고에 "(설치 루트 기준, 기본값)" 문구 추가.
3. [전체] 문제: 각 파일의 "삭제/이동 시 영향" 또는 "재생성 여부"에 대한 설명이 없어, 유지보수 관리자가 문제 발생 시 삭제해도 되는지 판단하기 어려움 -> 제안: 비고 열에 "삭제 시 재생성됨/삭제 시 데이터 유실" 등 한 줄씩 보강.

## 누락/추가 제안
- 각 파일의 형식(JSON/LiteDB 바이너리/NDJSON) 외에 대략적 크기·증가 속도(특히 data.db, 캡처 이미지, 로그) 정보가 있으면 디스크 용량 관리에 도움됨.
- 04장(설정 파일 레퍼런스)과 내용이 일부 겹치므로, 상호 참조 링크("자세한 필드 설명은 4장 참고") 추가 권장.

## 이미지 자리 검토
- 없음 — 본 챕터는 파일 경로 레퍼런스 표 하나로 구성되어 있어 그림이 필요치 않음. 적절.
