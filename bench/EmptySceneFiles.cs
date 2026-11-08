using System.Collections.Generic;
using Fomoxa.Networking.Standalone;

namespace Fomoxa.Networking.Rapier.Bench
{
    public sealed class EmptySceneFiles : ISceneFiles
    {
        public bool Knows(uint sceneId) => false;

        public byte[] Read(uint sceneId) => throw new KeyNotFoundException($"no scene file 0x{sceneId:X8}");
    }
}
