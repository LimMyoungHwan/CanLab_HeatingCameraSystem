---
slug: hardware-external-simulator
status: plan-ready
intent: clear
pending-action: write .omo/plans/hardware-external-simulator.md
approach: Add a separate console simulator process that emulates the real external boundaries (XGT FEnet TCP and existing NATS subjects) while Master runs with SimulationMode=false; do not add simulator behavior to Master or AgentUI.
---

# Draft: hardware-external-simulator

## Components (topology ledger)
<!-- Lock the SHAPE before depth. One row per top-level component that can succeed or fail independently. -->
| id | outcome | status | evidence path |
| --- | --- | --- | --- |
| C1 | Standalone simulator host/config/lifecycle | active | `HeatingCameraSystem.slnx:1-11`, `HeatingCameraSystem.ManagerE2EDriver/Program.cs:38-87` |
| C2 | External XGT FEnet chamber + blackbody register emulator | active | `HeatingCameraSystem.Protocols/PlcXgtClient.cs:32-215`, `HeatingCameraSystem.Core/Config/HardwareSettings.cs:47-151` |
| C3 | External NATS camera-agent emulator | active | `HeatingCameraSystem.Protocols/NatsCommunicationService.cs:29-83`, `HeatingCameraSystem.Protocols/Cameras/CameraNatsConnector.cs:101-223` |
| C4 | Master UI placeholder completion for simulated live/status/trends | active | `HeatingCameraSystem.Master/Views/DashboardView.xaml:137-242`, `HeatingCameraSystem.Master/ViewModels/DashboardViewModel.cs:136-192` |
| C5 | Hardware-free automated verification and runbook | active | `HeatingCameraSystem.ManagerE2EDriver/Program.cs:15-318`, `docs/deployment/simulation-mode.md:1-140` |

## Open assumptions (announced defaults)
<!-- Record any default you adopt instead of asking, so the user can veto it at the gate. -->
| assumption | adopted default | rationale | reversible? |
| --- | --- | --- | --- |
| Simulator form | Separate `net8.0` console project | User explicitly rejected in-process simulation; console is CI-friendly and smallest | yes |
| Serial/COM emulation | Deferred | Requires third-party virtual COM driver/admin installation and was excluded by approved default | yes |
| PLC protocol | Reuse installed `FEnetSimulationService` + `TcpChannelProvider` | Package v1.1.21 includes an official simulation service and sample server; avoids custom packet parser | yes |
| Blackbody external boundary | Simulator owns existing PLC BB registers; real-mode Master uses existing `PlcBlackBodyAdapter` until direct-controller protocol exists | No real blackbody transport is specified; this is the only current external protocol contract | yes |
| Physics | Deterministic accelerated ramp/busy behavior, no random drift | Tests need observable transitions without flaky timing | yes |
| AgentUI | Do not fake DirectShow/COM inside AgentUI; cover non-device ViewModels with tests and defer live AgentUI hardware emulation | True external AgentUI emulation requires virtual camera/COM drivers | yes |
| Test strategy | TDD with xUnit; optional local-NATS E2E | Existing suite is xUnit/Moq and integration tests already skip when NATS is unavailable | yes |

## Findings (cited - path:lines)

