# 12.9 Effectors

> Step 12.9 of the `unity` repository. Its `implementation/12-9-tier1-effectors-rapier.md` holds the full listings for this repository; the scene file models and the Editor step are in `12-9-tier1-scene-file-core.md` and `12-9-tier2-scene-file-editor.md` there. The design is P27 of `design/principles.md` there, line "Effector" (Q225 (7) A, Q240 (1) to (3) A, Q241 (1) to (3) A, Q243 (2) A), line "Hình va chạm thêm lúc chạy" (Q243 (1) A) and line "Character controller" for one-way platforms (Q240). The Core changed and `CORE_COMMIT` is now `1e8b06a`. Status: Applied.

## What changed

| Part | Change |
|---|---|
| Crate | `World2D` keeps a table of effectors, each on a set of colliders of one body or one static group: `fr2_effector_add`, `fr2_effector_add_static`, `fr2_effector_set`, `fr2_effector_remove`, `fr2_effector_contains`. Removing the body or the group removes its effectors. Platform and surface effectors run in a `modify_solver_contacts` hook; area, buoyancy and point effectors compute forces from the contact pairs after the step and apply them in the next one. A surface changes velocities right after the step. A collider mask replaces the layer collision matrix for the colliders of the effector |
| One-way platforms and `fr2_character_move` | The controller does not go through the solver, so the hook does not apply. A one-way platform that overlaps the character at the start is skipped, and a platform hit with a normal outside its arc is added to the skipped set and the move runs again. Each platform enters the set at most once |
| Snapshots | Format 5 carries the effector table. `Load` keeps the effectors of now: those in the snapshot take its description, those created after it keep theirs, those removed after it stay removed. The state of a platform pair lives in the manifold's `user_data`, which is part of Rapier's snapshot. `StateHash` still hashes body state only |
| Golden scenario | One `effectors` scenario for the 2D world after the `character` line: a conveyor ground, a one-way platform, wind, water and a point magnet on a kinematic body, with the wind reversed through `fr2_effector_set` at tick 70. A hash line and a `replay` line. The 44 older lines keep their values |
| C# API | `EffectorKind2D`, `EffectorSelection`, `EffectorForceMode`, `EffectorHandle`, `EffectorDesc2D` with one factory per kind, `RapierEffectors.FromFile` for `SceneFileEffector2D`. `RapierWorld2D` gains `AddEffector` for a body or a static group, `SetEffector`, `RemoveEffector` and `Contains`. `RapierScenes.AddEffector` takes the owning entity, a static group from `AddStatic2D`, or an unowned body |
| Scene files | `RapierScenes.LoadScene` builds the effectors of static colliders into the static group of the file, and remembers the effectors of scene object bodies so `AddBody2D` of the entity with that `SceneObjectId` attaches them. An effector index outside `Effectors2D` asks for a new export |
| Unity | `RapierPhysics` reads `Effector2D` components through `EffectorDescriptions` of the Core when it binds a `NetworkObject` and in `AddStatic2D(GameObject)`. Variation parameters are reported once per component and count as 0. `LoadScene` passes the scene file without `Objects`, since scene objects take their effectors from their components |

## Behaviour against Box2D

The prototype measured each effector with `Physics2D.Simulate` in Unity 6000.5 and the crate follows those results. The EditMode tests in `RapierEffectorsTest` build the same scene for Box2D and for `RapierWorld2D` and compare positions, angles and velocities. Forces and torques agree with Box2D to 4 or 5 digits in the first steps. Positions drift by about 1% because Rapier integrates positions over substeps; free fall without any effector shows the same drift, so the tests use a tolerance relative to the distance travelled.

The tests set `Physics2D.simulationMode` to `Script`. Under `FixedUpdate`, calling `Simulate` by hand makes Unity apply only the last force effector created in the scene, and the buoyancy of a body changes when another body is in the water.

A 2D capsule of 1 by 2 with mass 0.6 has an inertia of 0.134 under Box2D and 0.2045 under Rapier, so the same torque turns it 1.53 times slower. This is outside effectors; the buoyancy test keeps its capsule upright and without flow.

## Cost

Prototype, Linux x64, release, microseconds per `Step` in steady state, median of three runs.

| Scenario | 100 bodies | 1000 bodies |
|---|---:|---:|
| No effector, bodies asleep | 5.3 | 37.0 |
| Platform, bodies asleep | 6.2 | 40.7 |
| `fr2_body_add_force` on each body, no effector | 53.1 | 1160.6 |
| Area with the same force | 59.9 | 1178.7 |
| Platform, bodies pushed | 68.8 | 1180.3 |
| Surface | 68.8 | 560.8 |
| Buoyancy | 72.5 | 621.0 |

The effector part alone is about 70 ns per contact pair in the hook and about 80 ns per body inside an area after the step. The rest is Rapier simulating awake bodies.

## Results

- `Tools/check-golden.sh linux-x64` at `156206c`: matches the new `golden.txt`.
- `dotnet test tests/` with the libraries of `f82e88d`: 102/102 on Linux (.NET 8), 89 before this step plus 13 new tests.
- Unity EditMode tests of the package on Windows, with the Core of `unity` at `fa34769`: 37 tests, 36 passed, 0 failed. The skipped test is `TwoSidedClientCheck`.
- Two-sided check, Windows server, with the libraries of `f82e88d`: PASS. 1485 common ticks, the same world hash on every one, 0 reconcile mismatches. `TwoSidedScenario` uses no effector; the check confirms that the new libraries and the new Core leave the two-sided simulation unchanged.
- Two-sided check, Linux server: fails as in 11.10 and 12.3 to 12.8. The Unity client on Windows stays in `Starting` and the server records no ticks. The cause is the network path from WSL to Windows, not this step.
