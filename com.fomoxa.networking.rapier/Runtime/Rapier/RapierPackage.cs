using System;

namespace Fomoxa.Networking.Rapier
{
    public static class RapierPackage
    {
        public const string CoreVersion = "0.1.0";

        public static void CheckCore()
        {
            if (NetworkRuntime.Version != CoreVersion)
            {
                throw new InvalidOperationException($"com.fomoxa.networking.rapier needs com.fomoxa.networking {CoreVersion}; the project has {NetworkRuntime.Version}");
            }
        }
    }
}
