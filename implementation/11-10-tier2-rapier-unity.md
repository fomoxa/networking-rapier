# 11.10 Tier 2: `RapierPhysics` reports moved network objects

> The Unity part of step 11.10 of the `unity` repository in this repository. The Core side is in `implementation/11-10-tier1-transform-signals.md` of the `unity` repository, and `NetworkTransform` in its `implementation/11-10-tier2-transform-signals-docs.md`. Tier 1: [`11-10-tier1-rapier-motion.md`](11-10-tier1-rapier-motion.md). Status: Applied. Unity EditMode 13/13 on Unity 6000.5.7f1 (Windows) with the Core of the `unity` repository, one more test skipped because it is the two-sided check.

| | Work | Status |
|---|---|:---:|
| 1 | `RapierPhysics` implements `IPhysicsMotion` by forwarding to its `RapierScenes` | ✅ |
| 2 | `CollectMoved` reports the parts of a transform that the body does not decide: the local pose under a parent, a change of parent, the z of a 2D object (Q179 (1) C, (2) C) | ✅ |
| 3 | Unity tests | ✅ |

Rules: P16 (Q176 C as corrected, Q178 (2) A, Q179 (1) C, (2) C).

---

## 1. Overview

`NetworkRuntime` sets the Core's motion source from its `Physics` (`value as IPhysicsMotion`) and, when the physics implements it, calls `CollectMoved` once per tick, after the tick handlers and just before it syncs transforms. `RapierPhysics` is that physics in Unity, so it forwards both members to its `RapierScenes`, which knows the bodies of each `NetworkObject` (`AddBody(networkObject, …)` in `Bind`). Before `Begin` and after `Release` there is no `RapierScenes`: `ReportsMotion` is false and `CollectMoved` adds only the objects of the next section.

`RapierPhysics` binds a `NetworkObject` lazily, in the step that syncs bodies before each tick. A `NetworkTransform` on an object that is not bound yet is read every tick; the Core checks `ReportsMotion` again at every read, so the object moves to signal-driven reads at the first read after it is bound. `Unbind` calls `RapierScenes.RemoveBodies`, which reports the object once more, and the next read sends it back to per-tick reads.

### The parts of the transform that the body does not decide (Q179)

`WriteBack` writes the world pose of every bound body to its `Transform` after every step, whether the body moved or not. Two parts of what `NetworkTransform` sends are not the body's:

- `NetworkTransform` sends the local pose. Under a parent, the local pose changes when the parent moves, or when the object gets another parent, while the body sleeps.
- A 2D body decides x, y and the angle around z. `WriteBack` keeps the `Transform`'s z, which game code may change.

`CollectMoved` therefore walks the bound objects (not proxies) after it has added the scenes' entities, compares these parts with what it saw at the previous call, and reports the object when one of them differs:

- the parent (`ReferenceEquals`);
- for a 2D body, the z of the `Transform`;
- under a parent, the local position and rotation (`GetLocalPositionAndRotation`).

The comparisons are exact (`Equals`): `WriteBack` writing the same body pose under the same parent gives the same local pose, so a resting object is never reported (the tests check that the client receives nothing for 30 frames at rest). The first call that sees an object only records the values. A body that moved changes its local pose too and is reported by the scenes as well; the Core ignores repeats.

The order within a tick is: step and `WriteBack`, tick handlers, `CollectMoved`, `SyncTransforms`. A change made in a tick handler is sent in that tick, as with per-tick reads (test `AZChangedInATickHandlerIsSentInThatTick`). The comparisons sat in `WriteBack` in a first version, which already reads z and writes every bound object; a change made in a tick handler, after `WriteBack`, then reached the client one tick late. Comparing in `CollectMoved` costs a second pass over the bound objects each tick, which reads the parent, the z of 2D objects and the local pose of objects under a parent, and writes nothing.

A position set on the `Transform` of a bound object by game code is overwritten at the next step, with or without this change: the pose that the Core reads is the body's, apart from the parts above.

---

## 2. Code

`Runtime/Unity/RapierPhysics.cs`:

```csharp
    public sealed class RapierPhysics : NetworkPhysics, IPhysicsMotion
```

After `HistoryOf`:

