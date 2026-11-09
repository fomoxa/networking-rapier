# 12.8 Character controller

> Step 12.8 of the `unity` repository. Its `implementation/12-8-tier1-character-rapier.md` holds the full listings for this repository. The design is P27 of `design/principles.md` there, line "Character controller" (Q225 (6) A, Q237, Q238 A, Q239 (1) A, (2) B), with Q225 (2) A for queries that see the latest poses. The Core is unchanged and `CORE_COMMIT` stays `0cd6d52`. Status: Applied.

## What changed

| Part | Change |
|---|---|
| Crate | `fr_character_move` and `fr2_character_move` run `KinematicCharacterController::move_shape` for a kinematic body with exactly one non-trigger box, sphere (2D: circle) or capsule collider, and set the result as the next kinematic position, so the body gets there in the next `Step` with a matching velocity. The filter skips sensors, applies the layer collision matrix through the collision groups of the character collider, and keeps the `LayerMask` and `ExcludeBody` of the query filter. `IncludeTriggers` is not read. A positive push mass applies `solve_character_collision_impulses` to the dynamic bodies hit |
| Moved colliders | `move_shape` reads only the broad-phase tree and `collider.position()`, which `Step` updates. Before the cast, every collider moved, created or added since the last step gets its pose from its body and goes into the tree with `BroadPhaseBvh::set_aabb`. The cost follows the number of such colliders, not the size of the world |
| `Sliding` | True when the move went against `Up` and one of the surfaces hit faces up at an angle to `Up` of at least `MinSlopeSlideAngle`. Rapier's `is_sliding_down_slope` is not used: it is also set on flat ground when the move has a downward part |
| Golden scenario | One `character` scenario per world after the `joints` lines: a capsule walks through a trigger and a wall on a layer the matrix skips, climbs a step, is held back for 11 ticks by a kinematic gate placed with `SetBody` in the same tick, and pushes a dynamic box. A hash line and a `replay` line. The 40 older lines keep their values |
| C# API | `CharacterSettings`, `CharacterSettings2D`, `CharacterLength`, `CharacterAutostep`, `CharacterMove`, `CharacterMove2D`. `default(CharacterSettings)` gives Rapier 0.36's defaults. `RapierWorld` and `RapierWorld2D` gain `MoveCharacter(body, desired, seconds, settings[, filter])`, and `RapierScenes.MoveCharacter` takes the owning entity, as `CreateJoint` does |
| Unity | No change. `RapierPhysics` already writes kinematic bodies back to their `Transform`. `CharacterController` is still not supported |

## Cost of a move

Prototype, Linux x64, release, microseconds per `MoveCharacter` call for a capsule inside a grid of kinematic boxes. "Changed" is the number of boxes set with `SetBody` just before the call.

| Bodies | Changed | µs per call |
|---:|---:|---:|
| 100 | 0 | 13.22 |
| 100 | 10 | 14.34 |
| 100 | all | 20.81 |
| 1000 | 0 | 13.94 |
| 1000 | 10 | 13.85 |
| 1000 | all | 68.75 |
| 4000 | 0 | 16.49 |
| 4000 | 10 | 17.23 |
| 4000 | all | 251.20 |

In a normal tick only proxies and bodies placed through `NetworkTransform` have moved.

## Determinism

With static and dynamic bodies, a move before the step that indexes them and the same move after it agree bit for bit. Kinematic bodies differ: after their first step Rapier derives a tiny velocity from the next position (about 7e-6 m/s for a resting rotated box), and the controller replaces the normal part of the remaining move with the velocity of a kinematic body it touches. A character wedged between several kinematic boxes slides through up to 20 iterations, and the prototype measured up to 0.24 m between a move before that first step and the same move after it. The same sequence of calls still gives the same result, and a replay after `Load` repeats bit for bit, since the tree and the velocities are in the snapshot.

The first prototype of the golden scenario moved down by 0.15 per tick. Rapier 0.36's 2D capsule then stopped at the edge of the step, while the 3D capsule and a 2D box climbed it. The scenario moves down by 0.02 per tick.

## Results

- `Tools/check-golden.sh` on every runner of the `native-libraries` workflow at `c3306c2`: the hashes match the new `golden.txt`.
- `dotnet test tests/`: 89/89 on Linux (.NET 8), 77 before this step plus 12 new tests.
- Unity EditMode tests of the package on Windows, with the Core of `unity` at `8d00a05`: 28 tests, 27 passed, 0 failed. The skipped test is `TwoSidedClientCheck`, which runs only inside the two-sided check. The two new tests move a kinematic capsule on the server through `RapierPhysics.Scenes` until a wall stops it, and check that `CharacterController` is still reported as not supported.
- Two-sided check, Windows server: PASS. 1482 common ticks, the same world hash on every one, 0 reconcile mismatches.
- Two-sided check, Linux server: fails as in 11.10 and 12.3 to 12.7. The Unity client on Windows stays in `Starting` and the server records no ticks. The cause is the network path from WSL to Windows, not this step.
