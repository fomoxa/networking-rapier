# 11.10 Tier 1: moved bodies in the crate, `RapierWorld*` and `RapierScenes`

> The Rapier part of step 11.10 of the `unity` repository (step 10a of `design/improvement-proposals.md` §8 there). The design is P16 of `design/principles.md` in the `unity` repository, section `NetworkTransform`, "Khi nào đọc" (when to read; Q176, Q178), and the Core side, including the new `IPhysicsMotion` interface, is in its `implementation/11-10-tier1-transform-signals.md`. Tier 2 (`RapierPhysics`): [`11-10-tier2-rapier-unity.md`](11-10-tier2-rapier-unity.md). Status: Applied. `dotnet test` 46/46 on Linux (.NET 8) and Windows (.NET 9); the two-sided check passes with the Windows server (1487 common ticks, the same world hash on every one, 0 reconcile mismatches). The check with the Linux server still fails before the client starts, as on the unchanged repository (§4).

| | Work | Status |
|---|---|:---:|
| 1 | Crate: every world remembers the bodies written since the last collection; `fr_world_moved`, `fr_world_moved_copy`, `fr2_world_moved`, `fr2_world_moved_copy` | ✅ |
| 2 | `RapierWorld`, `RapierWorld2D`: `CollectMoved(List<BodyHandle>)` | ✅ |
| 3 | `RapierScenes` implements `IPhysicsMotion` | ✅ |
| 4 | Libraries rebuilt (Linux x64, Windows x64) | ✅ |
| 5 | `dotnet` tests | ✅ |

Rules: P16 (Q176 C as corrected, Q178 (1) A, (2) A).

---

## 1. Overview

The Core reads a transform that follows a Rapier body only after the backend reports that the body moved, then keeps reading it every tick until it has sent the settle (P16). The backend therefore has to report every change of a body's pose, from any cause:

