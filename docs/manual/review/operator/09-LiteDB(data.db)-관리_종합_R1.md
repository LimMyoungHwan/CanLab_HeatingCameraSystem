# 09-LiteDB(data.db)-관리 — 검토 종합 (Round 1)

## 판정

수정 필요도: **상**. 세 검토자가 공통 지적한 복원 절차, 완전 종료 확인, 초기화 영향 범위가 누락되어 있다. 또한 레시피의 실제 저장 경로를 바로잡아야 한다.

## 합의·다수 수정 (코드 확인됨)

- DB 위치는 `%LOCALAPPDATA%\HeatingCameraSystem\data.db`가 맞다.
- 백업·복원·초기화 전 Master를 완전히 종료하고 프로세스 종료까지 확인하도록 명시한다.
- `D:\backup`이 없으면 백업 명령이 실패하므로 폴더 생성 명령을 추가하고 파일명에는 초(`yyyyMMdd_HHmmss`)까지 넣는다.
- 복원 절차를 추가한다: Master 종료 → 현재 DB 별도 보관 → 백업본을 `data.db`로 복사 → 재기동 및 데이터 확인.
- 초기화는 즉시 삭제보다 기존 DB의 이름을 변경해 보관한 뒤 재기동하는 절차가 안전하다. `LiteDatabase` 생성 시 새 `data.db`가 만들어진다.
- `data.db`에는 촬영·챔버·알람 이력, 대시보드 레이아웃, 카메라 시리얼 설정 및 디바이스 정보가 저장된다. 초기화 시 이 항목들이 사라짐을 경고한다.
- 레시피는 현재 DB가 아니라 `%LOCALAPPDATA%\HeatingCameraSystem\recipe\*.json`에 저장된다. `recipe` 폴더를 별도로 백업해야 한다.
- 탐색기에서는 `%LOCALAPPDATA%`, PowerShell에서는 `$env:LOCALAPPDATA`를 사용하며 사용자 계정별 경로임을 설명한다.

## AI 지적이 부정확 (코드검증)

- agy의 `<Master 설치 폴더>\recipes\*.json` 표기는 틀렸다. 실제 경로는 `%LOCALAPPDATA%\HeatingCameraSystem\recipe\*.json`이며 폴더명도 `recipes`가 아닌 `recipe`이다.
- Claude의 `data.db-log`를 DB와 함께 삭제·백업하라는 제안은 애플리케이션 코드로 확인되지 않는다. Master의 정상 종료를 전제로 본문에는 넣지 않는다.
- 레시피가 `data.db` 초기화의 영향을 받는다는 취지의 설명은 부정확하다. LiteDB의 기존 `recipes` 컬렉션은 최초 파일 마이그레이션용일 뿐, 현재 저장소는 JSON 파일이다.

## 보류 (설비 안전/도메인 확인 필요)

- 정기 백업 주기와 보관 기간은 현장 운영 정책 확인 후 결정한다.
- `hardware.json` 동시 백업 여부와 복원 범위는 PLC·시리얼·NATS 설정 변경 절차 및 현장 승인 정책을 확인한 뒤 안내한다.
- 다른 프로그램 버전의 DB 복원 호환성은 명시된 마이그레이션 정책이 없어 단정하지 않는다.

## 근거 파일

- `HeatingCameraSystem.Master/Services/AppServices.cs`
- `HeatingCameraSystem.Master/Services/FileRecipeRepository.cs`
- `HeatingCameraSystem.Master/Services/MigrationService.cs`
- `HeatingCameraSystem.Master/Services/LiteDbCaptureHistoryRepository.cs`
- `HeatingCameraSystem.Master/Services/LiteDbChamberHistoryRepository.cs`
- `HeatingCameraSystem.Master/Services/LiteDbAlarmHistoryRepository.cs`
- `HeatingCameraSystem.Master/Services/LiteDbDashboardLayoutRepository.cs`
- `HeatingCameraSystem.Master/Services/LiteDbCameraSerialSettingsRepository.cs`
- `HeatingCameraSystem.Master/Services/LiteDbCameraDeviceRepository.cs`