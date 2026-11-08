# 12.6 Several bodies per object

> Step 12.6 of the `unity` repository. Its `implementation/12-6-tier1-bodies-rapier.md` holds the full listings for this repository, `12-6-tier1-bodies-core.md` the contract and the Core side (`ChildBody`, child bodies of `StandaloneEntity`, proxies per body), and `12-6-tier2-bodies-rigidbody.md` `NetworkObject.Bodies` and the Rigidbody backend. The design is P27 of `design/principles.md` there, line "Nhiều body" (Q225 (8) A, Q232 A), P16 line "Khi nào đọc" (Q232 (4) A, Q233 A), and P20 for the Core used by CI (Q227). Status: Applied.

## What changed

| Part | Change |
|---|---|
| `RapierScenes` | `PlaceProxy` takes the body as well as the entity, in a 3D and a 2D overload, and turns only that body into a proxy. The world type is compared as well as the handle, because the 3D and 2D worlds of one scene can hand out the same handle value. `EndProxy` still restores every proxied body of the entity |
| `RapierContacts` | Keeps the source `Collider` list of each body instead of one list per object, so contact events and contact points on a child body name the collider of that child. `AddBody` and `AddBody2D` take one body each |
| `RapierPhysics` | An object is now a list of parts, one per body. The root body comes first, then every child `Rigidbody` (or `Rigidbody2D`) whose GameObjects up to the root are all active. Each part has its own write-back, its own saved `isKinematic` or `bodyType`, and its own motion check (Q179). `BodiesOf` and `Bodies2DOf` return the bodies in that order |
| Client proxies | The root body always becomes a proxy. A child body becomes one only when its GameObject has a `NetworkTransform`, since only then does its pose come from the server (Q232 (3) A). Other child bodies are simulated by the client's own world |
| `bench/` | A console benchmark with ragdolls of 11 box bodies and no joints, on a console server and one loopback client |
| CI | `CORE_COMMIT` is `0cd6d52`, the Core split of this step pushed to `main` of `fomoxa/com.fomoxa.networking` without a tag (Q227). A new step builds the benchmark against that Core |

The crate, the golden scenario and the snapshot format (3) are unchanged, so this step has no native libraries to rebuild.

## Benchmark

Each ragdoll is a box torso with 10 box limbs declared through `GetChildBodies`, lying flat so that every body falls asleep. Every body has its own transform slot, and a counting source wraps each slot to record how often the runtime reads it. The benchmark times only the server frame. The falling phase is the first 30 ticks after the spawn. The resting phase is 60 ticks after 600 more.

```bash
dotnet run -c Release --project bench/Fomoxa.Rapier.Bench.csproj -- --ragdolls 10,100,500
```

Ubuntu 26.04 LTS on WSL, X64, 20 logical processors, .NET 8.0.30:

| Ragdolls | Bodies | Phase | Mean ms | p50 ms | p95 ms | Max ms | Slots read per tick |
|---:|---:|---|---:|---:|---:|---:|---:|
| 10 | 110 | falling | 0.379 | 0.158 | 1.258 | 4.652 | 110.0 |
| 10 | 110 | resting | 0.005 | 0.005 | 0.007 | 0.008 | 0.0 |
| 100 | 1100 | falling | 2.471 | 1.967 | 3.973 | 8.236 | 1100.0 |
| 100 | 1100 | resting | 0.050 | 0.047 | 0.067 | 0.104 | 0.0 |
| 500 | 5500 | falling | 13.047 | 9.315 | 20.763 | 67.635 | 5500.0 |
| 500 | 5500 | resting | 0.351 | 0.337 | 0.411 | 0.527 | 0.0 |

While the bodies fall, each slot is read once per tick. Once they sleep, no slot is read, so a resting object costs the same whether it has one body or eleven. The benchmark has no pass threshold and CI only builds it. Step 12.7 runs it again with joints.

An earlier version used sphere limbs piled on the torso. The spheres kept rolling and the pile kept jittering, so the bodies never slept and the resting phase still read 11 slots per tick for a single ragdoll.

## Results

- `dotnet test tests/`: 66/66 on Linux (.NET 8), 65 before this step plus 1 new test.
- Unity EditMode tests of the package on Windows, with the Core of `unity` at `8d00a05`: 18 tests, 17 passed, 0 failed. The skipped test is `TwoSidedClientCheck`, which runs only inside the two-sided check. The two new tests cover a child `Rigidbody` that Rapier moves on the server, and the proxy rule on the client.
- Two-sided check, Windows server: PASS. 1492 common ticks, the same world hash on every one, 0 reconcile mismatches.
- Two-sided check, Linux server: fails as in 11.10 and 12.3 to 12.5. The Unity client on Windows stays in `Starting` and the server records no ticks. The cause is the network path from WSL to Windows, not this step.
