# hardware-external-simulator - Work Plan

## TL;DR (For humans)
**What you'll get:** A separately launched simulator that behaves like two thermal-camera Agents plus an LS XGT chamber/blackbody/servo controller. The Master dashboard will show simulated live images, online state and temperature/humidity trends, and a one-command check will prove the full hardware-free flow.

**Why this approach:** The real applications stay in real-hardware mode and communicate through their production FEnet and NATS boundaries. That catches protocol, serialization and disconnection defects that internal fake objects cannot expose.

**What it will NOT do:** It will not install virtual camera/COM drivers, emulate shutters, add another in-application simulation switch, or attempt physically accurate thermal behavior. AgentUI live-camera testing remains hardware-dependent in this iteration.

**Effort:** Large
**Risk:** Medium-High — the installed FEnet server API is verified, but concurrent device-memory behavior, accelerated timing and multi-process UI/E2E cleanup require careful testing.
**Decisions to sanity-check:** Standalone console rather than GUI; COM/shutter excluded; blackbody temporarily travels through existing PLC registers until its real direct-control protocol is known.

Your next move: start implementation, or request the optional high-accuracy dual review first. Full execution detail follows below.

---

> TL;DR (machine): Add a standalone external FEnet+NATS simulator, complete Master Dashboard live/trend placeholders, and prove hardware-free contracts without expanding in-process simulation modes.

## Scope
### Must have
- Add `HeatingCameraSystem.Simulator`, a separately launched `net8.0` console executable included in `HeatingCameraSystem.slnx`.
- Run Master against it with `HardwareSettings.SimulationMode=false`; the simulator must impersonate external equipment only through the same production boundaries used by real hardware.
- Emulate LS XGT FEnet TCP on configurable loopback address/port (default `127.0.0.1:2004`) using the installed `VagabondK.Protocols.LSElectric.FEnet.Simulation.FEnetSimulationService` and `TcpChannelProvider`, following the package's official `SimpleFEnetServerSimulation` pattern.
- Maintain a thread-safe D/M/P device memory map compatible with `PlcSettings`, including chamber temperature/humidity, chamber run/control bits, blackbody 1/2 PV/SV, servo positions/busy/home/current point, equipment bits, fan speed, errors, IO and admin values read by `PlcXgtClient.ReadStatusAsync()`.
- Provide deterministic accelerated dynamics: 100 ms tick, default temperature rate 20 °C/s, humidity rate 40 %RH/s, blackbody rate 30 °C/s, and 500 ms servo busy duration. Rates must be configurable and never random.
- Emulate camera-side NATS behavior inside the separate process using existing messages/subjects only: per-camera heartbeat every 5 s, color-JPEG live frames every 100 ms, capture command handling, image persistence and capture results.
- Support a small management surface: JSON configuration plus console commands `status`, `plc online|offline`, `plc fault <0-19> on|off`, `camera <AgentId> online|fault|offline`, and `quit`.
- In real mode, route Master blackbody calls through the existing `PlcBlackBodyAdapter` so blackbody traffic reaches the external FEnet simulator; preserve the existing Fake path only for the already-existing `SimulationMode=true` behavior.
- Replace Master Dashboard's hardcoded camera `No Signal` tile and chart placeholder with NATS live images, stale-frame fallback, online count, and a bounded two-minute temperature/humidity sparkline built only with WPF primitives.
- Add deterministic xUnit contract tests and a one-command external-simulator E2E path that exercises real `PlcXgtClient`, real NATS serialization/subjects, `PlcBlackBodyAdapter`, synthetic camera live/capture and the existing recipe flow.
- Reconcile stale simulation documentation and provide a sample external-simulator config and exact run commands.

**Terminology:** existing **SimulationMode** means in-process `Fake*` selection inside current applications. New **Simulator** means the separate network-boundary executable added by this plan. They are not interchangeable; external-simulator QA always runs Master with `SimulationMode=false`.

### Must NOT have (guardrails, anti-slop, scope boundaries)
- Do not add, extend, rename or overload `SimulationMode` in Master, Agent, AgentUI or AgentManager for this feature.
- Do not remove existing in-process fakes; only stop real-mode blackbody from unconditionally using `FakeBlackBodyController`.
- Do not create simulator-only NATS subjects, DTOs or direct calls into Master services/ViewModels.
- Do not implement a custom FEnet packet parser; the installed package already supplies the simulation service.
- Do not add a WPF simulator manager, web API, database, scenario DSL, random drift/failures, physics-accurate thermal model or a new DI container.
- Do not emulate DirectShow devices, camera serial control, shutters or COM ports in this iteration. Serial reconnect log noise after 30 s in Master real mode is a documented limitation, not a reason to add an in-process stub.
- Do not claim AgentUI live-camera hardware coverage: without a virtual DirectShow/COM driver, only AgentUI's hardware-independent ViewModel/storage/log tests remain valid.
- Do not add a charting dependency; use bounded WPF `Polyline`/`PointCollection` rendering.
- Do not implement 2-point NUC, real blackbody serial/TCP protocol or hardware-address corrections that require equipment specifications.
- Do not touch, clean, stage or commit unrelated dirty paths: `.bkit/**`, `.omo/run-continuation/**`, `.council/**`, `HeatingCameraSystem.Tests/TestResults/**`, `docs/EnetClient*`, or `docs/PC통신라이브러리예제*`.