```csharp
        public bool ReportsMotion(INetworkEntity entity) => scenes != null && scenes.ReportsMotion(entity);

        public void CollectMoved(List<INetworkEntity> into)
        {
            scenes?.CollectMoved(into);
            foreach (KeyValuePair<NetworkObject, Bound> entry in bound)
            {
                Bound found = entry.Value;
                if (found.IsProxy || entry.Key == null || !found.HasBody)
                {
                    continue;
                }

                Transform target = entry.Key.transform;
                Transform parent = target.parent;
                bool shifted = found.Seen && !ReferenceEquals(parent, found.SeenParent);
                if (found.Body2D.IsValid)
                {
                    float z = target.position.z;
                    shifted |= found.Seen && !z.Equals(found.SeenZ);
                    found.SeenZ = z;
                }

                if (parent != null)
                {
                    target.GetLocalPositionAndRotation(out Vector3 localPosition, out Quaternion localRotation);
                    shifted |= found.Seen && (!localPosition.Equals(found.SeenLocalPosition) || !localRotation.Equals(found.SeenLocalRotation));
                    found.SeenLocalPosition = localPosition;
                    found.SeenLocalRotation = localRotation;
                }

                found.SeenParent = parent;
                found.Seen = true;
                if (shifted)
                {
                    into.Add(entry.Key);
                }
            }
        }
```

`Bound`, after `IsProxy`:

```csharp
            public bool Seen;
            public float SeenZ;
            public Transform SeenParent;
            public Vector3 SeenLocalPosition;
            public Quaternion SeenLocalRotation;
```

`WriteBack` does not change.

## 3. Tests

`Tests/Unity/RapierPhysicsTest.cs`: a new constant `ScaledBallPrefabId = 0xBA12`, four tests after `TheServerSimulatesSpawnedObjectsWithRapierAndMovesTheirTransforms`, and a helper before `Names`. The helper reads the Core's internal `EntityRecord.TransformOrderNode` by reflection, because `Fomoxa.Networking` does not expose its internals to this assembly; a record is in that list exactly when one of its transforms is read every tick. In EditMode the client only applies settles (interpolation runs in `Update`, which EditMode tests do not call), so the tests check `NetworkTransform.LastTick` for updates and the client's transform for settles.