| Cause | How it is reported |
|---|---|
| The simulation moves a dynamic or kinematic body | Rapier's active set: `physics.active_bodies()` holds the awake dynamic and kinematic bodies after a step. A body leaves it only once Rapier puts it to sleep. |
| `SetBody` (state written from C#, proxies, `StandaloneEntity.Position`) | `set_state` adds the body to `written`. Setting the pose also wakes a dynamic or kinematic body, but a static body never enters the active set. |
| `SetKind` | `set_kind` adds the body to `written`. |
| `Load` (rollback) | every body of the world is added to `written`. |
| Forces and impulses | they wake the body (`wake_up` is `true`), which is then in the active set. |

`collect_moved` takes `written` (and empties it), adds the ids of the active bodies (`user_data` holds the id), keeps the ids that still have a body, and stores the result, sorted, for `fr_world_moved_copy`. The order has no meaning for the Core, which sorts by spawn order.

`RapierWorld.CollectMoved` calls `fr_world_moved` for the count, grows its buffer if needed, copies with `fr_world_moved_copy` and adds the handles. It is meant to be called once per step: a second call before the next step returns the active bodies again, without the written ones. The Core calls it once per tick, after the tick handlers, so a body written in a tick handler is collected in that tick (test `AConsoleBallMovedThroughItsBodyInATickHandlerIsSentInThatTick`).

`RapierScenes` implements `IPhysicsMotion`:

- `ReportsMotion(entity)`: the entity has at least one body in a world that is not disposed. Unowned bodies belong to no entity.
- `CollectMoved(into)`: for every 3D world, then every 2D world, the entities that own the moved bodies (`owners`), then the entities that lost their bodies since the last call: `RemoveBodies(entity)`, and the owners of the bodies of a scene that `UnloadScene` disposed. The Core reads those entities once more and finds that `ReportsMotion` is now false, so they go back to per-tick reads. An entity with several moved bodies appears several times; the Core ignores repeats.

### Extra reads

A body whose state is set stays in Rapier's active set until Rapier puts it to sleep, about half a second without motion (the test sees 27 steps at 60 Hz for a kinematic body whose position was set once). Those are extra reads of an unchanged pose. Nothing extra is sent, because the Core compares with the last value sent.

---

## 2. Code

`native/fomoxa-rapier/src/world3d.rs`, changed parts (`world2d.rs` has the same changes with the `fr2_` names):

```diff
@@ -136,4 +136,6 @@ pub struct World {
     next_group: u32,
     scratch: Vec<u8>,
+    written: BTreeSet<u32>,
+    moved: Vec<u32>,
 }
 
@@ -150,4 +152,6 @@ impl World {
             next_group: 1,
             scratch: Vec::new(),
+            written: BTreeSet::new(),
+            moved: Vec::new(),
         }
     }
@@ -304,4 +308,5 @@ impl World {
             Some(body) => {
                 apply_state(body, state);
+                self.written.insert(id);
                 true
             }
@@ -314,4 +319,5 @@ impl World {
             Some(body) => {
                 body.set_body_type(body_type(kind), true);
+                self.written.insert(id);
                 true
             }
@@ -324,4 +330,13 @@ impl World {
     }
 
+    fn collect_moved(&mut self) -> usize {
+        let mut moved: BTreeSet<u32> = std::mem::take(&mut self.written);
+        for (_, body) in self.physics.active_bodies() {
+            moved.insert(body.user_data as u32);
+        }
+        self.moved = moved.into_iter().filter(|id| self.bodies.contains_key(id)).collect();
+        self.moved.len()
+    }
+
     fn snapshot(&mut self) -> usize {
         let handles = self
@@ -358,4 +373,6 @@ impl World {
         }
 
+        self.written.extend(self.bodies.keys().copied());
+
         let current: BTreeMap<u32, (FrBodyState, u32)> = self
             .bodies
@@ -931,4 +948,23 @@ pub unsafe extern "C" fn fr_collider_touching(world_ptr: *mut World, body: u32,
 }
 
+#[no_mangle]
+pub unsafe extern "C" fn fr_world_moved(world_ptr: *mut World) -> u32 {
+    world(world_ptr).map_or(0, |world| world.collect_moved() as u32)
+}
+
+#[no_mangle]
+pub unsafe extern "C" fn fr_world_moved_copy(world_ptr: *mut World, bodies: *mut u32, capacity: u32) -> u32 {
+    let world = match world(world_ptr) {
+        Some(world) => world,
+        None => return 0,
+    };
+    let count = world.moved.len().min(capacity as usize);
+    if count > 0 && !bodies.is_null() {
+        slice::from_raw_parts_mut(bodies, count).copy_from_slice(&world.moved[..count]);
+    }
+
+    count as u32
+}
+
 #[no_mangle]
 pub unsafe extern "C" fn fr_world_snapshot(world_ptr: *mut World) -> u32 {
```

`com.fomoxa.networking.rapier/Runtime/Rapier/RapierNative.cs`, before `fr_world_snapshot` and before `fr2_world_snapshot`:

```csharp
        [DllImport(Library)]
        internal static extern uint fr_world_moved(IntPtr world);

        [DllImport(Library)]
        internal static extern uint fr_world_moved_copy(IntPtr world, uint[] bodies, uint capacity);
```

```csharp
        [DllImport(Library)]
        internal static extern uint fr2_world_moved(IntPtr world);

        [DllImport(Library)]
        internal static extern uint fr2_world_moved_copy(IntPtr world, uint[] bodies, uint capacity);
```

`IRapierBodies.cs`: `using System.Collections.Generic;` and, after `SetRewindable`:

```csharp
        void CollectMoved(List<BodyHandle> into);
```

`RapierWorld.cs`: a buffer after `overlapped`, and the method after `Overlap` (`RapierWorld2D.cs` is the same with `fr2_`):

```csharp
        private uint[] moved = new uint[64];
```

```csharp
        public void CollectMoved(List<BodyHandle> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            uint count = RapierNative.fr_world_moved(world);
            if (count == 0)
            {
                return;
            }

            if (moved.Length < count)
            {
                moved = new uint[Math.Max(count, (uint)moved.Length * 2)];
            }

            count = RapierNative.fr_world_moved_copy(world, moved, count);
            for (int index = 0; index < (int)count; index++)
            {
                into.Add(new BodyHandle((int)moved[index]));
            }
        }
```

`RapierScenes.cs`:

```csharp
    public sealed class RapierScenes : IPhysicsScenes, IPhysicsMotion, IDisposable
```

Fields after `unowned2D`:

```csharp
        private readonly List<BodyHandle> movedBodies = new List<BodyHandle>();
        private readonly List<INetworkEntity> released = new List<INetworkEntity>();
```

`RemoveBodies` ends with `released.Add(entity);` after the loop that removes the parts. The two members of `IPhysicsMotion`, after `RemoveBodies`:

```csharp
        public bool ReportsMotion(INetworkEntity entity)
        {
            if (entity == null || !bodies.TryGetValue(entity, out List<Part> parts))
            {
                return false;
            }

            foreach (Part part in parts)
            {
                if (!part.World.IsDisposed)
                {
                    return true;
                }
            }

            return false;
        }

        public void CollectMoved(List<INetworkEntity> into)
        {
            foreach (RapierWorld world in worlds.Values)
            {
                CollectMoved(world, into);
            }

            foreach (RapierWorld2D world in worlds2D.Values)
            {
                CollectMoved(world, into);
            }

            into.AddRange(released);
            released.Clear();
        }
```

`Dispose` also clears `released`. Before `ForgetOwners`, and `ForgetOwners` itself, which now remembers the owners it forgets:

```csharp
        private void CollectMoved(IRapierBodies world, List<INetworkEntity> into)
        {
            movedBodies.Clear();
            world.CollectMoved(movedBodies);
            foreach (BodyHandle body in movedBodies)
            {
                if (owners.TryGetValue((world, body), out INetworkEntity entity))
                {
                    into.Add(entity);
                }
            }

            movedBodies.Clear();
        }

        private void ForgetOwners(IPhysicsSimulation world)
        {
            forgotten.Clear();
            foreach (KeyValuePair<(IPhysicsSimulation World, BodyHandle Body), INetworkEntity> owner in owners)
            {
                if (ReferenceEquals(owner.Key.World, world))
                {
                    forgotten.Add(owner.Key);
                    released.Add(owner.Value);
                }
            }

            foreach ((IPhysicsSimulation World, BodyHandle Body) key in forgotten)
            {
                owners.Remove(key);
            }

            forgotten.Clear();
        }
```

## 3. Tests

`tests/RapierWorldTest.cs`, before `TheMotionOfABodyLocksAxesTurnsGravityOffAndDamps`, and a helper before `Run`:

```csharp
        [Test]
        public void AFallingBodyIsMovedUntilItSleepsAndAWrittenOrLoadedBodyIsMovedOnce()
        {
            using (var world = new RapierWorld(Gravity))
            {
                world.AddStatic(new[] { Ground(0) });
                BodyHandle ball = world.CreateBody(Ball(new Vector3(0f, 2f, 0f), 1f));
                BodyHandle parked = world.CreateBody(Ball(new Vector3(5f, 0.5f, 0f), 1f));
                world.SetKind(parked, BodyKind.Kinematic);
                BodyHandle fixedBody = world.CreateBody(Ball(new Vector3(8f, 0.5f, 0f), 1f));
                world.SetKind(fixedBody, BodyKind.Static);
                BodyHandle removed = world.CreateBody(Ball(new Vector3(-5f, 2f, 0f), 1f));
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);

                world.Step(Tick);
                List<BodyHandle> falling = Moved(world);
                world.SetBody(removed, new BodyState { Position = new Vector3(-5f, 3f, 0f), Rotation = Quaternion.Identity });
                world.RemoveBody(removed);
                List<BodyHandle> afterRemove = Moved(world);
                Run(world, 600);
                Moved(world);
                world.Step(Tick);
                List<BodyHandle> resting = Moved(world);
                world.SetBody(parked, new BodyState { Position = new Vector3(5f, 1f, 0f), Rotation = Quaternion.Identity });
                world.SetBody(fixedBody, new BodyState { Position = new Vector3(8f, 1f, 0f), Rotation = Quaternion.Identity });
                world.Step(Tick);
                List<BodyHandle> written = Moved(world);
                Run(world, 120);
                Moved(world);
                world.Step(Tick);
                List<BodyHandle> asleepAgain = Moved(world);
                world.Load(snapshot);
                List<BodyHandle> loaded = Moved(world);

                CollectionAssert.AreEquivalent(new[] { ball, parked, fixedBody, removed }, falling);
                CollectionAssert.DoesNotContain(afterRemove, removed);
                CollectionAssert.IsEmpty(resting);
                CollectionAssert.AreEquivalent(new[] { parked, fixedBody }, written);
                CollectionAssert.IsEmpty(asleepAgain);
                CollectionAssert.AreEquivalent(new[] { ball, parked, fixedBody }, loaded);
            }
        }
```

```csharp
        private static List<BodyHandle> Moved(RapierWorld world)
        {
            var moved = new List<BodyHandle>();
            world.CollectMoved(moved);
            return moved;
        }
```

`tests/RapierWorld2DTest.cs` has the same test with `RapierWorld2D`, circles and `BodyState2D`, before `ADisposedWorldHasNoBodiesAndRefusesNewOnes`, and the same helper.

`tests/RapierScenesTest.cs`: a test of `IPhysicsMotion` before `AColliderTouchingTheGroundLeadsBackToItsEntityUntilItsBodiesAreRemoved`, and two tests with a console server and client and a real `RapierScenes` before `ATwoDimensionalSceneHasItsOwnWorldSteppedAfterTheThreeDimensionalOne`:

```csharp
        [Test]
        public void MovedBodiesLeadBackToTheirEntitiesAndEntitiesThatLoseTheirBodiesAreReportedOnce()
        {
            using (var scenes = new RapierScenes())
            {
                scenes.LoadScene(Arena(ArenaSceneId));
                StandaloneEntity falling = Entity();
                StandaloneEntity removed = Entity();
                StandaloneEntity unowned = Entity();
                StandaloneEntity flat = Entity();
                scenes.AddBody(falling, ArenaSceneId, Ball(new Vector3(0f, 3f, 0f)));
                scenes.AddBody2D(flat, ArenaSceneId, new BodyDesc2D(BodyKind.Dynamic, BodyShape2D.Circle(0.5f), new Vector2(0f, 3f), 0f, 1f));
                scenes.AddBody(falling, ArenaSceneId, Ball(new Vector3(0f, 5f, 0f)));
                scenes.AddBody(removed, ArenaSceneId, Ball(new Vector3(4f, 3f, 0f)));
                scenes.AddBody(ArenaSceneId, Ball(new Vector3(-4f, 3f, 0f)));

                Run(scenes, 1);
                List<INetworkEntity> moving = Moved(scenes);
                scenes.RemoveBodies(removed);
                List<INetworkEntity> afterRemove = Moved(scenes);
                bool removedReports = scenes.ReportsMotion(removed);
                List<INetworkEntity> onceRemoved = Moved(scenes);
                bool fallingReportsBeforeUnload = scenes.ReportsMotion(falling);
                scenes.UnloadScene(ArenaSceneId);
                List<INetworkEntity> afterUnload = Moved(scenes);

                CollectionAssert.AreEquivalent(new[] { falling, falling, removed, flat }, moving);
                CollectionAssert.AreEquivalent(new[] { falling, falling, removed, flat }, afterRemove);
                Assert.IsFalse(removedReports);
                CollectionAssert.AreEquivalent(new[] { falling, falling, flat }, onceRemoved);
                Assert.IsTrue(fallingReportsBeforeUnload);
                Assert.IsFalse(scenes.ReportsMotion(unowned));
                CollectionAssert.AreEquivalent(new[] { falling, falling, flat }, afterUnload);
                Assert.IsFalse(scenes.ReportsMotion(falling));
            }
        }

        [Test]
        public void AConsoleBallIsNotReadWhileItSleepsAndIsReadAgainOnceItIsPushed()
        {
            var files = new SceneFiles();
            files.Add(Arena(ArenaSceneId));
            var prefabs = new StandalonePrefabs();
            var onClient = new List<SyncedBall>();
            prefabs.Register(SyncedBallPrefabId, 0xBA12, () =>
            {
                var created = new SyncedBall();
                onClient.Add(created);
                return created;
            });
            var logged = new List<Exception>();
            var log = new NetworkLog(logged.Add, message => { });
            var settings = new NetworkSettings { PhysicsBackend = PhysicsBackend.Rapier };
            var factory = new LoopbackFactory();
            TimeSpan now = TimeSpan.Zero;
            using (var serverPhysics = new RapierScenes())
            using (var clientPhysics = new RapierScenes())
            {
                NetworkRuntime server = StandaloneRuntime.Create(TestObjects.Registry(), settings, factory, prefabs, new StandaloneBehaviours(), files, () => now, log, serverPhysics);
                NetworkRuntime client = StandaloneRuntime.Create(TestObjects.Registry(), settings, factory, prefabs, new StandaloneBehaviours(), files, () => now, log, clientPhysics);
                void Frames(int count)
                {
                    for (int frame = 0; frame < count; frame++)
                    {
                        now += TimeSpan.FromSeconds(Tick);
                        server.BeginFrame(now, Tick);
                        client.BeginFrame(now, Tick);
                        server.EndFrame();
                        client.EndFrame();
                    }
                }

                server.ServerManager.StartConnection(7777);
                client.ClientManager.StartConnection("127.0.0.1", 7777);
                Frames(10);
                server.ServerManager.Scenes.LoadGlobal(new[] { ArenaSceneId });
                Frames(10);
                var ball = new SyncedBall { SceneId = ArenaSceneId, Position = new Vector3(0f, 3f, 0f) };
                server.ServerManager.Spawn(ball);
                Frames(20);
                int whileFalling = Received(onClient).Count;
                Frames(600);
                List<TransformSample> received = Received(onClient);
                int asleep = received.Count;
                TransformSample settle = received[asleep - 1];
                Vector3 restingAt = ball.Position;
                bool trackedAtRest = ball.Record.TransformOrderNode == null;
                Frames(60);
                int stillAsleep = received.Count;
                PhysicsBody body = ball.Body;
                body.Velocity = new Vector3(3f, 0f, 0f);
                Frames(10);

                Assert.IsEmpty(logged);
                Assert.Greater(whileFalling, 1);
                Assert.IsTrue(settle.Settle);
                Assert.AreEqual(restingAt, settle.LocalPosition);
                Assert.IsTrue(trackedAtRest);
                Assert.AreEqual(asleep, stillAsleep);
                Assert.Greater(received.Count, stillAsleep + 1);
                Assert.Greater(received[received.Count - 1].LocalPosition.X, 0.1f);
            }
        }

        [Test]
        public void AConsoleBallMovedThroughItsBodyInATickHandlerIsSentInThatTick()
        {
            var files = new SceneFiles();
            files.Add(Arena(ArenaSceneId));
            var prefabs = new StandalonePrefabs();
            var onClient = new List<SyncedBall>();
            prefabs.Register(SyncedBallPrefabId, 0xBA12, () =>
            {
                var created = new SyncedBall();
                onClient.Add(created);
                return created;
            });
            var logged = new List<Exception>();
            var log = new NetworkLog(logged.Add, message => { });
            var settings = new NetworkSettings { PhysicsBackend = PhysicsBackend.Rapier };
            var factory = new LoopbackFactory();
            TimeSpan now = TimeSpan.Zero;
            using (var serverPhysics = new RapierScenes())
            using (var clientPhysics = new RapierScenes())
            {
                NetworkRuntime server = StandaloneRuntime.Create(TestObjects.Registry(), settings, factory, prefabs, new StandaloneBehaviours(), files, () => now, log, serverPhysics);
                NetworkRuntime client = StandaloneRuntime.Create(TestObjects.Registry(), settings, factory, prefabs, new StandaloneBehaviours(), files, () => now, log, clientPhysics);
                void Frames(int count)
                {
                    for (int frame = 0; frame < count; frame++)
                    {
                        now += TimeSpan.FromSeconds(Tick);
                        server.BeginFrame(now, Tick);
                        client.BeginFrame(now, Tick);
                        server.EndFrame();
                        client.EndFrame();
                    }
                }

                server.ServerManager.StartConnection(7777);
                client.ClientManager.StartConnection("127.0.0.1", 7777);
                Frames(10);
                server.ServerManager.Scenes.LoadGlobal(new[] { ArenaSceneId });
                Frames(10);
                var ball = new SyncedBall { SceneId = ArenaSceneId, Position = new Vector3(0f, 3f, 0f) };
                server.ServerManager.Spawn(ball);
                Frames(600);
                List<TransformSample> received = Received(onClient);
                int asleep = received.Count;
                Vector3 restingAt = ball.Position;
                uint movedAt = 0;
                void Move()
                {
                    if (movedAt == 0)
                    {
                        movedAt = server.TimeManager.Tick;
                        PhysicsBody body = ball.Body;
                        body.Position = restingAt + new Vector3(2f, 0f, 0f);
                    }
                }

                server.TimeManager.OnTick += Move;
                Frames(10);
                server.TimeManager.OnTick -= Move;

                Assert.IsEmpty(logged);
                Assert.Greater(received.Count, asleep);
                Assert.AreEqual(movedAt, received[asleep].Tick);
                Assert.AreEqual(restingAt.X + 2f, received[asleep].LocalPosition.X, 1e-4f);
            }
        }
```

New constant `SyncedBallPrefabId = 0x0000_0B02`, and helpers before `SceneFiles`:

```csharp
        private static List<INetworkEntity> Moved(RapierScenes scenes)
        {
            var moved = new List<INetworkEntity>();
            scenes.CollectMoved(moved);
            return moved;
        }

        private static List<TransformSample> Received(List<SyncedBall> onClient)
        {
            Assert.AreEqual(1, onClient.Count);
            return onClient[0].Pose.Received;
        }

        private sealed class SyncedBall : StandaloneEntity
        {
            public SyncedBall()
                : this(new PoseReceiver())
            {
            }

            private SyncedBall(PoseReceiver pose)
                : base(SyncedBallPrefabId, new EntityBehaviour[] { pose })
            {
                Pose = pose;
            }

            public PoseReceiver Pose { get; }

            public override bool TryGetBody(out BodyDesc body)
            {
                body = Ball(Vector3.Zero);
                return true;
            }
        }

        private sealed class PoseReceiver : EntityBehaviour, ITransformReceiver
        {
            public PoseReceiver()
            {
                SyncTransform(new TransformSync(new TransformSyncSettings(true, true, false, 0.01f, 0.1f, 0.01f)), this);
            }

            public List<TransformSample> Received { get; } = new List<TransformSample>();

            public void ResetReceive()
            {
            }

            public void Receive(in TransformSample sample) => Received.Add(sample);
        }
```

### Mutation check

Each mutation changes one place, rebuilds the Linux library when the crate changed, runs `dotnet test tests/`, then is undone.

| | Mutation | Result | Caught by |
|---|---|---|---|
| R1 | `set_state` does not add to `written` (3D) | caught | `AFallingBodyIsMovedUntilItSleepsAndAWrittenOrLoadedBodyIsMovedOnce` |
| R2 | `set_kind` does not add to `written` (3D) | caught | `AFallingBodyIsMovedUntilItSleepsAndAWrittenOrLoadedBodyIsMovedOnce` |
| R3 | `load` does not add every body to `written` (3D) | caught | `AFallingBodyIsMovedUntilItSleepsAndAWrittenOrLoadedBodyIsMovedOnce` |
| R4 | ids without a body are not filtered out (3D) | caught | `AFallingBodyIsMovedUntilItSleepsAndAWrittenOrLoadedBodyIsMovedOnce` |
| R5 | `set_state` does not add to `written` (2D) | caught | `AFallingBodyIsMovedUntilItSleepsAndAWrittenOrLoadedBodyIsMovedOnce` |
| R6 | `load` does not add every body to `written` (2D) | caught | `AFallingBodyIsMovedUntilItSleepsAndAWrittenOrLoadedBodyIsMovedOnce` |
| R7 | ids without a body are not filtered out (2D) | caught | `AFallingBodyIsMovedUntilItSleepsAndAWrittenOrLoadedBodyIsMovedOnce` |
| C1 | `RemoveBodies` does not report the entity | caught | `MovedBodiesLeadBackToTheirEntitiesAndEntitiesThatLoseTheirBodiesAreReportedOnce` |
| C2 | `UnloadScene` does not report the owners of its bodies | caught | `MovedBodiesLeadBackToTheirEntitiesAndEntitiesThatLoseTheirBodiesAreReportedOnce` |
| C3 | `ReportsMotion` ignores disposed worlds | caught | `MovedBodiesLeadBackToTheirEntitiesAndEntitiesThatLoseTheirBodiesAreReportedOnce` |
| C4 | `CollectMoved` skips the 2D worlds | caught | `MovedBodiesLeadBackToTheirEntitiesAndEntitiesThatLoseTheirBodiesAreReportedOnce` |
| C5 | `released` is not cleared after a collection | caught | `MovedBodiesLeadBackToTheirEntitiesAndEntitiesThatLoseTheirBodiesAreReportedOnce` |

The first run of C1 to C5 used a library still built from R7, because the script rebuilt the library only at the end; the 2D world test failed in each of them as well. The script now rebuilds after undoing each crate mutation, and the table shows the second run.

## 4. Cost

A console benchmark outside the repository (`prototypes/11-10-bench`, not committed) is the room benchmark of the `unity` repository with a Rapier body on every crate: a server with `RapierScenes`, one global scene with a ground, 20 crates and 2 peers per room, crates of a room in a group, state marked dirty. The crates rest for 240 ticks, then 300 ticks are measured. A moving crate is moved by 0.1 along x, back and forth, through `Position` before every tick. Three builds:

- before: this repository at `5cc0a34` with the Core at `71c5041`, the crate's pose read through an `ITransformSource` every tick;
- after, source: this step, the same `ITransformSource` read every tick, which isolates the cost of collecting moved bodies;
- after, entity: this step, `SyncTransform(sync, receiver)`, read on signals.

Linux, .NET 8, ms per server tick, mean of two runs:

| Rooms | Moving | Before | After, source | After, entity |
|---:|---:|---:|---:|---:|
| 500 | 0 % | 5.9 | 5.7 | 1.0 |
| 500 | 10 % | 10.1 | 10.3 | 5.1 |
| 2 000 | 0 % | 28.9 | 28.7 | 5.1 |
| 2 000 | 10 % | 49.1 | 49.4 | 28.2 |

The builds ran interleaved. The two runs of one case differ by up to 25 %, alike for every build; within one run, before and after, source, differ by at most 6 % either way, so collecting moved bodies costs nothing measurable. Read on signals, a tick at 2 000 rooms takes 82 % less time with every crate at rest and 43 % less with 10 % moving.

## 5. Results

- Libraries rebuilt from the crate: Linux x64 (`Tools/build-rapier.sh linux`) and Windows x64 (`Tools/build-rapier.sh windows`).
- `dotnet test tests/`: 46/46 on Linux (.NET 8) and Windows (.NET 9, `dotnet.exe` on a copy with `net9.0`).
- Mutation check: 12/12 caught.
- Two-sided check, Windows server: PASS, 1487 common ticks after the start, the same world hash on every one, 0 reconcile mismatches.
- Two-sided check, Linux server: fails. The server sees the peer and spawns its balls, but the Unity client on Windows stays in `Starting` and the server records no ticks. The unchanged repository (`5cc0a34`) failed the same way after the machine restarted, so the cause is the network path from WSL to Windows, not this change.
- A clone of the repository has no `test-project/Assets` folder (the folder is not tracked by git), and Unity then stops with "Couldn't set project path to: C:/…/C:/…/test-project". The working copy has the folder. Out of scope here.
- CI: `.github/workflows/ci.yml` builds the tests against `CORE_COMMIT` of `fomoxa/com.fomoxa.networking`. This change needs a Core with `IPhysicsMotion`, so `CORE_COMMIT` moves to the public Core commit that carries step 11.10, once that commit is pushed. Until then CI of this repository fails to compile.
