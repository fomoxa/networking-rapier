# 16.7 Two-sided ball input through the internal hook

> Step 16.7 of the `unity` repository. Its `implementation/16-7-rapier-two-sided-input.md` holds the full listing; the Core change is `16-6-tier2-unity-model-apis-removed.md` there. The design is P16 of `design/principles.md` there, line "Phạm vi" (Q283 (1) A), and the stage 16 plan (Q284 B). `CORE_COMMIT` stays as it is, since CI does not compile `Tests/Unity`. Status: Applied.

## What changed

`TwoSidedBall`, the Unity client of the two-sided check, overrides `OnRegisterInput` as `internal override`. Since stage 16.6 of the Core, `OnRegisterInput` on `NetworkBehaviour` is `internal virtual`, so the old `protected override` no longer compiles. `Fomoxa.Unity.Rapier.Tests` is already a friend assembly of `Fomoxa.Unity`. The body of the method and the server console are unchanged.

## Why

The ball keeps the handwritten input model of `TwoSidedScenario.cs`, which the server console shares through `BallEntity : EntityBehaviour`. Declaring the input as fields would give the Unity side a generated model that the server does not use, and the check would no longer compare the same simulation on both sides.

## Checks

`Tools/unity-windows-check.sh`: 38 tests, 37 passed, 1 skipped. `Tools/two-sided-check.sh windows`: PASS, 1485 common ticks, the same world hash on every common tick, 0 reconcile mismatches.