```csharp
        [Test]
        public void ARestingBallIsReadOnlyWhileRapierMovesItUnlessItSyncsScale()
        {
            NetworkObject ballPrefab = Prefab("Ball", BallPrefabId, ball => { ball.AddComponent<SphereCollider>().radius = 0.5f; ball.AddComponent<Rigidbody>(); ball.AddComponent<NetworkTransform>(); });
            NetworkObject scaledPrefab = Prefab("ScaledBall", ScaledBallPrefabId, ball =>
            {
                ball.AddComponent<SphereCollider>().radius = 0.5f;
                ball.AddComponent<Rigidbody>();
                var scaled = new SerializedObject(ball.AddComponent<NetworkTransform>());
                scaled.FindProperty("syncScale").boolValue = true;
                scaled.ApplyModifiedPropertiesWithoutUndo();
            });
            NetworkObject groundPrefab = Prefab("Ground", GroundPrefabId, ground => ground.AddComponent<BoxCollider>().size = new Vector3(20f, 1f, 20f));
            NetworkManager server = Manager(ballPrefab, scaledPrefab, groundPrefab);
            NetworkManager client = Manager(ballPrefab, scaledPrefab, groundPrefab);
            server.ServerManager.StartConnection(1);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);
            Spawn(server, groundPrefab, new Vector3(0f, -0.5f, 0f));
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 3f, 0f));
            NetworkObject scaledBall = Spawn(server, scaledPrefab, new Vector3(4f, 3f, 0f));
            RunFrames(5);
            NetworkTransform onClient = ((NetworkObject)client.ClientManager.Spawned[ball.ObjectId]).GetComponent<NetworkTransform>();
            uint lastTickAfterSpawn = onClient.LastTick;
            RunFrames(15);
            bool fallingReadEveryTick = ReadEveryTick(ball);
            uint lastTickWhileFalling = onClient.LastTick;

            RunFrames(600);
            bool restingReadEveryTick = ReadEveryTick(ball);
            float restingGap = Vector3.Distance(ball.transform.position, onClient.transform.position);
            uint lastTickAtRest = onClient.LastTick;
            RunFrames(30);
            uint lastTickStillAtRest = onClient.LastTick;
            PhysicsBody body = ball.Body;
            body.Velocity = new System.Numerics.Vector3(3f, 0f, 0f);
            RunFrames(5);

            Assert.IsFalse(fallingReadEveryTick);
            Assert.AreEqual(lastTickAfterSpawn + 15, lastTickWhileFalling);
            Assert.IsFalse(restingReadEveryTick);
            Assert.IsTrue(ReadEveryTick(scaledBall));
            Assert.Less(restingGap, 1e-4f);
            Assert.AreEqual(lastTickAtRest, lastTickStillAtRest);
            Assert.Greater(ball.transform.position.x, 0.1f);
            Assert.Greater(onClient.LastTick, lastTickStillAtRest + 2);
        }

        [Test]
        public void ARestingBallIsSentWhenItsParentMoves()
        {
            NetworkObject ballPrefab = Prefab("Ball", BallPrefabId, ball => { ball.AddComponent<SphereCollider>().radius = 0.5f; ball.AddComponent<Rigidbody>(); ball.AddComponent<NetworkTransform>(); });
            NetworkObject groundPrefab = Prefab("Ground", GroundPrefabId, ground => ground.AddComponent<BoxCollider>().size = new Vector3(20f, 1f, 20f));
            NetworkManager server = Manager(ballPrefab, groundPrefab);
            NetworkManager client = Manager(ballPrefab, groundPrefab);
            server.ServerManager.StartConnection(1);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);
            Spawn(server, groundPrefab, new Vector3(0f, -0.5f, 0f));
            var holder = new GameObject("Holder");
            created.Add(holder);
            NetworkObject ball = UnityEngine.Object.Instantiate(ballPrefab, new Vector3(0f, 3f, 0f), Quaternion.identity, holder.transform);
            created.Add(ball.gameObject);
            server.ServerManager.Spawn(ball);
            RunFrames(600);
            NetworkTransform onClient = ((NetworkObject)client.ClientManager.Spawned[ball.ObjectId]).GetComponent<NetworkTransform>();
            bool restingReadEveryTick = ReadEveryTick(ball);
            uint lastTickAtRest = onClient.LastTick;
            RunFrames(30);
            uint lastTickStillAtRest = onClient.LastTick;

            holder.transform.position += new Vector3(2f, 0f, 0f);
            RunFrames(5);
            float localUnderMovedParent = ball.transform.localPosition.x;
            float clientUnderMovedParent = onClient.transform.localPosition.x;
            uint lastTickUnderMovedParent = onClient.LastTick;
            ball.transform.SetParent(null, true);
            RunFrames(5);

            Assert.IsFalse(restingReadEveryTick);
            Assert.AreEqual(lastTickAtRest, lastTickStillAtRest);
            Assert.Greater(lastTickUnderMovedParent, lastTickStillAtRest);
            Assert.AreEqual(-2f, localUnderMovedParent, 0.05f);
            Assert.AreEqual(localUnderMovedParent, clientUnderMovedParent, 1e-4f);
            Assert.Greater(onClient.LastTick, lastTickUnderMovedParent);
            Assert.AreEqual(0f, ball.transform.localPosition.x, 0.05f);
            Assert.AreEqual(ball.transform.localPosition.x, onClient.transform.localPosition.x, 1e-4f);
        }

        [Test]
        public void ARestingTwoDimensionalBallIsSentWhenItsZChanges()
        {
            NetworkObject ballPrefab = Prefab("Ball2D", BallPrefabId, ball => { ball.AddComponent<CircleCollider2D>().radius = 0.5f; ball.AddComponent<Rigidbody2D>(); ball.AddComponent<NetworkTransform>(); });
            NetworkObject groundPrefab = Prefab("Ground2D", GroundPrefabId, ground => ground.AddComponent<BoxCollider2D>().size = new Vector2(20f, 1f));
            NetworkManager server = Manager(ballPrefab, groundPrefab);
            NetworkManager client = Manager(ballPrefab, groundPrefab);
            server.ServerManager.StartConnection(1);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);
            Spawn(server, groundPrefab, new Vector3(0f, -0.5f, 0f));
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 3f, 0f));
            RunFrames(600);
            NetworkTransform onClient = ((NetworkObject)client.ClientManager.Spawned[ball.ObjectId]).GetComponent<NetworkTransform>();
            bool restingReadEveryTick = ReadEveryTick(ball);
            uint lastTickAtRest = onClient.LastTick;
            RunFrames(30);
            uint lastTickStillAtRest = onClient.LastTick;

            Vector3 resting = ball.transform.position;
            ball.transform.position = new Vector3(resting.x, resting.y, 4f);
            RunFrames(5);

            Assert.IsFalse(restingReadEveryTick);
            Assert.AreEqual(lastTickAtRest, lastTickStillAtRest);
            Assert.AreEqual(4f, ball.transform.position.z);
            Assert.AreEqual(4f, onClient.transform.position.z, 1e-5f);
        }

        [Test]
        public void AZChangedInATickHandlerIsSentInThatTick()
        {
            NetworkObject ballPrefab = Prefab("Ball2D", BallPrefabId, ball => { ball.AddComponent<CircleCollider2D>().radius = 0.5f; ball.AddComponent<Rigidbody2D>(); ball.AddComponent<NetworkTransform>(); });
            NetworkObject groundPrefab = Prefab("Ground2D", GroundPrefabId, ground => ground.AddComponent<BoxCollider2D>().size = new Vector2(20f, 1f));
            NetworkManager server = Manager(ballPrefab, groundPrefab);
            NetworkManager client = Manager(ballPrefab, groundPrefab);
            server.ServerManager.StartConnection(1);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);
            Spawn(server, groundPrefab, new Vector3(0f, -0.5f, 0f));
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 3f, 0f));
            RunFrames(600);
            NetworkTransform onClient = ((NetworkObject)client.ClientManager.Spawned[ball.ObjectId]).GetComponent<NetworkTransform>();
            uint lastTickAtRest = onClient.LastTick;
            uint changedAt = 0;
            Action change = () =>
            {
                if (changedAt == 0)
                {
                    changedAt = server.TimeManager.Tick;
                    Vector3 resting = ball.transform.position;
                    ball.transform.position = new Vector3(resting.x, resting.y, 4f);
                }
            };
            server.TimeManager.OnTick += change;
            uint firstReceived = lastTickAtRest;
            for (int frame = 0; frame < 5 && firstReceived == lastTickAtRest; frame++)
            {
                RunFrames(1);
                firstReceived = onClient.LastTick;
            }

            server.TimeManager.OnTick -= change;
            RunFrames(5);

            Assert.AreNotEqual(0u, changedAt);
            Assert.AreEqual(changedAt, firstReceived);
            Assert.AreEqual(4f, onClient.transform.position.z, 1e-5f);
        }
```

