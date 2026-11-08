using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Networking.Rapier.Bench
{
    public sealed class LoopbackFactory : ITransportFactory
    {
        private const int MaxQueuedPackets = 4096;

        private readonly LoopbackListener listener = new LoopbackListener(MaxQueuedPackets);

        public int FrameBudget => 1200;

        public IListenerTransport CreateListener(ushort port, out ushort boundPort)
        {
            boundPort = port;
            return listener;
        }

        public ITransportConnector CreateConnector(string address, ushort port) => new Connector(listener);

        private sealed class Connector : ITransportConnector
        {
            private readonly LoopbackListener listener;

            public Connector(LoopbackListener listener)
            {
                this.listener = listener;
            }

            public ConnectStatus Poll(out ITransport transport)
            {
                transport = listener.Connect();
                return ConnectStatus.Connected;
            }

            public void Dispose()
            {
            }
        }
    }
}
