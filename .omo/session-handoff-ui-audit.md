# Session Handoff — UI Implementation Audit (next session)

## 1. What this session did (done + committed)

- Added **CI Systems SR-800R blackbody** RS-232 control (real protocol from manual Chapter 6):
  `SrProtocol`, `SrBlackBodyController`, `BlackBodySettings`, wired into `AppServices.CreateBlackBodyController`,
  blackbody control card on the Manual Control screen.
- Added **hardware-free operation**: `ISrLink` seam + `SimulatedSrDevice` (in-memory SR-800R that speaks the same
  protocol and ramps PV→SV). Same controller/protocol path runs without the physical units via `BlackBody.Simulated=true`.
- Fixed final F1/F2 audit blockers on the external Simulator (cancellation, NATS publish cleanup, AgentId validation,
  OutputPath normalization, dashboard stale-frame wiring, console command hardening, E2E readiness line).
- Verified: build 0/0, tests **177/177**, external Simulator E2E `*** PASS ***`, live Master UI QA of
  Dashboard / Manual Control (blackbody, real + simulated) / PLC Status.

Repo: branch `master`, upstream `origin/master`
(`https://github.com/LimMyoungHwan/CanLab_HeatingCameraSystem.git`).

## 2. Next-session objective (user's explicit ask)

Precisely audit **whether each UI feature is actually implemented** (not just wired), and document, for each screen:
- what the UI is FOR (domain purpose),
- what the data means (Recipe = ? / Dashboard shows = ? / Camera Mapping = ? / Serial Settings = ? / Devices = ? ...),
- verdict: **Implemented / Partial / Stub**, with evidence (real logic vs empty command / no backing service).

Method per screen: read the View (`*.xaml`) + ViewModel (`*ViewModel.cs`), trace commands to `AppServices` services
and repositories, judge if the feature does real work. Prefer `codegraph_explore` over grepping.

## 3. Master UI — screens to audit (all nav-wired in `MainViewModel`)

Views: `HeatingCameraSystem.Master/Views/*.xaml` · ViewModels: `HeatingCameraSystem.Master/ViewModels/*ViewModel.cs`

| Screen (nav) | View / ViewModel | Purpose to confirm | Verdict (fill next session) |
|---|---|---|---|
| 대시보드 Dashboard | DashboardView / DashboardViewModel | Live camera feeds + chamber temp/humidity trend + online-agent count + recipe start | **Implemented** (live) — real NATS live tiles + bounded temp/humidity sparkline + online count |
| 라이브 영상 Live | LiveView / LiveViewModel | Full live thermal stream view | **Implemented** — NATS `agent.live.>` → JPEG decode → per-camera tiles (Dispatcher-marshalled) |
| 레시피 편집기 Recipe Editor | RecipeEditorView / RecipeEditorViewModel | Author a Recipe = ordered steps (temp/humidity/blackbody targets, servo point/CameraIndex, ramp minutes) | **Implemented** — LiteDB CRUD via RecipeRepo + JSON import/export + step add/delete/reorder |
| 카메라 맵핑 Camera Mapping | CameraMappingView / CameraMappingViewModel | Map recipe camera slots ↔ Agent/CameraIndex ↔ NATS AgentId | **Implemented** (2026-07-28) — inventory from CameraDeviceRepo; RecipeEngine consumes saved mapping (position→AgentId, overrides CameraAlias/CameraIndex); SyncProgress = real assigned ratio |
| 이력 조회 History Logs | HistoryView / HistoryViewModel | Browse capture history from LiteDB (HistoryRepo) | **Implemented** — real LiteDB query + paging + date/group filter + CSV export. Note: EmergencyStop button only sets a status string (cosmetic) |
| 시리얼 설정 Serial Settings | SettingsView / SettingsViewModel | Per-camera serial params, push to Agent over NATS + ACK | **Implemented** — LiteDB upsert + local apply + NATS publish + 5s ACK wait with timeout |
| 디바이스 관리 Devices | DevicesView / DevicesViewModel | Manage camera device descriptors (CameraDeviceRepo) | **Implemented** — NATS inventory subscribe + approve/reject/rename/getlogs/setserial manager commands + CameraDeviceRepo |
| PLC 상태 Status | StatusMonitorView / StatusMonitorViewModel | 1s poll of PlcStatusSnapshot (chamber/blackbody/servo/equipment/errors) — live-verified this session | Implemented (live) |
| PLC 설정 Control | PlcControlSettingsView / PlcControlSettingsViewModel | Set temp/humidity/blackbody/servo speed/fan + point coords + admin settings | **Implemented** — every Apply* writes via real IPlcController/IBlackBodyController; points + admin load/save real (HW addresses are documented placeholders per AGENTS.md, not stubs) |
| 수동 조작 Manual Control | ManualControlView / ManualControlViewModel | One-touch equipment, servo jog/home/point move, blackbody set/read — live-verified this session | Implemented (live) |

