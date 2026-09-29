using System;
using UnityEngine;

namespace tinker.Silk
{
    internal static class SilkBeamGeometry
    {
        internal struct Sample
        {
            public Vector2 Point;
            public int Segment;
            public float T;
            public float AxisDistance;
        }

        // Intersect the strand with the player's movement coordinate. Unlike a
        // closest-point projection, this cannot turn sag into sideways drift.
        internal static bool TrySample(Vector2[] points, Vector2 query, bool vertical,
            Vector2 supportOffset, out Sample sample)
        {
            if (!TrySample(points, query - supportOffset, vertical, out sample)) return false;
            sample.Point += supportOffset;
            return true;
        }

        internal static bool TrySample(Vector2[] points,
            Vector2 query, bool vertical, out Sample sample)
        {
            sample = default;
            float best = float.MaxValue;
            float bestAxisDistance = float.MaxValue;
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector2 a = points[i], b = points[i + 1];
                float start = vertical ? a.y : a.x;
                float delta = vertical ? b.y - a.y : b.x - a.x;
                if (Math.Abs(delta) < 0.0001f) continue;
                float axis = vertical ? query.y : query.x;
                float t = Math.Max(0f, Math.Min(1f, (axis - start) / delta));
                Vector2 hit = a + (b - a) * t;
                float distance = (hit - query).sqrMagnitude;
                float axisDistance = Math.Abs(axis - (start + delta * t));
                if (axisDistance > bestAxisDistance + 0.0001f ||
                    (Math.Abs(axisDistance - bestAxisDistance) <= 0.0001f && distance >= best)) continue;
                best = distance;
                bestAxisDistance = axisDistance;
                sample = new Sample { Point = hit, Segment = i, T = t,
                    AxisDistance = axisDistance };
            }
            return best < float.MaxValue;
        }

        internal static bool WithinBeam(Vector2 query, bool vertical, Sample sample)
        {
            float normalDistance = Math.Abs(vertical ? query.x - sample.Point.x : query.y - sample.Point.y);
            return sample.AxisDistance <= 9.9f && normalDistance <= 10f;
        }
    }
}