```csharp
        private static bool ReadEveryTick(NetworkObject networkObject) =>
            typeof(EntityRecord).GetProperty("TransformOrderNode", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(((INetworkEntity)networkObject).Record) != null;
```

### Mutation check

Each mutation changes one place, runs the tests of `RapierPhysicsTest` through `Tools/unity-windows-check.sh` on copies of both repositories (`FOMOXA_CORE` set to the copy of the Core), then is undone.

| | Mutation | Result | Caught by |
|---|---|---|---|
| U1 | `RapierPhysics.ReportsMotion` always false | caught | `ARestingBallIsReadOnlyWhileRapierMovesItUnlessItSyncsScale`, `ARestingBallIsSentWhenItsParentMoves`, `ARestingTwoDimensionalBallIsSentWhenItsZChanges` |
| U2 | `NetworkTransform` always registers `Source` | caught | `ARestingBallIsReadOnlyWhileRapierMovesItUnlessItSyncsScale`, `ARestingBallIsSentWhenItsParentMoves`, `ARestingTwoDimensionalBallIsSentWhenItsZChanges` |
| U3 | the z of a 2D body is not compared | caught | `ARestingTwoDimensionalBallIsSentWhenItsZChanges`, `AZChangedInATickHandlerIsSentInThatTick` |
| U4 | the local pose under a parent is not compared | caught | `ARestingBallIsSentWhenItsParentMoves` |
| U5 | a change of parent is not reported | caught | `ARestingBallIsSentWhenItsParentMoves` |
| U6 | `CollectMoved` does not add the shifted objects | caught | `ARestingBallIsSentWhenItsParentMoves`, `ARestingTwoDimensionalBallIsSentWhenItsZChanges`, `AZChangedInATickHandlerIsSentInThatTick` |
| U7 | the Core collects right after the step, before the tick handlers | caught | `AZChangedInATickHandlerIsSentInThatTick` |

U2 changes `NetworkTransform` and U7 changes `NetworkRuntime` (the order of U7 is the one of the first version) in the copy of the Core; the others change `RapierPhysics`. `AZChangedInATickHandlerIsSentInThatTick` passes under U1 and U2 because per-tick reads also send the change in that tick.

## 4. Results

- Unity EditMode with the Core of the `unity` repository: 13/13, the two-sided test skipped.
- Mutation check: 7/7 caught.
- The first version of the first test expected the falling ball to be in the list of per-tick reads and its client copy to move. Both expectations were wrong: a falling ball is read through the queue because Rapier reports it every step, and in EditMode the client only applies settles. The test checks that the client receives an update for every tick while the ball falls, none while it rests, and updates again after the push.
