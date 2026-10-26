# FoxMainPC Bias 수정 통합 및 검증 기록

## 적용 대상

- 확인일: 2026-10-26 (한국 시간)
- PC: FoxMainPC
- 작업 폴더: `E:\Source\Canlab\HeatingCameraSystem`
- 통합 브랜치: `integration/bias-foxmain-20261026`
- 기존 로컬 커밋: `748a72a6aeda97ca0e32b9caf5f88d29de0b9120` — 촬영 계획·템플릿·실행 영수증·결과 이력 개선
- 가져온 원격 커밋: `5a328b8502c2cf701c9269282fa3f9d861537793` — Bias 재시도·NG 중단·수동 복원, 한글 차트 글꼴 수정 포함
- 통합 커밋: `d8f1f176de04d47e0fa224abc3f2b987179b471b`

## 통합 과정에서 확인한 사항

두 커밋이 같은 실행·카메라·저장 코드를 변경해 발생한 7개 파일의 충돌을 해결했다. 기존 촬영 계획의 스텝 영수증, 생산 파일 번호와 회차별 offset, RAW 대표 경로를 유지하면서 필수 Bias 증빙 확인과 NG 중단을 연결했다.

촬영 요청 키에는 실행 ID를 포함하되 첫 StepId와 마지막 회차를 유지했다. 새 실행의 늦은 응답 방지와 기존 이력·RAW 연결 기능이 함께 동작하도록 `RecipeCaptureRequestKey`에 오버로드를 추가했다.

통합 검증에서 탐색이 미수렴하면 최선 LSB 재적용 전에 예외를 던지는 회귀를 확인했다. 최선값을 재적용하고 새 프레임 5장으로 최종 측정한 뒤 성공 여부를 판정하도록 보완했다. 최종 값이 범위 밖이면 NG이며 적용 결과와 JSON을 만들지 않는다. 실패 로그에는 탐색·최종 측정값과 범위 밖 오차를 남긴다.

기존 테스트 중 Bias 파일을 생략하고 계속 진행하거나 RAW 전송 전에 성공 결과를 받는 동작을 기대한 항목은 새 요구사항에 맞췄다. 실패 시 촬영 미실행·4회 확인·이전 파일 보존, 성공 결과 시점의 실제 RAW 바이트와 Bias 파일을 검증한다. 좌표 이동 성공 mock은 상태 조회에도 실제 요청 좌표를 반영하도록 수정했으며 도착 확인과 제한 시간을 완화하지 않았다.

## 최종 검증 결과

| 항목 | 결과 |
| --- | --- |
| 빌드 환경 | Windows, .NET SDK 10.0.401, 솔루션의 기존 .NET 8 대상 |
| 전체 Debug 빌드 | 성공, 오류 0개, 경고 0개 |
| 최초 실패 항목 집중 재검증 | 13개 통과 |
| 최종 전체 자동 테스트 | 803개 통과, 실패 0개, 건너뜀 0개 |
| 최종 전체 테스트 표시 시간 | 2분 30초 |
| 실제 챔버·카메라 운전 | 실행하지 않음 |

최종 전체 테스트는 아래 명령으로 실행했다.

```powershell
dotnet test HeatingCameraSystem.slnx --no-build --logger "trx;LogFileName=bias-integration-full-final.trx" --results-directory tmp/foxmain-bias-integration-20261026 --verbosity minimal
```

로컬 검증 파일:

- `tmp/foxmain-bias-integration-20261026/bias-integration-focused.trx`
- `tmp/foxmain-bias-integration-20261026/bias-integration-full-final.trx`

## 기존 작업 보존

작업 전 수정돼 있던 아래 4개 파일은 별도 보존 후 복원했다. 이 수정은 통합 커밋과 분리해 기존 미커밋 상태로 유지한다. 위 테스트는 이 기존 수정도 포함한 FoxMainPC 작업 트리에서 실행했다.

- `.gitignore`
- `HeatingCameraSystem.Master/ViewModels/RecipeResultViewModel.cs`
- `HeatingCameraSystem.Master/Views/RecipeResultView.xaml.cs`
- `HeatingCameraSystem.Tests/RecipeResultRenderingTests.cs`

복원 결과를 보존본과 비교했으며, 결과 View에는 가져온 한글 차트 글꼴 설정 4줄만 추가됐다. 기존 미추적 폴더도 유지했다. 보존본은 stash `4b3ceb71d976be6bed4639e171f2e6dbef8272e1`에 남아 있다.

## 운용 안내

Master와 AgentUI는 공유 Core·Protocols를 포함해 같은 버전으로 사용한다. 수동 복원 절차는 [MANUAL-BIAS-RESTORE.md](MANUAL-BIAS-RESTORE.md), 재시도·NG·파일 검증과 로그는 [BIAS-RETRY-NG.md](BIAS-RETRY-NG.md)를 따른다. 실장비의 온도 이동·Bias 적용·최종 저장 경로는 현장에서 별도로 확인한다.
