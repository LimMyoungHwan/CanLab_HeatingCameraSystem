# 카메라·Bias 연동 소스 감사 및 수정 보고서

- 작성일: 2026-10-26
- 저장소: `LimMyoungHwan/CanLab_HeatingCameraSystem`
- 검토 기준 GitHub 커밋: [`0addabb76dc1cf422fe3453aa3429fd61507a6dd`](https://github.com/LimMyoungHwan/CanLab_HeatingCameraSystem/tree/0addabb76dc1cf422fe3453aa3429fd61507a6dd)
- 최종 수정 소스 커밋: [`1f292ecd1a593b98c72f58f102d3d0db942f035f`](https://github.com/LimMyoungHwan/CanLab_HeatingCameraSystem/commit/1f292ecd1a593b98c72f58f102d3d0db942f035f)
- 수정 소스 ZIP: `HeatingCameraSystem_modified_sources_1f292ec.zip` — 161개 파일, 478,468바이트
- ZIP SHA-256: `aee61f7161e5bb5685f698b564bb8c04d15673e7b1373afb555c9c6b8845f9df`
- 최종검증: **전체 솔루션 Debug 오류 0·경고 0, 전체 테스트 935/935 통과(실패 0·건너뜀 0), 실제 NATS 통합 3개 통과**
- 소스 반영 브랜치: `integration/bias-foxmain-20261026` — 소스 푸시 후 GitHub API 비교에서 같은 커밋임을 확인

## 1. 검토 결론과 범위

이번 검토에서는 Bias 실패를 다음 작업의 성공으로 오인할 수 있는 경로, 카메라의 영상·COM·Agent ID가 서로 어긋나는 경로, 실패 응답 또는 오래된 프레임을 정상 결과로 사용하는 경로를 확인하고 수정했다. 이전에 반영된 **최초 1회와 추가 재시도 3회, 필수 `bias.json` 확인, 최종 실패 시 NG와 후속 작업 중단** 정책을 유지하면서 카메라 통신부터 운영 화면까지 그 정책이 끊기지 않도록 보강했다.

GitHub 기준 커밋의 **제품·시뮬레이터·테스트를 포함한 C# 소스 323개를 모두 확보**하고, 확보한 소스 파일을 GitHub tree/blob 정보와 대조하여 정합성을 확인했다. 이 중 테스트 소스는 102개다. 검토 작업본의 원본도 별도로 보존했다. 이는 다른 버전의 파일을 섞어 검토하는 위험을 줄이기 위한 출처 확인이다. 기준 C# 파일은 Agent 3, AgentManager 8, AgentUI 13, Core 66, E2EDriver 2, ManagerE2EDriver 2, Master 68, Protocols 48, Simulator 11, Tests 102개다. 제품·시뮬레이터·테스트를 포함한 C# 소스 323개 확보가 모든 줄에 같은 깊이의 분석을 수행했다는 의미는 아니다. 심층 검토는 아래의 장치 식별·상태 변경·명령 응답·저장·취소 흐름에 집중했으며, 담당 영역을 나눈 뒤 호출 경계와 수정 내용을 교차 검토했다.

분석 단위는 개별 메서드뿐 아니라 다음과 같은 전체 흐름이었다.

1. USB 탐지와 물리 식별 → 영상 인덱스·COM 연결 → 현재 카메라 패널과 런타임.
2. Master 요청 → NATS 구독 준비 → Connector 요청 검증 → 패널·시리얼 실행 → 실제 결과 ACK.
3. Bias 적용 → 적용 ID와 읽기값 확인 → 최종 경로의 JSON 기록·내용 확인 → 후속 촬영.
4. USB 재연결·런타임 교체·화면 전환·프로그램 종료 → 기존 작업 취소 → 구독과 장치 수명 정리.
5. Manager 승인·기동·재시작 → AgentUI의 실제 신원과 상태 확인 → 필요한 카메라만 제어.

수정은 소스와 자동 회귀 검증을 대상으로 한다. 실제 카메라·COM·USB 분리/재연결·챔버를 가동한 실측 시험은 수행하지 않았다. 최종 사용 판단에는 7절의 장비 검증이 필요하다.

### 주요 검토·수정 영역

| 영역 | 중점 확인 내용 | 주요 구현 위치 |
|---|---|---|
| CL 시리얼 | 응답 상태, 명령 일치, 분할 패킷, 지연 응답, 전체 시간 제한, 취소와 포트 해제 | `Protocols/Cameras/CL/ClPacket.cs`, `ClSerialCameraClient.cs`, `ClSerialTransport.cs` |
| 장치·영상 | USB ContainerId, S/N 중복, WMI와 실제 영상 인덱스, 새 프레임과 런타임 수명 | `Protocols/UsbTopology.cs`, `WmiCameraEnumerator.cs`, `CameraInventoryDelta.cs`, `Cameras/VideoIndexReconciler.cs`, `CameraAutoRegistrar.cs`, `CameraRuntime.cs` |
| NATS·Connector | SUB 준비 확인, 구독 해제, 최신 descriptor, 작업 직렬화, 적용 ID, 저장 확인과 ACK | `Protocols/NatsCommunicationService.cs`, `Cameras/CameraNatsConnector.cs`, `Core/Interfaces/NatsSubscriptionExtensions.cs` |
| AgentUI | 원격 제어의 예외 전파, 수동/원격 작업 충돌, 런타임 교체, 저장 설정과 활성 설정 분리 | `AgentUI/App.xaml.cs`, `ViewModels/CameraPanelViewModel.cs`, `SettingsViewModel.cs`, `Services/AgentUiConfig.cs`, `CameraBindingPolicy.cs` |
| Master | 카메라 선택 보존, 설정 응답 상관관계, NG·영수증·후속 차단, 수동 취소, 화면·종료 수명 | `Master/Services/RecipeEngine.cs`, `CameraCommandClient.cs`, `AgentDirectory.cs`, `AppServices.cs` 및 관련 ViewModel·View |
| Manager·콘솔 Agent | 실제 Agent ID 채택, 재시작 중 Bias 보존, 로그 수집, 실제 촬영 인덱스와 결과 식별 | `AgentManager/Program.cs`, `Services/AgentSupervisor.cs`, `ManagerCommandHandler.cs`, `AgentLogFiles.cs`, `LogTailService.cs`, `LogDumpHandler.cs`, `Agent/Program.cs` |

이 표의 프로젝트 경로는 모두 `HeatingCameraSystem.` 접두사를 생략했다. 아래 회귀 테스트 파일은 별도 표시가 없으면 `HeatingCameraSystem.Tests/` 아래에 있다.

## 2. 유지·보강한 Bias 실행 계약

### 재시도와 성공 판정

재시도 소유자는 Agent이다. Master는 같은 작업을 다시 여러 번 발행하지 않고, 한 요청 안에서 수행되는 **최대 4회**의 최종 결과를 기다린다. 각 시도는 보정 또는 현재 적용값 확인과 필수 저장 단계를 포함한다. 파일을 남겨야 하는 요청은 최종 목적지에 기록한 JSON을 다시 읽어 내용을 확인한 뒤에만 성공 ACK를 보낸다. 화면 히스토그램이 목표에 도달했거나 메모리에 Bias 값이 있다는 사실만으로 파일 저장 성공을 인정하지 않는다.

기본 `BiasAttemptTimeoutSeconds`는 시도당 120초다. 시도 사이 간격은 500ms이며, Master의 기본 응답 대기 예산은 다음과 같다.

`4 × 120초 + 3 × 0.5초 + 통신 여유 10초 = 491.5초`

이 값은 소프트웨어의 대기 예산이다. 운영체제나 드라이버 내부에서 반환하지 않는 네이티브 호출의 강제 중단 시간까지 보장하는 값은 아니다. 설정 정규화와 예산 계산은 [BiasExecutionPolicy.cs](../HeatingCameraSystem.Core/Models/BiasExecutionPolicy.cs), 시도·저장·취소 처리는 [BiasOperationRunner.cs](../HeatingCameraSystem.Protocols/Cameras/BiasOperationRunner.cs)에 있다.

### 다음 작업으로 넘어가는 조건

Master는 요청 ID와 대상, 실제 카메라 번호, 명령 종류, 성공 여부, 파일 저장 증빙을 확인한다. 성공이라고 답했더라도 필수 증빙이 빠지면 NG이다. `biasUseCurrent`에는 비어 있지 않은 적용 ID와 요청한 Bias 대역의 일치도 필요하다. 실패 메시지는 알람·진행 상태와 실행 영수증에 남기며, 이후 촬영과 모터 이동을 실행하지 않는다.

저온에서 적용한 Bias로 다른 온도 조건의 레시피를 실행하거나 AgentUI에서 수동 복원 후 재개할 때에도 새 요청으로 현재 적용값을 채택한다. 적용 ID는 유지하되, **이번 실행·이번 조건의 저장 경로에 대한 증빙은 다시 만든다.** 해당 Bias 의존성이 설정된 생산 촬영은 요청 ID를 전달하며, Cold·Hot·Room 역할과 관계없이 필수 확인을 기다린다. 저장 파일이 사라졌거나 내용이 달라진 경우에는 확인한 동일 적용 결과로 파일 복구를 시도하고, 실패하면 촬영을 NG로 끝낸다.

런타임·물리 장치·COM 바인딩을 바꾸면 이전 Bias 적용 기록을 무효화한다. 한 카메라에서 Bias·셔터·NUC·촬영이 서로 겹쳐 레지스터나 촬영 조건을 바꾸지 않도록 Connector와 패널의 작업 제어를 연결했다. 취소에는 원본 요청 ID를 지정해 늦게 도착한 취소가 다른 실행에 적용되지 않게 했다.

관련 구현은 [RecipeEngine.cs](../HeatingCameraSystem.Master/Services/RecipeEngine.cs), [CameraNatsConnector.cs](../HeatingCameraSystem.Protocols/Cameras/CameraNatsConnector.cs), [CameraPanelViewModel.cs](../HeatingCameraSystem.AgentUI/ViewModels/CameraPanelViewModel.cs)에서 함께 확인할 수 있다.

## 3. 확정 결함과 수정 근거

다음 표는 코드의 실제 호출 흐름이나 재현 가능한 테스트 조건으로 확인한 항목이다. 각 테스트 파일의 존재는 회귀 사례를 구현했다는 뜻이며, 최종 실행 통과 결과는 5절에 기록했다.

### 3.1 시리얼·영상·물리 식별

| ID | 원인 → 결과 | 수정 | 회귀 테스트 파일 |
|---|---|---|---|
| S1 | CL 응답의 오류 결과 바이트를 확인하지 않음 → 장치가 ERR를 답해도 읽기/쓰기 성공으로 처리되어 잘못된 Bias 성공이 가능 | 결과 코드가 성공인지 검사하고 main/sub/RW가 현재 명령과 일치하는 응답만 채택 | `Protocols/ClPacketTests.cs`, `Protocols/ClSerialCameraClientTests.cs` |
| S2 | 분할 수신·잡음·이전 명령 응답을 충분히 구분하지 않고 바이트별 시간 제한을 적용 → 오래된 응답 채택이나 전체 대기 시간 증가 | 프레임 경계 재동기화, 다른 명령 응답 제외, 단일 전체 응답 기한, 실패 후 입력 정숙 구간 확인 | `Protocols/ClSerialCameraClientTests.cs` |
| S3 | 읽기·대기 작업과 포트 Dispose 수명이 겹침 → 종료 중 사용 중인 포트에 접근하거나 취소가 정상 반환으로 바뀔 수 있음 | 활성/대기 작업 취소, 포트 접근 직렬화, transport 해제 순서와 취소 예외 전파 보강 | `Protocols/ClSerialCameraClientTests.cs` |
| S4 | 새 프레임 대기가 실패해도 이전 캐시 프레임을 돌려주거나 Stop/Start가 겹침 → 오래된 영상을 새 촬영으로 저장 | 새 프레임 없으면 실패, 외부 취소 전파, Stop 시 캐시 제거, 기존 읽기·닫기 완료 후 재시작 | `CameraRuntimeLifecycleTests.cs`, `ConnectorDeepAuditTests.cs` |
| I1 | 실제 ContainerId가 없을 때 VID/PID·모델명·열거 순번으로 물리 신원을 추정 → 동일 모델의 영상 A와 시리얼 B를 연결할 수 있음 | 유효한 물리 식별자와 유일성 확인, 중복 S/N·모호한 후보 배제, 확인되지 않은 영상 인덱스는 `-1`로 비활성화 | `UsbPhysicalIdentityTests.cs`, `VideoIndexReconcilerTests.cs`, `CameraAutoRegistrarTests.cs` |
| I2 | 카메라 제거 또는 열거 실패 후 이전 영상 인덱스를 유지 → 남은 다른 카메라의 영상이 기존 센서 ID에 연결될 수 있음 | 전체 열거 실패도 인덱스를 무효화하고, 복구 시 확인된 물리 식별자로만 재연결 | `AgentUiConfigurationDeepAuditTests.cs`, `VideoIndexReconcilerTests.cs` |
| I3 | 관련 없는 PnP 변화에도 모든 패널/런타임을 재생성 → 다른 카메라에서 진행 중인 Bias까지 끊김 | 인벤토리 변경분만 통지하고 실제 물리 바인딩이 달라진 패널만 교체 | `CameraInventoryDeltaTests.cs`, `CameraPanelDeepAuditTests.cs`, `AgentUiConfigurationDeepAuditTests.cs` |

시리얼 수정은 7바이트 CL 패킷의 기존 계약을 유지한다. 단, 명령 구분 값이 완전히 동일한 과거 응답에 관한 프로토콜 한계는 6절에 따로 기록했다.

### 3.2 NATS·Connector·AgentUI

| ID | 원인 → 결과 | 수정 | 회귀 테스트 파일 |
|---|---|---|---|
| N1 | 구독 메서드가 실제 SUB 준비 전에 반환 → 즉시 도착하는 첫 ACK를 놓쳐 정상 장비가 시간 초과로 보임 | 구독 등록과 서버 PONG 확인 후 반환. 테스트의 임의 준비 지연도 제거 | `Protocols/NatsDeepAuditTests.cs`, `NatsIntegrationTests.cs`, `NatsCameraIntegrationDeepAuditTests.cs` |
| N2 | 실행·화면마다 구독이 누적되고 종료된 객체에 콜백이 남음 → 중복 처리, 이전 실행 참조 유지, 종료 경합 | scoped 구독과 실제 해제, 초기 등록 실패 복구, 화면·Engine·Connector 수명에 맞춘 취소와 정리 | `Protocols/NatsDeepAuditTests.cs`, `CameraCommandClientDeepAuditTests.cs`, `RecipeEngineDeepAuditTests.cs` |
| A1 | 구독 시점의 descriptor를 계속 캡처 → USB 재연결 후 이전 인덱스로 runtimeLoad하거나 잘못된 메타데이터를 응답 | 명령마다 현재 descriptor를 확인하고 이전 인덱스 요청을 하드웨어 실행 전에 거부 | `ConnectorDeepAuditTests.cs` |
| A2 | UI 명령 래퍼가 시리얼 예외를 흡수했는데 원격 호출자는 완료를 성공으로 간주 → 셔터/RUN 등의 실패를 성공 ACK로 발행 | 원격 실행 경로는 실제 실패·취소를 전달. 시리얼 연결 없음도 명시 실패 | `CameraPanelDeepAuditTests.cs`, `ConnectorDeepAuditTests.cs` |
| A3 | 로컬·원격 제어와 촬영이 별도 제어 흐름으로 겹침 → Bias 중 셔터·NUC·저장 조건 변경 또는 장치 교체 후 이전 세대 결과 사용 | 카메라별 작업 직렬화, 원격 촬영 lease, 바인딩 수명 토큰, 런타임 교체 전 취소, 적용 ID 무효화 | `CameraPanelDeepAuditTests.cs`, `ConnectorDeepAuditTests.cs` |
| A4 | 캡처 대기 도중 런타임이 교체되거나 같은 프레임이 반복 반환 → 이전 카메라 영상·중복 프레임을 정상 장수로 저장 | 런타임 참조와 바인딩을 재확인하고 생산 프레임의 최신성·타임스탬프 증가를 검사 | `ConnectorDeepAuditTests.cs` |
| A5 | 설정 객체를 즉시 변경하지만 NATS·저장소 등 서비스는 이전 값으로 동작 → 화면/저장 설정과 실제 실행 환경 불일치. 기록 실패에도 메모리 변경 가능 | 저장 성공 후 반영, 재시작이 필요한 변경은 별도 초안으로 유지, 활성 카메라 목록 참조 보존, `RestartRequired` 보고 | `AgentUiConfigurationDeepAuditTests.cs`, `ConnectorDeepAuditTests.cs` |

### 3.3 Master·Manager·운영 기록

| ID | 원인 → 결과 | 수정 | 회귀 테스트 파일 |
|---|---|---|---|
| M1 | 첫 하트비트 기준으로 오프라인 타겟을 삭제 → 저장된 선택·대역·흑체 역할 유실 | 저장/선택 대상은 오프라인 표시로 보존. 보고용 ID·Offline·이전 인덱스는 온라인 후보에서 제외 | `AgentSelectionDeepAuditTests.cs` |
| M2 | A 설정을 읽은 뒤 B를 선택해도 적용 가능하고 응답에 요청 상관관계가 없음 → A의 COM/카메라 설정을 B에 저장 | 대상 전환 시 loaded 상태 해제, 조회/적용 RequestId 및 Agent 일치 검사, 제한 시간 적용 | `AgentSelectionDeepAuditTests.cs`, `ConnectorDeepAuditTests.cs` |
| M3 | 수동 제어의 빈 요청 ID와 느슨한 ACK 연결 → 다른 명령의 ACK를 사용하거나 무기한 대기 | 공통 제어 client에서 요청·Agent·카메라·동작 검증, 제한 시간, Bias/촬영의 원본 요청 지정 취소 | `CameraCommandClientDeepAuditTests.cs` |
| M4 | 일반 카메라 제어 실패 ACK를 경고로 처리하고 진행 → 필요한 셔터/RUN/NUC 상태가 확보되지 않은 채 다음 촬영/이동 실행 | 성공 ACK 필수 확인, 실패/미확인은 `CAM-008` NG와 실패 영수증, 후속 작업 차단 | `RecipeEngineDeepAuditTests.cs` |
| M5 | 결과의 Agent·카메라 번호나 중복 CaptureId를 검증하지 않음 → 다른 카메라 결과 또는 같은 결과 두 건을 정상 장수로 계산 | 대상 불일치 즉시 NG와 targeted abort, 비어 있지 않은 CaptureId 중복 제외, 실행별 요청 ID와 구독 수명 유지 | `RecipeEngineDeepAuditTests.cs`, `RecipeEngineOutcomeTests.cs` |
| M6 | 창/화면 종료 시 진행 작업 취소보다 통신 종료가 먼저 일어남. 일부 캡처 취소가 정상 반환으로 끝남 → 원격 작업 잔존·잘못된 완료 판정 | 화면·타이머 정리, 비생산 캡처도 취소 전파, 미완료 fork 중단, 정상 창 닫기에서 NATS 종료 전 제한 시간 동안 취소 대기 | `CameraCommandClientDeepAuditTests.cs`, `RecipeEngineDeepAuditTests.cs`, `AppServicesShutdownTests.cs` |
| G1 | Manager가 별도 Agent ID를 만들거나 WMI 순번으로 영상 인덱스를 덮음 → 실제 AgentUI 구독과 제어 대상 불일치 | 같은 호스트의 유일한 ContainerId와 S/N으로 실제 Agent ID 채택, 실제 인덱스는 검증된 AgentUI 보고로 확인 | `AgentManagerDeepAuditTests.cs`, `ManagerStartupDeepAuditTests.cs` |
| G2 | Manager 시작 시 승인 카메라 전체에 runtimeLoad 발행 → 이미 실행 중인 AgentUI의 Bias와 촬영을 취소·무효화 | 기동은 원하는 상태만 복원. Connected는 유지하고 유효한 Offline 보고 뒤 자동 로드 1회. 명시 Restart는 유지하고 비승인/비활성화는 차단 | `ManagerStartupDeepAuditTests.cs` |
| G3 | Manager의 로그 경로·확장자 가정이 실제 AgentUI 로그와 다르고 카메라별 구분이 부족 → Bias 오류 누락 또는 다른 카메라 로그 혼입 | 신뢰 가능한 실제 로그 경로 보고, `.ndjson`·롤링 파일 처리, 구조화된 AgentId 필터와 진단 로그 연결 | `AgentManagerDeepAuditTests.cs` |
| C1 | 콘솔 Agent의 캡처 결과가 실제 카메라 인덱스·촬영 출처·개별 결과 ID를 충분히 전달하지 않음 → 엄격한 Master 검증과 결과 이력 연결이 어긋남 | 실제 인덱스, 요청 출처, 장마다 고유 CaptureId 전달 | `ConsoleCaptureDeepAuditTests.cs` |

## 4. 배포·운영에서 달라지는 점

**Master·AgentUI·공유 Core/Protocols·Manager를 같은 수정 세트로 맞춰야 한다.** 설정 메시지에는 RequestId가 추가되었고, 상태에는 물리 신원과 로그 경로를 사용한다. 기존 Bias 파일 증빙과 적용 ID 계약도 계속 필요하다. 구형 Agent가 요청 ID나 필수 파일 증빙을 돌려주지 않는 경우에는 성공으로 가정하지 않으므로 응답 시간 초과 또는 NG가 발생할 수 있다.

기존 레시피의 생산 저장 경로, 촬영 계획의 실행 영수증, StepId 기반 연결, 촬영 번호 offset은 유지한다. 일반 `.y16` 촬영과 생산 저장은 기존 문맥에 따라 구분한다. 저장 루트만 지정되었다는 이유로 대역·흑체 역할이 없는 일반 촬영을 일괄 생산 촬영으로 바꾸지 않는다. 기존 fixture 수정은 실제 `Connected` 상태와 실제 카메라 번호를 응답하도록 맞춘 것이며, 검증 조건을 완화하는 용도로 사용하지 않았다.

설정 화면에서 연결·저장·영상 형식 등 실행 환경을 바꿔 저장하면 재시작 안내가 나타난다. 저장된 값과 현재 실행 중인 값이 다를 수 있음을 `RestartRequired`로 표시한다. 물리 신원을 확정할 수 없는 카메라는 이전 인덱스를 추정해 열지 않고 오프라인 상태로 남는다. 이 경우 S/N·ContainerId·COM 연결을 확인해야 한다.

### 현장 로그 확인

AgentUI의 실제 로그 위치는 `%LOCALAPPDATA%\HeatingCameraSystem\AgentUI\logs\agentui-*.ndjson`이다. [AgentUiLog.cs](../HeatingCameraSystem.AgentUI/Services/AgentUiLog.cs)에서 일 단위 롤링과 AgentId 진단 속성을 확인할 수 있다. Bias 로그에는 요청/단계 문맥, 시도 횟수, 제한 시간, 경과 시간, 실패 원인, 저장 경로가 남는다. 수동 복원과 현재값 확인 로그에는 적용 ID·대역·GSK_LSB 기록/읽기값을 남긴다.

Master는 알람 화면과 `%LOCALAPPDATA%\HeatingCameraSystem\data.db`의 알람 이력을 사용한다. 알람 화면 표시와 이력 저장은 구분해야 한다. [AlarmSink.cs](../HeatingCameraSystem.Master/Services/AlarmSink.cs)의 이력 저장은 최선 노력 방식이므로, DB 기록 실패가 표시된 알람 자체를 없애지는 않는다. 현장 재현 자료에는 Agent 로그, Master 알람, 실행 영수증, 실제 `bias.json` 경로/내용을 함께 보관하는 것이 좋다.

Manager는 AgentUI가 보고한 경로를 검증한 뒤 `.ndjson`을 읽고 해당 AgentId의 로그만 수집한다. 비표준 사용자 프로필은 `ManagerSettings.AgentUiLogDirectory` 설정이 필요할 수 있다. 서비스 계정이 대화형 사용자 로그 폴더를 읽을 수 있는지도 실제 배포 환경에서 확인해야 한다.

## 5. 자동 검증 상태와 증거

| 검증 단계 | 확인 결과 | 해석 |
|---|---|---|
| GitHub 기준 소스 확보 | 제품·시뮬레이터·테스트를 포함한 C# 소스 323개 확보 및 tree/blob 정합성 확인. 테스트 소스 102개 포함 | 검토 입력 버전과 출처 확인 |
| 첫 Windows 빌드 | 오류 0, 경고 0 | 당시 통합 작업본의 컴파일 확인. 마지막 전체 솔루션 빌드도 동일하게 오류·경고 0 |
| 집중 회귀 테스트 1차 | 186개 통과, 1개 실패. 실패는 테스트 fixture 구성 오류를 확인하여 보완 | 이 수치를 전체 suite 최종 통과로 해석하지 않음 |
| 전체 테스트 1차 | 935개 중 934개 통과, 기존 UI fixture의 새 오프라인 배지 기대값 1개 실패. 온라인 상태 명시와 오프라인 전환 검증 보완 후 최종 전체 실행에서 통과 | 중간 결과를 보존했으며 아래 최종 통과 수치와 구분 |
| 실제 NATS 통합 시험 | 격리 NATS 2.10.24에서 3개 통과 | 명령·결과 왕복, CL 오류→Master 실패 ACK와 후속 10회 성공. 종료 후 서버 프로세스 없음 확인 |
| 최종 Windows 빌드·전체 테스트 | **빌드 오류 0·경고 0. 935개 통과, 실패 0, 건너뜀 0. 테스트 표시 시간 2분 35초** | 이전 808개에서 127개 증가. 모든 보완을 포함한 소스에 대해 실행 |
| 물리 카메라·시리얼·USB·챔버 실측 | 미실행 | 자동 테스트로 대체되지 않음 |

### 실제 NATS와 모의 장치를 결합한 검증

테스트에는 [공식 NATS Server v2.10.24 릴리스](https://github.com/nats-io/nats-server/releases/tag/v2.10.24)의 ZIP과 SHA256SUMS로 확인한 **NATS Server 2.10.24 Windows 배포본**을 임시 디렉터리에 준비했다. ZIP 크기는 6,012,484바이트이며 확인한 SHA-256은 다음과 같다.

`BF94C9A9F1685147FD95F6C62F26D16FB72DC8C8C592E2D8C9115E2491C508C3`

서버는 테스트용 `127.0.0.1` 임의 포트에서 실행하고 `HCS_TEST_NATS_URL`로 suite에 전달하며, 테스트 후 종료한다. 이 절차는 운영 NATS나 장비 연결을 사용하지 않는다.

[NatsCameraIntegrationDeepAuditTests.cs](../HeatingCameraSystem.Tests/NatsCameraIntegrationDeepAuditTests.cs)는 실제 NATS 통신에 실제 CL 파서와 모의 serial transport를 연결해 `ERR → 실패 ACK → Master client` 전파를 확인하도록 작성했다. 이어지는 10회 성공 요청에서 임의 구독 준비 지연 없이 ACK를 받는지, 매 요청 종료 후 Master 구독 수가 0인지, Connector 정리 후 Agent 구독이 남지 않는지를 검사한다. 물리 카메라의 응답을 사용한 시험은 아니다.

기존 [NatsIntegrationTests.cs](../HeatingCameraSystem.Tests/NatsIntegrationTests.cs)의 임의 300ms 준비 지연은 제거했다. 명시한 테스트 서버에 연결하지 못하면 실패하도록 해, 서버 연결 실패 후 조용한 반환을 실행 통과로 오인하지 않게 했다. 환경 변수를 지정하지 않은 개별 실행에는 실행을 생략하는 경로가 있으므로, **이번 최종 실행 결과에는 환경 변수와 실제 서버 기동 여부를 함께 확인해야 한다.**

기존 Bias 재시도·파일 저장·수동 복원·조건별 채택·계획 영수증 테스트도 함께 유지한다. 새 테스트만 통과한 결과로 전체 회귀 검증을 대신하지 않는다. TRX의 Counters와 개별 NATS 3개 결과를 실제 Windows 출력과 대조했다. 테스트용 NATS 서버는 `127.0.0.1:50574`에서 실행 후 종료했다. `tmp/foxmain-deep-audit-20261026/deep-audit-final.trx`에 최종 935개 결과가 남아 있다. 이전 실행 기록은 같은 폴더의 `deep-audit-first-full.trx`와 `deep-audit-focused.trx`에 보관했다.

GitHub 검토 작업본과 FoxMainPC에 적용한 변경 파일 84개는 UTF-8 BOM·줄바꿈·파일 끝 개행을 정규화한 SHA-256으로 대조했으며 내용 불일치가 없었다. Git 변경 공백 검사도 통과했다. 최종 빌드는 E2EDriver·ManagerE2EDriver를 포함한 솔루션 전체를 대상으로 다시 실행했고 오류·경고가 없었다.

## 6. 검증의 한계와 남아 있는 현장 확인

**펌웨어 응답 계약.** 모의 transport는 코드가 잘못된 응답을 거부하는지 검사한다. 실제 펌웨어가 모든 명령에서 결과 코드와 main/sub/RW를 동일한 계약으로 돌려주는지, 정상 ACK까지 걸리는 시간이 얼마인지는 실제 장비에서 확인해야 한다. 장치 버전별로 정상 응답 지연이 다르면 통신 시간 제한과 Bias 시도 시간 제한을 각각 검토해야 한다.

**동일 명령의 매우 늦은 응답.** 현재 CL 패킷에는 거래별 시퀀스 번호가 없다. 실패 후 수신 버퍼 정리와 입력 정숙 구간, 명령 필드 검증으로 이전 응답의 혼입 위험을 줄였지만, 정숙 구간을 지난 뒤 이전 거래의 완전히 같은 명령 응답이 도착하면 새 거래의 응답과 확실히 구별할 정보가 없다. 펌웨어 차원의 거래 번호나 그에 준하는 식별자 없이 이 경우까지 완전하게 판별한다고 할 수 없다. Bias 적용 후 GSK_LSB 읽기 확인과 현재 적용 ID 검사는 추가 방어이며, 프로토콜 자체의 식별 한계를 제거하지는 않는다.

**USB·영상 드라이버.** ContainerId·S/N·실제 영상 열거 결과로 바인딩을 제한했지만, 드라이버가 잘못된 신원을 제공하거나 네이티브 읽기/해제가 반환하지 않는 상황은 별도 문제다. 새 프레임 검사는 런타임의 프레임 시각과 참조를 사용하며, 센서 내부에서 생성한 물리 프레임 번호를 직접 검증하는 방식은 아니다. 동일 모델 2대와 USB 순서 변경 시험이 필요하다.

**저장·종료.** 파일 저장 후 읽기 확인은 그 시점의 경로와 내용을 검증한다. 이후 외부 프로그램의 파일 변경, 공유 서버 전원 차단, 장비가 반환하지 않는 네이티브 호출까지 원자적으로 통제하지는 못한다. 종료는 취소 전송과 제한 시간 동안의 정리를 시도하며, AgentUI에는 기존 네이티브 호출 정체에 대비한 종료 제한이 있다. 프로그램이 닫혔다는 사실만으로 장치 셔터·히터의 물리 정지까지 증명할 수는 없다.

## 7. 장비에서 수행할 최소 확인 시나리오

시험 전 적용한 Master·AgentUI·Manager 버전을 기록하고, 로그와 결과를 보관할 별도 제품 번호/저장 경로를 사용한다. 실행 중 실제 모터·챔버가 움직이는 시나리오는 운영자가 상태를 확인하면서 수행한다.

| 시나리오 | 확인할 결과 |
|---|---|
| 정상 저온 Bias 후 생산 촬영 | 이번 요청의 최대 4회 범위 안에서 성공하고, 최종 경로의 JSON 내용 확인 후 ACK와 촬영이 이어지는지 확인 |
| Bias 명령 실패 또는 쓰기 거부를 계속 유지 | 최초와 추가 3회 후 최종 NG, 실패 원인·시도 횟수 기록, 후속 촬영/이동 없음 확인 |
| 저장 권한 거부 또는 저장 경로 끊김 | 히스토그램이 정상이어도 저장 증빙 없이는 성공하지 않는지 확인. 오류 해소 후 새 실행에서 정상 저장 확인 |
| Bias 성공 후 파일 삭제·내용 변경 | 후속 촬영 전에 동일 적용 결과의 파일 검증/복구를 수행하고, 복구 불가 시 촬영 NG 확인 |
| 수동 Bias 복원 후 -10/+10 조건 레시피 실행 | -30 재탐색 없이 올바른 대역과 적용 ID를 채택하고 각 조건의 새 요청·저장 증빙을 사용하는지 확인 |
| 같은 모델 카메라 2대에서 A 제거·포트 순서 변경 | A의 ID/COM으로 B 영상을 저장하지 않는지 확인. 미확정 카메라는 오프라인/NG, 복귀 후 실제 S/N·영상 인덱스 대응 확인 |
| A Bias 중 B USB 변경 및 A 자체 런타임 교체 | B 변경만으로 A 작업이 무효화되지 않는지, A 바인딩 교체는 A 작업을 취소하고 이전 적용 ID를 거부하는지 확인 |
| Bias/촬영 중 화면 이동·취소·Master 종료 | 원본 요청을 대상으로 abort가 전송되고 이후 작업이 진행되지 않는지 확인. 앱 재실행 후 이전 ACK가 새 요청을 완료시키지 않는지 확인 |
| AgentUI 동작 중 Manager 서비스만 재시작 | 이미 Connected인 카메라의 Bias와 런타임이 유지되는지 확인. 명시 Restart는 해당 카메라만 재로드하는지 확인 |
| A 설정 조회 뒤 B 선택·원격 저장·재시작 | A 설정을 B에 바로 적용할 수 없는지, 요청이 맞는 응답만 표시되는지, 재시작 대기 값과 활성 값이 구분되는지 확인 |

실제 장비 결과에는 목표/측정 히스토그램 값뿐 아니라 S/N, Agent ID, 영상 인덱스, COM 포트, 요청 ID, 적용 ID, 최종 파일 경로를 함께 남겨야 원인을 다시 추적할 수 있다.

## 8. 최종 산출물 기록

| 항목 | 값 |
|---|---|
| GitHub 기준 커밋 | `0addabb76dc1cf422fe3453aa3429fd61507a6dd` |
| 최종 수정 소스 커밋·브랜치 | [`1f292ec`](https://github.com/LimMyoungHwan/CanLab_HeatingCameraSystem/commit/1f292ecd1a593b98c72f58f102d3d0db942f035f), `integration/bias-foxmain-20261026` |
| 최종 빌드·전체 테스트 결과 | Debug 오류 0·경고 0, 935/935 통과, 실패·건너뜀 0 |
| 실제 NATS 통합 테스트 로그/결과 | 최종 TRX의 NatsIntegrationTests 2개와 NatsCameraIntegrationDeepAuditTests 1개 모두 Passed, 준비 지연 없이 10회 후속 ACK 성공 |
| 수정 소스 ZIP·파일 수 | `HeatingCameraSystem_modified_sources_1f292ec.zip`, 161개 파일, 478,468바이트 |
| ZIP SHA-256 | `aee61f7161e5bb5685f698b564bb8c04d15673e7b1373afb555c9c6b8845f9df` |
| ZIP 비교 기준 | `62bf11199ad825a39cb6f1c07b670618956f5df2` 이후 변경한 소스·프로젝트·XAML·리소스·테스트의 상대 경로 유지. 문서·실행 파일·임시 로그 제외 |
| ZIP 내용 검증 | 161개 항목 모두 FoxMainPC 소스 커밋 및 GitHub 원격 tree의 blob SHA-1과 일치. 전달 ZIP SHA-256·CRC 검증 통과. 삭제 파일 없음 |
| 물리 장비 검증 기록 | 미실행. 운영자 시험 후 기록 |

이 보고서는 확인된 실패 경로와 수정·회귀 검증을 연결한 기록이다. 문서 기록 후에도 검증한 소스 커밋은 위의 `1f292ec`로 식별한다. 실제 장비 검증 결과를 추후 추가할 때에는 카메라·펌웨어 버전과 동일 제품 버전 여부를 함께 기록한다.
