# 12.10 Rigidbody2D components that are not simulated

> Step 12.10 of the `unity` repository. Its `implementation/12-10-rapier-simulated-bodies.md` holds the full listing for this repository; the Core part is `12-10-tier2-simulated-bodies.md` there. The design is P27 of `design/principles.md` there, line "Nhiều body" (Q245 A). `CORE_COMMIT` stays `1e8b06a`, since CI does not compile `Runtime/Unity`. Status: Applied.

## What changed

`RapierPhysics` leaves a `Rigidbody2D` out of the bodies of a `NetworkObject` when its Simulated is off at the moment the object is bound. On the root, the object has no root body in 2D; on a child, the child has no body. The colliders attached to that `Rigidbody2D` belong to no other body, so they are not built, and the joints on it are not built either. Turning Simulated on later does not change the bodies until the object spawns again. 3D is unchanged.

## Why

A 2D ragdoll whose bones stay off until the character dies is a common Unity pattern. Under 12.6 and 12.7 each bone became a dynamic Rapier body with its joints, so the bones fell around a living character and pushed it. Red Runner's character has seven such bones and six `HingeJoint2D` components; with a player build as the server, the character was pushed 3 to 5 units sideways within 3 seconds of joining.

## Tests

`RapierPhysicsTest.Rigidbody2DsThatAreNotSimulatedAtSpawnAreNotBodiesAndTheirJointsAreNotBuilt`: a 2D character with two bones that are not simulated, joined by a `HingeJoint2D`, has one body, lands on the ground, keeps its bones in place and logs nothing; an object whose root `Rigidbody2D` is not simulated has no body and does not fall.