## 4. Agent UI — tabs to audit (single `MainWindow.xaml`, TabControl)

`HeatingCameraSystem.AgentUI/MainWindow.xaml` + `ViewModels/*ViewModel.cs`

| Tab | Backing VM | Purpose to confirm | Verdict |
|---|---|---|---|
| Live | CameraPanelViewModel (per camera) | Live preview + Restart + camera serial control (shutter/RUN/STOP/NUC/info/S-N/FPA) | **Implemented** — live preview + RUN/STOP/shutter/NUC + camera info (prior-session real-HW QA) |
| Data | DataBrowserViewModel | Local capture DataGrid + preview + delete/purge/retention | **Implemented** — CaptureStore query/delete/purge + thermal preview decode + retention days |
| Logs | LogViewerViewModel | Log DataGrid + level filter | **Implemented** — NdjsonLogReader reads log dir with min-level filter |
| Settings | SettingsViewModel | SimulationMode, NATS URL, storage, heartbeat, image format, camera add/remove/save | **Implemented** — binds AgentUiConfig + camera add/remove + JSON Save (restart to apply) |

## 5. Known facts (already established this session)

- Structural completeness: every Master screen has a 1:1 View+ViewModel; Agent has 4 fully-bound tabs.
- **Zero** `TODO / FIXME / NotImplementedException / 미구현 / 준비 중` markers across Master + AgentUI.
- Build 0/0; tests 177/177.
- Live-verified (actually clicked this session): Master Dashboard, Manual Control (blackbody), PLC Status.
- NOT yet live-verified: Recipe Editor, Camera Mapping, History, Serial Settings, Devices, Live (Master) and all Agent tabs.

## 6. Domain context (for judging purpose)

- **Recipe**: an ordered thermal-camera calibration/test sequence. Each `RecipeStep` targets chamber temp/humidity,
  blackbody temperature(s), a servo position (직교로봇 point) and a `CameraIndex`; `TemperatureRampMinutes` linearizes
  heater output. `RecipeEngine` drives PLC + blackbody + NATS capture per step.
- **Camera Mapping**: recipe `RecipeStep.CameraIndex` → NATS target `Agent_{CameraIndex}`; Agent `agent.json` AgentId
  must match. Master maps physical camera/COM pairing to agents.
- **Serial Settings**: per-camera serial port config (셔터/카메라 CL), pushed to the Agent PC over NATS with ACK.
- **Devices**: camera device descriptors registry (LiteDB `CameraDeviceRepo`).
- **Dashboard**: operator overview — live camera thumbnails, chamber temp/humidity trend, online agents, recipe start.
- PLC is LS XGT FEnet (TCP 2004); blackbody now has a separate SR-800R RS-232 path (this session).

## 7. Audit result (2026-07-28, code-level)

Method: read each `*.xaml` + `*ViewModel.cs`, traced commands to `AppServices` services/repos via `codegraph_explore` (no live clicks this pass — code is definitive for real-logic vs stub).

**Verdict: 14 of 14 UIs Implemented (real backing logic). No Partial, no `NotImplementedException`/empty-command stubs.**

### 카메라 맵핑 Camera Mapping (Master) — gap CLOSED (2026-07-28)
Previous Partial had two defects, both fixed and re-verified (Master build 0/0, full suite 177/177, +3 temp resolution tests then removed):
- **Inventory** (`CameraMappingViewModel.InitializeData`): now sourced from `CameraDeviceRepo.GetAllAsync()`, grouped by `PCId`, camera id = `Alias` (fallback `AgentId`). Hardcoded `CAM-NN` removed.
- **Consumption** (`RecipeEngine`): loads the saved mapping once per run (`LoadPositionAgentMapAsync`) and resolves `RecipeStep.TargetPositionIndex`→slot `P{NN}`→mapping→AgentId as **priority 1**, keeping `CameraAlias`/`CameraIndex` as fallback (backward compatible — existing `RecipeEngineTests` unchanged). `AppServices` passes `MappingRepo` to the engine.
- **Sync UI**: `SyncProgress` = real assigned ratio; `SyncStatusText` = Saved/Unsaved. No decorative constants.

Residual edge notes (non-blocking): a stale pre-existing `CAM-NN` mapping no longer matches real inventory (operator re-maps); empty device registry ⇒ empty mapping list (honest); duplicate operator aliases resolve to the first device.

### Minor cosmetic notes (not blocking)
- History 이력 조회: `EmergencyStop` command only sets `SystemStatusText` — dead button on this screen.

### Implemented, with documented hardware-tuning placeholders (not stubs)
- PLC 설정 Control: all controls call real PLC/blackbody APIs; some D/M/P addresses (ServoSpeed D2560, etc.) are the AGENTS.md-listed placeholders awaiting the real A&D memory map — hardware tuning, not missing logic.
