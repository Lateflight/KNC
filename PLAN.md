# Plan: KNC, a new KSP mod for GNC simulation with realistic sensors and comms

## Context

kRPC gives external Python and C++ programs full, perfect-knowledge access to the game over a local socket.
Two things about that don't fit what I want:
1. It ignores CommNet: a craft with no link is still controllable.
2. It exposes the *true* state (exact attitude, rates, position), so there is no estimation problem to solve.

The goal is a real GNC experience inside KSP:
- Flight software only sees **modeled sensors** (noise, bias, misalignment, quantization, saturation, sample rate).
- It commands **modeled actuators**.
- It talks to the ground over a **modeled comm link** (availability plus light-time delay).
- The true state sits in a **debug-only** channel, used for logging estimation error.

This is a rewrite. kRPC's source goes in `KNC/krpc/` as a **read-only reference**:
- Clone it at the 0.6.0 release, the version installed in KSP, and keep it out of git.
- Reading it for ideas is fine.
- Copying code from its LGPL/GPL files would bring those licenses into this mod.
- `krpc/protobuf/` is MIT, so its message-framing approach can be reused freely.
  The  installed kRPC ships no license file, so confirm this split in the clone's LICENSE.

## Architecture

```
            KSP process (C# plugin, runs every physics tick, 50 Hz)
 ┌──────────────────────────────────────────────────────────────────┐
 │ Truth (internal only) ─► Sensor models ─► SensorFrame             │
 │                                               │                   │
 │ Actuator models ◄─ ActuatorCommand ◄──────────┤  Flight computer  │
 │                                               │  (part module)    │
 │ Comm channel model (CommNet link, delay, buffering)               │
 │ Debug truth tap (off unless enabled in settings)                  │
 └──────┬──────────────────────┬──────────────────────┬─────────────┘
   onboard socket          ground socket          debug socket
        │                      │                      │
  your FSW (Python)     your ground SW (Python)   logger/plots (Python)
```

- **Transport:** local TCP with length-prefixed protobuf messages.
  - It doesn't matter that TCP always works: the comm *model* inside the plugin decides what gets through.
  - Protobuf generates Python and C++ code from one schema file, which covers the later C++ port.
  - Set `TCP_NODELAY` on both ends and send each framed message with a single write.
    Otherwise Nagle's algorithm plus Windows' delayed ACK can stall each lockstep round trip by up to ~200 ms.
  - Pin the plugin to **Google.Protobuf 3.10.1** and generate its C# code with protoc 3.10.1.
    - kRPC 0.6.0 ships that version in `GameData/kRPC` (a .NET Framework build with no extra DLLs), so the two mods can be installed together.
    - Newer releases add System.Memory/Unsafe DLLs that can clash with other mods (KSPBurst ships its own Unsafe).
    - Python uses current protobuf with its own protoc; the wire format is the same.
