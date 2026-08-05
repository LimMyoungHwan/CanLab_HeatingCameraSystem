• # 04-설정-파일-레퍼런스 — kimi 검토 (Round 1)

  ## 종합 평가
  - 수정 필요도: 중
  - 한 줄 요약: 구조는 깔끔하고 대부분의 필드 기본값이 코드/샘플과 일치하나, AgentManager 파일 경로·자동생성 여부, agent.json 누락 필드, CameraAlias 라우팅 설명 등에서 사실 오류·왜곡이 있어 정정이 필요함.

  ## 수정 필요 항목
  1. [4.1 파일 위치] 문제: `manager-settings/state.json` 경로가 실제 코드와 다름. 실제는 `Manager\manager-settings.json`과 `Manager\manager-state.json` 두 파일이며, `manager-settings.json`은 파일이 없으면 메모리 기본값을 쓰고 **자동 생성하지 않음**(AgentManager/Program.cs:24-27). 또한 기본 루트 `C:\HeatingCameraSystem`은 인수로 오버라이드 가능함. -> 제안: 두 파일을 분리해 기술하고, `manager-settings.json`은 수동 배포/최초 편집, `manager-state.json`은 Manager 실행 중 자동 생성(승인 시)임을 명시한다. 예시: `manager-settings.json | {InstallRoot}\Manager\ | AgentManager | 아니오(기본값 사용)`, `manager-state.json | {InstallRoot}\Manager\ | AgentManager | 예`.

  2. [4.5 agent.json] 문제: `LogPath`, `CameraModel` 필드가 누락됨(AgentConfig.cs:17, 24). `CameraModel`은 `CameraModels\{CameraModel}.json`을 읽어 캡처 해상도를 적용하는 중요 필드임. 또한 `AgentId` 기본값 "머신명"은 CLI 인수 우선(`args[0]`)이며, 파일/인수 모두 없을 때만 `Environment.MachineName`이 적용됨(Agent/Program.cs:170-173). -> 제안: 두 필드를 추가하고, `AgentId` 기본값 설명에 "파일/인수 미지정 시 머신명" 조건을 덧붙인다.

  3. [4.4 BlackBody / RecipeEngine] 문제: `BlackBody.Units[]`를 "COM4/COM5"로만 요약하면 유닛 객체의 속성(`ConnectionType`, `PortName`, `BaudRate`, `IpAddress`, `Port` 등)을 알 수 없음. -> 제안: `Units[]`는 객체 배열이며, `ConnectionType`이 `Serial`이면 `PortName`(기본 COM4/COM5, 115200 8N1), `Ip`이면 `IpAddress`/`Port`(기본 192.168.1.100:5000)를 사용한다고 설명한다. 또한 누락된 `SimulatedRampCelsiusPerSecond`(기본 5.0), `InterMessageDelayMs`(50), `ReadTimeoutMs`(1500) 필드도 추가한다.

  4. [4.6 카메라 ↔ Agent ↔ NATS 매핑] 문제: "CameraAlias에 해당 카메라 Alias를 적어 DB 조회로 라우팅한다"는 표현이 부정확함. 실제는 `ICameraDeviceRepository.GetByAliasAsync` 조회이며, alias가 없거나 조회 실패 시 `CameraIndex`로 fallback(`Agent_{CameraIndex}`)함(RecipeEngine.cs:187-196). 또한 RecipeModels.cs 주석은 `CameraIndex` 범위를 1~64로 표기했으나 본문 표는 0/1을 예시로 듦. -> 제안: "Alias가 설정되면 Alias를 먼저 조회해 AgentId를 결정하며, 조회 실패/미설정 시 `Agent_{CameraIndex}`로 fallback한다"고 수정하고, CameraIndex 0/1 예시와 병행해 인덱스 범위에 대한 안내를 보강한다.

  5. [4.2 hardware.json] 문제: `DataRetentionDays` 설명 "촬영 이미지·이력 보관 일수"에서 `이력`이 DB의 어떤 이력까지 정리되는지 불명확함. -> 제안: `BackgroundDataCleanupService`가 `ImageCache` 디렉터리와 `HistoryRepo`의 촬영 이력을 함께 정리함을 명시하고, `0=정리 안 함`일 때 디스크/DB 무한 증가 주의를 덧붙인다.

  6. [4.3 hardware.json — PLC 주요 필드] 문제: "주요 필드"라는 취지에도 불구하고, 운영에 필요한 핵심 항목(비트/워드)이 다수 누락됨. 예: `BitChamberRun`, `BitHumidityControl`, `BitErrorReset`, `BitBuzzerOff`, `ServoX/Y HomeBit/ErrorCode`, `FanSpeed`, `StepCurrent`/`StepTotal`, 상태 램프 비트(`StatusHeater` 등), `ErrorBitBase`/`InputBitBase`/`OutputBitBase`, 관리자 설정 `AdminMfcMinOutput`/`AdminMfcMaxOutput`/`AdminPairGlassBoundary`/`AdminBypassBoundary`, `PulseHoldMs`, `CoordinateMoveDelayMs` 등. -> 제안: "전체 필드 목록은 `HardwareSettings.cs`/`PlcSettings.cs` 참조" 문구를 추가하거나, 운영자가 반드시 확인해야 할 항목(서보/조그/장비 원터치/관리자 한계값)을 보강한다.

  7. [4.1/전체] 문제: JSON 설정 파일 편집 후 **재시작 필요**에 대한 안내가 전혀 없음. -> 제안: 4.1 또는 4.2 시작 부분에 "설정 변경 후에는 해당 프로세스(Master/Agent/AgentManager)를 재시작해야 적용된다"는 경고를 추가한다.

  ## 누락/추가 제안
  - `4.x AgentManager 설정 파일` 섹션 추가: `manager-settings.json`의 `PCId`, `NatsUrl`, `SimulateEnumeration`, `SimulateAgentMode`, `LogRetentionDays`, `AgentExePath`, `AgentUiExePath` 등 운영/시뮬레이션에 필요한 필드를 설명함.
  - `4.x SerialSettings` 세부 항목 추가: 셔터 제어용 COM 설정(`PortName`, `BaudRate`, `DataBits`, `Parity`, `StopBits`)과, PLC 직렬이 아닌 **카메라 셔터 전용**임을 명확히 함.
  - `4.x CameraPairings` 섹션 추가: `CameraPairingEntry`의 `CameraUsbParentId`, `ShutterComPort` 등 자동 COM 매핑 구조 설명.
  - `hardware.json` 변경 시 백업 권고: PLC 디바이스 주소를 잘못 수정하면 챔버/서보 동작에 직접 영향을 미치므로, 편집 전 백업 문구 추가.
  - `data.db` 경고 보강: LiteDB 파일은 직접 편집하지 말 것, Master를 통해 조회/관리할 것.

  ## 이미지 자리 검토
  - 본 챕터에 `[그림 N]` 자리가 전혀 없음. 설정 파일 레퍼런스 특성상 테이블 위주로 충분하므로 현재로서는 이미지 추가가 필수는 아님. 다만 `4.6` 매핑 흐름을 한눈에 보여주는 다이어그램이 있으면 운영자 이해에 도움될 수 있음.

  ## (선택) 수정 제안 전문
  (해당 없음)
