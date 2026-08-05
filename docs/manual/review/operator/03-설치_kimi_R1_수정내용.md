• # 03-설치 — Kimi 검토 (Round 1)

  ## 종합 평가
  - 수정 필요도: 중
  - 한 줄 요약: 전반적인 흐름은 올바르나, 필수 선행조건·hardware.json 편집 항목·Agent CLI 세부사항·카메라 인덱스 주의사항이 누락되어 실제 설치 시 재작업 가능성이 있다.

  ## 수정 필요 항목
  1. [3.3 NATS 서버] 문제: `docker compose`(공용), `Select-String`, `Test-NetConnection`(PowerShell 전용)이 한 블록에 섞여 있어 독자가 복사/실행 환경을 혼동할 수 있다. -> 제안: 명령어를 PowerShell 기준으로 통일하거나, 셸별로 구분하고 NATS 모니터링 포트(8222) 확인까지 추가한다.
  2. [3.4 Master 설치] 문제: "hardware.json을 실제 환경으로 수정"만 있고, 구체적으로 어떤 항목(PLC IP, NATS URL, SerialSettings, PLC 디바이스 주소 등)을 수정해야 하는지 안내가 없다. -> 제안: 수정해야 할 주요 필드를 bullet로 나열하거나, 3.4에 하위 단계를 추가해 hardware.json 편집 항목을 명시한다.
  3. [3.5 Agent 설치] 문제: CLI 인수 형식(`SimulationMode`는 bool인지 string인지, `AgentId` 형식 예시 등)과 agent.json 자동생성/CLI 오버라이드 동작이 불분명하다. -> 제안: 인수별 타입·예시(`Agent_0`, `nats://127.0.0.1:4222`)를 표로 정리하고, agent.json과 CLI 인수 중 어느 것이 우선하는지 명확히 한다.
  4. [3.6 카메라·시리얼 셔터 확인] 문제: "장치관리자의 이미징 장치 인덱스(보통 0/1) → agent.json CameraIndex"라는 표현은 오해의 소지가 있다. Device Manager 순서와 OpenCvSharp이 할당하는 비디오 인덱스가 항상 일치하지는 않는다. -> 제안: "장치관리자에서 카메라 존재를 확인한 뒤, Agent 실행 로그 또는 캡처 테스트로 실제 CameraIndex를 검증한다"로 수정한다.
  5. [3.7 설치 검증 체크리스트 #1] 문제: `dotnet --info 8.x`는 SDK 정보를 출력하며 런타임 여부를 한눈에 확인하기 어렵다. -> 제안: `dotnet --list-runtimes`에서 `Microsoft.NETCore.App 8.x` 확인으로 변경한다.
  6. [3.7 설치 검증 체크리스트 #7] 문제: "(실PLC) 온습도 — 대시보드 값 갱신"만 있어 정상 범위나 갱신 주기(1초 폴링)를 알 수 없다. -> 제안: "대시보드의 온습도 값이 1초 간격으로 갱신되는지 확인"으로 구체화한다.

  ## 누락/추가 제안
  - Docker Desktop 설치 여부와 WSL2/Windows Container 사전 요구사항 추가.
  - Master·Agent 실행에 필요한 Windows 방화벽 인바운드/아웃바운드 허용 절차(특히 Agent PC에서의 4222/tcp 아웃바운드) 추가.
  - OpenCvSharp4 런타임 종속성(Visual C++ 재배포 가능 패키지 등) 언급.
  - Agent를 서비스/자동 시작으로 등록할 경우의注意사항 또는 권장 여부.
  - 설치 실패 시 점검 방법(Troubleshooting) 소절 추가: NATS 연결 실패, PLC 연결 실패, 카메라 미인식 등.
  - 체크리스트에 "카메라 실제 캡처 이미지 확인" 및 "시리얼 셔터 개폐 명령 확인" 항목 추가.
  - 용어 일관성: "Master(WPF)·Agent(OpenCvSharp) 모두 Windows" 문장에서 Master/Agent 용어가 3.1의 "Master(WPF)·Agent(OpenCvSharp)"와 일치하지만, 이후 "Agent"라고만 쓰이는 경우가 있어 통일하면 좋음.

  ## 이미지 자리 검토
  - [그림 2] 적절 — NATS 기동 확인은 docker ps 출력 또는 콘솔 화면으로 직관적이다.
  - [그림 3] 적절 — `%LOCALAPPDATA%\HeatingCameraSystem\` 폴더 경로를 탐색기로 보여주는 것이 hardware.json/data.db 생성 위치를 명확히 전달한다.
  - [그림 4] 적절 — 장치관리자의 이미징 장치와 포트(COM & LPT)를 한 화면에 담는 것이 효과적이다. 단, CameraIndex가 Device Manager 인덱스와 항상 일치하지 않음을 본문에서 보완해야 한다.
  - [그림 5] 적절 — 'Connected to NATS' 로그는 Agent 기동 성공을 확인하는 직접적 증거다.
  - [그림 6] 적절 — 대시보드의 초록 점은 Master가 Agent를 인식했음을 직관적으로 보여준다. "창 오른쪽"이라는 위치 설명은 UI 레이아웃이 확정된 경우에만 유지한다.

  ## (선택) 수정 제안 전문
  (없음)
