# 07-Agent-Manager(자동-발견·승인) — 검토 종합 (Round 1)

## 판정

- Codex: 수정 필요도 **중**
- Claude: 수정 필요도 **중**
- agy: 수정 필요도 **중**
- Kimi: 수정 필요도 **상**
- 종합: 설치·설정 경로·화면 명칭 보완에는 수렴했다. 다만 Agent 실행 방식과 일부 UI 기능은 충돌했으며, 현재 코드는 **Manager가 Agent/AgentUI 프로세스를 직접 기동하지 않고 이미 실행 중인 AgentUI에 `runtimeLoad`/`runtimeUnload`를 전송하는 S7 구조**임을 확인했다. 따라서 Kimi의 핵심 아키텍처 지적을 채택한다.

## 합의·다수 수정 (코드 확인됨)

- [도입부] “Master 승인 후 Agent 자동 기동·감독”은 현재 구조와 다름 -> 각 Agent PC의 운영자 로그인 세션에서 `HCS-Manager`와 `HCS-AgentUI`가 각각 실행되며, Manager는 승인 상태에 따라 AgentUI에 카메라 런타임 로드·언로드 명령을 전송한다고 수정. 수동 Agent와 혼용하지 않는 운영 원칙도 명시한다. (Codex, Kimi)
- [7.1 설치] 실행 위치·권한·배포 단계가 불완전함 -> 저장소 루트의 관리자 PowerShell에서 실행하고, Manager와 AgentUI 게시물을 각각 `<InstallRoot>\Manager`, `<InstallRoot>\AgentUI`에 복사한 뒤 `HCS-Manager`, `HCS-AgentUI` 로그온 작업을 등록하도록 수정한다. `install.ps1`은 디렉터리·설정·방화벽만 준비하며 게시물을 복사하지 않는다. (Codex, Claude, agy, Kimi)
- [7.1 설치] `--self-contained false` 선행 조건 누락 -> 대상 PC에 해당 게시물 실행에 필요한 .NET 8 런타임이 필요함을 명시한다. (Codex)
- [7.1 설치] 예약 작업 계정과 무인 실행 조건 누락 -> `-User` 기본값은 현재 사용자이고 `Interactive`, `RunLevel Limited`, 로그온 트리거로 등록됨을 명시한다. 무인 PC는 조직 보안정책에 맞는 자동 로그온이 별도로 필요하다. (Codex, Claude, agy, Kimi)
- [7.1 설치] 설치 확인 절차 누락 -> `Get-ScheduledTask -TaskName HCS-Manager`, `Get-ScheduledTask -TaskName HCS-AgentUI`로 등록 상태를 확인하고, 실행 파일 경로와 NATS 연결 로그를 점검하도록 보완한다. (Codex)
- [7.2 설정 파일] 파일 경로·생성 주체 누락 -> 기본 경로를 `<InstallRoot>\Manager\manager-settings.json`, `<InstallRoot>\Manager\manager-state.json`으로 명시한다. 전자는 `install.ps1`이 생성하고 후자는 카메라 상태가 최초 저장될 때 생성된다. (Codex, Claude)
- [7.2 `manager-settings.json`] 실제 필드와 불일치 -> `PCId`, `NatsUrl`, `SimulateEnumeration`, `SimulateAgentMode`, `LogRetentionDays`, `WarnAlertEnabled`, `InstallRoot`, `AgentExePath`, `AgentUiExePath`로 정정한다. `SimulationMode` 단일 필드는 현재 없음. (agy, Kimi)
- [7.2 `manager-state.json`] 저장 필드 설명이 불완전함 -> `HardwareId`, `AgentId`, `Alias`, `OpenCvIndex`, `StoragePath`, `IsApproved`, `FirstSeen`, `LastSeen`, `RestartFails`, `IsDisabled`를 실제 JSON 필드명과 `true/false` 표기로 설명한다. 직접 편집 금지는 유지한다. (Codex, Kimi)
- [7.2 설정 적용] 안전한 편집·적용 시점 누락 -> `manager-settings.json`은 Manager 시작 시 한 번 읽으므로 Manager 종료 후 편집하고 재시작해야 한다고 명시한다. 상태 파일은 UI 명령으로 관리한다. (Codex)
- [7.3 화면 위치·그림 10] “Agent 설정 화면의 디바이스 관리 탭”은 현재 UI와 다름 -> Master의 별도 **장치 관리(`DevicesView`) 화면**으로 통일하고 그림 10도 해당 화면을 캡처하도록 수정한다. (Codex, Claude, Kimi)
- [7.3 승인] 프로세스 기동으로 잘못 설명됨 -> `IsApproved=true`, `IsDisabled=false`, 필요 시 `AgentId` 할당 후 AgentUI에 `runtimeLoad`를 전송한다고 수정한다. (Kimi)
- [7.3 거부] Agent 프로세스 종료 설명이 틀림 -> `IsApproved=false`로 저장하고 `runtimeUnload`를 전송한다. 장치 등록 자체는 상태 파일에 남아 있어 다시 승인할 수 있다. (Codex, Claude, Kimi)
- [7.3 이름 저장] 저장 주체가 불명확함 -> Master가 Manager에 `Rename` 명령을 보내 `manager-state.json`의 Alias를 갱신하고, 별도로 Master의 LiteDB `CameraDevice`에도 저장한다고 명시한다. Alias 기반 레시피는 변경 후 기존 `RecipeStep.CameraAlias`를 찾지 못하면 `Agent_{CameraIndex}`로 폴백하므로 관련 레시피 확인이 필요하다. 레시피 자체는 LiteDB가 아니라 `FileRecipeRepository`의 JSON 파일에 저장된다. (Codex, Kimi)
- [7.3 시리얼 전송] 입력·적용 설명 부족 -> Port, Baud, Data Bits, Parity, Stop Bits를 입력하고 승인된 장치를 대상으로 전송한다고 명시한다. Master는 명령 발행 직후 상태 표시줄을 갱신하며 Agent 적용 성공 ACK를 확인하는 UI는 현재 없다. (Codex, agy)
- [7.3 로그 가져오기] 운영 흐름이 모호함 -> 선택 장치의 `<InstallRoot>\logs\<AgentId>\*.log`를 Manager가 최대 5MB까지 읽어 gzip으로 응답하고, Master가 압축을 풀어 오른쪽 로그 영역에 표시하며 30초 초과 시 응답 없음으로 표시한다고 수정한다. (Codex, agy, Kimi)
- [7.3 UI 구성] 실제 화면 설명 부족 -> 장치 목록, Alias 및 승인·거부·이름 저장·로그 가져오기, 시리얼 설정 패널, 경고 상자, 로그 표시 영역, 하단 상태 표시줄을 현재 `DevicesView` 기준으로 설명한다. (agy)
- [7.3 상태 표시] “대시보드 초록 점 = 촬영 준비 완료”는 근거가 부족함 -> 장치 화면의 `IsRunning`은 승인·로드 희망 상태와 최근 15초 이내 AgentUI 하트비트가 모두 충족된 상태임을 설명하고, 촬영 준비 완료로 단정하지 않는다. (Codex, Claude)
- [[참고] 반복 크래시] 지수 백오프·5회 초과 영구 드롭 설명은 현행 코드와 다름 -> 해당 단락을 삭제한다. 프로세스 재시작은 Manager의 카메라별 감독 로직이 아니라 각 예약 작업의 “1분 간격, 최대 3회” 정책이다. (Codex, Claude, Kimi)
- [장애 확인] 자동 발견·기동 실패 진단 정보 부족 -> WMI 열거, NATS 연결, Manager/AgentUI 실행 파일과 예약 작업 상태, Manager 로그를 구분하여 확인하도록 보완한다. (Codex)

