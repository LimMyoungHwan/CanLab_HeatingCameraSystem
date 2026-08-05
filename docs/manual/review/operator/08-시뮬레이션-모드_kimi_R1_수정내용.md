• # 08-시뮬레이션-모드 — kimi 검토 (Round 1)

  ## 종합 평가
  - 수정 필요도: 상
  - 한 줄 요약: 시뮬레이션 개념표는 있으나, 모드 전환 절차, 실행 방법, 주의사항, 결과 해석 등 운영자가 실제로 활용할 수 있는 구체적 내용이 대부분 누락됨.

  ## 수정 필요 항목
  1. [표/전체] 문제: hardware.json / agent.json의 설정 항목명(SimulationMode)이 생략되어 있음. "hardware.json true", "agent.json false"처럼 값만 나열하면 어떤 필드를 변경해야 하는지 불분명. -> 제안: "hardware.json SimulationMode=true", "agent.json SimulationMode=false"처럼 필드명을 명시.
  2. [하이브리드 행] 문제: "agent.json false(웹캠)" 표현이 모호함. agent.json SimulationMode=false가 실제 카메라를 의미하는지, 웹캠이 별도 설정인지 알 수 없음. -> 제안: "agent.json SimulationMode=false (실제 카메라 또는 웹캠 사용)"으로 수정하고, 웹캠 구성이 별도 설정임을 명시.
  3. [스크립트 목록] 문제: 스크립트 경로만 나열하고 실행 방법, 전제조건, 예상 출력, 실패 시 조치가 없음. -> 제안: 각 스크립트에 대해 실행 전 준비사항, 기본 실행 명령, 정상 출력 예시를 1~2문장씩 추가.
  4. [섹션 구성] 문제: 시뮬레이션 모드로 전환하는 절차가 없음. -> 제안: "1. 현재 모드 확인 → 2. 설정 파일 수정 → 3. 애플리케이션 재시작 → 4. 검증" 순서의 절차 섹션 추가.

  ## 누락/추가 제안
  - 운영 환경과 시뮬레이션 모드 전환 시 주의사항(예: 실제 PLC/셔터/카메라 동작 여부, 생산 데이터에 미치는 영향) 추가.
  - hardware.json / agent.json의 실제 파일 위치(%LOCALAPPDATA%\HeatingCameraSystem\ 및 Agent exe 폴더) 안내.
  - SimulationMode 변경 후 애플리케이션 재시작이 필요하다는 명시.
  - 외부 Simulator 프로세스 실행 전 NATS/PLC IP·포트 설정 등 전제조건 추가.
  - [그림 11] 외에 설정 파일 편집 화면, 외부 Simulator 콘솔 등 추가 이미지 제안.

  ## 이미지 자리 검토
  - [그림 11] 부적절 — "run-e2e-simulation.ps1 통과 화면" 설명만 있고, 어떤 콘솔 출력이 PASS를 의미하는지, 실패 시 어떤 로그를 확인해야 하는지 설명이 부족. 콘솔 출력 예시를 텍스트로 함께 제시하고, 그림 배치 목적을 보강해야 함.

  ## (선택) 수정 제안 전문
  생략
