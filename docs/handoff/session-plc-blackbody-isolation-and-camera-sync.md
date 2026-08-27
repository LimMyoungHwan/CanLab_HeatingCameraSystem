# Handoff — PLC/Blackbody read isolation + Master↔AgentUI camera live-sync

Status: **IMPLEMENTED (2026-08-25).** Analysis below is retained as the rationale record.
Date: 2026-08-22 (analysis) → 2026-08-25 (implementation)

## What was implemented

Issue 1 — PLC/blackbody isolation (all 5 fix directions):
- `PlcStatusService` now runs **two independent timers**. The PLC snapshot publishes right after
  `ReadStatusAsync()` and never waits on blackbody I/O.
- State fully separated: `IsConnected`/`StatusMessage` are PLC-only; blackbody owns
  `IsBlackBodyConnected`/`BlackBodyStatusMessage` + per-unit `BlackBody1Faulted`/`BlackBody2Faulted`.
- Double read eliminated: `PlcStatusService` is the single blackbody reader. `DashboardViewModel`
  and `StatusMonitorViewModel` lost their direct re-read and now consume the published values
  (new `BlackBodyUpdated` event keeps them updating while the PLC is down).
- `SrBlackBodyController.WithUnit` gate wait is now bounded (`ReadTimeoutMs + InterMessageDelayMs`),
  so a hung unit can no longer pile callers up behind it.
- PLC mirroring (`WriteBlackBodyTemperaturesAsync`) runs only for units that read successfully and
  its failure no longer marks the unit faulted.

Issue 2 — immediate camera add/remove reflection. Scope is the **cameras listed under an Agent PC**;
an Agent PC that is merely unreachable still stays visible (greyed offline) exactly as before.
- `AgentStatusMessage.HostAgentIds` carries the publishing host's live camera inventory. No new
  topic — the existing heartbeat became the inventory carrier.
  - `null` = legacy sender reported nothing -> Master never reconciles (no blind deletion).
  - empty = reported, host genuinely has no camera left.
  - sender absent from its own inventory = inventory-only report, creates no node.
- `CameraNatsConnector` stamps it and publishes immediately from `SyncSubscriptionsAsync()`
  (the hotplug/rebuild hook), so changes land without waiting for the heartbeat interval. When the
  host's last camera is gone there is no camera left to carry the report, so it publishes a
  host-level one keyed by machine name — silence is not a signal over pub/sub.
- Master reconciles per host: cameras absent from the inventory are dropped at once, a re-slotted
  camera replaces its stale `CAM-NN` node, dashboard slot bindings are cleared on removal, and
  `_persistedLayout` is deliberately left intact so a returning camera rebinds to its slot.

Verification: build 0 errors / 0 warnings, **314 tests pass** (14 new in
`PlcBlackBodyIsolationTests` + `DashboardAgentInventoryTests`).

## Follow-up (same session): half-failure visibility

Investigation showed a camera could be half-dead and still look healthy on Master, because the only
state on the wire (`CameraStatus`) is derived purely from the video runtime.

- **Serial down, video fine** was completely invisible on Master. `AgentStatusMessage` now carries
  `IsSerialConnected` (nullable; null = legacy sender, no fault rendered) sourced from the AgentUI
  panel's `HasSerialControl`, and `DashboardView` renders a `NO SERIAL` badge on both the camera tree
  row and the feed tile. It is deliberately a separate axis from `CameraStatus`.
  Video-only cameras are not a supported configuration, so absent serial always counts as a fault.
- **Frame starvation** (`IThermalFrameSource.Read()` returning null forever, no exception) kept the
  runtime at `Running`, so the tree said `Connected` while the feed tile said `No Signal`.
  `CameraRuntime` now faults after `frameTimeoutMs` (default 3 s, constructor-tunable) without a
  frame and returns to `Running` on the next real frame.

- **AgentUI local panel** previously showed a serial fault only as a grey line plus the serial
  section silently disappearing, and a faulted video runtime as grey text over a black box. It now
  shows a `NO SERIAL` badge on the panel header, a `NO SIGNAL` overlay on the video area while the
  runtime is faulted, and red emphasis on the faulted status and serial lines. Labels go through the
  existing `{loc:Loc}` system (`Cam_NoSerial`, `Cam_NoSignal`, ko/en).
  `AgentUiLocalizationIntegrityTests` now fails the build if a `{loc:Loc}` key used in
  `MainWindow.xaml` is missing from either language file, or if the two key sets diverge.

