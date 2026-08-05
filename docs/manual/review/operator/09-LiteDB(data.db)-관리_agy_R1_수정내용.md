# 09-LiteDB(data.db)-관리 — agy 검토 (Round 1)

## 종합 평가
- 수정 필요도: 중
- 한 줄 요약: 핵심 백업/초기화 명령은 작성되어 있으나, 관리자 매뉴얼로서 복구 절차, 파일 잠금 주의사항, PowerShell 백업 시 디렉토리 생성 조건 및 구체적인 절차 안내가 부족하여 체계적인 단원 구성 보완이 필요함.

## 수정 필요 항목
1. [개요 및 저장 데이터 범위] 문제: `data.db`에 저장되는 데이터와 외부 파일(`hardware.json`, `recipe/*.json`)로 분리 저장되는 데이터의 구분이 명확하지 않음. -> 제안: 표나 구분 목록을 제공하여 `data.db`와 기타 설정 파일의 저장 항목 역할을 명확히 기술.
2. [백업 스크립트] 문제: PowerShell 명령어 스크립트에서 백업 저장 디렉토리(`D:\backup`)가 미리 존재하지 않으면 `Copy-Item` 실행 시 오류가 발생함. -> 제안: 백업 폴더 자동 생성 코드(`New-Item -ItemType Directory -Force -Path "D:\backup"`)를 백업 스크립트에 포함하거나 사전 준비 단계 안내 추가.
3. [DB 초기화 파급효과] 문제: `data.db` 삭제 후 재기동 시 초기화되는 항목(이력 데이터 외 대시보드 레이아웃, 카메라 디바이스 설정 등)에 대한 경고가 부족함. -> 제안: 백업의 필수성과 초기화 후 재설정 필요 사항을 주의사항(Warning)으로 명시.

## 누락/추가 제안
- **복구(Restore) 절차 누락**: 백업해 둔 `.db` 파일로부터 복구하는 명확한 순서(Master 완전 종료 → 기존 `data.db` 대체/덮어쓰기 → Master 재기동) 추가 필요.
- **프로세스 종료 및 파일 잠금 주의사항**: Master PC 프로그램이 실행 중인 상태에서 DB 파일을 복사/삭제할 경우 LiteDB 파일 락(Lock) 또는 데이터 손상이 발생할 수 있으므로, 반드시 Master 프로그램을 먼저 종료해야 함을 강조.
- **`hardware.json` 동시 백업 권장**: `%LOCALAPPDATA%\HeatingCameraSystem\` 경로에는 PLC 및 하드웨어 설정이 담긴 `hardware.json`도 위치하므로, 백업 시 함께 복사하는 것을 권장하는 안내 추가.

## 이미지 자리 검토
- **[그림 1] 추가 필요 (부적절/누락)** — 탐색기 및 명령 프롬프트 환경에서 `%LOCALAPPDATA%\HeatingCameraSystem` 경로 탐색 과정 visual 자리가 없음.
  - *제안 사유*: 관리자가 `%LOCALAPPDATA%` 환경변수 경로를 Windows 탐색기 주소창에 입력하여 `data.db` 및 `hardware.json` 파일의 실제 위치를 확인할 수 있도록 `📷 [그림 1] %LOCALAPPDATA%\HeatingCameraSystem 폴더 내 DB 및 설정 파일 위치` 예시 화면 위치 추가 권장.

---

## (선택) 수정 제안 전문

```markdown
# 9. LiteDB(data.db) 관리

본 단원에서는 시스템 운영 중 발생한 이력 데이터 및 화면 설정을 저장하는 LiteDB(`data.db`) 파일의 위치, 백업, 복구 및 초기화 절차를 안내합니다.

---

## 9.1 저장 위치 및 데이터 범위

HeatingCameraSystem의 DB 및 데이터 파일은 저장 목적에 따라 다음과 같이 분리되어 저장됩니다.

| 구분 | 파일/폴더 위치 | 저장 데이터 항목 |
|---|---|---|
| **LiteDB Database** | `%LOCALAPPDATA%\HeatingCameraSystem\data.db` | 촬영 이력, 챔버 온습도 이력, 알람 발생 이력, 대시보드 레이아웃, 카메라 시리얼/디바이스 정보 |
| **하드웨어 설정** | `%LOCALAPPDATA%\HeatingCameraSystem\hardware.json` | PLC 통신 설정, 시리얼 셔터 포트, NATS 브로커 주소 |
| **레시피 파일** | `<Master 설치 폴더>\recipes\*.json` | 열처리 레시피 단계, 목표 온도 및 캡처 조건 |

> [!CAUTION]
> **Master 프로그램 종료 필수**
> `data.db` 백업, 복구, 삭제 작업 전에는 반드시 **HeatingCameraSystem Master 앱을 완전히 종료**해야 합니다. 프로그램이 실행 중일 경우 파일 잠금(Lock) 또는 데이터 손상이 발생할 수 있습니다.

---

## 9.2 LiteDB(data.db) 백업

유지보수 및 시스템 점검 전 DB를 안전하게 백업합니다.

### 백업 절차
1. HeatingCameraSystem Master 프로그램을 종료합니다.
2. PowerShell을 실행하고 아래 백업 명령을 수행합니다.

```powershell
# 백업 디렉토리 생성 (없는 경우)
New-Item -ItemType Directory -Force -Path "D:\backup"

# 타임스탬프를 포함한 data.db 파일 백업
Copy-Item "$env:LOCALAPPDATA\HeatingCameraSystem\data.db" "D:\backup\data_$(Get-Date -Format yyyyMMdd_HHmm).db"

# (권장) hardware.json 설정 파일 함께 백업
Copy-Item "$env:LOCALAPPDATA\HeatingCameraSystem\hardware.json" "D:\backup\hardware_$(Get-Date -Format yyyyMMdd_HHmm).json"
```

📷 [그림 1] %LOCALAPPDATA%\HeatingCameraSystem 폴더 및 백업 실행 화면

---

## 9.3 LiteDB(data.db) 복구

손상되거나 이전 시점으로 데이터베이스를 복원해야 하는 경우 아래 절차를 수행합니다.

### 복구 절차
1. Master 프로그램을 종료합니다.
2. 백업된 DB 파일(`data_YYYYMMDD_HHMM.db`)을 원본 경로로 복사하면서 파일명을 `data.db`로 변경합니다.
   ```powershell
   Copy-Item "D:\backup\data_20260805_1300.db" "$env:LOCALAPPDATA\HeatingCameraSystem\data.db" -Force
   ```
3. Master 프로그램을 재기동하여 이력 데이터 및 설정이 정상 복원되었는지 확인합니다.

---

## 9.4 LiteDB(data.db) 초기화

DB 파일 손상이나 이력 데이터 청소가 필요한 경우 초기화를 진행합니다.

> [!WARNING]
> DB 초기화 시 촬영/챔버/알람 이력뿐만 아니라 **대시보드 레이아웃 및 카메라 디바이스 설정도 함께 초기화**됩니다. 반드시 작업 전 백업을 수행하십시오.

### 초기화 절차
1. Master 프로그램을 종료합니다.
2. 기존 DB 파일을 삭제합니다.
   ```powershell
   Remove-Item "$env:LOCALAPPDATA\HeatingCameraSystem\data.db" -Force
   ```
3. Master 프로그램을 다시 실행합니다.
   - Master 프로그램 기동 시 `data.db` 파일이 없으면 초기 상태의 빈 데이터베이스가 자동으로 생성됩니다.
```
