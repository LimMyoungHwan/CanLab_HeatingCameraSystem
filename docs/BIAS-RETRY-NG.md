# BIAS 재시도와 NG 중단

작성 기준: 2026-10-26. 이 문서는 수정한 소스의 동작과 장비 검증 항목을 설명한다. 빌드·자동 테스트·실장비 테스트 결과는 아래 반영 상태에 별도로 기록한다.

## 성공 조건과 재시도

레시피의 BIAS 단계는 **최초 1회와 추가 재시도 3회, 최대 4회** 실행한다. 성공한 시점에 재시도를 끝낸다. AgentUI가 재시도를 담당하며, Master는 한 번의 BIAS 요청에 대한 최종 결과를 기다린다. Master와 AgentUI가 각각 4번씩 실행하는 구조가 아니다.

한 번의 시도는 다음 순서로 진행한다.

1. 이번 시도의 보정을 실행한다. 이전 성공 JSON을 무효화하고, 레지스터 적용 이후 새 프레임으로 목표 범위에 들어왔는지 확인한다.
2. 이번 보정으로 생성한 JSON의 필수 값과 형식을 검사한다.
3. Agent의 로컬 버퍼에 기록하고 내용을 읽어 확인한다.
4. 레시피의 최종 저장 위치로 전송한다. 임시 파일의 내용을 확인한 뒤 최종 `bias.json`으로 교체한다.
5. **최종 경로의 파일을 다시 읽어 이번 보정 JSON과 일치하는지 확인한 뒤** 성공 ACK를 보낸다.

보정, JSON 생성, 기록, 전송 또는 최종 읽기 확인 중 어느 단계든 실패하면 해당 시도를 실패로 기록한다. BIAS 단계의 다음 시도는 보정부터 다시 수행한다. 파일이 존재한다는 사실만으로 이전 결과를 이번 성공으로 인정하지 않는다. 따라서 히스토그램이 정상으로 보이더라도 필수 파일 저장 확인이 끝나지 않으면 레시피가 다음 단계로 진행하지 않는다.

최종 저장 경로는 기존 생산 폴더 규칙을 따른다.

```text
<레시피 SaveRootPath>\<센서번호_제품번호>\<조건폴더>\bias.json
```

제품번호가 없으면 센서번호만 사용한다. `bias.json`은 `cold` 등의 흑체 폴더 바로 위에 있는 조건 폴더에 저장한다. 실제 경로는 Master의 `BIAS OK` 로그와 AgentUI의 `path` 필드로 확인한다.

근거: [BiasOperationRunner](../HeatingCameraSystem.Protocols/Cameras/BiasOperationRunner.cs), [CameraPanelViewModel](../HeatingCameraSystem.AgentUI/ViewModels/CameraPanelViewModel.cs), [ProductionCaptureSink](../HeatingCameraSystem.Protocols/Cameras/ProductionCaptureSink.cs).

## NG와 후속 작업 차단

4번 모두 실패하면 AgentUI가 마지막 실패 원인과 시도 횟수를 포함한 실패 ACK를 보낸다. Master는 **NG를 표시하고 레시피를 중단**한다. 이후 스텝의 촬영·이동 명령은 실행하지 않는다. BIAS 대상이 없거나 요청을 보낼 수 없는 경우, 최종 응답이 제한 시간 안에 도착하지 않는 경우에도 NG로 중단한다.

Master가 성공으로 인정하려면 ACK에 성공 상태, 파일 저장 확인, 최종 파일 경로, 유효한 시도 횟수와 일치하는 Agent·명령 정보가 있어야 한다. 파일 증빙이 없는 구형 Agent의 단순 성공 응답은 NG가 된다. 요청 ID에 실행 식별자를 포함하므로 이전 레시피 실행의 늦은 응답이 이번 결과를 대신하지 않는다.

성공한 BIAS 요청 ID는 같은 카메라·조건의 후속 촬영에 전달한다. AgentUI는 촬영 전에 요청 ID와 저장 경로가 일치하는지 확인하고 최종 파일을 읽는다. 파일이 없어졌거나 내용이 바뀌었다면, **이미 성공한 요청의 JSON으로 파일 복구를 최초 1회와 추가 3회 시도**한다. 이 파일 복구 과정에서 보정을 다시 수행하지 않는다. 복구도 실패하면 촬영 전에 실패 결과를 반환하고 Master가 NG로 중단한다. 이 필수 확인은 `WaitForCaptureResult=false`여도 생략하지 않는다.

사용자가 중단하거나 응답 대기 시간이 초과되면 Master는 원래 요청 ID를 지정해 취소를 전송한다. 취소 이후 레시피를 자동으로 재개하지 않는다. AgentUI는 진행 중인 작업의 종료를 기다린 뒤 다음 작업을 허용한다. NG 이후 챔버 정리 동작은 기존 레시피의 `KeepChamberRunning` 설정을 따른다.

