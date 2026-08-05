# 09-LiteDB(data.db)-관리 — codex 검토 (Round 1)

## 종합 평가

- 수정 필요도: 상
- 핵심 요약: 데이터베이스의 위치와 기본 복사 명령은 제시되어 있으나, 백업 대상 폴더 준비, Master 완전 종료 확인, 복원 절차, 초기화로 삭제되는 데이터, 레시피 백업 경로가 빠져 있어 관리자가 안전하게 작업하기 어렵다.

## 수정 필요 항목

1. [백업 절차] 문제: “Master 종료 후”라는 조건만 있어 백그라운드에 프로세스가 남은 경우를 확인할 수 없다. 실행 중인 DB를 복사하면 일관성이 보장되지 않을 수 있다. -> 제안: 작업 관리자에서 `HeatingCameraSystem.Master` 프로세스가 완전히 종료되었는지 확인한 뒤 복사하도록 명시한다.
2. [백업 명령] 문제: `D:\backup` 폴더가 없으면 `Copy-Item` 명령이 실패한다. 또한 백업 파일명이 분 단위라 같은 분에 재실행하면 덮어쓰기 확인이 발생할 수 있다. -> 제안: `New-Item -ItemType Directory -Force`로 폴더를 먼저 만들고 파일명에 초(`ss`)까지 포함한다.
3. [복원] 문제: 백업 방법만 있고 실제 복원 절차가 없다. -> 제안: Master 종료, 현재 DB 별도 보관, 선택한 백업 파일을 `data.db`로 복사, Master 재실행 및 데이터 확인 순서를 추가한다.
4. [초기화] 문제: `data.db` 삭제 시 어떤 데이터가 없어지는지와 복구 가능 조건이 명확하지 않다. -> 제안: 삭제 전에 반드시 백업하고, 촬영·챔버·알람 이력 등 DB 저장 데이터가 모두 초기화된다는 경고를 넣는다.
5. [초기화 안전성] 문제: 즉시 삭제하도록 안내해 오조작 시 복구하기 어렵다. -> 제안: 먼저 `data.db`를 날짜가 포함된 이름으로 변경한 뒤 Master를 실행하는 방식을 권장한다. 새 DB가 정상 생성된 것을 확인한 후 이전 파일을 보관하거나 삭제한다.
6. [레시피 데이터] 문제: 레시피가 별도 JSON 파일이라는 설명만 있고 실제 폴더 위치와 복원 방법이 없다. -> 제안: 운영 환경에서 확인된 정확한 레시피 폴더 경로를 명시하고, DB 백업만으로 레시피가 보존되지 않는다고 강조한다.
7. [경로 표기] 문제: `%LOCALAPPDATA%` 표기와 PowerShell의 `$env:LOCALAPPDATA` 표기가 함께 사용되지만 두 표현의 관계가 설명되지 않는다. -> 제안: 탐색기에서는 `%LOCALAPPDATA%`, PowerShell에서는 `$env:LOCALAPPDATA`를 사용한다고 짧게 설명한다.
8. [데이터 범위] 문제: `data.db`에 저장된 항목을 한 줄에 나열했으나 각 항목이 현재 버전에서 실제로 DB에 저장되는지 확인 근거가 제시되지 않는다. -> 제안: 구현과 대조해 저장 항목을 확정하고, 확정되지 않은 항목은 단정적으로 기재하지 않는다.

## 누락/추가 제안

- 백업과 복원 작업에는 동일한 Windows 사용자 계정을 사용하도록 안내한다. `%LOCALAPPDATA%` 경로는 사용자별로 다르다.
- 백업 후 파일이 생성되었는지 확인하고, 필요하면 파일 크기가 0바이트가 아닌지 점검하는 단계를 추가한다.
- 복원 전에 현재 `data.db`를 별도 이름으로 보관해 복원 실패 시 되돌릴 수 있게 한다.
- 복원한 DB가 현재 프로그램 버전과 호환되는지 확인하도록 안내한다.
- 정기 백업 주기와 보관 기간은 현장 운영 정책에 따라 별도로 정하도록 안내한다.
- 실제 레시피 저장 경로가 확인되기 전에는 임의의 경로를 문서에 기재하지 않는다.

## 이미지 자리 검토

