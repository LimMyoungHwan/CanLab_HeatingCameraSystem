# F4 — Scope fidelity & external-boundary audit

**Result: APPROVE** (Oracle independent audit, 2026-07-29)

F4 was the one final-wave gate never completed — the prior session's reviewer models were all
rate-limited/unavailable, leaving F4 inconclusive (see `final-audit-remediation.txt` §F4 note).
Simulator code is unchanged since commit `b2fcffb` (all later commits = UI restructure + view
cleanup), so the earlier F1/F2/F3 verdicts remain valid; only F4 was re-run.

## Per-criterion findings (all PASS)

1. **External E2E uses production client with SimulationMode=false** — `E2EDriver` `--external-simulator`
   branches to `RunExternalSimulatorAsync` before any fake path; constructs real `PlcXgtClient` +
   `PlcBlackBodyAdapter` + `NatsCommunicationService` (Program.cs:20,39,51,67). Master real mode
   (AppServices.cs:68,77,93) creates `PlcXgtClient` + real serial/camera services + `ConnectionMonitor`.
2. **Simulator communicates only over FEnet/NATS** — `FEnetPlcSimulator` via `TcpChannelProvider` +
   `FEnetSimulationService`; `NatsCameraAgentSimulator` via `NatsCommunicationService`.
   `git grep` = no `using HeatingCameraSystem.(Master|Agent|AgentUI)` under the simulator.
3. **No new mode/subject/DTO/dependency** — no `SimulationMode` usage in the simulator; the only Master
   sim/real change is `CreateBlackBodyController` routing (AppServices.cs:96). No hardcoded `master.`/`agent.`
   subject literals, no `*Message` DTOs. csproj references only Core + Protocols + pinned NATS.Net 2.8.1 /
   VagabondK.Protocols.LSElectric 1.1.21 / VagabondK.Protocols.Channels 1.1.22.
4. **COM/AgentUI hardware emulation absent** — `git grep -i` = no DirectShow / System.IO.Ports / SerialPort /
   ShutterController / VideoCapture / OpenCv in the simulator. Camera path is synthetic
   (`SyntheticThermalScene` / `SyntheticCaptureStore`), published over NATS, not hardware APIs.
5. **Internal SimulationMode regression intact** — fresh `dotnet test HeatingCameraSystem.slnx --no-build`
   = 190 passed / 0 failed / 0 skipped. Internal fake path still present + guarded in `E2EDriver.Main`.
6. **Only intended files staged** — `git diff --cached` empty at audit time; unrelated dirty/untracked
   `.bkit` / `.omo/run-continuation` / `.council` / docs archives / `ImageStorage` remain UNstaged per guardrail.

## Fresh external roundtrip re-proof (`final-f4-e2e-rerun.txt`)

`docs/deployment/run-external-simulator-e2e.ps1` (NATS already up, no docker) → **`*** PASS ***` exit 0**
- 4/4 captures, Agent_0 = 2, Agent_1 = 2, 0 missing (JPEGs 40–41 KB)
- Final PLC: T=30.0, H=55.0, point=4, busy=False (external state converged)
- Teardown verified: FEnet 2004 closed, no orphan simulator process

**F4 VERDICT: APPROVE**
