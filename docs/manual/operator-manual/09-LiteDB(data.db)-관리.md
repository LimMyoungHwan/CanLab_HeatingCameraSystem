# 9. LiteDB(data.db) 관리

- 위치: `%LOCALAPPDATA%\HeatingCameraSystem\data.db` (사용자 계정별 경로)
- 저장 항목: 촬영·챔버·알람 이력, 대시보드 레이아웃, 카메라 시리얼 설정 및 디바이스 정보
- 탐색기에서는 `%LOCALAPPDATA%`, PowerShell에서는 `$env:LOCALAPPDATA`를 사용
- 백업·복원·초기화 전 Master를 완전히 종료하고 프로세스 종료까지 확인
- 레시피는 `%LOCALAPPDATA%\HeatingCameraSystem\recipe\*.json`으로 별도 저장되므로 `recipe` 폴더도 별도 백업

## 백업

```powershell
New-Item -ItemType Directory -Path "D:\backup" -Force
Copy-Item "$env:LOCALAPPDATA\HeatingCameraSystem\data.db" "D:\backup\data_$(Get-Date -Format yyyyMMdd_HHmmss).db"
Copy-Item "$env:LOCALAPPDATA\HeatingCameraSystem\recipe" "D:\backup\recipe_$(Get-Date -Format yyyyMMdd_HHmmss)" -Recurse
```

백업 후 대상 폴더에 `data_*.db`와 `recipe_*` 폴더가 생성됐는지, 파일 크기와 수정 시각이 정상인지 확인한다.

## 복원

1. Master를 완전히 종료하고 프로세스 종료까지 확인합니다.
2. 현재 DB를 별도 이름으로 보관합니다.
3. 백업본을 `data.db`로 복사합니다.
4. Master를 다시 실행하고 데이터를 확인합니다.

```powershell
Rename-Item "$env:LOCALAPPDATA\HeatingCameraSystem\data.db" "data_before_restore_$(Get-Date -Format yyyyMMdd_HHmmss).db"
Copy-Item "D:\backup\data_백업시각.db" "$env:LOCALAPPDATA\HeatingCameraSystem\data.db"
```

레시피를 복원하려면 Master 종료 상태에서 백업한 `recipe_*` 폴더의 `*.json`을 `%LOCALAPPDATA%\HeatingCameraSystem\recipe\`로 복사한다.

## 초기화

초기화하면 촬영·챔버·알람 이력, 대시보드 레이아웃, 카메라 시리얼 설정 및 디바이스 정보가 사라집니다. Master를 완전히 종료하고 프로세스 종료까지 확인한 후 기존 DB의 이름을 변경해 보관합니다. 다음 기동 시 빈 `data.db`가 자동 생성됩니다.

```powershell
Rename-Item "$env:LOCALAPPDATA\HeatingCameraSystem\data.db" "data_before_reset_$(Get-Date -Format yyyyMMdd_HHmmss).db"
```