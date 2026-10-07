using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Rapier
{
    internal interface IRapierBodies : IPhysicsSimulation
    {
        bool IsDisposed { get; }

        bool Contains(BodyHandle body);

        bool RemoveBody(BodyHandle body);

        BodyKind GetKind(BodyHandle body);

        void SetKind(BodyHandle body, BodyKind kind);

        void SetRewindable(BodyHandle body, bool rewindable);
    }
}
