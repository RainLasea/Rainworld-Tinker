using UnityEngine;

namespace Tinker.Silk.Bridge
{
    // Reconstructed from oWebNode Create/Draw: four persistent offsets, a
    // length-scaled point on each connected strand, one closed line chain.
    internal static class SilkJunctionPattern
    {
        internal static float[] CreateOffsets(int seed)
        {
            var random = new System.Random(seed);
            var offsets = new float[4];
            for (int i = 0; i < offsets.Length; i++) offsets[i] = 4f + (float)random.NextDouble() * 6f;
            return offsets;
        }

        internal static float Reach(float offset, float restLength, float currentLength)
        {
            // One world pixel is our guard for the native denominator's as-yet
            // unidentified constant; ordinary bridges are much longer than it.
            return Mathf.Min(offset * currentLength / Mathf.Max(1f, restLength), currentLength);
        }

        internal static int EdgeCount(int connectedStrands) => connectedStrands > 2 ? connectedStrands : 0;

    }
}
