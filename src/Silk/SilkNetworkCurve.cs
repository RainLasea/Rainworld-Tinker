using System;
using System.Collections.Generic;
using UnityEngine;

namespace tinker.Silk
{
    // Material coordinates preserve junctions and climb parameters across snapshots.
    internal sealed class SilkNetworkCurve
    {
        private float[] coordinates = Array.Empty<float>();
        private Vector2[] basis, visual, previousBasis, previousVisual, targetBasis, targetVisual;
        private int remaining;
        internal int Count => coordinates.Length;
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);

        internal static bool Valid(float[] coordinates, Vector2[] basis, Vector2[] visual, int segments)
        {
            if (coordinates == null || basis == null || visual == null || coordinates.Length < 2 ||
                coordinates.Length > 255 || basis.Length != coordinates.Length || visual.Length != coordinates.Length ||
                segments < 1 || segments > 32 || coordinates[0] != 0f || coordinates[coordinates.Length - 1] != segments) return false;
            for (int i = 0; i < coordinates.Length; i++)
                if (!Finite(coordinates[i]) || !Finite(basis[i]) || !Finite(visual[i]) ||
                    (i > 0 && coordinates[i] <= coordinates[i - 1])) return false;
            return true;
        }

        internal void Receive(float[] material, Vector2[] basePoints, Vector2[] visualPoints)
        {
            var nextBase = new Vector2[material.Length];
            var nextVisual = new Vector2[material.Length];
            for (int i = 0; i < material.Length; i++)
            {
                nextBase[i] = Count > 0 ? Sample(material[i], 1f, false) : basePoints[i];
                nextVisual[i] = Count > 0 ? Sample(material[i], 1f, true) : visualPoints[i];
            }
            coordinates = (float[])material.Clone();
            basis = nextBase; visual = nextVisual;
            previousBasis = (Vector2[])basis.Clone(); previousVisual = (Vector2[])visual.Clone();
            targetBasis = (Vector2[])basePoints.Clone(); targetVisual = (Vector2[])visualPoints.Clone();
            remaining = 3;
        }

        internal void Advance()
        {
            for (int i = 0; i < Count; i++)
            {
                previousBasis[i] = basis[i]; previousVisual[i] = visual[i];
                if (remaining > 0)
                {
                    basis[i] = Vector2.Lerp(basis[i], targetBasis[i], 1f / remaining);
                    visual[i] = Vector2.Lerp(visual[i], targetVisual[i], 1f / remaining);
                }
            }
            if (remaining > 0) remaining--;
        }

        internal Vector2 Sample(float material, float timeStacker, bool withWaves)
        {
            int index = Array.BinarySearch(coordinates, Mathf.Clamp(material, coordinates[0], coordinates[Count - 1]));
            int edge = Mathf.Clamp(index >= 0 ? index : ~index - 1, 0, Count - 2);
            float t = Mathf.InverseLerp(coordinates[edge], coordinates[edge + 1], material);
            var now = withWaves ? visual : basis;
            var last = withWaves ? previousVisual : previousBasis;
            return Vector2.Lerp(Vector2.Lerp(last[edge], now[edge], timeStacker),
                Vector2.Lerp(last[edge + 1], now[edge + 1], timeStacker), t);
        }

        internal float Parameter(float fraction)
        {
            float along = Mathf.Clamp01(fraction) * (Count - 1);
            int edge = Mathf.Min((int)along, Count - 2);
            return Mathf.Lerp(coordinates[edge], coordinates[edge + 1], along - edge);
        }

        internal List<Vector2> Path() => new List<Vector2>(visual);
        internal Vector2 Closest(Vector2 point, out int segment, out float t)
        {
            float best = float.MaxValue, material = 0f;
            Vector2 result = visual[0];
            for (int i = 0; i < Count - 1; i++)
            {
                Vector2 delta = visual[i + 1] - visual[i];
                float local = delta.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(point - visual[i], delta) / delta.sqrMagnitude) : 0f;
                Vector2 candidate = visual[i] + delta * local;
                float distance = (point - candidate).sqrMagnitude;
                if (distance >= best) continue;
                best = distance; result = candidate;
                material = Mathf.Lerp(coordinates[i], coordinates[i + 1], local);
            }
            segment = Mathf.Min((int)material, (int)coordinates[Count - 1] - 1);
            t = material - segment;
            return result;
        }
    }
}