- Master chooses fake implementations only through `HardwareSettings.SimulationMode` in `HeatingCameraSystem.Master/Services/AppServices.cs:68-93`; external simulator acceptance requires running with that value false.
- `PlcXgtClient` talks FEnet over TCP and maps all chamber, blackbody, servo, equipment, error, IO and admin fields through `PlcSettings` in `HeatingCameraSystem.Protocols/PlcXgtClient.cs:32-215` and `HeatingCameraSystem.Core/Config/HardwareSettings.cs:47-151`.
- NuGet `VagabondK.Protocols.LSElectric` 1.1.21 exposes `FEnetSimulationService`; `VagabondK.Protocols.Channels` 1.1.22 exposes `TcpChannelProvider`, so no custom FEnet parser is needed.
- Existing NATS camera contracts are `master.cmd.capture.{AgentId}`, `master.cmd.capture.all`, `agent.result.capture.{AgentId}`, `agent.status.{AgentId}`, and `agent.live.{AgentId}` in `HeatingCameraSystem.Protocols/NatsCommunicationService.cs:29-83`.
- Dashboard still renders `No Signal` and `Chart Area Placeholder` at `HeatingCameraSystem.Master/Views/DashboardView.xaml:186-189,239-242`, although live frames already arrive in `HeatingCameraSystem.Master/ViewModels/LiveViewModel.cs:29-65`.
- Existing E2E driver already proves fake inventory/Agent capture but launches the internal fake Agent path, not a standalone external device simulator (`HeatingCameraSystem.ManagerE2EDriver/Program.cs:15-25,238-307`).
- Existing simulation documentation is stale (Modbus references and old test totals) in `docs/deployment/simulation-mode.md:7-12,124-130`.
- Worktree has unrelated `.bkit`, `.omo/run-continuation`, `.council`, `TestResults`, and `docs` artifacts; executor must not overwrite, delete, stage or commit them.

## Decisions (with rationale)

- Add one standalone simulator executable; keep its configuration and runtime state outside Master/AgentUI.
- Simulator must use real production contracts: FEnet TCP for PLC/chamber/blackbody and existing NATS messages for cameras.
- Run Master with `SimulationMode=false` during external-simulator E2E; no direct calls into `AppServices` or ViewModels from the simulator.
- Use the package-provided FEnet simulation API instead of implementing LS packet parsing.
- Make simulation deterministic and accelerated; expose explicit disconnect/fault knobs via simulator JSON/CLI, not randomness.
- Complete only demonstrated UI placeholders and simulator observability; do not reopen completed command bindings or redesign all WPF screens.

## Scope IN

- New simulator project, settings validation, graceful startup/shutdown and logs.
- FEnet memory map and deterministic chamber/blackbody/servo behavior matching configured D/M/P addresses.
- NATS camera identities, heartbeats, thermal live JPEGs, and capture results using existing subjects/models.
- Real-mode blackbody routing through the existing PLC adapter until a physical blackbody protocol is supplied.
- Master Dashboard camera image/status and bounded temperature/humidity trend display without a new chart package.
- Unit/contract tests, optional NATS E2E, simulator runbook and sample settings.

## Scope OUT (Must NOT have)

- No new or expanded in-process SimulationMode behavior in Master, Agent, or AgentUI.
- No simulator-only NATS subjects or direct Master service calls.
- No custom FEnet wire parser when the installed package simulation service supports the contract.
- No virtual DirectShow camera or virtual COM driver installation; no serial/shutter emulator in this iteration.
- No physics-accurate thermal model, random failures, scenario DSL, WPF simulator manager, or new DI framework.
- No 2-point NUC calibration or real-blackbody protocol implementation without hardware/specification.
- No unrelated cleanup, formatting, dependency upgrades, or dirty-worktree artifact changes.

## Open questions

None. User approved the separate console simulator and the announced default excluding COM emulation.

## Approval gate
status: approved
approved-by: user
approved-scope: standalone console simulator; external FEnet + NATS boundaries; COM excluded
pending-action: user chooses start-work or optional high-accuracy dual review; do not implement in planner session

## Review receipts
- mandatory-metis-session: `ses_06e6ce949ffeyK03fA3xa0nDxh`
- mandatory-metis-result: incorporated with corrections
- fixes-applied: made real-mode blackbody routing explicit; excluded shutter from acceptance; confirmed Simulator itself owns NATS fake Agents; added protocol contract test; reconciled stale docs; added deterministic timing/failure criteria and dirty-worktree guardrails.
- metis-claim-overridden-by-primary-evidence: official VagabondK sample proves `TcpChannelProvider` implements `IChannel` and is passed directly to `FEnetSimulationService`; no unresolved provider/channel wiring spike remains beyond the permanent contract test.
- high-accuracy-review: not requested; pending user choice after plan delivery.
<!-- When exploration is exhausted and unknowns are answered, set status: awaiting-approval. -->
<!-- That durable record is the loop guard: on a later turn read it and resume at the gate instead of re-running exploration. -->