## Verification strategy
> Zero human intervention - all verification is agent-executed.
- Test decision: TDD with the existing xUnit 2.5.3 + Moq 4.20.72 project. Write/observe a failing focused test before each non-XAML implementation, then implement and rerun the focused test. XAML is verified by build plus agent-operated WPF visual QA.
- Baseline commands: `dotnet build HeatingCameraSystem.slnx` then `dotnet test HeatingCameraSystem.slnx --no-build`.
- Focused contract commands use each todo's concrete `dotnet test HeatingCameraSystem.Tests/HeatingCameraSystem.Tests.csproj --filter FullyQualifiedName~...` invocation.
- Network tests bind loopback only. NATS-dependent tests first probe `nats://127.0.0.1:4222`; unit tests must not silently pass by skipping, while explicitly categorized integration/E2E tests may report a clear skip when the broker is absent.
- Timing assertions use bounded polling (`WaitAsync`/deadline), not fixed sleeps as proof. Exact byte/timestamp equality is not asserted for JPEG output.
- WPF final QA is agent-operated with Windows MCP: launch simulator, launch Master against a temporarily backed-up/restored `hardware.json`, interact with Dashboard, and capture screenshots/logs. No user clicks are required.
- Evidence: each todo writes the concrete success/failure path listed in its QA scenarios under `.omo/evidence/`.

## Execution strategy
### Parallel execution waves
> Target 5-8 todos per wave. Fewer than 3 (except the final) means you under-split.

- **Wave 1 — contracts/foundations (parallel, 5):** T1-T5. Lock project/package contract, settings/state, memory, thermal generation, and testable Dashboard data flow.
- **Wave 2 — functional subsystem envelope (5):** T6-T10. Implement FEnet, NATS, host management, real-mode blackbody routing and Dashboard XAML; the dependency matrix intentionally delays T8 until T6/T7 complete while the other work proceeds in parallel.
- **Wave 3 — integration/handoff (parallel, 3):** T11-T13. Prove the full external path, package launch assets and reconcile documentation.
- Do not start Wave 2 until all Wave 1 contract signatures compile. Do not start Wave 3 until all Wave 2 focused tests pass.

### Dependency matrix
| Todo | Depends on | Blocks | Can parallelize with |
| --- | --- | --- | --- |
| T1 | - | T6, T7, T8 | T2, T3, T4, T5 |
| T2 | - | T6, T7, T8 | T1, T3, T4, T5 |
| T3 | - | T6 | T1, T2, T4, T5 |
| T4 | - | T7 | T1, T2, T3, T5 |
| T5 | - | T10 | T1, T2, T3, T4 |
| T6 | T1, T2, T3 | T8, T9, T11 | T7, T10 |
| T7 | T1, T2, T4 | T8, T11 | T6, T9, T10 |
| T8 | T1, T2, T6, T7 | T11, T12 | T9, T10 |
| T9 | T6 | T11 | T7, T8, T10 |
| T10 | T5 | T11 | T6, T7, T8, T9 |
| T11 | T6, T7, T8, T9, T10 | Final wave | T12, T13 |
| T12 | T8 | Final wave | T11, T13 |
| T13 | T6, T7, T8, T9, T10 | Final wave | T11, T12 |