Not done (was item 3 of the proposal, not requested): recipe-driven shutter failures are still
fire-and-forget — `RecipeEditorViewModel` never subscribes to `CameraControlAck`, so a shutter that
fails mid-recipe still surfaces nowhere.

## Still open

- **Stable camera identity (original item 3 of Issue 2)** — `CameraNode` is still keyed by
  `CAM-{CameraIndex}`, not S/N or `UsbContainerId`. A re-slotted camera therefore leaves its
  persisted dashboard slot blank instead of following the camera. Requires the S/N to be added to
  `AgentStatusMessage` first.

---

## Issue 1 — Blackbody read must not block/affect PLC read (and vice-versa)

### User requirement
- 흑체 2대 중 1대라도 실패해도 PLC는 계속 읽어서 갱신.
- 흑체 실패 → 흑체 에러만 표시. PLC 실패 → 흑체는 계속 읽음. 완전 독립(대칭).

### Current behavior (files/lines)
- `HeatingCameraSystem.Master/Services/PlcStatusService.cs` `PollAsync` (L39-90), 1s `DispatcherTimer`:
  - L45 `var s = await _plc.ReadStatusAsync();` — PLC read OK first.
  - L46-67 blackbody loop: per-unit `GetCurrentTemperatureAsync`/`GetTargetTemperatureAsync` + `_plc.WriteBlackBodyTemperaturesAsync` (writes BB temps INTO the PLC). Per-unit try/catch.
  - **L68 `Snapshot = s;` and L88 `Updated?.Invoke` run AFTER the blackbody loop** → a slow/hung blackbody read delays the PLC snapshot publish.
  - Blackbody catch (L58-65) sets shared `IsConnected=false` + `StatusMessage=Dash_ReadFailed` (then L74 flips back true). PLC + blackbody share one connection/status state.
- `HeatingCameraSystem.Master/ViewModels/DashboardViewModel.cs`:
  - `OnSharedPlcStatus` (L440) subscribes `AppServices.PlcStatus.Updated` → `ApplyStatus` + `RefreshBlackBodyAsync` (L449).
  - `RefreshBlackBodyAsync` (L570-592): if a real `BlackBodyController` exists it **RE-READS blackbody directly** (L583-586), ignoring the snapshot's BB values. → **blackbody read happens TWICE per cycle** (PlcStatusService + Dashboard).
  - L447: `if (!st.IsConnected) return;` — dashboard skips applying PLC status when the (shared) IsConnected is false.
- `HeatingCameraSystem.Protocols/SrBlackBodyController.cs`:
  - Per-unit `Unit` with its own `ISrLink` + `SemaphoreSlim Gate`. IP → `UdpSrLink(cfg, ReadTimeoutMs)`; serial → `SerialPortSrLink(cfg, ReadTimeoutMs)`.
  - `WithUnit` (L117-131): `await u.Gate.WaitAsync()` has **NO timeout**; `QueryFloatLocked` (L153) is `Task.Run` doing blocking `u.Link.Read()` (up to ReadTimeoutMs).
  - Per-unit isolation exists (index + gate), so unit1 failure shouldn't corrupt unit0 — but each dead-unit read costs ~ReadTimeoutMs.

### Root cause (why "blackbody down → PLC frozen")
A dead blackbody unit's read blocks for `ReadTimeoutMs` each call. Per cycle that's up to `2 units × 2 reads × 2 pollers` (PlcStatusService + Dashboard.RefreshBlackBodyAsync) × ReadTimeoutMs of blocking. Because the blackbody loop sits BEFORE `Snapshot=s`/`Updated`, and the poll `_polling` flag skips overlapping ticks, the **PLC snapshot stops refreshing** even though the PLC read itself succeeded. Shared `IsConnected` also conflates the two.

### Fix direction (next session)
1. **Publish PLC snapshot immediately after `ReadStatusAsync`**, before any blackbody work. Blackbody updates the BB fields separately/afterwards.
2. **Fully separate state**: PLC own `IsConnected`/`StatusMessage`; blackbody own per-unit connected/error + a distinct blackbody alarm/indicator. Blackbody failure must NOT touch PLC connection status, and vice-versa.
3. **Eliminate the double read**: make PlcStatusService the single blackbody reader (fills snapshot.BlackBody*), and have `DashboardViewModel.RefreshBlackBodyAsync` just consume `snapshot.BlackBody*` (delete the direct re-read). Halves I/O + contention on a dead unit.
4. **Bound blackbody reads**: add a timeout to `WithUnit` gate wait + overall per-unit read (e.g. `Task.WhenAny(read, Delay(timeout))`), and/or move blackbody polling to its own background loop so a hung unit never stalls the 1s PLC cadence.
5. Keep `WriteBlackBodyTemperaturesAsync` (PLC display of BB temps) but only when a unit read succeeded; never let it block PLC status reads.

