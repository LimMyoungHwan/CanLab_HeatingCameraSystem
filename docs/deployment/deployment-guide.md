# 배포 가이드 (Deployment Guide)

실제 현장 배포 절차. `publish.ps1`로 배포 폴더를 만든 뒤, PC별로 폴더를 복사하고 설정 파일을 편집한다.

## 1. 시스템 구성

```
                         ┌─────────────────┐
                         │   NATS 서버     │  nats://<host>:4222
                         │ (Master PC 또는 │
                         │  전용 서버)     │
                         └────────┬────────┘
                    ┌─────────────┼─────────────┐
          ┌─────────┴────────┐         ┌────────┴─────────┐
          │   Master PC      │         │   Camera PC #N   │
          │  master_bin\     │         │  agent_bin\ 또는 │
          │  Master.exe      │         │  agent_console_bin\
          │  + PLC(XGT FEnet)│         │  + USB 카메라    │
          │  + 흑체(SR-800R) │         │  + 시리얼 셔터   │
          │  + LiteDB        │         │                  │
          └──────────────────┘         └──────────────────┘
```

- **NATS 서버**: 중앙 메시지 버스. Master ↔ Agent 통신. Master PC 또는 전용 서버에서 1개 실행.
- **Master PC**: 운영자 콘솔(`master_bin\Master.exe`). NATS + PLC + 흑체 + LiteDB 연결.
- **Camera PC**: 카메라당 1대. AgentUI(운영자 UI, 라이브뷰) **또는** Agent(헤드리스 콘솔) 중 택1.

## 2. 배포 파일 생성

레포 루트에서:

```powershell
.\publish.ps1                 # framework-dependent — 각 PC에 .NET 8 Desktop Runtime 설치 필요
.\publish.ps1 -SelfContained  # 포터블 — 런타임 번들, .NET 설치 불필요 (용량 큼)
```

생성물 (각 폴더 통째로 복사해서 배포):

| 폴더 | 대상 PC | 실행파일 |
|---|---|---|
| `master_bin\` | Master PC | `HeatingCameraSystem.Master.exe` |
| `agent_bin\` | Camera PC (UI) | `HeatingCameraSystem.AgentUI.exe` |
| `agent_console_bin\` | Camera PC (헤드리스) | `HeatingCameraSystem.Agent.exe` |

> 현장 PC에 .NET 미설치가 우려되면 `-SelfContained` 권장. 설치돼 있으면 기본(framework-dependent)이 용량 작음.

## 3. NATS 서버 실행 (먼저)

Master PC 또는 전용 서버에서:

```powershell
# Docker (권장)
docker compose -f docs/deployment/docker-compose.yml up -d
# 또는 단일 실행파일
.\nats-server.exe
```

접속 URL(`nats://<서버IP>:4222`)을 Master/Agent 설정에 넣는다.

## 4. Master PC 배포

1. `master_bin\` 폴더를 Master PC로 복사.
2. `HeatingCameraSystem.Master.exe` 최초 실행 → `%LOCALAPPDATA%\HeatingCameraSystem\hardware.json` 자동 생성.
3. `hardware.json` 편집:
   - `SimulationMode`: `false` (실 하드웨어)
   - `Nats.Url`: `nats://<서버IP>:4222`
   - `Plc.IpAddress` / `Plc.Port`: 실 PLC(XGT FEnet, 기본 2004)
   - PLC D/M/P 주소, `BlackBody`(SR-800R) 포트 등 현장값
4. Master 재시작.

## 5. Camera PC 배포 (카메라당)

### 5a. AgentUI (운영자 UI 버전, 권장)

1. `agent_bin\` 폴더를 Camera PC로 복사.
2. `HeatingCameraSystem.AgentUI.exe` 실행 → `%LOCALAPPDATA%\HeatingCameraSystem\AgentUI\agentui.json` 자동 생성.
3. Settings 탭에서:
   - `Simulation mode` 체크 해제
   - `NATS URL`: `nats://<서버IP>:4222`
   - 카메라 행마다 `Device Name`(예: `CLTC_T_VGA_G2_S_r150`) 입력 → **`COM 자동 감지`** → COM 자동 매칭 → **Save**
4. 재시작.
5. **자동 시작(권장)**: 관리자 PowerShell에서
   `./docs/deployment/install-agentui-task.ps1 -InstallRoot C:\HeatingCameraSystem`
   → 로그온 시 AgentUI 자동 실행(실패 시 3회 재시작) 예약작업 등록. 무인 운영은 **자동 로그인** 필요
   (Sysinternals Autologon 권장 — 스크립트 `.DESCRIPTION` 참고). 창 없이 서빙만 하려면 `-Headless`.

### 5b. Agent (헤드리스 콘솔 버전 — 진단/폴백용)

> AgentUI(5a)가 기본 배포 경로다. 콘솔 Agent 는 은퇴하지 않고 **단일 카메라 진단·폴백**용으로 유지된다
> (Manager 자동 감독 경로에서는 더 이상 spawn 되지 않음 — S7).

1. `agent_console_bin\` 폴더를 Camera PC로 복사.
2. 실행: `HeatingCameraSystem.Agent.exe` (인수로 오버라이드 가능: `HeatingCameraSystem.Agent.exe Agent_Bay1 nats://<서버IP>:4222`).
3. exe 폴더의 `agent.json` 편집(AgentId, CameraIndex, NATS URL) 후 재시작.

## 6. 설정 파일 위치 요약

| 파일 | 위치 | 앱 |
|---|---|---|
| `hardware.json` | `%LOCALAPPDATA%\HeatingCameraSystem\` | Master |
| `data.db` (LiteDB) | `%LOCALAPPDATA%\HeatingCameraSystem\` | Master |
| `agentui.json` | `%LOCALAPPDATA%\HeatingCameraSystem\AgentUI\` | AgentUI |
| `agent.json` | `<Agent exe 폴더>\` | Agent 콘솔 |
| 캡처 이미지 | AgentUI/Agent exe 폴더 하위 | Agent 측 |

> 설정 파일은 배포 폴더에 없다 — 최초 실행 시 자동 생성되므로 각 PC에서 편집한다.

## 7. 업데이트 / 롤백

- **업데이트**: `publish.ps1` 재실행 → 새 폴더를 배포 폴더에 덮어쓰기(설정 파일은 `%LOCALAPPDATA%`에 있어 유지됨).
- **롤백**: 이전 배포 폴더 백업본으로 교체.

## 8. 사전 요구사항

- Windows 10/11 (x64).
- framework-dependent 배포 시: 각 PC에 **.NET 8 Desktop Runtime** 설치. (`-SelfContained` 사용 시 불필요.)
- Camera PC: USB 열화상 카메라 + 시리얼(가상 COM) 드라이버.
- Master PC: PLC(XGT FEnet TCP) 네트워크 도달, 흑체(SR-800R) 시리얼.