## Todos
> Implementation + Test = ONE todo. Never separate.
<!-- APPEND TASK BATCHES BELOW THIS LINE WITH edit/apply_patch - never rewrite the headers above. -->
- [ ] 1. Scaffold the standalone simulator and lock the FEnet package contract
  What to do / Must NOT do: Create `HeatingCameraSystem.Simulator/HeatingCameraSystem.Simulator.csproj` as a `net8.0` console executable, reference Core and Protocols, pin the already-used VagabondK package versions, add it to `HeatingCameraSystem.slnx`, and add a test-project reference. Add `ExternalFEnetContractTests` proving the official package pattern compiles and accepts a real `PlcXgtClient` loopback read/write: `IChannel channel = new TcpChannelProvider(IPAddress.Loopback, port)`, `new FEnetSimulationService(channel)`, register read/write callbacks, cast to `ChannelProvider` and call `Start()`. Do not write a packet parser or introduce another protocol dependency.
  Parallelization: Wave 1 | Blocked by: none | Blocks: T6, T7, T8
  References (executor has NO interview context - be exhaustive): `HeatingCameraSystem.slnx:1-11`; `HeatingCameraSystem.Protocols/HeatingCameraSystem.Protocols.csproj:7-16`; `HeatingCameraSystem.ManagerE2EDriver/HeatingCameraSystem.ManagerE2EDriver.csproj:1-22`; `HeatingCameraSystem.Protocols/PlcXgtClient.cs:32-47,297-378`; installed API `VagabondK.Protocols.LSElectric` 1.1.21 `FEnetSimulationService`; installed API `VagabondK.Protocols.Channels` 1.1.22 `TcpChannelProvider`; official sample `https://github.com/Vagabond-K/VagabondK.Protocols/blob/master/Samples/LS%20ELECTRIC/SimpleFEnetServerSimulation/Program.cs`.
  Acceptance criteria (agent-executable): `dotnet build HeatingCameraSystem.slnx` exits 0 with no warnings; `dotnet test HeatingCameraSystem.Tests/HeatingCameraSystem.Tests.csproj --no-build --filter FullyQualifiedName~ExternalFEnetContractTests` exits 0 and proves a `PlcXgtClient` can write and read a known D-word through the package simulation service on loopback.
  QA scenarios (exact tool + invocation): Happy — PowerShell runs the focused test and redirects full output to `.omo/evidence/task-1-hardware-external-simulator.txt`; Failure — start a `TcpListener` on the selected test port first, rerun the bind test, assert a deterministic address-in-use failure rather than a hang, record `.omo/evidence/task-1-hardware-external-simulator-failure.txt`.
  Commit: Y | `feat(simulator): scaffold external simulator`

- [ ] 2. Define validated simulator settings and concurrent runtime state
  What to do / Must NOT do: Add `SimulatorSettings.Load(string path)` and immutable settings records for endpoint, dynamics and cameras. Required defaults: loopback/2004; NATS localhost; output under simulator base directory; 100 ms dynamics tick; rates 20 °C/s, 40 %RH/s and 30 °C/s; servo 500 ms; 5 s heartbeat; 100 ms live frame interval; two cameras `Agent_0/0` and `Agent_1/1`. Add a single thread-safe `SimulatorState` containing PLC online/fault bits and camera mode (`Online`, `Faulted`, `Offline`). Validate unique/nonblank AgentIds, unique nonnegative indices, loopback-only listen address by default, ports 1-65535, positive rates/intervals, 1-64 cameras, positive even frame dimensions, and writable output path. Do not create a mutable global config singleton, hot reload or scenario DSL.
  Parallelization: Wave 1 | Blocked by: none | Blocks: T6, T7, T8
  References: `HeatingCameraSystem.AgentUI/Services/AgentUiConfig.cs:17-97` for load/create conventions; `HeatingCameraSystem.Core/Config/HardwareSettings.cs:47-177` for PLC/device defaults; `HeatingCameraSystem.AgentManager/Config/ManagerSettings.cs:3-29` for simple config style; `HeatingCameraSystem.Core/Models/CameraDescriptor.cs:1-10` for camera identity.
  Acceptance criteria: `SimulatorSettingsTests` verifies defaults, JSON roundtrip, duplicate AgentId/index rejection, invalid address/port/rates/interval/path rejection, and concurrent state transitions; focused test command exits 0 without touching `%LOCALAPPDATA%`.
  QA scenarios: Happy — `dotnet test ... --filter FullyQualifiedName~SimulatorSettingsTests` with output `.omo/evidence/task-2-hardware-external-simulator.txt`; Failure — run Simulator with malformed JSON and assert exit code 2 plus one actionable error naming the property, evidence `.omo/evidence/task-2-hardware-external-simulator-failure.txt`.
  Commit: Y | `feat(simulator): add validated settings and state`

- [ ] 3. Implement the generic XGT device-memory primitive
  What to do / Must NOT do: Add `FEnetDeviceMemory`, a lock-protected byte store keyed by `DeviceType` using the package sample's Bit/Byte/Word/DoubleWord/LongWord semantics. Expose typed read/write helpers for `DeviceVariable` and logical `PlcSettings` tokens so the dynamics engine can update the same cells requested over FEnet. Support individual and continuous requests, bounds checking and NAK for unsupported/out-of-range access. Preserve `UseHexBitIndex=true` semantics for XGB bit areas and D-word dotted-bit masking. Do not encode business behavior in this class.
  Parallelization: Wave 1 | Blocked by: none | Blocks: T6
  References: official `SimpleFEnetServerSimulation/Program.cs` memory handlers; `HeatingCameraSystem.Protocols/PlcXgtClient.cs:252-307` token/index rules; `HeatingCameraSystem.Protocols/PlcXgtClient.cs:309-378` exact read/write shapes; `HeatingCameraSystem.Core/Config/HardwareSettings.cs:55-57,59-151` required device areas.
  Acceptance criteria: `FEnetDeviceMemoryTests` covers bit, dotted D-word bit, word, continuous byte range, RMW preservation, hex P/M indexing, concurrent reads/writes and out-of-range rejection; all focused tests pass under repeated execution (`--repeat` equivalent PowerShell loop 20 times) with identical final values.
  QA scenarios: Happy — run the focused suite 20 times and save `.omo/evidence/task-3-hardware-external-simulator.txt`; Failure — request an unsupported area and an out-of-range address, assert NAK/error without process termination, save `.omo/evidence/task-3-hardware-external-simulator-failure.txt`.
  Commit: Y | `feat(simulator): add XGT device memory`