주요 알람 코드는 `CAM-006`(BIAS 실행 실패·최종 응답 실패)과 `CAM-007`(필수 파일 확인 또는 관련 촬영 결과 실패)이다. 실패 원인은 알람 메시지에 함께 기록한다.

근거: [RecipeEngine](../HeatingCameraSystem.Master/Services/RecipeEngine.cs), [CameraNatsConnector](../HeatingCameraSystem.Protocols/Cameras/CameraNatsConnector.cs), [AlarmCodes](../HeatingCameraSystem.Master/Services/AlarmCodes.cs).

## 제한 시간 설정

Master 설정 파일 `%LOCALAPPDATA%\HeatingCameraSystem\hardware.json`의 `RecipeEngine.BiasAttemptTimeoutSeconds`가 **보정부터 최종 파일 확인까지 한 시도에 적용하는 제한 시간**이다. 기본값은 120초다. 기존 설정 파일에서는 다른 항목을 유지하고 필요한 속성만 추가하거나 수정한다.

```json
{
  "RecipeEngine": {
    "BiasAttemptTimeoutSeconds": 120
  }
}
```

| 항목 | 적용값 |
| --- | --- |
| 최대 시도 횟수 | 최초 1회 + 재시도 3회 = 4회 |
| 시도당 기본 제한 | 120초 |
| 실패 후 다음 시도까지 간격 | 500ms |
| Master 최종 응답 대기 예산 | `4 × 시도 제한 + 3 × 0.5초 + 10초` |
| 기본 설정의 응답 대기 예산 | 491.5초, 즉 8분 11.5초 |
| 설정값 정규화 | 0 이하이면 기본 120초, 양수이면 1~3600초 범위로 제한 |

491.5초는 매번 기다리는 고정 시간이 아니라 기본 설정의 전체 응답 대기 예산이다. 앞선 시도가 성공하거나 실패를 즉시 반환하면 더 일찍 끝난다. 취소 요청 후 실제 장치 작업이 종료되기 전에는 같은 카메라의 새 작업을 겹쳐 실행하지 않는다.

근거: [BiasExecutionPolicy](../HeatingCameraSystem.Core/Models/BiasExecutionPolicy.cs), [HardwareSettings](../HeatingCameraSystem.Core/Config/HardwareSettings.cs), [AppServices](../HeatingCameraSystem.Master/Services/AppServices.cs).

## 로그 위치와 확인 항목

아래 경로는 소스의 기존 설정에서 확인한 위치다. `%LOCALAPPDATA%`는 각 프로그램을 실행하는 Windows 계정의 로컬 애플리케이션 데이터 폴더다.

| 대상 | 실제 위치 또는 화면 | 확인할 내용 |
| --- | --- | --- |
| AgentUI 상세 로그 | `%LOCALAPPDATA%\HeatingCameraSystem\AgentUI\logs\agentui-*.ndjson` | 보정 결과, 각 시도 시작·실패·성공·취소, 최종 NG, 파일 확인·복구 |
| Master 알람 | 실시간 알람 목록과 알람 이력 화면 | BIAS 요청 시작, 파일 확인 완료, 최종 NG와 중단한 스텝 |
| Master 알람 이력 저장소 | `%LOCALAPPDATA%\HeatingCameraSystem\data.db` | 시간, 심각도, 코드, 발생 위치, 메시지 |
| Agent 기본 생산 버퍼 | `%LOCALAPPDATA%\HeatingCameraSystem\AgentUI\Captures\production` | 최종 전송 전의 생산 파일과 전송 실패 시 남은 파일 |

AgentUI 로그는 일 단위로 나뉘며 최대 14개 파일을 보관한다. AgentUI의 `StoragePath`를 별도로 지정했다면 생산 버퍼는 `<StoragePath>\production`이다. 최종 `bias.json`은 위 버퍼가 아닌 레시피에 지정된 최종 저장 경로에서 확인한다. Master 알람 이력 기록은 기존 비동기 최선 노력 방식이므로, 저장소 장애 시 이력 저장까지 보장하는 구조는 아니다.

