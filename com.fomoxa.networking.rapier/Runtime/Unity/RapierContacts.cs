using System.Collections.Generic;
using Fomoxa.Networking.Rapier;
using Fomoxa.Networking.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Rapier
{
    internal sealed class RapierContacts : IContactQuery, IContactQuery2D
    {
        private readonly RapierScenes scenes;
        private readonly Dictionary<IPhysicsSimulation, ContactTracker<Collider>> trackers = new Dictionary<IPhysicsSimulation, ContactTracker<Collider>>();
        private readonly Dictionary<IPhysicsSimulation, ContactTracker<Collider2D>> trackers2D = new Dictionary<IPhysicsSimulation, ContactTracker<Collider2D>>();
        private readonly Dictionary<IPhysicsSimulation, List<Collider>> statics = new Dictionary<IPhysicsSimulation, List<Collider>>();
        private readonly Dictionary<IPhysicsSimulation, List<Collider2D>> statics2D = new Dictionary<IPhysicsSimulation, List<Collider2D>>();
        private readonly Dictionary<Collider, Place> places = new Dictionary<Collider, Place>();
        private readonly Dictionary<Collider2D, Place> places2D = new Dictionary<Collider2D, Place>();
        private readonly Dictionary<NetworkObject, BodyColliders> bodies = new Dictionary<NetworkObject, BodyColliders>();
        private readonly Dictionary<uint, SceneColliders> loaded = new Dictionary<uint, SceneColliders>();
        private readonly List<RapierCollider> touched = new List<RapierCollider>();
        private readonly List<NetworkTrigger> triggers = new List<NetworkTrigger>();
        private readonly List<NetworkCollision> collisions = new List<NetworkCollision>();
        private readonly List<NetworkTrigger2D> triggers2D = new List<NetworkTrigger2D>();
        private readonly List<NetworkCollision2D> collisions2D = new List<NetworkCollision2D>();

        public RapierContacts(RapierScenes scenes)
        {
            this.scenes = scenes;
        }

        public IContactTracker TrackerOf(IPhysicsSimulation world)
        {
            if (world == null)
            {
                return null;
            }

            if (trackers.TryGetValue(world, out ContactTracker<Collider> tracker))
            {
                return tracker;
            }

            return trackers2D.TryGetValue(world, out ContactTracker<Collider2D> tracker2D) ? tracker2D : null;
        }

        public void AddBody(NetworkObject networkObject, RapierWorld world, List<Collider> sources, RapierWorld2D world2D, List<Collider2D> sources2D)
        {
            var entry = new BodyColliders { World = world, World2D = world2D, Sources = sources, Sources2D = sources2D };
            bodies[networkObject] = entry;
            if (world != null && scenes.TryGetBody(networkObject, world, out BodyHandle body))
            {
                Locate(sources, world, body, places);
            }

            if (world2D != null && scenes.TryGetBody(networkObject, world2D, out BodyHandle body2D))
            {
                Locate(sources2D, world2D, body2D, places2D);
            }
        }

        public void RemoveBody(NetworkObject networkObject)
        {
            if (!bodies.Remove(networkObject, out BodyColliders entry))
            {
                return;
            }

            Release(entry.Claimed);
            Forget(entry.Sources, entry.World, places);
            Forget(entry.Sources2D, entry.World2D, places2D);
        }

        public void Claim(NetworkObject networkObject)
        {
            if (networkObject == null || !bodies.TryGetValue(networkObject, out BodyColliders entry))
            {
                return;
            }

            if (entry.World != null)
            {
                ContactTracker<Collider> tracker = TrackerFor(entry.World);
                networkObject.GetComponentsInChildren(true, triggers);
                networkObject.GetComponentsInChildren(true, collisions);
                Claim(tracker, entry.Claimed);
            }

            if (entry.World2D != null)
            {
                ContactTracker<Collider2D> tracker2D = TrackerFor2D(entry.World2D);
                networkObject.GetComponentsInChildren(true, triggers2D);
                networkObject.GetComponentsInChildren(true, collisions2D);
                Claim(tracker2D, entry.Claimed);
            }
        }

        public void LoadScene(Scene scene, uint sceneId, RapierWorld world, int colliderCount, RapierWorld2D world2D, int colliderCount2D)
        {
            var entry = new SceneColliders();
            loaded[sceneId] = entry;
            if (world != null)
            {
                var sources = new List<Collider>();
                BodyDescriptions.StaticColliders(scene, sources);
                if (sources.Count == colliderCount)
                {
                    entry.World = world;
                    statics[world] = sources;
                    Locate(sources, world, default, places);
                    ContactTracker<Collider> tracker = TrackerFor(world);
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        root.GetComponentsInChildren(true, triggers);
                        root.GetComponentsInChildren(true, collisions);
                        DropNetworkObjects(triggers);
                        DropNetworkObjects(collisions);
                        Claim(tracker, entry.Claimed);
                    }
                }
                else
                {
                    Mismatch(scene, "3D", sources.Count, colliderCount);
                }
            }

            if (world2D != null)
            {
                var sources2D = new List<Collider2D>();
                BodyDescriptions.StaticColliders2D(scene, sources2D);
                if (sources2D.Count == colliderCount2D)
                {
                    entry.World2D = world2D;
                    statics2D[world2D] = sources2D;
                    Locate(sources2D, world2D, default, places2D);
                    ContactTracker<Collider2D> tracker2D = TrackerFor2D(world2D);
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        root.GetComponentsInChildren(true, triggers2D);
                        root.GetComponentsInChildren(true, collisions2D);
                        DropNetworkObjects(triggers2D);
                        DropNetworkObjects(collisions2D);
                        Claim(tracker2D, entry.Claimed);
                    }
                }
                else
                {
                    Mismatch(scene, "2D", sources2D.Count, colliderCount2D);
                }
            }
        }

        public void UnloadScene(uint sceneId)
        {
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

        public void Clear()
        {
            foreach (BodyColliders entry in bodies.Values)
            {
                Release(entry.Claimed);
            }

            foreach (SceneColliders entry in loaded.Values)
            {
                Release(entry.Claimed);
            }

            bodies.Clear();
            loaded.Clear();
            trackers.Clear();
            trackers2D.Clear();
            statics.Clear();
            statics2D.Clear();
            places.Clear();
            places2D.Clear();
        }

        public void Collect(Collider own, HashSet<Collider> into)
        {
            if (!places.TryGetValue(own, out Place place) || !(place.World is RapierWorld world) || world.IsDisposed)
            {
                return;
            }

            touched.Clear();
            for (int piece = 0; piece < place.Count; piece++)
            {
                world.Touching(place.At(piece), touched);
            }

            foreach (RapierCollider collider in touched)
            {
                Collider other = ColliderAt(world, collider);
                if (other != null)
                {
                    into.Add(other);
                }
            }

            touched.Clear();
        }

        public void Collect(Collider2D own, HashSet<Collider2D> into)
        {
            if (!places2D.TryGetValue(own, out Place place) || !(place.World is RapierWorld2D world) || world.IsDisposed)
            {
                return;
            }

            touched.Clear();
            for (int piece = 0; piece < place.Count; piece++)
            {
                world.Touching(place.At(piece), touched);
            }

            foreach (RapierCollider collider in touched)
            {
                Collider2D other = ColliderAt(world, collider);
                if (other != null)
                {
                    into.Add(other);
                }
            }

            touched.Clear();
        }

        private Collider ColliderAt(RapierWorld world, RapierCollider collider)
        {
            if (collider.IsStatic)
            {
                return statics.TryGetValue(world, out List<Collider> sources) && collider.Index < sources.Count ? sources[collider.Index] : null;
            }

            return scenes.EntityOf(world, collider.Body) is NetworkObject networkObject && bodies.TryGetValue(networkObject, out BodyColliders entry) && ReferenceEquals(entry.World, world) && collider.Index < entry.Sources.Count
                ? entry.Sources[collider.Index]
                : null;
        }

        private Collider2D ColliderAt(RapierWorld2D world, RapierCollider collider)
        {
            if (collider.IsStatic)
            {
                return statics2D.TryGetValue(world, out List<Collider2D> sources) && collider.Index < sources.Count ? sources[collider.Index] : null;
            }

            return scenes.EntityOf(world, collider.Body) is NetworkObject networkObject && bodies.TryGetValue(networkObject, out BodyColliders entry) && ReferenceEquals(entry.World2D, world) && collider.Index < entry.Sources2D.Count
                ? entry.Sources2D[collider.Index]
                : null;
        }

        private ContactTracker<Collider> TrackerFor(IPhysicsSimulation world)
        {
            if (!trackers.TryGetValue(world, out ContactTracker<Collider> tracker))
            {
                tracker = new ContactTracker<Collider>();
                trackers.Add(world, tracker);
            }

            return tracker;
        }

        private ContactTracker<Collider2D> TrackerFor2D(IPhysicsSimulation world)
        {
            if (!trackers2D.TryGetValue(world, out ContactTracker<Collider2D> tracker))
            {
                tracker = new ContactTracker<Collider2D>();
                trackers2D.Add(world, tracker);
            }

            return tracker;
        }

        private void Claim(ContactTracker<Collider> tracker, List<MonoBehaviour> claimed)
        {
            foreach (NetworkTrigger trigger in triggers)
            {
                if (trigger.Attach(tracker, this))
                {
                    claimed.Add(trigger);
                }
            }

            foreach (NetworkCollision collision in collisions)
            {
                if (collision.Attach(tracker, this))
                {
                    claimed.Add(collision);
                }
            }

            triggers.Clear();
            collisions.Clear();
        }

        private void Claim(ContactTracker<Collider2D> tracker, List<MonoBehaviour> claimed)
        {
            foreach (NetworkTrigger2D trigger in triggers2D)
            {
                if (trigger.Attach(tracker, this))
                {
                    claimed.Add(trigger);
                }
            }

            foreach (NetworkCollision2D collision in collisions2D)
            {
                if (collision.Attach(tracker, this))
                {
                    claimed.Add(collision);
                }
            }

            triggers2D.Clear();
            collisions2D.Clear();
        }

        private void Release(List<MonoBehaviour> claimed)
        {
            foreach (MonoBehaviour component in claimed)
            {
                switch (component)
                {
                    case NetworkTrigger trigger:
                        trigger.Detach((IContactQuery)this);
                        break;
                    case NetworkCollision collision:
                        collision.Detach((IContactQuery)this);
                        break;
                    case NetworkTrigger2D trigger2D:
                        trigger2D.Detach((IContactQuery2D)this);
                        break;
                    case NetworkCollision2D collision2D:
                        collision2D.Detach((IContactQuery2D)this);
                        break;
                }
            }

            claimed.Clear();
        }

        private static void Locate<TCollider>(List<TCollider> sources, IPhysicsSimulation world, BodyHandle body, Dictionary<TCollider, Place> into)
        {
            for (int index = 0; index < sources.Count; index++)
            {
                TCollider source = sources[index];
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

        private static void Forget<TCollider>(List<TCollider> sources, IPhysicsSimulation world, Dictionary<TCollider, Place> from)
        {
            if (sources == null || world == null)
            {
                return;
            }

            foreach (TCollider source in sources)
            {
                if (from.TryGetValue(source, out Place place) && ReferenceEquals(place.World, world))
                {
                    from.Remove(source);
                }
            }
        }

        private static void DropNetworkObjects<TComponent>(List<TComponent> components)
            where TComponent : Component =>
            components.RemoveAll(component => component.GetComponentInParent<NetworkObject>(true) != null);

        private static void Mismatch(Scene scene, string dimension, int found, int expected) =>
            Debug.LogWarning($"[Fomoxa] scene {scene.path} has {found} static {dimension} colliders but its scene file has {expected}; contacts with its static {dimension} geometry are left out. Export the network scene files again");

        private readonly struct Place
        {
            public Place(IPhysicsSimulation world, RapierCollider first, int count)
            {
                World = world;
                First = first;
                Count = count;
            }

            public IPhysicsSimulation World { get; }

            public RapierCollider First { get; }

            public int Count { get; }

            public RapierCollider At(int piece) =>
                First.IsStatic ? RapierCollider.Static(First.Index + piece) : RapierCollider.OfBody(First.Body, First.Index + piece);
        }

        private sealed class BodyColliders
        {
            public RapierWorld World;
            public RapierWorld2D World2D;
            public List<Collider> Sources;
            public List<Collider2D> Sources2D;
            public readonly List<MonoBehaviour> Claimed = new List<MonoBehaviour>();
        }

        private sealed class SceneColliders
        {
            public RapierWorld World;
            public RapierWorld2D World2D;
            public readonly List<MonoBehaviour> Claimed = new List<MonoBehaviour>();
        }
    }
}
