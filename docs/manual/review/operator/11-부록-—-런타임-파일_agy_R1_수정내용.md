# 11-부록-—-런타임-파일 — agy 검토 (Round 1)

## 종합 평가
- 수정 필요도: 중
- 한 줄 요약: 런타임 파일 목록 표는 명확하게 요약되어 있으나, 대상 독자(설치·설정·유지보수 관리자) 관점에서 파일별 자동 생성 동작, 편집 후 재시작 여부, 백업/복구 절차 및 경로 탐색 방법에 대한 세부 안내가 누락되어 보강이 필요합니다.

## 수정 필요 항목
1. [11. 부록 — 런타임 파일 표 및 서술] 문제: 단순 경로 표만 존재하여 파일 손상/삭제 시 동작(자동 생성 여부) 및 편집 후 재시작 필요 여부 등 실무 유지보수 정보가 부족함 -> 제안: 파일별 자동 생성 여부, 편집/재시작 가이드, 백업 중요도를 표 및 하단 세부 설명으로 보강.
2. [경로 표기 및 탐색 가이드] 문제: `%LOCALAPPDATA%` 환경변수 표기만으로는 현장 관리자가 실제 디렉터리를 찾는 데 어려움을 겪을 수 있음 -> 제안: Windows 실행 창(`Win + R`)을 이용한 빠른 접근 명령(`%LOCALAPPDATA%\HeatingCameraSystem`)과 실제 대표 경로 예시 추가.

## 누락/추가 제안
- **자동 생성 및 초기화 동작 안내**: `hardware.json` 및 `agent.json` 등 주요 설정 파일은 삭제되거나 없을 경우 최초 실행 시 기본값으로 자동 생성된다는 시스템 동작 특성 명시.
- **백업 및 복구 관리 절차**: 데이터 손실 방지를 위해 `data.db` (이력 DB), `recipe\*.json` (레시피 데이터), `hardware.json` (하드웨어/PLC 설정) 파일의 백업 주기 및 복구 방법 추가.
- **용량 관리 정책 안내**: `Agent 로그` (7일 보관 NDJSON) 및 `캡처 이미지` (`StoragePath`) 저장 디렉터리의 디스크 용량 관리 및 주기적 점검 지침 명시.

## 이미지 자리 검토
-현재 챕터에 `📷 [그림 N]` 지정 자리가 없습니다.
- **제안**: [그림 11.1] 런타임 파일 디렉터리 구조 및 백업 대상 파일 안내도를 추가하여 관리자가 백업 대상 파일과 로그/이미지 저장소를 시각적으로 쉽게 구분할 수 있도록 개선 권장.

## (선택) 수정 제안 전문

```markdown
# 11. 부록 — 런타임 파일

본 부록에서는 HeatingCameraSystem의 동작에 필요한 주요 런타임 파일, 설정 파일, 데이터베이스 및 로그 파일의 저장 위치와 관리 방법을 안내합니다.

## 11.1 런타임 파일 목록

| 파일/디렉터리 | 저장 위치 | 자동 생성 | 편집 후 동작 | 주요 역할 및 비고 |
| --- | --- | :---: | :---: | --- |
| `hardware.json` | `%LOCALAPPDATA%\HeatingCameraSystem\` | O | 재시작 필요 | Master PC 하드웨어/PLC/통신 설정 파일 |
| `data.db` | `%LOCALAPPDATA%\HeatingCameraSystem\` | O | 런타임 반영 | LiteDB 기반 시스템 운용 이력 및 알람 데이터 |
| `recipe\*.json` | `%LOCALAPPDATA%\HeatingCameraSystem\recipe\` | X | 런타임 반영 | 가열/촬영 공정 레시피 설정 파일 |
| `agent.json` | `<Agent exe 폴더>\` | O | 재시작 필요 | Agent PC 설정 (AgentId, NATS URL, 저장 경로 등) |
| `캡처 이미지` | Agent `StoragePath` / Master `ImageCache` | O | 런타임 반영 | Agent 로컬 원본 이미지 및 Master 캐시 이미지 |
| `manager-settings/state.json` | `C:\HeatingCameraSystem\Manager\` | O | 재시작 필요 | AgentManager 관리 호스트 설정 및 상태 파일 |
| `Agent 로그` | `C:\HeatingCameraSystem\logs\{AgentId}\` | O | 런타임 반영 | Agent 일자별 로그 (NDJSON 포맷, 7일 자동 보관) |

> 📷 [그림 11.1] Master PC 및 Agent PC 런타임 디렉터리 구조 및 주요 백업 파일 구분도

---

## 11.2 디렉터리 접근 및 탐색 방법

1. **Master PC 설정 경로 접근 (`%LOCALAPPDATA%`)**
   - 키보드의 `Win + R` 키를 눌러 [실행] 창을 열고 `%LOCALAPPDATA%\HeatingCameraSystem\` 입력 후 [확인]을 클릭합니다.
   - 실제 절대 경로는 일반적으로 `C:\Users\<사용자계정>\AppData\Local\HeatingCameraSystem\`입니다.

2. **Agent PC 런타임 경로 접근**
   - Agent 실행 파일(`HeatingCameraSystem.Agent.exe`)이 위치한 폴더 내에 `agent.json` 및 `ImageStorage\` 디렉터리가 생성됩니다.

---

## 11.3 유지보수 및 백업 가이드

1. **설정 파일 자동 생성 및 초기화**
   - `hardware.json` 및 `agent.json` 파일이 존재하지 않는 상태에서 프로그램을 실행하면 표준 기본값으로 파일이 자동 생성됩니다.
   - 설정 파일 수정 후에는 반드시 해당 애플리케이션을 재시작해야 수정된 설정이 적용됩니다.

2. **권장 백업 대상**
   - **정기 백업 대상**: `hardware.json`, `recipe\*.json`, `data.db`
   - 시스템 재설치 또는 PC 교체 시 위 파일들을 복사하여 동일한 경로에 restored하면 기존 레시피 및 장비 설정을 복구할 수 있습니다.

3. **로그 및 디스크 용량 관리**
   - Agent 로그 파일은 `C:\HeatingCameraSystem\logs\{AgentId}\`에 NDJSON 형식으로 저장되며, 7일이 지난 로그는 자동으로 정리됩니다.
   - 캡처 이미지가 저장되는 `StoragePath` 디렉터리의 디스크 잔여 용량을 주기적으로 점검하시기 바랍니다.
```
