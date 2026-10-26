# Handoff: 흑체 꺼짐 안내 (commit 0927ab5)

## 배경
이전 세션 문제의 원인은 흑체(SR-800N) 전원 꺼짐. 꺼져 있으면 시리얼 timeout / UDP 무응답. 원격으로 켤 수 없고, 전원 꺼짐과 케이블 빠짐은 코드로 구분 불가.

## 구현 (A+B+C, 사용자 승인)
- A `PlcStatusService.PollBlackBodyAsync`: `DescribeBlackBodyFailure(i, ex)` — TimeoutException/SocketException이면 `BlackBody_NoResponse`("흑체 N 응답 없음 — 전원·케이블 확인"), 그 외는 `Dash_ReadFailed` 원문. 유닛별 faulted 에지에서 BB-001 Error 알람, 복구 시 Info(`BlackBody_Restored`).
- B `RecipeEngine.ExecuteRecipeAsync`: 흑체 스텝이 있으면 실행 전 유닛별 GetCurrentTemperatureAsync 1회, 실패 시 Warning(`Alarm_Msg_BlackBodyPreflightFailed`). 실행은 막지 않음.
- C `RecipeEngine.ExecuteBlackBodyStepAsync`: 안정화 대기 중 판독 실패 시 Error 알람 + InvalidOperationException으로 레시피 중단(예전: 경고 후 촬영 진행). 기존 catch가 "스텝 실패"(PLC 코드) 알람을 하나 더 올림.
- 테스트: `RecipeEngineTests.ExecuteRecipeAsync_BlackBodyOff_WarnsBeforeRunAndStopsBeforeCapture`. 491 passed.

## 다음 세션 확인할 것
- 실장비에서 흑체 전원 끈 상태로 Master 실행 → 대시보드 문구·알람 확인, 레시피 실행 전 경고, 안정화 대기에서 중단되는지.
- 남은 한계: WaitForStabilization=false 흑체 스텝은 SetTemperature가 무응답 전송이라 중단 안 됨(B 경고만). 필요하면 설정 직후 1회 판독 검증 추가.
- 미추적 파일(커밋 안 함): `.banyancode/`, `.serena/memories/handoff/`, `docs/장비 Check List_260921.xlsx`.
