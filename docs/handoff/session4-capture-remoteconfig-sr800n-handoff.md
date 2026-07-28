# Handoff — 캡처 버스트 + Master 원격설정 + SR-800N 프로토콜 재작성 (세션4)

이전: `docs/handoff/session3-mapping-serial-deploy-handoff.md`. UI 감사: `.omo/session-handoff-ui-audit.md`.

## 이번 세션 한 일

빌드 **12 proj 0/0**, 테스트 **173/173**(아래 flaky 1건 주의).

### 1. History EMERGENCY STOP 죽은 버튼 배선 (커밋됨, 실동작 QA 완료)
- `HistoryViewModel.EmergencyStop()`가 footer 문자열만 세팅하던 것 → 실 `IPlcController.TriggerEmergencyStopAsync()`(M2000) 호출로 배선. `ManualControlViewModel` 패턴 미러(null-guard + try/catch).
- **실동작 검증 완료**: Master(sim) 실행 → 이력조회 → EMERGENCY STOP 클릭 → footer `Nominal`→`EMERGENCY STOPPED` 전환 확인.

### 2. 캡처 N장 + AgentId별 하위폴더 (Phase 1, 커밋됨, 실동작 QA 완료)
- `AgentUiConfig.CaptureBurstCount`(기본1) 추가.
- `ThermalCaptureWriter`: `rootDir/{AgentId}/` 하위폴더 자동 + 파일명 충돌 가드(같은 ms 버스트 덮어쓰기 방지).
- `CameraNatsConnector.HandleCaptureAsync`: Master 캡처명령 수신 시 N장 루프(첫 장은 캐시 재사용, 이후 `maxAge:Zero`로 프레시 프레임 강제).
- AgentUI 카메라 패널에 **"캡처 저장" 버튼**(로컬 N장 저장) + Settings 탭에 캡처장수 필드.
- **실동작 검증 완료**: AgentUI(sim, burst=3) → 캡처 저장 → `Agent_1/`·`Agent_2/` 하위폴더에 각 정확히 3장(.y16+.json+.tif), distinct 타임스탬프 확인.

### 3. Master ↔ AgentUI 원격 설정 조회/변경 (Phase 2, 커밋됨, ⚠️ **실동작 QA 미완**)
- 신규 NATS 프로토콜(SerialConfig 패턴 미러, additive): `AgentConfigMessages.cs`(Request/Snapshot/Apply/Ack + `AgentConfigSnapshot`). 토픽 `master.config.agent.get/set.{AgentId}`, `agent.config.agent.snapshot/ack.{AgentId}`.
- `INatsCommunicationService`+`NatsCommunicationService`에 pub/sub 8개 추가. `FakeNats`(테스트 2곳) 스텁 추가.
- AgentUI: `CameraNatsConnector`에 `getConfigSnapshot`/`applyConfigSnapshot` 콜백 → 카메라 AgentId별 get 구독(현재 config 스냅샷 응답)/set 구독(agentui.json 저장 + ack "재시작 필요"). `App.xaml.cs` 배선.
- Master: 신규 **"Agent 설정" 화면**(`AgentSettingsViewModel`/`AgentSettingsView`) — 온라인 agent 선택→조회→전체 설정(sim/nats/storage/heartbeat/포맷/캡처장수/카메라그리드) 편집→전송→ack. `MainViewModel` 내비 + `MainWindow.xaml` DataTemplate/버튼.
- **적용 방식**: 저장만 + AgentUI 재시작 시 반영(사용자 선택). 카메라 여러 대여도 config는 PC 단위(agentui.json 전체) — 어느 AgentId로 조회/전송해도 같은 PC config.
- ⚠️ **다음 세션 필수**: 두 앱(Master+AgentUI) 동시 실행 + NATS 왕복 **실동작 QA 미완**. 코드/빌드/테스트만 통과. QA 절차는 아래 §"Phase 2 QA 재개".

