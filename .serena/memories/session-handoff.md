# Session Handoff (2026-07-23) — 슬레이브(카메라) PC 독립 GUI (AgentUI)

Full doc: `docs/handoff/agent-ui-slave-gui-handoff.md` (read first).

## Done + committed/pushed
S1-S6. Build 0/0, tests 97/97, **console Agent + Master UNCHANGED** (all new files).
- S1 f1e630e: AgentUI WPF project (net8.0-windows, CommunityToolkit.Mvvm)
- S2: `CameraRuntime` (per-cam 1 VideoCapture + 1 Y16 loop, atomic latest, capture-by-tee; `IThermalFrameSource`: real `CltcThermalFrameSource` / fake `FakeThermalFrameSource`)
- S3: `CameraRuntimeManager` (multi-cam + per-cam isolation) + offline live view + single-instance mutex + panel restart
- S4-data: `ThermalCaptureWriter`(.y16+.json) + `LiteDbCaptureIndex` + `CaptureStore`(save/query/retention) + `ThermalFrameReader`
- S5-local: `CapturePipelineE2ETests` (radiometric lossless round-trip)
- S6 c323c77: `CameraNatsConnector` (per-cam subscribe→tee→local .y16→JPG result+heartbeat, optional bg retry) + `ThermalPreviewEncoder` + App wiring

## Reviewed & decided (council codex/agy unanimous AGREE + Oracle High-conf)
- **Model Y**: 1 AgentUI proc per camera-PC owns ALL local cams (N `CameraRuntime`, distinct indices, no handle contention).
- **capture-by-tee**: NATS capture snapshots live loop (no 2nd open, no pause). UVC = 1 handle/proc → live+capture must share one process.
- Per-camera logical AgentId preserved (`BuildAgentId(pcId,hwid)`) → Master NATS contract unchanged.
- Session 0: GUI autostart via **logon Scheduled Task** (NOT CreateProcessAsUser); auto-login for unattended.
- Data: `.y16`(LE 14bit) + `.json` sidecar + LiteDB index. PNG persist deferred (reconstruct on demand).
- **User decided: auto-login/operator-login OK → Model Y stays** (unattended-no-login NOT required).

## Remaining (next session)
- **S4-UI**: data browser + log viewer(Serilog NDJSON) + settings tabs. New files. ⚠️ WPF → needs **real-desktop visual QA** (current env = non-interactive window station, cannot render/click).
- **S5-full**: extend E2E with NATS + Manager monitoring.
- **S7 ⚠️ MODIFIES EXISTING SC-12 code**: `AgentSupervisor` spawn-per-camera → supervise-1-AgentUI; `ManagerCommandHandler` Reject/Disable/Restart → per-camera unload (NOT process kill); preserve public method signatures + `AgentManagerTests`. **UNDECIDED**: Manager(service)→AgentUI(GUI) per-camera-unload **IPC mechanism** (named pipe / localhost / file) — decide before implementing. Add `Manager.AgentUiExePath`.
- **S8**: `ManagerE2EDriver` fake-runtime update (no WPF in CI) + AgentUI `--headless` + logon scheduled-task deploy + auto-login doc + retire/keep console Agent.

## Env limitation
Non-interactive window station → WPF visual QA impossible here. All backend/data verified via unit/integration tests only. AgentUI runs default SimulationMode (2 sim cameras) so it launches with no hardware — needs real desktop to see it.

## Recommended next-session start
Decide S7 IPC mechanism → implement S7; OR do S4-UI if real desktop available for QA.
Council review scratch (uncommitted): `.council/`.
