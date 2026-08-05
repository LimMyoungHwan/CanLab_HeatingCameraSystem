# 09-LiteDB(data.db)-관리 — 검토 종합 (Round 1)

## 판정

- Codex: 수정 필요도 **상**. 복원·안전한 초기화·백업 검증을 중점 지적했으나, 다수 항목은 현재 본문에 이미 반영됨.
- Claude: 수정 필요도 **중**. 복원과 실행 중 조작 경고는 타당하나, `data.db-log` 동시 처리 주장은 현재 코드에서 확인되지 않음.
- agy: 수정 필요도 **중**. 백업 폴더 생성과 `hardware.json` 구분은 타당하나, 제안문의 레시피 경로는 코드와 충돌함.
- Kimi: 수정 필요도 **상**. 절차 구조화와 백업 결과 확인은 타당하나, 레시피 기본 경로가 없다는 지적은 현재 코드·본문과 불일치함.
- 검토는 **Master 완전 종료, 복원 절차, 백업 결과 확인, 초기화 영향 명시**에 수렴함. **레시피 경로와 `data.db-log` 처리**에서는 일부 지적이 코드와 충돌함.

## 합의·다수 수정 (코드 확인됨)

- [백업·복원·초기화] Master가 실행되는 동안 `AppServices.Db`가 열린 상태로 유지됨 -> 모든 파일 작업 전에 Master와 해당 프로세스의 완전 종료를 명시한다. 현재 본문에 반영됨. (Codex, Claude, agy, Kimi)
- [백업 명령] 대상 폴더가 없으면 복사가 실패함 -> `New-Item -ItemType Directory -Force`를 먼저 실행하고 파일명에 초 단위 시각을 사용한다. 현재 본문에 반영됨. (Codex, agy)
- [백업 확인] 복사 명령만으로 성공 여부를 판단하기 어려움 -> 백업 파일의 생성 여부·크기·수정 시각 확인 단계를 추가한다. (Codex, Kimi)
- [복원] 현재 DB 보관 후 백업본을 `data.db`로 복사하고 재기동하여 데이터를 확인하도록 한다. 현재 본문에 반영됨. (Codex, Claude, agy, Kimi)
- [초기화] 즉시 삭제보다 기존 파일 이름 변경이 복구 가능성을 높임 -> 이름 변경 후 새 DB 생성과 화면 데이터를 확인하도록 한다. 현재 본문에 반영됨. (Codex, Kimi)
- [저장 범위] `data.db`에는 `capture_history`, `chamber_history`, `alarm_history`, `dashboard_layout`, `camera_serial_settings`, `CameraDevice` 및 마이그레이션 정보가 저장됨 -> 촬영·챔버·알람 이력, 대시보드 레이아웃, 카메라 시리얼 설정·디바이스 정보가 초기화된다는 현재 설명을 유지한다. (Codex, Claude, agy, Kimi)
- [레시피] `FileRecipeRepository`가 `%LOCALAPPDATA%\HeatingCameraSystem\recipe\*.json`에 저장함 -> DB와 레시피를 분리해 설명하고 `recipe` 폴더를 별도 백업한다. 현재 본문에 반영됨. 레시피 복원 절차도 별도로 한 줄 추가한다. (Codex, agy, Kimi)
- [경로 표기] 실제 기본 경로는 사용자별 `LocalApplicationData\HeatingCameraSystem`임 -> 탐색기의 `%LOCALAPPDATA%`와 PowerShell의 `$env:LOCALAPPDATA` 구분을 유지한다. (Codex, Kimi)
- [설정 파일] `hardware.json`에는 PLC·NATS·시리얼·흑체·레시피 엔진 설정이 저장됨 -> 전체 시스템 백업 범위를 설명할 때 DB와 구분하여 별도 백업 대상으로 안내한다. (agy)

## AI 지적이 부정확 (코드검증)

- agy 제안문의 레시피 경로 `<Master 설치 폴더>\recipes\*.json`은 틀림. 실제 경로는 `%LOCALAPPDATA%\HeatingCameraSystem\recipe\*.json`임.
- Kimi의 “recipe 폴더 기본 경로가 없음” 및 Codex의 “정확한 경로 확인 필요” 지적은 현재 코드 기준으로 부정확함. `AppServices`가 LocalApplicationData 경로를 `FileRecipeRepository`에 전달하고 저장소가 그 아래 `recipe` 폴더를 생성함.
- Claude의 `data.db-log`를 반드시 함께 백업·삭제해야 한다는 주장은 현재 애플리케이션 코드에서 확인되지 않음. 코드는 `data.db`만 명시적으로 열고 마이그레이션 백업도 `data.db`만 복사하므로, 검증되지 않은 로그 파일 절차를 본문에 추가하지 않는다.
- 레시피를 `data.db` 저장 데이터로 취급하면 안 됨. 현재 레시피는 JSON 파일 저장 방식이며, LiteDB의 기존 레시피 컬렉션은 최초 1회 파일로 마이그레이션하는 용도로만 사용됨.
- “다른 프로그램 버전의 DB는 호환되지 않을 수 있다”는 경고는 현재 코드에서 구체적인 비호환 조건이 확인되지 않으므로 단정하지 않는다.

## 보류 (설비 안전/도메인 확인 필요)

- 정기 백업 주기와 보관 기간은 현장 운영·감사 정책 확인 후 결정한다.
- `hardware.json` 복원은 PLC 주소와 통신 설정을 변경할 수 있으므로, 실제 설비 구성과 일치하는지 확인된 절차 없이 자동 복원하도록 안내하지 않는다.
- 자동 백업이나 Windows 작업 스케줄러 적용은 Master 종료 조건과 현장 가동 시간을 확정한 뒤 별도 운영 절차로 검토한다.

## 근거 파일

- `09-LiteDB(data.db)-관리_codex_R1_수정내용.md`
- `09-LiteDB(data.db)-관리_claude_R1_수정내용.md`
- `09-LiteDB(data.db)-관리_agy_R1_수정내용.md`
- `09-LiteDB(data.db)-관리_kimi_R1_수정내용.md`