| 로그 구간 | 주요 필드와 표식 |
| --- | --- |
| Master BIAS 시작 | 요청 ID, Agent, 명령, 목표값, 최대 시도 횟수, 시도 제한 시간, 저장 루트 |
| Master BIAS 성공 | 요청 ID, Agent, 성공 시도 번호/4, 최종 파일 경로 |
| Agent 시도 | `[BIAS] START`, `FAIL`, `OK`, `NG`, `CANCEL`; `agent`, `request`, `op`, `target`, `attempt` 또는 `attempts` |
| Agent 시간·원인 | 시작의 `timeoutSeconds`, 성공·실패의 `elapsedMs`, 실패의 `reason`; 제한 시간 초과 시 보정/저장 단계와 제한 시간 |
| Agent 보정 검증 | 모드, 목표값, 최종 측정값, 적용한 `GSK_LSB`, 목표 오차 |
| 후속 촬영 파일 확인 | `[BIAS FILE] VERIFIED` 또는 `REWRITE`; `agent`, `step`, `biasRequest`, `path`, 복구 이유 |

동일 요청 ID로 Master와 AgentUI 로그를 연결해 확인한다. AgentUI의 `[BIAS] FAIL`은 Warning, 최종 `[BIAS] NG`는 Error 수준으로 남긴다. 각 시도 실패 후 다음 `START`가 이어지는지와 최종 `OK` 또는 `NG`를 함께 확인하면 시도 횟수와 종료 원인을 구분할 수 있다.

근거: [AgentUiLog](../HeatingCameraSystem.AgentUI/Services/AgentUiLog.cs), [AgentUiConfig](../HeatingCameraSystem.AgentUI/Services/AgentUiConfig.cs), [AgentUI App](../HeatingCameraSystem.AgentUI/App.xaml.cs), [AlarmSink](../HeatingCameraSystem.Master/Services/AlarmSink.cs), [AppServices](../HeatingCameraSystem.Master/Services/AppServices.cs).

## 배포와 최소 장비 검증 시나리오

**Master와 AgentUI를 같은 수정 버전으로 함께 반영해야 한다.** 공유 Core·Protocols 변경도 포함해 각각 빌드한다. 이번 변경은 파일 저장 증빙, 필수 BIAS 요청 ID, 취소 요청을 주고받는 계약을 추가하므로 한쪽만 교체한 구성을 정상 운영 구성으로 사용하지 않는다.

장비 검증은 다음 5가지 시나리오를 기준으로 수행한다. 각 항목에서 Master 알람, 해당 AgentUI 로그, 최종 저장 폴더를 함께 확인한다.

| 번호 | 시나리오 | 기대 결과 |
| --- | --- | --- |
| 1 | 정상 카메라와 쓰기 가능한 최종 저장 경로로 BIAS 후 촬영·이동 레시피 실행 | 첫 시도에서 최종 `bias.json` 내용 확인 후 `BIAS OK`가 발생한다. ACK 시점에 최종 파일이 존재하고 이번 레지스터 값이 저장되어 있다. 후속 촬영·이동이 정상 진행한다. |
| 2 | 초기 시도에서 일시적 보정 실패를 만들고 재시도 중 원인 해소 | 실패 시도와 다음 시도가 같은 요청 ID로 기록된다. 성공 시도에서 즉시 종료하고 이후 스텝으로 진행한다. 가능하면 4번째 시도 성공도 확인한다. 전체 BIAS 시도는 4회를 넘지 않는다. |
| 3 | 프레임 수신 실패 또는 목표 미달 상태를 계속 유지 | 1/4~4/4가 모두 실패한 뒤 NG가 표시된다. BIAS 이후의 촬영·이동 명령이 실행되지 않고 마지막 보정 실패 원인이 남는다. |
| 4 | 보정은 가능하지만 최종 저장 위치의 쓰기·전송·읽기 확인이 계속 실패하도록 구성 | 저장 실패를 포함해 최대 4회 시도한 뒤 NG로 중단한다. 히스토그램이 정상이어도 진행하지 않는다. 같은 폴더에 이전 `bias.json`이 있어도 이번 저장 성공으로 인정하지 않는다. |
| 5 | BIAS 성공 후 후속 촬영 전에 최종 `bias.json`을 삭제하거나 다른 내용으로 변경 | 촬영 전에 파일 재검증과 복구가 실행된다. 복구 성공 시 이번 요청의 JSON으로 복원한 후 촬영한다. 저장 장애도 유지해 복구 4회를 실패시키면 촬영과 이후 이동 없이 NG로 중단한다. |

검증 기록에는 사용한 레시피, Master·AgentUI 버전, 실행 시각, 요청 ID, 실제 시도 횟수, NG/성공 결과와 최종 파일 경로를 남긴다. 수동 중단을 확인할 때는 실행 중인 BIAS가 취소되고 후속 스텝이 시작되지 않는지도 함께 확인한다.

## 작성 시점의 검증 및 반영 상태

- 소스 수정과 회귀 테스트 코드는 준비했다. 로컬 작업 환경에 `dotnet`이 없어 **빌드와 자동 테스트를 실행하지 않았다**.
- **실장비 테스트는 실행하지 않았다.** 위 시나리오는 사용자가 장비에서 검증할 항목이다.