- [ ] 4. Build deterministic synthetic thermal scenes and capture files
  What to do / Must NOT do: Add `SyntheticThermalScene.NextFrame(cameraIndex)` producing valid 14-bit 640x480-by-default frames with stable per-camera background and moving hot spot, plus `SyntheticCaptureStore` that persists a JPEG under the configured simulator output and returns both path and bytes. Reuse existing `ThermalFrame`, `ThermalPreviewEncoder` and `ThermalColorizer`; do not copy OpenCV camera acquisition, NUC logic or invent another image DTO. Inject a tick/clock for deterministic tests; never use random pixels.
  Parallelization: Wave 1 | Blocked by: none | Blocks: T7
  References: `HeatingCameraSystem.Protocols/Simulation/FakeThermalFrameSource.cs:8-60` synthetic pattern precedent; `HeatingCameraSystem.Protocols/Simulation/FakeLiveThermalCamera.cs:102-129`; `HeatingCameraSystem.Protocols/Cameras/ThermalPreviewEncoder.cs`; `HeatingCameraSystem.Core/Models/NatsMessages.cs:42-60`; `HeatingCameraSystem.Agent/Services/FakeCameraCaptureService.cs:36-59` storage/identity precedent.
  Acceptance criteria: `SyntheticThermalSceneTests` asserts dimensions, all pixels `<=0x3FFF`, deterministic same-camera/tick output, different camera identity, moving hotspot, valid JPEG magic bytes and unique sanitized output paths; no hardware access occurs.
  QA scenarios: Happy — focused test output `.omo/evidence/task-4-hardware-external-simulator.txt`; Failure — configure an unwritable output path and assert a typed failure propagated to the caller without a partial success record, evidence `.omo/evidence/task-4-hardware-external-simulator-failure.txt`.
  Commit: Y | `feat(simulator): generate synthetic thermal captures`

- [ ] 5. Make Dashboard live/trend data flow testable and bounded
  What to do / Must NOT do: Extend `CameraNode`/`DashboardViewModel` with `LiveImage`, `LastLiveFrameUtc`, `OnlineAgentCount`, and bounded temperature/humidity samples. Add a public dependency-taking constructor (`IPlcController`, `INatsCommunicationService`, recipe repository/loader abstraction as required, `bool startTimers`) while preserving the parameterless production constructor. Subscribe to `agent.live.>` through existing service, decode on a background thread, freeze the bitmap, and marshal bound-property/collection changes to the Dispatcher. Normalize the latest 60 samples into two `PointCollection`s in a fixed 0..100 x 0..40 coordinate space. Cache the Dashboard instance in `MainViewModel` so navigation does not multiply long-lived NATS subscriptions/timers. Do not add a new event bus, global live-frame store or chart package.
  Parallelization: Wave 1 | Blocked by: none | Blocks: T10
  References: `HeatingCameraSystem.Master/ViewModels/DashboardViewModel.cs:59-123,136-192`; `HeatingCameraSystem.Master/ViewModels/LiveViewModel.cs:20-85` decode/Dispatcher precedent; `HeatingCameraSystem.Protocols/NatsCommunicationService.cs:72-83,111-135`; `HeatingCameraSystem.Master/ViewModels/MainViewModel.cs:14-32`; `HeatingCameraSystem.Master/Views/DashboardView.xaml:186-194,219-242,256-267`.
  Acceptance criteria: `DashboardLiveTrendTests` sends status/live messages through fakes, verifies one camera tile update on Dispatcher, stale timestamp handling, online count, max 60 samples, correct normalized point bounds and same cached Dashboard object after repeated navigation; focused tests exit 0.
  QA scenarios: Happy — focused tests save `.omo/evidence/task-5-hardware-external-simulator.txt`; Failure — malformed JPEG and background callback must not crash or mutate `LiveImage`, and 61st sample must evict the oldest, evidence `.omo/evidence/task-5-hardware-external-simulator-failure.txt`.
  Commit: Y | `feat(master): add dashboard live trend pipeline`

