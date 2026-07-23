# Session Handoff (2026-07-23) — 슬레이브(카메라) PC 독립 GUI (AgentUI)

Full doc: `docs/handoff/agent-ui-slave-gui-handoff.md` (read first).

## Done + committed/pushed
S1-S6 + S4-UI. Build 0/0, tests 101/101, **console Agent + Master UNCHANGED** (all new files).
- S1 f1e630e: AgentUI WPF project (net8.0-windows, CommunityToolkit.Mvvm)
- S2: `CameraRuntime` (per-cam 1 VideoCapture + 1 Y16 loop, atomic latest, capture-by-tee; `IThermalFrameSource`: real `CltcThermalFrameSource` / fake `FakeThermalFrameSource`)
- S3: `CameraRuntimeManager` (multi-cam + per-cam isolation) + offline live view + single-instance mutex + panel restart
- S4-data: `ThermalCaptureWriter`(.y16+.json) + `LiteDbCaptureIndex` + `CaptureStore`(save/query/retention) + `ThermalFrameReader`
- S5-local: `CapturePipelineE2ETests` (radiometric lossless round-trip)
- S6 c323c77: `CameraNatsConnector` (per-cam subscribe→tee→local .y16→JPG result+heartbeat, optional bg retry) + `ThermalPreviewEncoder` + App wiring
- S4-UI 3993175: MainWindow TabControl(Live/Data/Logs/Settings) — data browser(query/delete/retention/Y16 preview) + log viewer(Serilog NDJSON→`NdjsonLogReader`→level filter) + settings tab(agentui.json edit/save) + `AgentUiLog`(App-level lifecycle/Faulted logging). LogEntry/LogEntryLevel(Core), NdjsonLogReader(Protocols)+4 tests. Visual-QA'd live: all 4 tabs, logging pipeline proven end-to-end.

## Reviewed & decided (council codex/agy unanimous AGREE + Oracle High-conf)
- **Model Y**: 1 AgentUI proc per camera-PC owns ALL local cams (N `CameraRuntime`, distinct indices, no handle contention).
- **capture-by-tee**: NATS capture snapshots live loop (no 2nd open, no pause). UVC = 1 handle/proc → live+capture must share one process.
- Per-camera logical AgentId preserved (`BuildAgentId(pcId,hwid)`) → Master NATS contract unchanged.
- Session 0: GUI autostart via **logon Scheduled Task** (NOT CreateProcessAsUser); auto-login for unattended.
- Data: `.y16`(LE 14bit) + `.json` sidecar + LiteDB index. PNG persist deferred (reconstruct on demand).
- **User decided: auto-login/operator-login OK → Model Y stays** (unattended-no-login NOT required).

## Remaining (next session)
- **S5-full**: extend E2E with NATS + Manager monitoring. ⚠️ live real-camera part needs hardware (fake + NATS-docker doable without).
- **S7 ⚠️ MODIFIES EXISTING SC-12 code**: `AgentSupervisor` spawn-per-camera → supervise-1-AgentUI; `ManagerCommandHandler` Reject/Disable/Restart → per-camera unload (NOT process kill); preserve public method signatures + `AgentManagerTests`. **UNDECIDED**: Manager(service)→AgentUI(GUI) per-camera-unload **IPC mechanism** (named pipe / localhost / file) — decide before implementing. Add `Manager.AgentUiExePath`.
- **S8**: `ManagerE2EDriver` fake-runtime update (no WPF in CI) + AgentUI `--headless` + logon scheduled-task deploy + auto-login doc + retire/keep console Agent.

## Env — visual QA WORKS here (method)
Desktop IS interactive (contrary to prior note). WPF visual QA recipe used this session:
1. Build, then `Start-Process <exe> -PassThru`; `(Get-Process).MainWindowHandle` != 0 → window rendered.
2. Capture: `[Win]::SetForegroundWindow(hwnd)` + `GetWindowRect` + `System.Drawing` `CopyFromScreen` → PNG under `%TEMP%\opencode\`; Read the PNG.
3. Click tabs/buttons: Windows MCP `Click-Tool loc=[screenX,screenY]` where screen = windowRect(Left,Top) + image offset. Re-get rect before each click (window can move on foreground).
Note: Windows MCP `State-Tool` errored ("list.remove"); screenshot+coord-click worked. AgentUI default SimulationMode (2 sim cams) → launches with no hardware.

## Recommended next-session start (hardware-independent)
**S8** (ManagerE2EDriver fake + `--headless` + logon scheduled-task deploy + auto-login doc) — pure infra, no design fork. OR **S7** (Manager redefine — needs Manager→AgentUI per-camera-unload **IPC decision** + modifies SC-12 code).
Council review scratch (uncommitted): `.council/`.