- **Timing: selectable in settings, real-time by default.**
  - **Real-time (default):** the plugin sends the `SensorFrame` and never waits.
    - It applies the newest command that has arrived and holds the last command if a deadline is missed.
    - It counts and reports deadline overruns.
    - A message can arrive in pieces over several ticks, so the plugin collects bytes until the whole message is in.
    - The delay comes from the host PC (Windows, Python, the game's frame rate), not from a modeled flight computer, so this mode answers "can my code keep up?".
      When the game runs slower than real time (heavy installs such as RP-1), the same solve time counts as less game time.
  - **Lockstep (repeatable option):** at each flight-software cycle, the plugin
    1. sends a `SensorFrame`;
    2. **blocks the physics tick** until the matching `ActuatorCommand` arrives (a timeout keeps the game from hanging, and is counted and logged because it makes the run timing-dependent);
    3. applies the command after a set **compute delay** (default one tick), which models the flight computer's latency.
    - Sensors, actuators and comms are modeled exactly as in real-time mode; only the host PC's timing is taken out.
    - Uses: rerunning a flight exactly to debug it, telling algorithm problems from timing problems (SIL before HIL), and sweeping the compute delay.
    - A slow Python loop slows the game down but never changes the results, like a non-real-time SIL.
    - The wait uses `Poll` with a limit of a few seconds, as a crash detector; it sleeps instead of burning CPU.
      Prefer it over the stream's read timeout, which on Windows can leave the connection unusable.
  - Both modes share one scheduler class with a `WaitForCommand` flag.
    Build lockstep first, in milestone 1: it is simpler (send, wait, apply) and makes the plumbing easy to debug. Add real-time right after it.
  - The FSW rate is set by the flight computer part (for example 10 or 25 Hz).
    Cycles are scheduled by game time (UT), not by counting ticks: a tick is 0.02 s only at 1×, and physics warp lengthens it (`TimeWarp.fixedDeltaTime`).
  - **Time warp:** physics warp is blocked while lockstep runs.
    Rails warp packs the vessel (no physics), so the FSW is suspended while `vessel.packed` and resumes with the real elapsed time.
  - kRPC's `Core.RPCServerUpdate` with its `BlockingRecv`/`RecvTimeout` settings (`krpc/core/src/Core.cs`) is the reference for blocking inside `FixedUpdate`.
- **Randomness:** every sensor has its own seeded random number generator, so runs are reproducible.
  - Seed it from the global seed and the part's `persistentId`, so identical parts get different streams.
  - Use a small generator whose whole state is one `ulong` (for example SplitMix64), plus Box–Muller for Gaussian noise.
    `System.Random` has no way to read or write its state, so it can't be saved with the craft.

## Project layout (new, in `C:\Users\iammi\Desktop\KNC\`)

```
plugin/KNC.csproj            .NET Framework 4.7.2, references KSP_x64_Data/Managed/*.dll; post-build copies to the dev install's GameData
plugin/src/Truth/            TruthState: reads Vessel/Part each tick, converts to clean frames (internal access only)
plugin/src/Sensors/          SensorModule base + ModuleKncGyro, ModuleKncAccel, later star tracker, sun sensor, altimeter, GNSS
plugin/src/Actuators/        throttle, RCS, reaction wheel, gimbal commands via Vessel.OnFlyByWire, with rate and saturation limits
plugin/src/FlightComputer/   ModuleKncComputer: FSW rate, power draw, program storage (OnSave/OnLoad), launches the onboard Python process
plugin/src/Comms/            link to KSC (vessel.Connection.IsConnectedHome), delay = Σ hop distance / c along the path home (CommNetwork.FindHome), message queues
plugin/src/Bus/              socket servers, framing, scheduler (real-time and lockstep)
plugin/tests/                dotnet test project for the model code; runs without KSP
proto/knc.proto              SensorFrame, ActuatorCommand, Uplink, Downlink, TruthFrame
GameData/KNC/Parts/*.cfg     sensor parts with noise parameters in the config
GameData/KNC/Patches/*.cfg   ModuleManager patch that adds ModuleKncComputer to stock probe cores
python/kgnc/                 SDK: OnboardApp.step(t, sensors) -> commands; Ground.uplink()/telemetry(); debug.truth()
examples/                    rate-damping (detumble) controller; attitude hold with a simple estimator
krpc/                        kRPC 0.6.0 source, read-only reference, not committed
```

Model code (sensor error models, random generator, framing, scheduler) uses no KSP or Unity types,
so `plugin/tests/` can check it with `dotnet test` without starting the game.

## Dev environment (checked 2026-09-27)

- **KSP:** 1.12.5.3190 (Unity 2019.4.18f1) from Steam, in `C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program`.
  The managed DLLs are in `KSP_x64_Data\Managed`.
- **Dev install:** the main install has about 60 mod folders (Kopernicus, Parallax, FAR, kOS, kRPC 0.6.0, …) and takes about 3.5 minutes to reach the main menu.
  - Deploy builds to a separate copy (for example `C:\KSP_dev`) whose GameData holds only `Squad`, `SquadExpansion`, ModuleManager 4.2.3 and KNC.
  - It restarts much faster, and the truth layer gets stock physics and aerodynamics.
- **Saves:** both existing saves use CommNet with `requireSignalForControl = True` and plasma blackout on.
- **Tools:** dotnet SDK 6.0.428, .NET Framework 4.7.2 targeting pack, Visual Studio 2026 Community, VS Code, Python 3.13.3.
  Still needed: protoc 3.10.1 for the C# code. Python can generate its code with the `grpcio-tools` package.

## Sensor model (for example, a gyro)

ω_meas = Q(ADC){ sat( (I + M)(I + S) ω_true + b(t) + n ) }

- **M:** misalignment. **S:** scale-factor error.
- **b(t):** bias = turn-on bias + first-order Gauss–Markov term (bias instability) + optional random walk (rate random walk).
  A random walk alone gives a +½ Allan-deviation slope, not a bias-instability floor.
- **n:** white noise (angle random walk).
- **Q:** quantization. **sat:** measurement range.

Rules for all sensors:
- Every parameter goes in the part `.cfg`.
- Each measurement is expressed in the **sensor's own frame**: the part's mount orientation times the internal mounting matrix.
- Noise is discretized with the actual sample interval.
- Sensor state (bias terms, generator state) is saved in `OnSave`/`OnLoad`, so a quickload continues the same sensor instead of power-cycling it.
- The accelerometer measures **specific force** (total acceleration minus gravity). KSP's `vessel.perturbation` may already be exactly that; check before relying on it.
  It is a vessel (center-of-mass) value: a sensor away from the CoM also sees ω×(ω×r) + α×r.
- Watch out for KSP frame quirks in the truth layer:
  - floating origin and Krakensbane velocity offset;
  - world frame co-rotating with the planet at low altitude: there, part angular velocity leaves out the body's rotation (Kerbin: about 60°/h), and the gyro truth must add it back;
  - physics jitter at part joints.
  Build and validate `TruthState` before any sensor uses it.

## Comm model

- **Onboard socket:** always live while the flight computer part exists and has ElectricCharge.
  - Onboard commands must act even with no link. Both saves use `requireSignalForControl = True`,
    so check that commands applied through `OnFlyByWire` aren't limited without a signal.
- **Ground socket:** a message is accepted only if the link is up.
  - The link is up when CommNet has a path to KSC (`IsConnectedHome`).
    `ControlPath` isn't enough: it leads to the nearest control source, which can be a remote-pilot vessel.
  - It is delivered at `UT_now + delay`; delivery is in game time, so time warp behaves correctly.
  - Messages are dropped if the link is down at delivery.
- **Uplink:** a program or parameters, stored in the computer part and saved with the craft.
  Stored programs are encoded (for example URL-safe base64): ConfigNode values are single-line, and `//` starts a comment.
- **Downlink:** buffered onboard during blackout, released when the link returns.

## Working style

The user is new to C# but experienced in programming and GNC:
- Explain C#, Unity and KSP concepts as they come up: `PartModule` lifecycle, `FixedUpdate`, `KSPField`, `.cfg`/ConfigNode, `OnFlyByWire`, Mono/.NET 4.7.2 limits.
- No need to explain general programming or GNC math.
- Keep the plugin code plain: small classes, no advanced C# tricks.
- Milestone 0a: the user writes all the code; Claude answers questions and reviews it.

## Milestones (each one runs end-to-end before the next starts)

0. **Learn C#, then the KSP API.**
   - **0a. C# basics, outside KSP.** A console app with `dotnet`, covering:
     - classes, properties, value vs reference types (`struct`/`class`);
     - lists and dictionaries, interfaces, exceptions;
     - `System.Random`, sockets (`TcpListener`).
     - Exercise: a C# TCP server that exchanges messages with a Python `socket` client.
     - Target `net472` (C# 7.3 by default), like the plugin, so everything learned works inside KSP.
   - **0b. Unity/KSP model.**
     - `MonoBehaviour` lifecycle: `Awake`/`Start`/`Update`/`FixedUpdate`.
     - `[KSPAddon]` vs `PartModule`, `[KSPField]` and `.cfg`/ConfigNode.
     - `Vessel`/`Part` objects, the `FlightGlobals` and `CommNet` APIs, `OnFlyByWire`.
     - `TimeWarp` (physics vs. rails warp, `Vessel.packed`), `TimingManager` (what runs when inside a tick), Krakensbane.
     - The CommNet calls this plan relies on: `IsConnectedHome`, `CommNetwork.FindHome`, `ControlPath`.
     - Reference: kRPC code in `krpc/service/SpaceCenter/src/` (read only).
   - **0c. Toolchain:** an empty plugin that loads, logs every `FixedUpdate` to `KSP.log`, and deploys on build to the dev install.
     - Then a toy `PartModule` that shows one `[KSPField]` value, read from a `.cfg`, in the part's right-click menu.
1. **Loop:** Python receives a TruthFrame on the debug socket each tick and sends a throttle command: first in lockstep, then in real-time mode.
2. **Truth layer:** clean inertial and body frames, checked against in-game readouts.
3. **IMU:** gyro and accelerometer parts with the full error model. FSW gets only these measurements.
   Example: detumble using only the gyro.
4. **Actuators:** RCS, reaction wheels, engine gimbal with limits.
   Decide the command level first:
   - `FlightCtrlState` (through `OnFlyByWire`) is one normalized [-1, 1] command for the whole vessel; KSP splits it across actuators, and SAS can interfere.
   - Physical per-actuator commands (wheel torque, gimbal angle, thruster on-time) need custom modules that apply torque and force directly.
5. **Flight computer:** probe-core module, onboard process launch, power dependence.
6. **Comms:** ground socket, CommNet gating, light-time delay, uplink and downlink.
7. **More sensors:** star tracker, sun sensor, radar altimeter, GNSS-like fix.
8. **C++ client:** from the same `.proto`.

## Long-horizon plans (after milestone 8)

These come later, but the rules under each one apply from milestone 1, so they don't need a rewrite.

- **RP-1 / Realism Overhaul / RSS.**
  - No planet constants in code: radius, GM and rotation rate come from the `CelestialBody` (Earth turns at 15°/h, Kerbin at 60°/h).
  - The truth layer uses double precision: at Earth-radius distances, single precision resolves only about 0.5 m.
  - Engines and RCS are commanded through their own part modules, so RealFuels (ignitions, ullage, minimum throttle) and TestFlight (failures) still apply.
    Custom force modules are only for non-propulsive actuators such as reaction wheels.
  - Comms use only the general CommNet API. RP-1 runs RealAntennas, which is built on CommNet; its link data rates could later limit the downlink.
  - Deal with these at integration time: Principia (it replaces gravity, which the accelerometer truth subtracts), RP-1 avionics (control limits and their own power draw),
    and RP-1 tech-tree and cost configs for KNC parts.
- **Microcontroller in the loop over UART.** The flight software runs on a microcontroller connected by a serial cable (UART or USB serial).
  - The onboard link is one interface with two implementations, TCP and serial. It carries all onboard traffic (sensor frames, commands, delivered uplinks, downlinks), so one cable is enough.
    The flight computer part then points at a serial port instead of launching a Python process.
  - A serial line can drop or corrupt bytes (TCP handles that for you), so serial frames need a start marker and a checksum to resynchronize after errors, for example COBS framing plus a CRC.
  - Keep messages small: a 115200-baud UART moves about 11.5 kB/s, roughly 230 bytes per direction per 50 Hz tick. Boards with native USB serial are much faster.
  - Keep the `.proto` usable with nanopb (protobuf for microcontrollers): give every list and string a maximum size.
    Prefer raw sensor counts (what real sensors send) or single-precision floats; many microcontrollers handle doubles slowly.
  - Real-time mode over serial is hardware-in-the-loop; lockstep over serial is processor-in-the-loop.
  - Reference: kRPC already supports serial, with its own `KRPC.IO.Ports.dll` and a C-nano client built on nanopb (read only, as with the rest of kRPC).

## Verification

- **Truth:** compare TruthState with KSP's own UI values (altitude, speed, and rates on a spinning craft).
  A craft parked on the pad should show Kerbin's rotation rate in the gyro truth (about 2.9e-4 rad/s).
- **Sensors:** first run the pure model in `plugin/tests` for hours of samples and fit the Allan deviation;
  the fitted noise terms should match the inputs.
  Then log about 1 hour of in-game gyro data together with truth through the debug channel, and compute the Allan deviation of (measured − true).
  A parked craft still moves (joint jitter, single-precision physics), and for a good gyro that motion can exceed the modeled noise.
- **Lockstep:** first measure KSP's own run-to-run spread: the same open-loop commands from one quicksave, in orbit (no clamps or ground contact).
  Then two lockstep runs with the same seed and quicksave should agree within that spread, with no timeouts logged.
- **Real-time:** an FSW that sleeps longer than its cycle gets its overruns counted and its last command held; a fast FSW shows no overruns.
- **Comms:** craft behind the Mun: ground messages are dropped, onboard FSW keeps flying, downlink arrives after the link returns.
  The measured delay should match the length of the path to KSC / c.
