• # 07-Agent-Manager(자동-발견·승인) — kimi 검토 (Round 1)

  ## 종합 평가
  - 수정 필요도: 상
  - 한 줄 요약: S7 아키텍처 재설계(Manager가 AgentUI를 프로세스 기동하지 않고 NATS runtimeLoad/Unload로 감독) 이후 내용이 대폭 어긋나 있어 전면 수정이 필요함.

  ## 수정 필요 항목
  1. [개요] 문제: "Master에서 승인하면 Agent를 자동 기동·감독한다"는 Agent.exe를 기동하는 것처럼 읽힘. -> 제안: "Manager는 Master의 승인 명령을 받아 이미 실행 중인 AgentUI에 대해 NATS `runtimeLoad`/`runtimeUnload`를 전송해 카메라 런타임을 로드/언로드한다"로 수정.

  2. [7.2 설정 파일 / manager-settings.json] 문제: `SimulationMode` 필드는 현재 없음. -> 제안: `SimulateEnumeration`(WMI 대신 가상 카메라 열거)과 `SimulateAgentMode`(Agent가 가짜 캡처 사용) 두 필드로 설명하고, 각자 동작을 구분할 것.

  3. [7.2 설정 파일 / manager-settings.json] 문제: "`AgentExePath` 경로에 Agent 빌드 필요"는 오해. -> 제안: "실제 카메라 구동 주체는 AgentUI이므로 `<InstallRoot>\AgentUI\HeatingCameraSystem.AgentUI.exe` 배포가 필요. AgentExePath는 콘솔 Agent(선택적/진단용)용"으로 수정.

  4. [7.3 / 절차] 문제: "Agent 설정 화면의 디바이스 관리 탭"은 잘못된 위치. -> 제안: "Master PC의 **장치(Devices)** 화면/탭"으로 정정. Agent 설정 화면(`AgentSettingsView`)은 NATS 주소·카메라 목록 등 Agent 설정 편집용이며 승인 UI는 `DevicesView`임.

  5. [7.3 / 버튼] 문제: "Agent 기동(영구드롭 재기동에도 사용)"은 프로세스 기동으로 오인. -> 제안: "승인 시 `IsApproved=true`, `IsDisabled=false`, AgentId 할당 후 AgentUI에 `runtimeLoad` 전송"으로 수정.

  6. [7.3 / 버튼] 문제: "거부 | IsApproved=false + Agent 프로세스 종료"는 틀림. -> 제안: "거부 시 `IsApproved=false` 및 AgentUI에 `runtimeUnload` 전송"으로 수정.

  7. [7.3 / 버튼] 문제: "이름 저장 | Alias를 상태파일+LiteDB에 저장"의 LiteDB 저장은 Master의 `DevicesViewModel.RenameAsync`에서 수행되며 Manager 측은 아님. -> 제안: "Master 장치 화면의 이름 변경은 Manager에 `Rename` 명령을 보내고, 동시에 Master의 LiteDB에도 별도 저장"으로 명확히 구분.

  8. [7.3 / 버튼] 문제: "로그 가져오기 | Agent NDJSON 로그를 gzip으로 요청·표시"는 실제로 Manager가 gzip으로 묶어 Master에 응답하므로 표현이 모호함. -> 제안: "Manager의 로그 폴더에서 `.log` 파일을 읽어 gzip으로 압축해 Master로 전송하고, Master UI에서 풀어 표시"로 수정.

  9. [참고] 문제: "Agent 프로세스가 반복 크래시하면 지수 백오프… 영구 드롭"은 S7 이전 동작. -> 제안: 해당 단락 삭제. S7에서는 Manager가 프로세스를 기동하지 않으므로 backoff/재시도/영구드롭 로직이 `AgentSupervisor`에 없음. 대신 AgentUI 자체의 예약작업 재시작 정책(1분 간격 3회)을 언급하거나 아예 생략.

  10. [7.3] 문제: `Disable`(비활성화) 버튼/명령이 누락됨. -> 제안: 버튼 표에 `Disable` 추가: "`IsDisabled=true`로 설정하고 `runtimeUnload` 전송. 다시 승인 전까지 로드되지 않음."

  11. [7.3] 문제: `Restart` 명령도 누락됨. -> 제안: 버튼/기능으로 "`runtimeLoad`를 재전송해 해당 카메라 런타임을 다시 로드" 추가.

  12. [7.1 설치] 문제: 설치 예시에 AgentUI 배포 및 `install-agentui-task.ps1` 실행 단계가 없음. -> 제안: 7.1에 "AgentUI 빌드 출력을 `<InstallRoot>\AgentUI\`에 복사하고 `./docs/deployment/install-agentui-task.ps1 -InstallRoot C:\HeatingCameraSystem`로 로그온 예약작업 등록" 추가. Manager만 띄워서는 카메라가 실제 구동되지 않음.

  13. [7.1 설치] 문제: `dotnet publish ... -o publish\Manager` 경로가 이후 `install.ps1`의 `-InstallRoot`와 직접 연결되지 않음. -> 제안: publish 출력을 `<InstallRoot>\Manager\`에 복사하거나 `-o C:\HeatingCameraSystem\Manager` 예시를 보강.

  14. [7.2 / manager-state.json] 문제: `Cameras[]` 필드 설명이 불완전. -> 제안: `HardwareId/AgentId/Alias/IsApproved/IsDisabled` 외에 `OpenCvIndex/StoragePath/RestartFails/FirstSeen/LastSeen` 등 실제 저장 항목을 함께 기술하고 "직접 편집 금지"는 유지.

  ## 누락/추가 제안
  - Master 장치 화면에서 `HasAlert`/`LastAlert` 표시에 대한 설명 추가(로그 테일에서 ERROR/FATAL 수집).
  - Manager가 "운영자 로그인 세션 콘솔 앱"임을 명시하고, 무인 운영 시 Windows 자동 로그인 필요 사항 추가(`install-manager-task.ps1` 주석 참고).
  - Manager와 AgentUI가 별도의 로그온 예약작업(HCS-Manager, HCS-AgentUI)으로 각각 기동된다는 구조도 추가.
  - `SimulateEnumeration=true` 사용 시 실제 USB 카메라 없이 2대 가상 카메라가 발견되는 점을 언급하면 운영자/테스트 담당자가 혼란을 덜 겪음.
  - NATS 서버가 먼저 가동 중이어야 Manager가 시작됨을 사전 조건으로 추가.

  ## 이미지 자리 검토
  - [그림 10] 부적절 — "Agent 설정 화면의 디바이스 관리 탭"이라는 캡처 대상이 실제 화면과 다름. 승인/거부/이름 변경/로그/시리얼 기능은 Master의 `DevicesView`에 있으므로, Master 장치 화면을 캡처 대상으로 변경해야 함. "미승인 카메라가 목록에 있는 상태"와 표시 영역은 적절.

  ## (선택) 수정 제안 전문
  (해당 챕터는 S7 구조 변경으로 인해 전반적인 재작성이 필요하므로 별도 전문 생략)
