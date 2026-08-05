• # 11-부록-—-런타임-파일 — kimi 검토 (Round 1)

  ## 종합 평가
  - 수정 필요도: 상
  - 한 줄 요약: 경로·파일명 요약표는 유용하지만, 실제 구현과 다른 항목과 누락된 파일이 있어 유지보수 관리자가 오판할 수 있다.

  ## 수정 필요 항목
  1. [표 6행 `manager-settings/state.json`] 문제: 코드상 `manager-settings.json`(설정)과 `manager-state.json`(상태)은 별개 파일임. 슬래시 표기는 하나의 파일이나 하위 폴더로 오인될 수 있음 -> 제안: 두 파일로 분리해 각각의 역할(설정/런타임 상태)을 명시하고, `<InstallRoot>\Manager\` 기본값임을 표기한다.
  2. [표 7행 `Agent 로그`] 문제: `C:\HeatingCameraSystem\logs\{AgentId}\`는 Manager가 조회하도록 기대하는 경로이지, 콘솔 Agent의 기본 저장 위치가 아님. 콘솔 Agent는 기본적으로 `<Agent exe>\logs\agent-YYYYMMDD.log`에 Serilog 일별 롤링(최근 7개 파일 보존, 확장자 `.log`)으로 기록함. 또한 AgentUI 로그는 별도 경로임 -> 제안: "Manager 조회 대상 로그 경로(기본 InstallRoot 기준)"와 "콘솔 Agent 기본 로그 위치"를 구분하거나, 현재 표기에 "(Manager 조회 기준)"을 추가하고 AgentUI 로그 항목을 추가한다.
  3. [표 4행 `캡처 이미지`] 문제: "Agent StoragePath / Master ImageCache"는 경로 값과 설정 키가 혼재되어 있고 기본 경로가 제시되지 않음 -> 제안: Agent 원본(`<Agent exe>\ImageStorage\` 기본, `agent.json`의 `StoragePath`로 변경 가능)과 Master 캐시(`%LOCALAPPDATA%\HeatingCameraSystem\ImageCache\`)를 분리해 표기한다.
  4. [표 전체] 문제: 파일별 자동 생성 여부, 편집 후 재시작 필요 여부, 삭제 시 영향이 누락됨 -> 제안: 비고 열 또는 하위 단락에 `hardware.json`(최초 자동 생성·재시작 필요), `data.db`(자동 생성·삭제 시 이력 유실), `agent.json`(일반 실행 시 자동 생성·재시작 필요), `recipe*.json`(수동 저장 시 생성) 등을 명시한다.
  5. [표 3행 `recipe\*.json`] 문제: "레시피"만으로는 `data.db`와의 역할 분리가 불분명함 -> 제안: "현재 레시피 저장소(Master). `data.db`는 레이아웃·이력·카메라 설정 등을 저장"한다고 보강한다.

  ## 누락/추가 제안
  - `agentui.json` 누락: AgentUI 설정 파일 `%LOCALAPPDATA%\HeatingCameraSystem\AgentUI\agentui.json`을 추가한다.
  - 백업·복구 절차: `hardware.json`, `data.db`, `recipe\*.json`, `manager-state.json` 등 주요 파일의 백업 대상과 복구 순서를 간단히 추가한다.
  - 용량 관리: `data.db`, 캡처 이미지, 로그의 증가 특성과 디스크 여유 공간 확인 필요성을 언급한다.
  - 경로 접근법: `%LOCALAPPDATA%`를 파일 탐색기 주소 표시줄에 입력하는 방법 등 구체적 예시를 추가한다.

  ## 이미지 자리 검토
  - 현재 `[그림 N]` 자리 없음. 경로 중심 표만으로는 시각적 이해가 어려우므로, [그림 11-1] `%LOCALAPPDATA%\HeatingCameraSystem\` 폴더 구조(하위 `recipe`, `ImageCache` 등)와 Agent/Master/Manager 파일 흐름을 보여주는 안내도 추가를 권장. 없음.
