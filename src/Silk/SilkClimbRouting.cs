using System;
using UnityEngine;

namespace tinker.Silk
{
    // Junctions are geometric, not a choice between two horizontal/vertical flags.
    // All coordinates refer to the load-bearing curve; ripples cannot change routes.
    internal static class SilkClimbRouting
    {
        internal const float Reach = 12f;
        private const float LookAhead = 16f;
        private const float ContinuityBias = 0.12f;

        internal struct Route
        {
            public Vector2 Point;
            public Vector2 Outgoing;
            public float Coordinate;
            public float Score;
        }

        internal static bool TryRoute(Vector2[] current, Vector2[] candidate,
            Vector2 contact, Vector2 input, out Route best)
        {
            best = default;
            best.Score = float.NegativeInfinity;
            if (input.sqrMagnitude < 0.01f) return false;
            input = input.normalized;
            for (int i = 0; i < current.Length - 1; i++)
            {
                Vector2 a = current[i], b = current[i + 1];
                if ((Project(contact, a, b, out _) - contact).sqrMagnitude > Reach * Reach) continue;
                for (int j = 0; j < candidate.Length - 1; j++)
                {
                    Vector2 c = candidate[j], d = candidate[j + 1];
                    if (!Junction(a, b, c, d, out float t, out float u)) continue;
                    Vector2 point = c + (d - c) * u;
                    float distance = (point - contact).magnitude;
                    if (distance > Reach) continue;
                    if (!TryJunction(current, i + t, candidate, j + u, contact, input, out var route)) continue;
                    if (Better(route, best)) best = route;
                }
            }
            return !float.IsNegativeInfinity(best.Score);
        }

        internal static bool TryJunction(Vector2[] current, float from, Vector2[] candidate, float to,
            Vector2 contact, Vector2 input, out Route route)
        {
            route = default;
            if (input.sqrMagnitude < 0.01f) return false;
            input = input.normalized;
            int segment = Math.Min((int)to, candidate.Length - 2);
            Vector2 point = candidate[segment] + (candidate[segment + 1] - candidate[segment]) * (to - segment);
            float distance = (point - contact).magnitude;
            if (distance > Reach) return false;
            float score = ForwardScore(candidate, to, input, out Vector2 outgoing);
            float staying = ForwardScore(current, from, input, out _);
            // No branch behind the input; ties stay on the current strand.
            if (score < 0.3f || score <= staying + ContinuityBias) return false;
            route = new Route { Point = point, Coordinate = to,
                Outgoing = outgoing, Score = score - distance * 0.0125f };
            return true;
        }

        internal static bool Better(Route route, Route best)
        {
            if (Math.Abs(route.Score - best.Score) > 0.0001f) return route.Score > best.Score;
            // Geometry resolves equal candidates, independently of bridge creation order.
            if (route.Point.x != best.Point.x) return route.Point.x < best.Point.x;
            if (route.Point.y != best.Point.y) return route.Point.y < best.Point.y;
            if (route.Outgoing.x != best.Outgoing.x) return route.Outgoing.x < best.Outgoing.x;
            return route.Outgoing.y < best.Outgoing.y;
        }

        private static float ForwardScore(Vector2[] points, float coordinate, Vector2 input, out Vector2 outgoing)
        {
            int segment = Math.Min((int)coordinate, points.Length - 2);
            Vector2 origin = points[segment] + (points[segment + 1] - points[segment]) * (coordinate - segment);
            outgoing = Vector2.zero;
            float best = 0f;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector2 end = origin;
                float remaining = LookAhead;
                for (int k = sign > 0 ? segment + 1 : segment; k >= 0 && k < points.Length; k += sign)
                {
                    Vector2 delta = points[k] - end;
                    float length = delta.magnitude;
                    if (length < 0.0001f) continue;
                    float step = Math.Min(remaining, length);
                    end += delta * (step / length);
                    remaining -= step;
                    if (remaining <= 0.0001f) break;
                }
                Vector2 travel = end - origin;
                float distance = travel.magnitude;
                if (distance < 0.001f) continue;
                // A strand ending at this junction has no outgoing direction there.
                float score = Vector2.Dot(travel / distance, input) * Math.Min(1f, distance / 6f);
                if (score <= best) continue;
                best = score;
                outgoing = travel / distance;
            }
            return best;
        }

        private static Vector2 Project(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            Vector2 delta = b - a;
            t = delta.sqrMagnitude < 0.000001f ? 0f :
                Math.Max(0f, Math.Min(1f, Vector2.Dot(p - a, delta) / delta.sqrMagnitude));
            return a + delta * t;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool Junction(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float t, out float u)
        {
            Vector2 ab = b - a, cd = d - c;
            t = u = 0f;
            if (ab.sqrMagnitude < 0.000001f || cd.sqrMagnitude < 0.000001f) return false;
            float cross = Cross(ab, cd);
            if (Math.Abs(cross) > 0.0001f)
            {
                t = Cross(c - a, cd) / cross;
                u = Cross(c - a, ab) / cross;
                if (t >= 0f && t <= 1f && u >= 0f && u <= 1f) return true;
            }
            // Coincident endpoints/T junctions, including collinear continuations.
            // This is numerical tolerance, not a proximity grab of parallel strands.
            if ((Project(a, c, d, out u) - a).sqrMagnitude < 0.01f) { t = 0f; return true; }
            if ((Project(b, c, d, out u) - b).sqrMagnitude < 0.01f) { t = 1f; return true; }
            if ((Project(c, a, b, out t) - c).sqrMagnitude < 0.01f) { u = 0f; return true; }
            if ((Project(d, a, b, out t) - d).sqrMagnitude < 0.01f) { u = 1f; return true; }
            return false;
        }
    }
}