- [ ] 6. Implement the external FEnet chamber/blackbody/servo endpoint
  What to do / Must NOT do: Add `FEnetPlcSimulator : IDisposable` with `Start()`/`Stop()` and `PlcDynamicsEngine`. Wire all four simulation-service request events to T3 memory. Initialize default PV/SV and status cells from settings. Every 100 ms move online chamber PV, humidity PV and both blackbody PVs toward their SVs at configured capped rates. Detect point/absolute move trigger rising edges; set X/Y busy bits immediately, hold for exactly configured 500 ms, then update target positions/current point and clear busy. Mirror equipment, admin, fan, error and IO writes/reads. `plc offline` stops accepting/responding; reconnect recreates the listener cleanly. Unsupported requests produce NAK, not crashes. No random behavior and no wall-clock sleeps inside request handlers.
  Parallelization: Wave 2 | Blocked by: T1, T2, T3 | Blocks: T8, T9, T11
  References: `HeatingCameraSystem.Protocols/PlcXgtClient.cs:58-215` full production request surface; `HeatingCameraSystem.Core/Config/HardwareSettings.cs:59-151` device map; `HeatingCameraSystem.Protocols/Simulation/FakePlcController.cs:33-241` expected state semantics only; official FEnet server sample; `HeatingCameraSystem.Master/Services/RecipeEngine.cs:57-96` polling/tolerance requirements.
  Acceptance criteria: `ExternalPlcSimulatorTests` uses real `PlcXgtClient` against loopback to verify connect, initial 25°C/50%RH, chamber ramp from 25→40°C within 1.5 s at 20°C/s and within 0.5°C tolerance, blackbody 25→55°C within 1.5 s at 30°C/s, servo busy observed before completion and cleared with target coordinates after 500 ms, equipment/admin/error/IO roundtrips, disconnect failure and restart recovery. No test directly invokes fake PLC APIs.
  QA scenarios: Happy — focused suite output `.omo/evidence/task-6-hardware-external-simulator.txt`; Failure — toggle PLC offline mid-ramp, assert client read fails within its 3 s timeout and state resumes after online without corruption, evidence `.omo/evidence/task-6-hardware-external-simulator-failure.txt`.
  Commit: Y | `feat(simulator): emulate XGT chamber and blackbody`

- [ ] 7. Implement external NATS camera agents
  What to do / Must NOT do: Add `NatsCameraAgentSimulator : IAsyncDisposable` using `NatsCommunicationService` and T4 generation/storage. For every configured camera, subscribe to its capture subject and the existing broadcast behavior, publish status every configured 5 s, publish color-JPEG live frames every 100 ms, and preserve AgentId/CameraIndex/RecipeStepId/Timestamp. Online cameras capture successfully with nonempty bytes/path; Faulted cameras publish an immediate failed result; Offline cameras publish no heartbeat/live/capture response. Prevent duplicate broadcast handling and isolate one camera failure from others. Do not add subjects, DTOs or bypass NATS serialization.
  Parallelization: Wave 2 | Blocked by: T1, T2, T4 | Blocks: T8, T11
  References: `HeatingCameraSystem.Protocols/Cameras/CameraNatsConnector.cs:11-20,81-99,101-223`; `HeatingCameraSystem.Protocols/NatsCommunicationService.cs:29-83`; `HeatingCameraSystem.Core/Models/NatsMessages.cs:27-60`; `HeatingCameraSystem.Tests/CameraNatsConnectorTests.cs`; `HeatingCameraSystem.Tests/NatsIntegrationTests.cs:10-104` broker probing pattern.
  Acceptance criteria: `ExternalCameraAgentIntegrationTests` against local NATS verifies two identities, heartbeat fields, at least three valid live JPEG frames per camera within 2 s, targeted and broadcast captures, unique saved files, exact RecipeStepId preservation, Faulted failure response and Offline silence past one heartbeat interval. Tests are tagged `Category=ExternalNats` and report explicit skip only when broker probe fails.
  QA scenarios: Happy — start repo Docker NATS, run focused integration tests, capture `.omo/evidence/task-7-hardware-external-simulator.txt`; Failure — set Agent_1 Offline, assert Agent_0 continues and Agent_1 emits no status/live/result, evidence `.omo/evidence/task-7-hardware-external-simulator-failure.txt`.
  Commit: Y | `feat(simulator): emulate NATS camera agents`