## AI 지적이 부정확 (코드검증)

- agy의 “Manager가 `AgentUiExePath`를 우선 기동하고 `AgentExePath`를 폴백으로 기동한다”는 설명은 부정확하다. 두 경로는 설정에 존재하지만 현재 `AgentSupervisor`는 어느 프로세스도 실행하지 않으며 NATS 런타임 명령만 전송한다.
- agy의 “시리얼 설정과 로그가 Agent 설정 메뉴의 디바이스 관리 탭에 있다”는 화면 위치는 부정확하다. 실제 구현은 별도 `DevicesView`이다.
- Kimi의 “Disable·Restart 버튼이 누락됐다”는 지적은 백엔드 명령 기준으로만 맞다. `ManagerCommandOp.Disable/Restart`와 처리 코드는 존재하지만 현재 `DevicesView`에는 해당 버튼이나 명령 바인딩이 없으므로 운영 매뉴얼에 화면 기능으로 추가하면 안 된다.
- Kimi의 “Manager 로그 폴더에서 `.log`를 수집한다”는 표현은 경로상 부정확하다. 실제 수집 경로는 `<InstallRoot>\logs\<AgentId>\*.log`이다.
- Codex의 “거부 시 목록에서 제외될 수 있음” 및 Claude의 상태별 제외 제안은 현재 동작과 다르다. 거부는 등록 항목을 삭제하지 않고 `IsApproved=false`로 유지한다.
- 리뷰에 언급된 “기간·대상·저장 위치 선택” 기능은 현재 없다. 로그 요청 대상은 선택 장치이고, 요청 크기는 5MB로 고정되며 결과는 화면에 표시된다.

## 보류 (설비 안전/도메인 확인 필요)

- WMI `DeviceID`가 동일 모델 카메라 여러 대, USB 포트 변경, 다른 PC 이동 후에도 현장 장비에서 동일하게 유지되는지는 실제 카메라·드라이버 조합으로 확인해야 한다.
- 장치 교체 시 기존 Alias를 새 하드웨어에 승계할지, 기존 등록을 보존할지는 현장 추적성 정책 확인이 필요하다.
- 하트비트 정상만으로 촬영·셔터·열화상 데이터 준비 완료를 판정할 수 있는지는 실제 장비 운전 기준 확인이 필요하다.
- 시리얼 설정값과 전송 후 정상 적용 판정 방법은 실제 카메라·셔터 사양 확인이 필요하다.

## 근거 파일

- `07-Agent-Manager(자동-발견·승인)_codex_R1_수정내용.md`
- `07-Agent-Manager(자동-발견·승인)_claude_R1_수정내용.md`
- `07-Agent-Manager(자동-발견·승인)_agy_R1_수정내용.md`
- `07-Agent-Manager(자동-발견·승인)_kimi_R1_수정내용.md`