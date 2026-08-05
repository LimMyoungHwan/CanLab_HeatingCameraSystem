# 9. LiteDB(data.db) 관리

- 위치: %LOCALAPPDATA%\HeatingCameraSystem\data.db (촬영/챔버/알람 이력, 대시보드 레이아웃, 카메라 시리얼/디바이스)
- 백업(Master 종료 후): data.db 복사
- 초기화: Master 종료 후 data.db 삭제 → 다음 기동 시 빈 DB 재생성(백업 먼저!)
- 레시피는 recipe 폴더 JSON으로 별도 저장 → 폴더 백업/복사로 이전

```
Copy-Item "$env:LOCALAPPDATA\HeatingCameraSystem\data.db" "D:\backup\data_$(Get-Date -Format yyyyMMdd_HHmm).db"
```