- [ ] 8. Compose the simulator host and bounded console management
  What to do / Must NOT do: Add `SimulatorHost` and `Program.Main`. Load one JSON path argument (default `simulator.json` next to executable), construct T6/T7, start PLC then NATS, print one readiness line `SIMULATOR READY plc=<address>:<port> cameras=<count> nats=<url>`, and stop gracefully on Ctrl+C/`quit`. Implement only approved commands: `status`, `plc online|offline`, `plc fault <0-19> on|off`, `camera <AgentId> online|fault|offline`, `quit`; invalid commands print usage without terminating. Exit codes: 0 normal, 2 config, 3 FEnet bind/start, 4 NATS connect. Dispose all timers/listeners/NATS once. Do not add background service installation, GUI, hot reload or command DSL.
  Parallelization: Wave 2 | Blocked by: T1, T2, T6, T7 | Blocks: T11, T12
  References: `HeatingCameraSystem.ManagerE2EDriver/Program.cs:38-87,310-318` exit/startup precedent; `HeatingCameraSystem.Agent/Program.cs:93-102` Ctrl+C lifetime; T2 contracts `SimulatorSettings`/`SimulatorState`; T6 `FEnetPlcSimulator`; T7 `NatsCameraAgentSimulator`.
  Acceptance criteria: `SimulatorHostTests` verifies startup order, readiness text, every command/state transition, invalid-command recovery, Ctrl+C-equivalent cancellation and idempotent disposal. Running with valid config while NATS is available remains alive until cancellation; invalid JSON, occupied port and unavailable NATS return exact nonzero codes without orphan processes.
  QA scenarios: Happy — PowerShell starts the executable, waits for readiness, pipes `status` then `quit`, verifies exit 0 and saves `.omo/evidence/task-8-hardware-external-simulator.txt`; Failure — occupy port 2004 and assert exit 3 within 5 s, then verify no listener remains, evidence `.omo/evidence/task-8-hardware-external-simulator-failure.txt`.
  Commit: Y | `feat(simulator): add host and console controls`

- [ ] 9. Route real-mode blackbody operations through the external PLC boundary
  What to do / Must NOT do: Change only Master service selection so `SimulationMode=true` preserves `FakeBlackBodyController`, while `SimulationMode=false` creates `PlcBlackBodyAdapter(PlcController)`. Keep direct-controller replacement explicitly pending physical protocol/spec. Add adapter contract tests over T6's real FEnet endpoint and a focused service-selection assertion at the smallest testable seam; if necessary extract only a tiny `CreateBlackBodyController(settings, plc)` factory method, not a DI rewrite. Do not change recipe semantics or remove existing fakes.
  Parallelization: Wave 2 | Blocked by: T6 | Blocks: T11
  References: `HeatingCameraSystem.Master/Services/AppServices.cs:68-93`; `HeatingCameraSystem.Protocols/PlcBlackBodyAdapter.cs:8-39`; `HeatingCameraSystem.Protocols/Simulation/FakeBlackBodyController.cs:8-59`; `HeatingCameraSystem.Master/Services/RecipeEngine.cs:20-42,88-96`; `HeatingCameraSystem.Core/Interfaces/IBlackBodyController.cs:6-30`.
  Acceptance criteria: focused `BlackBodyRoutingTests` proves true mode selects Fake, false mode selects PLC adapter, and adapter writes/reads BB0 and BB1 independently through real `PlcXgtClient`+external FEnet server with accelerated PV convergence. Full build has zero warnings.
  QA scenarios: Happy — focused output `.omo/evidence/task-9-hardware-external-simulator.txt`; Failure — stop FEnet endpoint before `SetTemperatureAsync`, assert operation fails rather than silently snapping local fake state, evidence `.omo/evidence/task-9-hardware-external-simulator-failure.txt`.
  Commit: Y | `fix(master): route real blackbody through PLC`

- [ ] 10. Replace Dashboard camera/chart placeholders with bound WPF primitives
  What to do / Must NOT do: In each populated camera tile render `Image Source={Binding Camera.LiveImage}` and show `No Signal` only when image is null or older than 2 s; preserve empty-slot drag/drop state. Replace chart placeholder with a fixed-coordinate `Viewbox`/`Canvas` containing two bound `Polyline`s plus current value labels/legend. Bind the sidebar count to `OnlineAgentCount` instead of hardcoded `64 Active Units`. Add a clear empty/no-data state and preserve current dark resources/layout. Do not alter unrelated navigation, controls, drag/drop, recipe controls or add external chart packages.
  Parallelization: Wave 2 | Blocked by: T5 | Blocks: T11
  References: `HeatingCameraSystem.Master/Views/DashboardView.xaml:137-200,206-242,256-267`; `HeatingCameraSystem.Master/ViewModels/DashboardViewModel.cs:180-192`; T5 properties/tests; `HeatingCameraSystem.Master/Views/LiveView.xaml:18-30` image presentation precedent.
  Acceptance criteria: `dotnet build HeatingCameraSystem.Master/HeatingCameraSystem.Master.csproj` exits 0 with zero binding/compiler warnings; agent-operated WPF run shows two simulator cameras updating, `No Signal` for an Offline camera within 2 s, bounded red/cyan trend lines after PLC samples, and online count changing with camera state.
  QA scenarios: Happy — Windows MCP launches Simulator+Master, waits 6 s, captures `.omo/evidence/task-10-hardware-external-simulator.png`; Failure — issue `camera Agent_1 offline`, wait 6 s, capture Agent_1 `No Signal`, Agent_0 still moving, and updated online count in `.omo/evidence/task-10-hardware-external-simulator-failure.png`.
  Commit: Y | `feat(master): render dashboard live feeds and trends`

