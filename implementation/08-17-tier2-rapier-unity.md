# 08.17 Tier 2: static groups and unowned bodies in `RapierPhysics`

> The Unity part of sub-step 08.17 in this repository; the plan and the contract are in `implementation/08-prediction-physics.md` of the `unity` repository, and the Unity side of the Core (`NetworkPhysics`, `BodyDescriptions`) is in its `implementation/08-17-tier2-runtime-statics.md`. Tier 1: [`08-17-tier1-rapier-statics.md`](08-17-tier1-rapier-statics.md). Built and run on a copy of both repositories: Unity EditMode 9/9 on Unity 6000.5.7f1 (Windows), one more test skipped because it is the two-sided check; the two-sided check passes with the Linux and the Windows server.

| | Work | Status |
|---|---|:---:|
| 1 | `RapierPhysics`: the nine new members of `NetworkPhysics` | ✅ |
| 2 | `RapierContacts`: static groups with their contact components | ✅ |
| 3 | Two-sided check: the ball has the same motion on both sides | ✅ |
| 4 | Unity tests | ✅ |

Rules: P27 (Q166 (3) A; Q167 (1) A, (2) A; Q168 (1) A, (2) A).

---

## 1. Overview

- The members that take a `Scene` pass the scene's `SceneId` (the one `RapierPhysics` uses for the scene's worlds) to `RapierScenes`. They throw `InvalidOperationException` before `Begin`; `RemoveStatic`, `RemoveBody`, `RemoveBody2D` do nothing then.
- Before adding, `RapierPhysics` syncs the scenes (the step it also runs before each tick), so the scene file's static colliders are always the first group of a world and keep the indices 0 to n−1 that the contact table of the scene expects. A group added before the first tick would otherwise take those indices.
- `AddStatic(GameObject root)`, `AddStatic2D(GameObject root)`:
  - list the static colliders under `root` with `BodyDescriptions.StaticColliders(root, …)` / `StaticColliders2D(root, …)` and describe each source once (2D skips repeated neighbours, as the scene file export does);
  - add them as one group of the world of `root.scene`;
  - give the group's static indices to the contact table of that world and attach the `NetworkTrigger*` and `NetworkCollision*` under `root` (not under a `NetworkObject`) to the world's tracker, as for the static objects of a scene (Q166 (3) A).
- `RemoveStatic` detaches the group's components, clears its entries in the contact table and removes the group from the world. Unloading a scene does the same for every group of the scene.
- The contact table of a world is indexed by static index. A group fills its own range; removed ranges stay empty, as the crate never reuses indices.

### Two-sided check

The Unity converter now carries `Rigidbody.angularDamping`, whose Unity default is 0.05, while the server built the ball without damping; the first run after the change showed 3404 reconcile mismatches. `TwoSidedScenario.BallMotion` (no locks, no gravity, no damping) is now the motion of the server's ball description, and the client sets its prefab's `Rigidbody` from it. With that the check passes again.

---

## 2. Code

`Runtime/Unity/RapierPhysics.cs`, added members:

```csharp
public override StaticGroup AddStatic(Scene scene, IReadOnlyList<ColliderDesc> colliders) => Begun().AddStatic(IdOf(scene), colliders);

public override StaticGroup AddStatic2D(Scene scene, IReadOnlyList<ColliderDesc2D> colliders) => Begun().AddStatic2D(IdOf(scene), colliders);

public override StaticGroup AddStatic(GameObject root)
{
    if (root == null)
    {
        throw new ArgumentNullException(nameof(root));
    }

    uint sceneId = IdOf(root.scene);
    var sources = new List<Collider>();
    var colliders = new List<ColliderDesc>();
    BodyDescriptions.StaticColliders(root, sources);
    for (int index = 0; index < sources.Count; index++)
    {
        if (index == 0 || sources[index] != sources[index - 1])
        {
            BodyDescriptions.DescribeStatic(sources[index], null, colliders);
        }
    }

    StaticGroup group = Begun().AddStatic(sceneId, colliders);
    contacts.AddGroup(group, scenes.WorldOf(sceneId), sources, root);
    return group;
}

public override StaticGroup AddStatic2D(GameObject root)
{
    if (root == null)
    {
        throw new ArgumentNullException(nameof(root));
    }

    uint sceneId = IdOf(root.scene);
    var sources = new List<Collider2D>();
    var colliders = new List<ColliderDesc2D>();
    BodyDescriptions.StaticColliders2D(root, sources);
    for (int index = 0; index < sources.Count; index++)
    {
        if (index == 0 || sources[index] != sources[index - 1])
        {
            BodyDescriptions.DescribeStatic2D(sources[index], null, colliders);
        }
    }

    StaticGroup group = Begun().AddStatic2D(sceneId, colliders);
    contacts.AddGroup2D(group, scenes.WorldOf2D(sceneId), sources, root);
    return group;
}

public override void RemoveStatic(StaticGroup group)
{
    if (scenes == null)
    {
        return;
    }

    contacts.RemoveGroup(group.Id);
    scenes.RemoveStatic(group);
}

public override PhysicsBody AddBody(Scene scene, in BodyDesc body) => Begun().AddBody(IdOf(scene), body);

public override PhysicsBody2D AddBody2D(Scene scene, in BodyDesc2D body) => Begun().AddBody2D(IdOf(scene), body);

public override void RemoveBody(PhysicsBody body) => scenes?.RemoveBody(body);

public override void RemoveBody2D(PhysicsBody2D body) => scenes?.RemoveBody2D(body);

private RapierScenes Begun()
{
    if (scenes == null)
    {
        throw new InvalidOperationException("this RapierPhysics has not begun; start its NetworkManager first");
    }

    SyncScenes();
    return scenes;
}
```

