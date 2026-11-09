# 12.7 Joints

> Step 12.7 of the `unity` repository. Its `implementation/12-7-tier1-joints-rapier.md` holds the full listings for this repository. The design is P27 of `design/principles.md` there, line "Joint" (Q225 (5) A, Q234 (1) to (4) A, Q235 (1) A and (2) A, Q236 A), and line "Nhiều body" for the benchmark (Q232 (5) A). The Core is unchanged and `CORE_COMMIT` stays `0cd6d52`. Status: Applied.

## What changed

| Part | Change |
|---|---|
| Crate | Fixed, revolute, prismatic, spherical, rope and spring joints in the 3D world, and the same set without spherical in the 2D world, through `fr_joint_create`, `fr_joint_remove`, `fr_joint_contains`, `fr_joint_set` and their `fr2_` versions. Frames are local to each body and the main axis is X. Motors are force based. A joint to the world uses a fixed anchor body that the world creates the first time it is needed. That body has no id, so it is not in the hash, the moving body list or the queries |
| `Load` | The world keeps a joint table, and the snapshot (format `4`) stores it with the anchor body. A joint in both the snapshot and the current table keeps its handle, its solver state and its description from the snapshot. A joint created after the snapshot, or one attached to a rebuilt body, is built again from its current description under the same id. A joint removed after the snapshot stays removed. Removing a body removes its joints |
| Golden scenario | One `joints` scenario per world after the `contacts` lines: a chain of box links with every joint kind, a rope created at tick 61 and a `SetJoint` at tick 70, with a hash line, a `replay` line and a `load` line. The 34 older lines keep their values |
| C# API | `JointDesc`, `JointDesc2D`, `JointHandle`, `JointKind`, `JointLimit`, `JointMotorDesc`, `JointSoftness`. `RapierWorld` and `RapierWorld2D` gain `CreateJoint`, `RemoveJoint`, `Contains` and `SetJoint`. `RapierScenes.CreateJoint` attaches a joint to a body of an entity, so `RemoveBodies` removes the joint too. The motor type is `JointMotorDesc` because `JointMotor` clashes with `UnityEngine.JointMotor` in code that uses both namespaces |
| `RapierJoints` | Converts `FixedJoint`, `HingeJoint`, `SpringJoint`, `CharacterJoint`, `FixedJoint2D`, `HingeJoint2D`, `SliderJoint2D`, `SpringJoint2D` and `DistanceJoint2D`. The angle and motor signs were measured against PhysX and Box2D. Parameters Rapier cannot honour are named in one warning per component |
| `RapierPhysics` | After building every body of an object, `Bind` builds the joint components of each part. The connected body must be empty (the world) or a body of the same object. A finite break force or torque is reported as an error and the joint is built without breaking. Other component types are reported as unsupported |
| `bench/` | Each ragdoll can have 10 joints, one per limb. Every ragdoll count runs once without and once with joints |

## Benchmark

Same ragdolls as step 12.6, with spherical joints at the head, shoulders, waist and thighs and revolute joints at the forearms and shins. The server creates the joints in `OnStartServer` of the torso behaviour.

```bash
dotnet run -c Release --project bench/Fomoxa.Rapier.Bench.csproj -- --ragdolls 10,100,500
```

Ubuntu 26.04 LTS on WSL, X64, 20 logical processors, .NET 8.0.30, at commit `a2c0f39`:

| Ragdolls | Bodies | Joints | Phase | Mean ms | p50 ms | p95 ms | Max ms | Slots read per tick |
|---:|---:|---:|---|---:|---:|---:|---:|---:|
| 10 | 110 | 0 | falling | 0.729 | 0.311 | 2.191 | 8.420 | 110.0 |
| 10 | 110 | 0 | resting | 0.011 | 0.010 | 0.015 | 0.049 | 0.0 |
| 10 | 110 | 100 | falling | 1.913 | 1.973 | 2.935 | 4.599 | 110.0 |
| 10 | 110 | 100 | resting | 0.019 | 0.016 | 0.031 | 0.142 | 0.0 |
| 100 | 1100 | 0 | falling | 4.210 | 3.396 | 6.877 | 11.456 | 1100.0 |
| 100 | 1100 | 0 | resting | 0.084 | 0.077 | 0.115 | 0.161 | 0.0 |
| 100 | 1100 | 1000 | falling | 8.276 | 8.664 | 12.203 | 14.369 | 1100.0 |
| 100 | 1100 | 1000 | resting | 0.074 | 0.070 | 0.092 | 0.116 | 0.0 |
| 500 | 5500 | 0 | falling | 15.401 | 10.048 | 20.730 | 96.061 | 5500.0 |
| 500 | 5500 | 0 | resting | 0.655 | 0.656 | 0.776 | 0.844 | 0.0 |
| 500 | 5500 | 5000 | falling | 40.088 | 43.520 | 55.230 | 119.696 | 5500.0 |
| 500 | 5500 | 5000 | resting | 0.550 | 0.534 | 0.673 | 0.727 | 0.0 |

Jointed bodies still fall asleep, so a resting ragdoll reads no slot and costs about the same with or without joints. While the ragdolls fall, joints make a tick 2.0 to 2.6 times slower. The rows without joints are slower than the table of step 12.6 on the same machine because the machine was in a different state, so compare rows of one run only. The benchmark has no pass threshold.

## Results

- `Tools/check-golden.sh` on every runner of the `native-libraries` workflow at `30c7c94`: the hashes match the new `golden.txt`.
- `dotnet test tests/`: 77/77 on Linux (.NET 8), 66 before this step plus 11 new tests.
- Unity EditMode tests of the package on Windows, with the Core of `unity` at `8d00a05`: 26 tests, 25 passed, 0 failed. The skipped test is `TwoSidedClientCheck`, which runs only inside the two-sided check. The new tests compare hinges, character joints, spring joints, 2D hinges, sliders, distance joints and 2D spring joints with PhysX and Box2D, and cover joints of a prefab at spawn and a jointed limb simulated by the client against its proxy root.
- Two-sided check, Windows server: PASS. 1477 common ticks, the same world hash on every one, 0 reconcile mismatches.
- Two-sided check, Linux server: fails as in 11.10 and 12.3 to 12.6. The Unity client on Windows stays in `Starting` and the server records no ticks. The cause is the network path from WSL to Windows, not this step.