- [ ] 11. Prove the full external hardware-free roundtrip
  What to do / Must NOT do: Add an external mode to the existing `HeatingCameraSystem.E2EDriver` (preserve its current default behavior) that uses real `PlcXgtClient`, `PlcBlackBodyAdapter` and real NATS while the separate Simulator supplies PLC and camera endpoints. Execute a two-camera/four-step recipe-equivalent flow: chamber 30°C/55%RH convergence, XY/point move busy transition, BB targets 35/40/45/50°C convergence, capture commands, image-byte/path verification and final state. It must never instantiate any `Protocols.Simulation.Fake*` type in external mode. Include failure exits for missing Simulator, missing NATS, camera Offline and PLC drop. Do not launch internal fake Agents.
  Parallelization: Wave 3 | Blocked by: T6, T7, T8, T9, T10 | Blocks: final verification
  References: `HeatingCameraSystem.E2EDriver/Program.cs:10-129`; `HeatingCameraSystem.ManagerE2EDriver/Program.cs:26-28,310-318` exit-code pattern; `HeatingCameraSystem.Master/Services/RecipeEngine.cs:44-156`; `HeatingCameraSystem.Protocols/PlcXgtClient.cs:58-215`; `HeatingCameraSystem.Protocols/PlcBlackBodyAdapter.cs:18-37`.
  Acceptance criteria: with NATS+Simulator running, `dotnet run --project HeatingCameraSystem.E2EDriver -- --external-simulator nats://127.0.0.1:4222 127.0.0.1 2004` exits 0, prints `*** PASS ***`, receives exactly four successful captures (two per Agent), writes four nonempty images, and verifies final chamber/BB/servo state. Static check plus runtime logs confirm no `Fake*` construction on this path.
  QA scenarios: Happy — exact command output `.omo/evidence/task-11-hardware-external-simulator.txt`; Failure — toggle Agent_1 Offline before its step and assert the driver exits its documented camera-timeout code within configured timeout, then toggle PLC Offline and assert the documented PLC error exit, evidence `.omo/evidence/task-11-hardware-external-simulator-failure.txt`.
  Commit: Y | `test(e2e): verify external simulator roundtrip`

- [ ] 12. Add sample configuration and one-command lifecycle scripts
  What to do / Must NOT do: Add `HeatingCameraSystem.Simulator/simulator.example.json` matching T2 defaults and `docs/deployment/run-external-simulator-e2e.ps1`. Script parameters allow NATS URL, FEnet port and configuration; it builds, starts repo Docker NATS if requested, starts Simulator as a child, waits for the exact readiness line, runs T11, and always terminates only processes/containers it started in `finally`. Add a separate `start-external-simulator.ps1` that copies the example only when the target config is absent. Never overwrite `%LOCALAPPDATA%\HeatingCameraSystem\hardware.json`, existing simulator config, user NATS containers or dirty files.
  Parallelization: Wave 3 | Blocked by: T8 | Blocks: final verification
  References: `docs/deployment/run-e2e-simulation.ps1`; `docs/deployment/docker-compose.yml`; `docs/samples/hardware.simulation.json`; T8 readiness/exit contracts; T11 command.
  Acceptance criteria: from a clean shell with NATS available, `powershell -ExecutionPolicy Bypass -File docs/deployment/run-external-simulator-e2e.ps1` exits 0 and prints the Simulator readiness plus E2E PASS; rerunning is idempotent; forced E2E failure still leaves no Simulator child and does not stop a pre-existing NATS container.
  QA scenarios: Happy — script log `.omo/evidence/task-12-hardware-external-simulator.txt`; Failure — invoke with an occupied FEnet port and assert nonzero exit, actionable message and complete child cleanup, evidence `.omo/evidence/task-12-hardware-external-simulator-failure.txt`.
  Commit: Y | `chore(simulator): add launch and E2E scripts`