- 이미지 자리 없음. 이 절차는 PowerShell 명령과 단계별 경고가 핵심이므로 필수 이미지는 아니다. 추가한다면 파일 탐색기 주소창에 `%LOCALAPPDATA%\HeatingCameraSystem`을 입력한 화면 한 장이 경로 확인에 도움이 된다.

## (선택) 수정 제안 전문

# 9. LiteDB(data.db) 관리

## 9.1 데이터베이스 위치

Master의 운영 데이터는 다음 파일에 저장됩니다.

```text
%LOCALAPPDATA%\HeatingCameraSystem\data.db
```

파일 탐색기에서는 위 경로를 그대로 입력합니다. PowerShell에서는 `%LOCALAPPDATA%` 대신 `$env:LOCALAPPDATA`를 사용합니다.

> **주의:** `%LOCALAPPDATA%`는 Windows 사용자별 경로입니다. Master를 실행하는 사용자 계정으로 백업과 복원을 수행하십시오.

`data.db`에는 촬영·챔버·알람 이력 등 Master의 운영 데이터가 저장됩니다. 레시피는 별도 JSON 파일로 관리되므로 `data.db`만 백업해서는 레시피가 보존되지 않습니다.

## 9.2 데이터베이스 백업

1. Master를 종료합니다.
2. 작업 관리자에서 `HeatingCameraSystem.Master` 프로세스가 남아 있지 않은지 확인합니다.
3. PowerShell을 실행합니다.
4. 다음 명령으로 백업 폴더를 만들고 DB를 복사합니다.

```powershell
New-Item -ItemType Directory -Path "D:\backup" -Force
Copy-Item "$env:LOCALAPPDATA\HeatingCameraSystem\data.db" "D:\backup\data_$(Get-Date -Format yyyyMMdd_HHmmss).db"
```

5. `D:\backup`에 백업 파일이 생성되었는지 확인합니다.
6. 레시피 폴더도 별도로 백업합니다. 정확한 레시피 저장 경로는 해당 시스템의 운영 설정을 확인하십시오.

> **주의:** Master가 실행 중일 때 `data.db`를 복사하거나 교체하지 마십시오.

## 9.3 데이터베이스 복원

1. Master를 종료합니다.
2. 작업 관리자에서 `HeatingCameraSystem.Master` 프로세스가 남아 있지 않은지 확인합니다.
3. 현재 DB를 되돌릴 수 있도록 별도 이름으로 보관합니다.

```powershell
Move-Item "$env:LOCALAPPDATA\HeatingCameraSystem\data.db" "$env:LOCALAPPDATA\HeatingCameraSystem\data_before_restore_$(Get-Date -Format yyyyMMdd_HHmmss).db"
```

4. 복원할 백업 파일을 `data.db`라는 이름으로 복사합니다.

```powershell
Copy-Item "D:\backup\data_YYYYMMDD_HHMMSS.db" "$env:LOCALAPPDATA\HeatingCameraSystem\data.db"
```

5. Master를 실행하고 필요한 이력과 설정이 정상적으로 표시되는지 확인합니다.
6. 문제가 발생하면 Master를 다시 종료한 뒤 복원 전 보관 파일을 `data.db`로 되돌립니다.

> **주의:** 다른 프로그램 버전에서 생성한 DB는 호환되지 않을 수 있습니다. 복원 후 반드시 주요 화면과 이력을 확인하십시오.

## 9.4 데이터베이스 초기화

데이터베이스를 초기화하면 DB에 저장된 운영 데이터가 사라집니다. 먼저 백업을 완료하십시오.

1. Master를 종료합니다.
2. 작업 관리자에서 `HeatingCameraSystem.Master` 프로세스가 남아 있지 않은지 확인합니다.
3. 기존 DB를 즉시 삭제하지 말고 별도 이름으로 변경합니다.

```powershell
Move-Item "$env:LOCALAPPDATA\HeatingCameraSystem\data.db" "$env:LOCALAPPDATA\HeatingCameraSystem\data_before_reset_$(Get-Date -Format yyyyMMdd_HHmmss).db"
```

4. Master를 실행합니다.
5. 새 `data.db`가 자동으로 생성되고 프로그램이 정상적으로 시작되는지 확인합니다.
6. 필요한 점검이 끝날 때까지 이전 DB 파일을 보관합니다.

> **경고:** 레시피는 별도 JSON 파일이므로 `data.db` 초기화만으로 삭제되지 않습니다. 레시피를 초기화하거나 복원할 때는 레시피 폴더를 별도로 관리하십시오.