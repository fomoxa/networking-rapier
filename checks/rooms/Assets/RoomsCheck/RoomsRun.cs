using Fomoxa.Unity;

namespace FomoxaChecks
{
    public abstract class RoomsRun
    {
        protected NetworkManager Manager { get; private set; }

        protected NetworkObject PlayerPrefab { get; private set; }

        protected NetworkObject CratePrefab { get; private set; }

        public abstract string Progress { get; }

        public void Start(NetworkManager manager, NetworkObject playerPrefab, NetworkObject cratePrefab)
        {
            Manager = manager;
            PlayerPrefab = playerPrefab;
            CratePrefab = cratePrefab;
            Begin();
        }

        public abstract string Poll();

        public abstract void Stop();

        protected abstract void Begin();
    }
}
