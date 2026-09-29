using System.Collections.Generic;
using UnityEngine;

namespace tinker.Silk
{
    // Shared geometry and fixed-tick simulation for released silk and broken bridges.
    // There is one strand per release, regardless of how many cameras draw it.
    internal static class SilkFade
    {
        private const float PointEpsilon = 0.01f;

        internal static List<Vector2> CleanPath(IList<Vector2> path)
        {
            var result = new List<Vector2>();
            if (path == null) return result;
            foreach (var point in path)
            {
                if (float.IsNaN(point.x) || float.IsNaN(point.y) ||
                    float.IsInfinity(point.x) || float.IsInfinity(point.y)) return new List<Vector2>();
                if (result.Count == 0 || (point - result[result.Count - 1]).sqrMagnitude > PointEpsilon * PointEpsilon)
                    result.Add(point);
            }
            return result;
        }

        internal static float Length(IList<Vector2> path)
        {
            float length = 0f;
            for (int i = 1; i < path.Count; i++) length += Vector2.Distance(path[i - 1], path[i]);
            return length;
        }

        internal static Vector2[] Resample(IList<Vector2> path, int count)
        {
            var points = CleanPath(path);
            if (points.Count < 2 || count < 2) return new Vector2[0];
            float length = Length(points);
            var result = new Vector2[count];
            result[0] = points[0];
            result[count - 1] = points[points.Count - 1];
            int edge = 0;
            float consumed = 0f;
            float edgeLength = Vector2.Distance(points[0], points[1]);
            for (int i = 1; i < count - 1; i++)
            {
                float distance = length * i / (count - 1);
                while (edge < points.Count - 2 && consumed + edgeLength < distance)
                {
                    consumed += edgeLength;
                    edge++;
                    edgeLength = Vector2.Distance(points[edge], points[edge + 1]);
                }
                result[i] = Vector2.Lerp(points[edge], points[edge + 1], (distance - consumed) / edgeLength);
            }
            return result;
        }

        internal static bool Split(IList<Vector2> path, Vector2 impact, out List<Vector2> left, out List<Vector2> right)
        {
            left = new List<Vector2>();
            right = new List<Vector2>();
            var points = CleanPath(path);
            int cut = -1;
            float best = float.MaxValue;
            Vector2 projected = Vector2.zero;
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 delta = points[i + 1] - points[i];
                float t = Mathf.Clamp01(Vector2.Dot(impact - points[i], delta) / delta.sqrMagnitude);
                Vector2 candidate = points[i] + delta * t;
                float distance = (impact - candidate).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    cut = i;
                    projected = candidate;
                }
            }
            if (cut < 0) return false;
            for (int i = 0; i <= cut; i++) left.Add(points[i]);
            left.Add(projected);
            right.Add(projected);
            for (int i = cut + 1; i < points.Count; i++) right.Add(points[i]);
            left = CleanPath(left);
            right = CleanPath(right);
            // Both halves run from the surviving outer end to the free cut end.
            right.Reverse();
            return true;
        }

        internal sealed class Strand
        {
            internal readonly Vector2[] Positions;
            internal readonly Vector2[] Previous;
            private readonly Vector2[] velocities;
            private readonly float[] restLengths;
            private readonly bool recoil;
            private bool pinStart;
            private int age;
            internal float Alpha => Mathf.Clamp01(1f - age * 0.015f);
            internal bool Finished => Positions.Length < 2 || Alpha <= 0f;
            internal bool Pinned => recoil && pinStart && age < 10;

            internal Strand(IList<Vector2> path, bool recoil = false, bool pinStart = false)
            {
                var clean = CleanPath(path);
                float length = Length(clean);
                Positions = Resample(clean, Mathf.Clamp(Mathf.CeilToInt(length / 8f) + 1, 2, 161));
                Previous = (Vector2[])Positions.Clone();
                velocities = new Vector2[Positions.Length];
                restLengths = new float[Mathf.Max(0, Positions.Length - 1)];
                this.recoil = recoil;
                this.pinStart = pinStart;
                for (int i = 0; i < restLengths.Length; i++)
                    restLengths[i] = Vector2.Distance(Positions[i], Positions[i + 1]);
                for (int i = 0; i < Positions.Length; i++)
                {
                    float t = (float)i / (Positions.Length - 1);
                    Vector2 tangent = (Positions[Mathf.Max(0, i - 1)] - Positions[Mathf.Min(Positions.Length - 1, i + 1)]).normalized;
                    Vector2 normal = new Vector2(-tangent.y, tangent.x);
                    // A coherent kick, strongest at the cut, avoids random knots.
                    float kick = recoil ? Mathf.Min(12f, length * 0.08f) : 0f;
                    velocities[i] = tangent * (kick * t * t) + normal * (Mathf.Sin(t * Mathf.PI) * kick * 0.16f);
                }
            }

            internal void Update(float gravity, Vector2? anchor = null)
            {
                if (Finished) return;
                bool pinned = Pinned;
                Vector2 fixedPoint = anchor ?? Positions[0];
                for (int i = 0; i < Positions.Length; i++)
                {
                    Previous[i] = Positions[i];
                    velocities[i] *= 0.92f;
                    float gravityScale = recoil ? Mathf.Lerp(0.2f, 0.8f, age / 10f) : 0.8f;
                    velocities[i].y -= gravity * gravityScale;
                    Positions[i] += velocities[i];
                }
                // Release stored tension over a few ticks before the common falling fade.
                float contraction = recoil ? Mathf.Lerp(1f, 0.78f, (age + 1f) / 10f) : 1f;
                for (int iteration = 0; iteration < 8; iteration++)
                {
                    if (pinned) Positions[0] = fixedPoint;
                    for (int n = 0; n < restLengths.Length; n++)
                    {
                        int i = (iteration & 1) == 0 ? n : restLengths.Length - 1 - n;
                        Vector2 delta = Positions[i + 1] - Positions[i];
                        float distance = delta.magnitude;
                        if (distance < 0.0001f) continue;
                        Vector2 correction = delta * ((distance - restLengths[i] * contraction) / distance);
                        if (pinned && i == 0) Positions[i + 1] -= correction;
                        else
                        {
                            Positions[i] += correction * 0.5f;
                            Positions[i + 1] -= correction * 0.5f;
                        }
                    }
                }
                for (int i = 0; i < Positions.Length; i++) velocities[i] = Positions[i] - Previous[i];
                age++;
            }

            internal void ResolveContact(int index, Vector2 position)
            {
                Positions[index] = position;
                velocities[index] *= 0.4f;
            }

            internal void ReleaseAnchor() => pinStart = false;

            internal Vector2 Sample(int index, float timeStacker) => Vector2.Lerp(Previous[index], Positions[index], timeStacker);
        }
    }
}
