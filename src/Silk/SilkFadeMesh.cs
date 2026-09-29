using UnityEngine;

namespace tinker.Silk
{
    internal static class SilkFadeMesh
    {
        internal static TriangleMesh Create(int pointCount)
        {
            var triangles = new TriangleMesh.Triangle[(pointCount - 1) * 2];
            for (int i = 0; i < pointCount - 1; i++)
            {
                int v = i * 2;
                triangles[i * 2] = new TriangleMesh.Triangle(v, v + 1, v + 2);
                triangles[i * 2 + 1] = new TriangleMesh.Triangle(v + 1, v + 2, v + 3);
            }
            // Share vertices at each joint: no duplicate first segment or overlapping quads.
            return new TriangleMesh("Futile_White", triangles, false, false);
        }

        internal static void Draw(TriangleMesh mesh, Vector2[] points, Vector2[] previous,
            float timeStacker, Vector2 camPos, float width)
        {
            mesh.isVisible = points.Length >= 2;
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 point = previous == null ? points[i] : Vector2.Lerp(previous[i], points[i], timeStacker);
                int before = Mathf.Max(0, i - 1), after = Mathf.Min(points.Length - 1, i + 1);
                Vector2 a = previous == null ? points[before] : Vector2.Lerp(previous[before], points[before], timeStacker);
                Vector2 b = previous == null ? points[after] : Vector2.Lerp(previous[after], points[after], timeStacker);
                Vector2 tangent = (b - a).normalized;
                Vector2 normal = new Vector2(-tangent.y, tangent.x);
                float t = (float)i / (points.Length - 1);
                Vector2 offset = normal * (width * (1f - Mathf.Abs(t * 2f - 1f) * 0.2f));
                mesh.MoveVertice(i * 2, point - offset - camPos);
                mesh.MoveVertice(i * 2 + 1, point + offset - camPos);
            }
        }
    }
}
