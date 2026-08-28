# Handoff — Recipe chamber-drift safety gate (this session) + next-session backlog

Status: **IMPLEMENTED + COMMITTED (2026-08-28).**
Commits: `3b62b6c` feat(recipe): operator-set chamber safety-band tolerances ·
`48404e0` feat(recipe): pause on chamber drift with manual operator resume.
Verification: build 12 proj 0 err/0 warn · full suite **334 passed / 0 failed** · manual QA in SimulationMode Master (editor renders safety inputs, dashboard paused panel correctly hidden, save exercises ToDomain→LiteDB).

## What was implemented

Recipe execution now enforces an operator-set safety band.
- Two per-recipe fields: `SafetyTempTolerance` (1.0℃ default), `SafetyHumidityTolerance` (5.0%RH default) — `Recipe` model + editor inputs (ko/en localized).
- `RecipeEngine.ExecuteRecipeAsync` gained an optional last param `Func<CancellationToken,Task>? waitForResumeAsync = null` (backward compatible — null = old alarm-and-continue; the 6 existing call sites are unchanged).
- **Initial stabilization** now waits for temperature (0.5℃ reach band, unchanged) AND humidity (within `SafetyHumidityTolerance`). Auto-wait, no alarm.
- **Per-step, before capture**: `ReadSafetyBandAsync` checks temp+humidity vs global target ± safety tolerances. Out of band → `AlarmSink.Raise(Error)` + progress phase "안전조건 이탈 — 사용자 확인 대기" + await the resume gate; on resume re-check, loop until in band or cancelled. Capture is never published while out of band.
- Master UI: `DashboardViewModel.IsRecipePaused` + `_pauseGate` TCS + `ResumeRecipeCommand`; the gate is wired into `StartRecipeAsync` and UI-marshaled via the existing `RunOnUi`. `DashboardView` shows a Resume button + paused indicator (visible only when paused).
- Reach band (0.5, tight) is deliberately distinct from the drift/safety band (operator-set, wider): reach precisely, then tolerate some drift before alarming.

## Key files
- `HeatingCameraSystem.Core/Models/RecipeModels.cs` — the two safety-tolerance fields.
- `HeatingCameraSystem.Master/Services/RecipeEngine.cs` — `ReadSafetyBandAsync`, initial humidity gate, per-step drift loop, `waitForResumeAsync`.
- `HeatingCameraSystem.Master/ViewModels/DashboardViewModel.cs` — `WaitForResumeAsync`, `ResumeRecipeCommand`, `IsRecipePaused`.
- `HeatingCameraSystem.Master/ViewModels/RecipeEditorViewModel.cs` — VM mapping of the two fields.
- Tests: `RecipeEngineTests` (S1-S4), `DashboardRecipePauseTests`, `RecipeCopyTests` (round-trip).

## Known / notes
- `// ponytail:` in RecipeEngine marks the deliberate "alarm on every band crossing, no debounce" ceiling — add debounce only if operators complain of alarm spam near the edge.
- Drift-paused *visible* state cannot be triggered in SimulationMode (FakePlc snaps humidity to target, never drifts); it is covered by the engine + VM unit tests instead.
- Operators must set real safety tolerances per recipe; defaults are 1.0℃ / 5.0%RH.

---

## Next-session backlog (ordered)

### 1. Recipe-driven shutter gating (the ORIGINAL request — NOT built this session)
Desired flow: shutter default closed → servo moves to camera → blackbody in range → **open shutter at capture start → capture → close shutter → move to next camera**. Today the shutter opens once at `StartLiveAsync` (AgentUI) and stays open; `RecipeEngine` never touches the shutter. Implementing this requires:
- Per-step: Master sends `CameraControlMessage(ShutterOpen)` → wait ack → publish capture → `CameraControlMessage(ShutterClose)`.
- **Depends on item 2** (ack visibility) — a shutter that fails to open must block/alarm the step, not fire-and-forget.
- Decide the shutter path first: AgentUI `ICameraSerialClient.SetShutterAsync(bool)` vs `ISerialShutterController.OpenShutterAsync` (raw `0x04..`) — confirm which is the real thermal-camera shutter.
- Architectural tension: commit `3d6cb09` deliberately decoupled video from serial; making the shutter a hard capture gate re-couples them — define the shutter-fail policy (skip step / abort / retry).

### 2. Recipe shutter/camera-control ACK visibility (pre-existing item 2)
`RecipeEditorViewModel` sends `CameraControlOps` but never subscribes `CameraControlAckMessage` (`agent.ack.camera.{AgentId}`), so a mid-recipe shutter/camera failure surfaces nowhere. `SubscribeCameraControlAckAsync` already exists on `INatsCommunicationService`. Camera-free testable (Mock nats + captured ack callback).

### 3. Simultaneous capture mode is UI-only
`RecipeModel.IsSequentialMode` exists on the VM but NOT on Core `Recipe` (dropped on save), and `RecipeEngine` runs a strictly sequential `for`. Wire it: persist the flag on Core Recipe, branch the engine to fan out captures for simultaneous mode.

### 4. Step-level chamber targets (fields exist, engine ignores)
`RecipeStep.TargetChamberTemperature/Humidity` are populated but `RecipeEngine` only uses the global targets. To support per-step environment profiles, drive chamber SV per step (and the new safety band would then track the step target instead of global).

### 5. Minor
- `RecipeStep.TargetPositionIndex` is a dead field (movement uses `PositionX/Y` direct coords only).
- Pre-existing: stable camera identity — `CameraNode` keyed by `CAM-{index}`, not S/N; add S/N to `AgentStatusMessage` first so a re-slotted camera keeps its dashboard slot.

### Camera-free testability
Items 1, 2, 3, 4 are all unit-testable without a physical camera using the established `Mock<INatsCommunicationService>` + captured-callback pattern (see `RecipeEngineTests` / `DashboardRecipePauseTests`).