- [ ] 13. Reconcile operator/developer documentation
  What to do / Must NOT do: Rewrite `docs/deployment/simulation-mode.md` to clearly separate legacy in-process SimulationMode from the new external Simulator, replace stale Modbus/FluentModbus/test-count claims with XGT FEnet and current commands, document camera/chamber/blackbody coverage and COM/AgentUI limitations, and link the new executable/config/scripts. Update `README.md`, `docs/manual/README.md`, `docs/manual/00-overview.md`, `01-installation.md`, `02-configuration.md` and `03-usage.md` only where they contain stale protocol/project/simulator instructions. Include safe Master configuration guidance: `SimulationMode=false`, PLC `127.0.0.1:2004`, local NATS, and backup/restore rather than destructive replacement. Do not promise physical timing, shutter/COM, AgentUI live-camera or real-blackbody protocol coverage.
  Parallelization: Wave 3 | Blocked by: T6, T7, T8, T9, T10 | Blocks: final verification
  References: `docs/deployment/simulation-mode.md:1-140`; `docs/manual/README.md:14-31`; `docs/manual/00-overview.md`; `docs/manual/01-installation.md`; `docs/manual/02-configuration.md`; `docs/manual/03-usage.md`; `README.md`; `AGENTS.md` known hardware placeholders/architecture.
  Acceptance criteria: documentation search finds no claim that the current PLC is Modbus/port 502 or that the obsolete test total is current; every documented command/path exists; a fresh reader can choose internal Fake mode or external Simulator without ambiguity; link checker/path existence PowerShell assertions pass.
  QA scenarios: Happy — PowerShell validates all referenced local paths/commands and saves `.omo/evidence/task-13-hardware-external-simulator.txt`; Failure — search for stale `PlcModbusClient|FluentModbus|port 502|38/38|59/59` in active manuals returns zero undocumented hits, report `.omo/evidence/task-13-hardware-external-simulator-failure.txt`.
  Commit: Y | `docs(simulator): document external hardware testing`

## Final verification wave
> Runs in parallel after ALL todos. ALL must APPROVE. Surface results and wait for the user's explicit okay before declaring complete.
- [ ] F1. **Plan compliance audit** — Oracle reads this plan and the final diff, maps every Must-have/Must-NOT and T1-T13 criterion to concrete code/test/evidence, rejects self-reported or grep-only done claims, and writes `.omo/evidence/final-f1-hardware-external-simulator.md`. Must return unconditional APPROVE.
- [ ] F2. **Code quality/security review** — reviewer checks FEnet/NATS disposal, loop cancellation, bounds validation, thread safety, loopback exposure, path handling, no random timing, no swallowed startup errors, nullable compliance, no file over 250 pure LOC without justified split, and dirty-worktree isolation; run build/tests and write `.omo/evidence/final-f2-hardware-external-simulator.md`. Must APPROVE.
- [ ] F3. **Agent-operated real UI QA** — with NATS and standalone Simulator, back up `%LOCALAPPDATA%\HeatingCameraSystem\hardware.json`, write a temporary real-mode loopback config, launch Master via Windows MCP, verify two live feeds, online count, trends, recipe/control/status interactions, then camera/PLC failure states; capture screenshots/logs and restore the original config in `finally`, proving its hash matches. Evidence `.omo/evidence/final-f3-hardware-external-simulator/`. Must APPROVE; no user intervention.
- [ ] F4. **Scope fidelity and external-boundary audit** — verify external E2E runs Master/client code with `SimulationMode=false`, Simulator communicates only over FEnet/NATS, no new simulator mode/subject/DTO/dependency exists, COM/AgentUI hardware emulation remains absent, existing internal SimulationMode still passes regression tests, and only intended files were staged. Evidence `.omo/evidence/final-f4-hardware-external-simulator.md`. Must APPROVE.

## Commit strategy
- Use the atomic commit listed on each todo; merge dependent contract/implementation commits only if the execution environment cannot build between parallel commits.
- Stage exact paths (`git add -- <paths>`) per todo. Never use `git add -A`, `git add .`, destructive clean/reset or broad formatting because the worktree already contains unrelated user artifacts.
- Keep package versions aligned with existing Protocols references: `VagabondK.Protocols.LSElectric` 1.1.21, `VagabondK.Protocols.Channels` 1.1.22, `NATS.Net` 2.8.1. No dependency upgrades in this plan.
- Before each commit: focused tests for that todo; before handoff: complete solution build/test plus T11/T12 external E2E.

## Success criteria
- `HeatingCameraSystem.Simulator` builds as a separately launchable process and reaches readiness on loopback with two cameras.
- A real `PlcXgtClient` reads/writes the external FEnet endpoint and observes deterministic chamber, blackbody and servo transitions; no custom FEnet parser exists.
- Existing NATS contracts deliver camera heartbeat, live frame and capture roundtrips from the separate process with exact identity/step preservation.
- External E2E uses no in-process `Fake*` construction, exits 0 and verifies four captures plus final PLC/BB/servo state.
- Master `SimulationMode=false` routes blackbody over PLC, while existing `SimulationMode=true` behavior remains regression-tested.
- Dashboard replaces both hardcoded placeholders, shows two updating camera feeds, bounded trends, stale/offline fallback and accurate online count.
- `dotnet build HeatingCameraSystem.slnx` and `dotnet test HeatingCameraSystem.slnx --no-build` pass with zero errors/warnings and no new flaky timing tests.
- One-command script starts/verifies/cleans the external simulator safely; active manuals describe XGT FEnet and distinguish internal Fake mode from the external Simulator.
- Final F1-F4 reviewers all return unconditional APPROVE, evidence artifacts exist, user config is restored byte-for-byte, and unrelated dirty files remain untouched.