### 4. SR-800N 흑체 프로토콜 재작성 (커밋됨, 골든 벡터 검증)
- **배경**: 이전 세션은 흑체를 **SR-800R ASCII**(`SETTEMPERATURE 100.0\r`, 9600)로 구현했으나, 사용자가 실제 **SR-800N** 문서(`참고/SR800N Protocol 6057060A.pdf`, `참고/SR800N 6057050A User Manual.pdf`) 제공 → 실제는 **바이너리 VIP 프로토콜**. 완전 재작성.
- `SrProtocol`: 바이너리 프레임 `[0xAA][0x01][Size BE2][Service][Data][CS]`, Service 0x06=Set/0x08=Get, 파라미터코드(OpMode `0x07F0`, SetPtAbs `0x07F1`, CurTemp `0x07D7`, CurSetPt `0x07F3`), IEEE754 **big-endian** float, 체크섬=`(byte)(-sum)`.
- `ISrLink` `Write(byte[])`/`Read()`. `SerialPortSrLink` 115200 + 프레임 조립 읽기. `SimulatedSrDevice` 바이너리 파싱/응답(램프 유지).
- `SrBlackBodyController` binary send/query. `BlackBodySettings` baud **9600→115200**, InterMessageDelay 300→50.
- **골든 벡터 검증**: `SrProtocol.SetTemperature(100f)` == 문서 §3.1.2 `AA 01 00 0A 06 07 F1 00 04 42 C8 00 00 3F` **바이트 일치**, `SetMode(1)` == §3.1.1 `AA 01 00 07 06 07 F0 00 01 01 4F` 일치, 체크섬/ParseFloat/sim 왕복 통과.

## 다음 세션 (열린 항목)

### Phase 2 QA 재개 (최우선)
1. NATS 실행 확인(`nats-server` 가동중).
2. `agentui.json` 백업 → sim 모드 + 카메라 Agent_1/Agent_2로 임시 설정 → AgentUI 실행(NATS 붙어 heartbeat 발행 + config get/set 구독).
3. Master 실행 → "Agent 설정" 화면 → 온라인 드롭다운에 Agent_1/2 표시 확인 → 조회 → 전체 설정 로드 확인(특히 카메라 그리드가 CameraDescriptor 라운드트립 되는지).
4. CaptureBurstCount 등 필드 변경 → 전송 → ack "저장됨. 재시작 필요" 확인 → `agentui.json`에 실제 반영됐는지 파일 확인.
5. 검증 후 agentui.json 복원.
- **미검증 리스크**: `CameraDescriptor`(record) NATS JSON 라운드트립 — 안 되면 Snapshot용 plain-DTO 추가 필요.

### SR-800N 실운영
- `hardware.json`의 `BlackBody.Units` 포트(COM4/COM5 placeholder)를 실 SR-800N COM으로, `BlackBody.Enabled=true` 설정.
- `GetTargetTemperature`는 Current Set Point(`0x07F3`)로 매핑 — SV 조회 의도 맞는지 확인. Ethernet UDP(port 5200)는 문서상 옵션이나 미구현(RS232만).
- 실 하드웨어 연결 후 통신 검증 필요(현재 골든 벡터=스펙 준거 증거).

### 기존 열린 항목(세션3에서 이월)
- DeviceName 중앙관리: 물리적으로 에이전트 로컬 매칭 필요 + AgentManager 레인과 인접 → 현행(AgentUI Settings) 유지 권장.
- **AgentManager 레인**: 다른 세션 동시 편집 가능성 — 회피 지속.
- 2점 NUC(실 블랙바디), PLC 실주소 치환(A&D 명세).

## 알려진 이슈
- `CaptureStoreTests.Save_Index_Reconstruct_And_Purge`: LiteDB 전역 BsonMapper의 **병렬실행 flaky**(격리·재실행 모두 통과, 내 변경과 무관). 전체 스위트 재실행 시 간헐 실패 가능.

## 빌드/테스트
```powershell
dotnet build HeatingCameraSystem.slnx          # 12 proj 0/0
dotnet test HeatingCameraSystem.Tests/HeatingCameraSystem.Tests.csproj  # 173 (flaky 1 주의)
```

## 환경
- `hardware.json` SimMode=true(가짜 PLC/BB). `agentui.json` SimMode=false(실카메라 Agent_1/COM7·Agent_2/COM8, Tiff16, CaptureBurstCount 미설정=기본1).
- NATS 가동중. branch master, upstream origin/master.
- `참고/` SR-800N PDF 2종 = 참조용(커밋 제외).
