using UnityEngine;

namespace Tinker.Silk.Bridge
{
    // Geometry shared by selection and swept flight collision. No game state.
    internal static class SilkTargetGeometry
    {
        internal const float MaxRange = 1200f;
        internal const float MinRange = 8f;
        internal const float AssistRadius = 12f;

        internal static Vector2 ClosestPoint(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            return a + d * (d.sqrMagnitude > 0.000001f
                ? Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude) : 0f);
        }

        internal static bool SegmentHit(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 hit)
        {
            hit = a;
            Vector2 r = b - a, s = d - c;
            float cross = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(cross) < 0.000001f)
            {
                // Collinear beams/silk still have an entry point.
                if (r.sqrMagnitude < 0.000001f ||
                    Mathf.Abs((c.x - a.x) * r.y - (c.y - a.y) * r.x) > 0.001f) return false;
                float t0 = Vector2.Dot(c - a, r) / r.sqrMagnitude;
                float t1 = Vector2.Dot(d - a, r) / r.sqrMagnitude;
                float enter = Mathf.Max(0f, Mathf.Min(t0, t1));
                if (enter > Mathf.Min(1f, Mathf.Max(t0, t1))) return false;
                hit = a + enter * r;
                return true;
            }
            float t = ((c.x - a.x) * s.y - (c.y - a.y) * s.x) / cross;
            float u = ((c.x - a.x) * r.y - (c.y - a.y) * r.x) / cross;
            if (t < 0f || t > 1f || u < 0f || u > 1f) return false;
            hit = a + t * r;
            return true;
        }

        internal static bool CircleHit(Vector2 a, Vector2 b, Vector2 center, float radius, out Vector2 hit)
        {
            hit = a;
            Vector2 delta = b - a, offset = a - center;
            float c = offset.sqrMagnitude - radius * radius;
            if (c <= 0f) return true;
            float lengthSq = delta.sqrMagnitude;
            if (lengthSq < 0.000001f) return false;
            float dot = Vector2.Dot(offset, delta);
            float discriminant = dot * dot - lengthSq * c;
            if (discriminant < 0f) return false;
            float t = (-dot - Mathf.Sqrt(discriminant)) / lengthSq;
            if (t < 0f || t > 1f) return false;
            hit = a + t * delta;
            return true;
        }

        internal static bool RectHit(Vector2 a, Vector2 b, Vector2 min, Vector2 max, out Vector2 hit)
        {
            float enter = 0f, exit = 1f;
            hit = a;
            Vector2 delta = b - a;
            if (!Slab(a.x, delta.x, min.x, max.x, ref enter, ref exit) ||
                !Slab(a.y, delta.y, min.y, max.y, ref enter, ref exit)) return false;
            hit = a + enter * delta;
            return true;
        }

        private static bool Slab(float start, float delta, float min, float max, ref float enter, ref float exit)
        {
            if (Mathf.Abs(delta) < 0.000001f) return start >= min && start <= max;
            float t0 = (min - start) / delta, t1 = (max - start) / delta;
            enter = Mathf.Max(enter, Mathf.Min(t0, t1));
            exit = Mathf.Min(exit, Mathf.Max(t0, t1));
            return enter <= exit;
        }

        internal static bool CanAssist(Vector2 origin, Vector2 cursor, Vector2 point)
        {
            Vector2 aim = cursor - origin, candidate = point - origin;
            return (point - cursor).sqrMagnitude <= AssistRadius * AssistRadius &&
                candidate.sqrMagnitude >= MinRange * MinRange &&
                candidate.sqrMagnitude <= MaxRange * MaxRange &&
                Vector2.Dot(aim.normalized, candidate.normalized) >= 0.994522f; // six degrees
        }

        internal static bool Better(Vector2 point, Vector2 best, Vector2 origin, Vector2 cursor)
        {
            float distance = (point - cursor).sqrMagnitude, previous = (best - cursor).sqrMagnitude;
            return distance < previous - 0.01f ||
                (Mathf.Abs(distance - previous) <= 0.01f &&
                 (point - origin).sqrMagnitude < (best - origin).sqrMagnitude);
        }
    }
}
