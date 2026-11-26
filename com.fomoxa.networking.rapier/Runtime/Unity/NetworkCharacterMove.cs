using Fomoxa.Networking.Rapier;
using UnityEngine;

namespace Fomoxa.Unity.Rapier
{
    public readonly struct NetworkCharacterMove
    {
        public NetworkCharacterMove(Vector3 moved, bool grounded, bool sliding)
        {
            Moved = moved;
            Grounded = grounded;
            Sliding = sliding;
        }

        public Vector3 Moved { get; }

        public bool Grounded { get; }

        public bool Sliding { get; }

        internal static NetworkCharacterMove From(in CharacterMove move) => new NetworkCharacterMove(move.Moved.ToUnity(), move.Grounded, move.Sliding);
    }

    public readonly struct NetworkCharacterMove2D
    {
        public NetworkCharacterMove2D(Vector2 moved, bool grounded, bool sliding)
        {
            Moved = moved;
            Grounded = grounded;
            Sliding = sliding;
        }

        public Vector2 Moved { get; }

        public bool Grounded { get; }

        public bool Sliding { get; }

        internal static NetworkCharacterMove2D From(in CharacterMove2D move) => new NetworkCharacterMove2D(move.Moved.ToUnity(), move.Grounded, move.Sliding);
    }
}
