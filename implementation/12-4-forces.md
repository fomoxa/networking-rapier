# 12.4 Torques, forces at a point and continuous collision

> Step 12.4 of the `unity` repository. Its `implementation/12-4-tier1-forces-rapier.md` holds the full listings for this repository, `12-4-tier1-forces-core.md` the contract and the Core side, and `12-4-tier2-forces-rigidbody.md` the Rigidbody backend. The design is P27 of `design/principles.md` there, line "Lực và CCD" (Q225 (3) A, Q228 A), with the force semantics of Q131, and P20 for the Core used by CI (Q227). Status: Applied.

## What changed

| Part | Change |
|---|---|
| Crate | `fr_body_add_torque`, `fr_body_add_torque_impulse`, `fr_body_add_force_at_point`, `fr_body_add_impulse_at_point` and their 2D versions (the 2D torque is a `f32`). `fr_body_create` and `fr2_body_create` take a `ccd` flag, which the body recipe keeps and `insert_body` passes to `ccd_enabled` |
| Step | `step` now calls `reset_torques` as well as `reset_forces`. Rapier keeps user torques across steps, and `add_force_at_point` writes one |
| `RapierNative`, `RapierWorld`, `RapierWorld2D` | Implement the four new members of `IPhysicsWorld` and `IPhysicsWorld2D`, and pass `BodyMotion.ContinuousCollision` to the library |
| Golden scenario | Two new lines per world after `inside`: `forces` (60 steps of torques, torque impulses, forces and impulses at a point, then the world hash) and `ccd` (two fast balls against a thin kinematic wall, one with the flag). The old lines keep their values |
| CI | `CORE_COMMIT` is `45ba933`, the Core split of this step pushed to `main` of `fomoxa/com.fomoxa.networking` without a tag (Q227) |

The snapshot format stays 3: the CCD flag lives in the Rapier body, which the snapshot already serializes, and the body recipe used to rebuild a removed body on `Load` carries it.

## What the CCD flag means in Rapier 0.36

A fast dynamic body always sweeps against fixed colliders in Rapier 0.36, whether the flag is set or not. The flag makes the body a "bullet" that also sweeps kinematic and dynamic bodies, except other bullets. So without the flag, a fast body on this backend does not go through a thin static wall, while on the Rigidbody backend with `Discrete` it can. Both sides of a predicted object use the same backend, so the difference does not cause reconcile mismatches.

The golden `ccd` line and the tests use a kinematic wall for that reason: the ball with the flag stops at it (`434be948`, 203.91 after bouncing back) and the ball without it goes through (`43660000`, 230). A test also checks that a thin static wall stops a fast ball without the flag.

Rapier caps the angular speed of a body at about 45 degrees per substep unless `allow_fast_rotation` is set. At 1/60 s per step the cap is about 47 rad/s. This step leaves it as it is; the force tests use a 0.1 s step to stay under it.

## Results

- `Tools/check-golden.sh linux-x64` on the development machine matches the new `golden.txt`. The `native-libraries` workflow on `f4d1fbe` passed on every runner, and its libraries are committed in `107fcd1` (`built-from.txt` = `f4d1fbe`).
- `dotnet test tests/`: 61/61 on Linux (.NET 8), 57 before this step plus 4 new tests.
- Two-sided check, Windows server: PASS. 1484 common ticks, the same world hash on every one, 0 reconcile mismatches.
- Two-sided check, Linux server: fails as in 11.10 and 12.3. The server spawns the balls for the peer, but the Unity client on Windows stays in `Starting` and the server records no ticks. The cause is the network path from WSL to Windows, not this step.
- Unity players were not run again for this step. Step 12.10 repeats the player check of 12.2, and the golden scenario now includes the force and CCD lines.