`Runtime/Unity/RapierContacts.cs`, added fields and members:

```csharp
private readonly Dictionary<int, GroupColliders> groups = new Dictionary<int, GroupColliders>();
private readonly List<int> leavingGroups = new List<int>();

public void AddGroup(StaticGroup group, RapierWorld world, List<Collider> sources, GameObject root)
{
    if (!statics.TryGetValue(world, out List<Collider> indexed))
    {
        indexed = new List<Collider>();
        statics.Add(world, indexed);
    }

    Assign(indexed, group.FirstIndex, sources);
    Locate(sources, world, default, places, group.FirstIndex);
    var entry = new GroupColliders { SceneId = group.SceneId, World = world, Sources = sources, FirstIndex = group.FirstIndex };
    groups.Add(group.Id, entry);
    root.GetComponentsInChildren(true, triggers);
    root.GetComponentsInChildren(true, collisions);
    DropNetworkObjects(triggers);
    DropNetworkObjects(collisions);
    Claim(TrackerFor(world), entry.Claimed);
}

public void AddGroup2D(StaticGroup group, RapierWorld2D world, List<Collider2D> sources, GameObject root)
{
    if (!statics2D.TryGetValue(world, out List<Collider2D> indexed))
    {
        indexed = new List<Collider2D>();
        statics2D.Add(world, indexed);
    }

    Assign(indexed, group.FirstIndex, sources);
    Locate(sources, world, default, places2D, group.FirstIndex);
    var entry = new GroupColliders { SceneId = group.SceneId, World2D = world, Sources2D = sources, FirstIndex = group.FirstIndex };
    groups.Add(group.Id, entry);
    root.GetComponentsInChildren(true, triggers2D);
    root.GetComponentsInChildren(true, collisions2D);
    DropNetworkObjects(triggers2D);
    DropNetworkObjects(collisions2D);
    Claim(TrackerFor2D(world), entry.Claimed);
}

public void RemoveGroup(int groupId)
{
    if (!groups.Remove(groupId, out GroupColliders entry))
    {
        return;
    }

    Release(entry.Claimed);
    if (entry.World != null && statics.TryGetValue(entry.World, out List<Collider> indexed))
    {
        Forget(entry.Sources, entry.World, places);
        Clear(indexed, entry.FirstIndex, entry.Sources.Count);
    }

    if (entry.World2D != null && statics2D.TryGetValue(entry.World2D, out List<Collider2D> indexed2D))
    {
        Forget(entry.Sources2D, entry.World2D, places2D);
        Clear(indexed2D, entry.FirstIndex, entry.Sources2D.Count);
    }
}

private static void Assign<TCollider>(List<TCollider> indexed, int firstIndex, List<TCollider> sources)
    where TCollider : class
{
    while (indexed.Count < firstIndex + sources.Count)
    {
        indexed.Add(null);
    }

    for (int offset = 0; offset < sources.Count; offset++)
    {
        indexed[firstIndex + offset] = sources[offset];
    }
}

private static void Clear<TCollider>(List<TCollider> indexed, int firstIndex, int count)
    where TCollider : class
{
    for (int index = firstIndex; index < firstIndex + count && index < indexed.Count; index++)
    {
        indexed[index] = null;
    }
}

private sealed class GroupColliders
{
    public uint SceneId;
    public int FirstIndex;
    public RapierWorld World;
    public RapierWorld2D World2D;
    public List<Collider> Sources;
    public List<Collider2D> Sources2D;
    public readonly List<MonoBehaviour> Claimed = new List<MonoBehaviour>();
}
```

Changed in the same file: `UnloadScene` removes the scene's groups first, `Clear` releases the groups' components and clears `groups`, and `Locate` takes the first static index:

