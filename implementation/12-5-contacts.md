# 12.5 Contact points

> Step 12.5 of the `unity` repository. Its `implementation/12-5-tier1-contacts-rapier.md` holds the full listings for this repository, `12-5-tier1-contacts-core.md` the contract and the Core side, and `12-5-tier2-contacts-rigidbody.md` the Rigidbody backend and `NetworkCollision`. The design is P27 of `design/principles.md` there, line "Điểm tiếp xúc" (Q225 (4) A, Q229 (1) A, Q229 (2) A, Q230 C, Q231 C), and P20 for the Core used by CI (Q227). Status: Applied.

## What changed

| Part | Change |
|---|---|
| Crate | `fr_body_contact_points` (a body against one other body, or against every static collider when the other handle is 0, with the query filter of 12.3) and `fr_collider_contact_points` (one collider pair, indexed like `fr_collider_touching`), plus their 2D versions. Both write into the caller's buffer and return the total count, so a caller with a short buffer grows it and calls again |
| `RapierNative`, `RapierWorld`, `RapierWorld2D` | Implement `GetContactPoints` of `IPhysicsWorld` and `IPhysicsWorld2D`, and `ContactPoints` for a collider pair |
| `RapierContacts` | Implements `CollectContactPoints` of `IContactQuery` and `IContactQuery2D`, which `NetworkCollision` and `NetworkCollision2D` call once they are attached |
| Golden scenario | One new `contacts` line per world after `ccd`: the number of points of the 60 bodies against the static colliders, then the bits of the summed depth. The old lines keep their values |
| CI | `CORE_COMMIT` is `13d8a6e`, the Core split of this step pushed to `main` of `fomoxa/com.fomoxa.networking` without a tag (Q227) |

The snapshot format stays 3.

## How the points are read from Rapier 0.36

The crate walks the narrow phase pairs of the collider and keeps those with an active contact. Each manifold point gives a point on the surface of each collider. The crate moves both into world space with the current collider poses, and with the subshape poses for compound shapes, trimeshes and heightfields.

- The normal is the manifold normal of collider 1 in world space, flipped when the queried collider is collider 1, so it points from the other side to the queried side.
- The depth is `(p1 − p2) · n1` from the current poses. The manifold's own `dist` can be stale, because Rapier reuses manifolds between steps without always writing it again. On a box resting on the ground it read `-0` at every point, while the depth from the poses was about `1.4e-4` in 3D and `8.6e-5` in 2D.
- Points deeper than minus the prediction distance (`0.002` by default) are dropped. Manifolds keep points outside the contact margin, up to `0.67` away on compound geometry. Points inside the margin are returned with a negative depth.
- The reported point is the midpoint of the two surface points.

Sensors have no manifolds, so a pair with a trigger has no points.

The snapshot already serializes the narrow phase, so after `Load` the points are those of the loaded tick, which is what Q231 C asks of every backend. A body rebuilt on `Load` has no contacts until the next step, as P27 already records.

## Results

- `Tools/check-golden.sh linux-x64` on the development machine matches the new `golden.txt` (`3d contacts 184 bdf7099a`, `2d contacts 45 bc9653f4`). The `native-libraries` workflow on `e7a6c55` passed on every runner, and its libraries are committed in `4c7b0dc` (`built-from.txt` = `e7a6c55`). CI on `e7a6c55` failed only at "Committed libraries match the native source", as expected.
- `dotnet test tests/`: 65/65 on Linux (.NET 8), 61 before this step plus 4 new tests.
- Unity EditMode tests of the package on Windows, with the Core of `unity` at `0bdde05`: 16 tests, 15 passed, 0 failed. The skipped test is `TwoSidedClientCheck`, which runs only inside the two-sided check. The two new tests read the contact points of a ball on the ground through `NetworkCollision` and `NetworkCollision2D`.
- Two-sided check, Windows server: PASS. 1486 common ticks, the same world hash on every one, 0 reconcile mismatches.
- Two-sided check, Linux server: fails as in 11.10, 12.3 and 12.4. The server spawns the balls for the peer, but the Unity client on Windows stays in `Starting` and the server records no ticks. The cause is the network path from WSL to Windows, not this step.
- Unity players were not run again for this step. Step 12.10 repeats the player check of 12.2, and the golden scenario now includes the contact lines.