### Verify
- Simulate 1 blackbody IP down → PLC values keep updating at 1s; only blackbody-1(or 2) shows error. Then PLC down → blackbody still reads. Unit0 up + unit1 down → unit0 value fine, unit1 error only.

---

## Issue 2 — Master must reflect AgentUI camera add/remove immediately

### User requirement
AgentUI에서 카메라 제거/추가(교체)하면 Master가 즉각 반영: 구 항목 삭제 + 신규 연결로 갱신.

### Current behavior (files/lines)
- AgentUI `HeatingCameraSystem.Protocols/Cameras/CameraNatsConnector.cs`:
  - `PublishHeartbeats` (L332) every `HeartbeatSeconds`: for EACH camera in `_cameras`, publishes one `AgentStatusMessage{AgentId, Alias, HostName, CameraIndex, CameraStatus, Timestamp}` (L343-355). **No "removed" / full-inventory message.** A removed camera just stops being heartbeated.
- `AgentStatusMessage` (`Core/Models/NatsMessages.cs` L27-36): one message = one camera; no list, no removal flag.
- Master `DashboardViewModel.SubscribeAgentStatusAsync` (L266-300): find/create `AgentNode` by `msg.AgentId`, find/create `CameraNode` by `CAM-{CameraIndex:D2}`. **Add-only — never removes.**
- `CheckOfflineAgents` (L396-411): 5s timer; if `LastHeartbeat < now-15s` → `IsOnline=false` + cameras `CameraStatus.Offline`. **Marks offline, never removes the node.**
- `AgentDirectory` (`Master/Services/AgentDirectory.cs`): alias→AgentId for recipe routing, **explicitly "No eviction"** (not the UI list).

### Root cause
- Master only ADDS agents/cameras; removal is never modeled. Re-slotting a camera in AgentUI changes its auto-numbered `host_Agent_N` AgentId → the old node lingers forever (goes grey after 15s) while the new one appears up to 5s later. Result: duplicate/stale cameras, not "즉각 갱신".
- No explicit inventory/removal signal from AgentUI; Master can only infer via 15s timeout, and even then doesn't delete.
- Identity keyed on volatile AgentId+index instead of a stable key (S/N / ContainerId / Alias).

### Fix direction (next session)
1. **AgentUI publishes an authoritative inventory** per host — either a dedicated `agent.inventory.{host}` message (full current camera list) pushed **on change** (hook into `RebuildCameraPanels`/settings-save/hotplug) + periodically, or extend the heartbeat to carry the full set. Master reconciles: any Master-side camera/agent for that host NOT in the latest inventory → **remove immediately**.
2. **Master eviction**: reconcile the host's camera set on each inventory/heartbeat; remove stale `AgentNode`/`CameraNode` (and their dashboard-slot bindings) instead of only greying them.
3. **Stable identity**: key `CameraNode` by S/N or UsbContainerId (or Alias) so a re-slotted camera updates in place rather than spawning a duplicate.
4. Push-on-change (not 5s poll) gives the "즉각" behavior; keep heartbeat as the liveness/fallback.
5. Watch dashboard-slot layout rebind (`RebindPersistedLayouts` L327) + `_agentMap` when removing nodes so persisted mode2-5 assignments don't dangle.

### Verify
- AgentUI 카메라 제거 → Master 목록에서 즉시 사라짐. 새 카메라 추가 → 즉시 나타남. 교체(제거+추가) → 구 항목 없어지고 신규만 표시.

---

## Key files
- `HeatingCameraSystem.Master/Services/PlcStatusService.cs` — shared 1s PLC+BB poll (Issue 1 core).
- `HeatingCameraSystem.Master/ViewModels/DashboardViewModel.cs` — OnSharedPlcStatus/RefreshBlackBodyAsync (Issue 1 double-read), SubscribeAgentStatusAsync/CheckOfflineAgents (Issue 2 core).
- `HeatingCameraSystem.Protocols/SrBlackBodyController.cs` — per-unit BB links + timeouts (Issue 1).
- `HeatingCameraSystem.Protocols/Cameras/CameraNatsConnector.cs` — PublishHeartbeats (Issue 2 source).
- `HeatingCameraSystem.Core/Models/NatsMessages.cs` — AgentStatusMessage (Issue 2 payload; needs inventory/removal).
- `HeatingCameraSystem.Master/Services/AgentDirectory.cs` — alias map (no eviction).