```csharp
public void UnloadScene(uint sceneId)
{
    leavingGroups.Clear();
    foreach (KeyValuePair<int, GroupColliders> group in groups)
    {
        if (group.Value.SceneId == sceneId)
        {
            leavingGroups.Add(group.Key);
        }
    }

    foreach (int groupId in leavingGroups)
    {
        RemoveGroup(groupId);
    }

    leavingGroups.Clear();
    if (!loaded.Remove(sceneId, out SceneColliders entry))
    {
        return;
    }

    Release(entry.Claimed);
    if (entry.World != null && statics.Remove(entry.World, out List<Collider> sources))
    {
        Forget(sources, entry.World, places);
        trackers.Remove(entry.World);
    }

    if (entry.World2D != null && statics2D.Remove(entry.World2D, out List<Collider2D> sources2D))
    {
        Forget(sources2D, entry.World2D, places2D);
        trackers2D.Remove(entry.World2D);
    }
}

private static void Locate<TCollider>(List<TCollider> sources, IPhysicsSimulation world, BodyHandle body, Dictionary<TCollider, Place> into, int firstIndex = 0)
{
    for (int offset = 0; offset < sources.Count; offset++)
    {
        TCollider source = sources[offset];
        int index = firstIndex + offset;
        if (into.TryGetValue(source, out Place place) && ReferenceEquals(place.World, world) && place.First.Index + place.Count == index)
        {
            into[source] = new Place(world, place.First, place.Count + 1);
        }
        else
        {
            into[source] = new Place(world, body.IsValid ? RapierCollider.OfBody(body, index) : RapierCollider.Static(index), 1);
        }
    }
}
```

`Tests/Unity/TwoSided/TwoSidedScenario.cs`, changed:

```csharp
public static readonly BodyMotion BallMotion = new BodyMotion(BodyLocks.None, false, 0f, 0f);

public static BodyDesc Ball(Vector3 position) =>
    new BodyDesc(BodyKind.Dynamic, new[] { new ColliderDesc(BodyShape.Sphere(0.5f), Vector3.Zero, Quaternion.Identity, BallMaterial, 0, false) }, position, Quaternion.Identity, 1f, BallMotion);
```

`Tests/Unity/TwoSided/TwoSidedClientCheck.cs`, the ball's `Rigidbody`:

```csharp
var rigidbody = template.AddComponent<Rigidbody>();
rigidbody.mass = 1f;
rigidbody.useGravity = TwoSidedScenario.BallMotion.UseGravity;
rigidbody.linearDamping = TwoSidedScenario.BallMotion.LinearDamping;
rigidbody.angularDamping = TwoSidedScenario.BallMotion.AngularDamping;
```

---

## 3. Tests

`Tests/Unity/RapierPhysicsTest.cs`, added:

```csharp
[Test]
public void ABlockAddedAtRuntimeHoldsABallAndItsTriggerSeesTheBallUntilTheBlockIsRemoved()
{
    NetworkObject ballPrefab = Prefab("Ball2D", BallPrefabId, ball =>
    {
        ball.AddComponent<CircleCollider2D>().radius = 0.25f;
        ball.AddComponent<Rigidbody2D>();
    });
    NetworkManager server = Manager(ballPrefab);
    UseActiveScene(server);
    server.ServerManager.StartConnection(1);
    var physics = server.GetComponent<RapierPhysics>();
    var block = new GameObject("Block");
    created.Add(block);
    var floor = new GameObject("Floor");
    floor.transform.SetParent(block.transform, false);
    floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);
    floor.AddComponent<BoxCollider2D>().size = new Vector2(10f, 1f);
    var zone = new GameObject("Zone");
    zone.transform.SetParent(block.transform, false);
    zone.transform.localPosition = new Vector3(0f, 2f, 0f);
    var zoneBox = zone.AddComponent<BoxCollider2D>();
    zoneBox.size = Vector2.one;
    zoneBox.isTrigger = true;
    var trigger = Enable(zone.AddComponent<NetworkTrigger2D>());
    var events = new List<string>();
    trigger.OnEnter += other => events.Add("enter " + other.name);
    trigger.OnExit += other => events.Add("exit " + other.name);

    StaticGroup group = physics.AddStatic2D(block);
    NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 4f, 0f));
    RunFrames(180);

    Assert.AreEqual((ArenaSceneId, 0, 2), (group.SceneId, group.FirstIndex, group.Count));
    Assert.AreEqual(0.25f, ball.transform.position.y, 0.05f);
    Assert.IsTrue(trigger.IsAttached);
    CollectionAssert.AreEqual(new[] { "enter Ball2D(Clone)", "exit Ball2D(Clone)" }, events);

    physics.RemoveStatic(group);
    RunFrames(60);

    Assert.IsFalse(trigger.IsAttached);
    Assert.Less(ball.transform.position.y, -1f);
}

[Test]
public void UnownedBodiesAndGroupsNeedAPhysicsThatHasBegun()
{
    var holder = new GameObject("Unbegun");
    created.Add(holder);
    var physics = holder.AddComponent<RapierPhysics>();

    Assert.Throws<InvalidOperationException>(() => physics.AddStatic2D(SceneManager.GetActiveScene(), Array.Empty<ColliderDesc2D>()));
    Assert.Throws<InvalidOperationException>(() => physics.AddBody2D(SceneManager.GetActiveScene(), new BodyDesc2D(BodyKind.Kinematic, BodyShape2D.Circle(1f), System.Numerics.Vector2.Zero, 0f, 0f)));
    Assert.DoesNotThrow(() => physics.RemoveStatic(default));
}
```
