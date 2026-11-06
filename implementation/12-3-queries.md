# 12.3 Query filter, shape casts, shape overlaps and the query pipeline

> Step 12.3 of the `unity` repository. Its `implementation/12-3-tier1-queries-rapier.md` holds the full listings for this repository, `12-3-tier1-queries-core.md` the query contract and the Core side, and `12-3-tier2-queries-rigidbody.md` the Rigidbody backend. The design is P27 of `design/principles.md` there, line "Truy vấn" (Q225 (2) A, Q226 (1) A, Q226 (2) A), and P20 for the Core used by CI (Q227). Status: Applied.

## What changed

| Part | Change |
|---|---|
| `checks/two-sided` | References `Fomoxa.Net.WebSockets.dll` and `BouncyCastle.Cryptography.dll`, which Core 0.2.0 needs; the check did not build before |
| Crate | `FrQueryFilter` (layer mask, excluded body, triggers), `FrQueryShape`, `FrQueryShape2D`; `fr_world_shape_cast`, `fr2_world_shape_cast`; `fr_world_raycast`, `fr_world_overlap` and their 2D versions take a filter, and overlaps take a box, sphere (circle) or capsule with a pose |
| Query pipeline | Queries go through Rapier's BVH (`as_query_pipeline`). Colliders added or moved since the last step (`AddStatic`, `CreateBody`, `SetBody`, `Load`) sit in a dirty set, are left out of the pipeline and are tested one by one with their current pose. `step` clears the set; snapshots store it (`SNAPSHOT_FORMAT` 3) |
| Start inside a collider | Rays use `solid = true` and shape casts `stop_at_penetration`, so a query that starts inside a collider hits it at distance 0 |
| `RapierNative`, `RapierWorld`, `RapierWorld2D` | Implement `Raycast(…, in QueryFilter, out hit)`, `ShapeCast` and `Overlap` of the Core. 3D vectors and quaternions go to the library as `in Vector3` and `in Quaternion` instead of a new `float[]` per call |
| Golden scenario | New lines per world after `replay`: `ray-skip`, `ray-layer`, `cast`, `overlap`, `moved` (a body set without a step, which takes the dirty path) and `inside`. The old lines, `3d ray` included, keep their values |
| CI | `CORE_COMMIT` is `c17b34b`, the Core split pushed to `main` of `fomoxa/com.fomoxa.networking` without a tag (Q227) |

The old `Raycast(origin, direction, max, out hit)` and `Overlap(center, radius, results)` are now extension methods of the Core (`PhysicsQueries`) that pass `QueryFilter.All`: every layer, triggers included, no excluded body. Code that calls them compiles unchanged.

## Pipeline and dirty path give the same answers

A prototype with 1000 3D bodies and 600 2D bodies gave bit-identical results on both paths for rays, shape casts, overlaps and filtered rays. The test `QueriesGiveTheSameAnswersBeforeAndAfterTheStepThatIndexesTheBodies` keeps this: it queries 64 kinematic bodies while all of them are dirty, steps, queries again through the pipeline and compares body, distance, point, normal and overlap set.

Microseconds per query on the prototype (Linux x64, release):

| Bodies | Ray | Ray, 10 dirty | Ray, all dirty | Shape cast | Shape cast, all dirty | Overlap | Overlap, all dirty |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 100 | 0.39 | 1.08 | 7.32 | 3.79 | 35.4 | 0.30 | 17.6 |
| 1000 | 0.69 | 1.32 | 67.8 | 4.53 | 300 | 1.11 | 166 |
| 4000 | 0.94 | 1.60 | 266 | 5.36 | 1131 | 1.60 | 666 |

The "all dirty" columns cost the same as the old loop over every collider.

## Results

- `Tools/check-golden.sh linux-x64` on the development machine matches the new `golden.txt`. The `native-libraries` workflow on `a9545d6` passed on every runner, and its libraries are committed in `91d95f8` (`built-from.txt` = `a9545d6`).
- `dotnet test tests/`: 57/57 on Linux (.NET 8), 47 before this step plus 10 new tests.
- Two-sided check, Windows server: PASS. 1484 common ticks, the same world hash on every one, 0 reconcile mismatches.
- Two-sided check, Linux server: fails as in 11.10. The server spawns the balls for the peer, but the Unity client on Windows stays in `Starting` and the server records no ticks. The cause is the network path from WSL to Windows, not this step.
- Unity players were not run again for this step. Step 12.10 repeats the player check of 12.2, and the golden scenario now includes the query lines.
