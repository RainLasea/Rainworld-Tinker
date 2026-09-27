using System;
using System.Collections.Generic;
using UnityEngine;

namespace tinker.Silk
{
    // Time is measured in Rain World's simulation ticks, never render frames.
    // Shared by the bridge solver and the player's tensile spring.
    internal static class SilkDynamics
    {
        public const int Substeps = 2;
        public const int Iterations = 10;
        public const float Gravity = 0.02f;
        public const float Damping = 0.965f;
        private const float RestLengthFactor = 1f;
        private const float StretchCompliance = 0.00025f;
        private const float Pretension = 32f;
        private const float PullStiffness = 0.12f;
        private const float PullDamping = 0.36f;

        public static float PullAcceleration(float extension, float outwardSpeed)
        {
            // A slack thread cannot push. Damping acts only radially, leaving
            // tangential swing momentum intact, including on release.
            if (extension <= 0f) return 0f;
            return Mathf.Clamp(extension * PullStiffness + outwardSpeed * PullDamping, 0f, 8f);
        }

        public static Vector2 SwingAcceleration(Vector2 towardAnchor, int horizontalInput)
        {
            Vector2 direction = towardAnchor.normalized;
            Vector2 input = new Vector2(horizontalInput * 0.5f, 0f);
            return input - direction * Vector2.Dot(input, direction);
        }

        public static float Wave(float t, float phase, float amplitude)
        {
            // The envelope fixes both ends. Counter-propagating harmonics give
            // a fine plucked-thread ripple, with a strict amplitude bound.
            if (t <= 0f || t >= 1f) return 0f;
            return Mathf.Sin(t * Mathf.PI) * amplitude / 1.3f *
                (Mathf.Sin(t * Mathf.PI * 6f - phase) + 0.3f * Mathf.Sin(t * Mathf.PI * 10f + phase * 0.8f));
        }

        public static Vector2 Curve(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            if (t <= 0f) return b;
            if (t >= 1f) return c;
            Vector2 curved = 0.5f * ((2f * b) + (c - a) * t +
                (2f * a - 5f * b + 4f * c - d) * (t * t) +
                (-a + 3f * b - 3f * c + d) * (t * t * t));
            Vector2 linear = Vector2.Lerp(b, c, t);
            // At sharp bends keep the drawing close to the collision polyline.
            return linear + Vector2.ClampMagnitude(curved - linear, 1.5f);
        }

        // A launch starts independently of player velocity. The signed spring
        // overshoots zero, then settles; drawing only interpolates its tick state.
        internal sealed class LaunchSpring
        {
            public float Value { get; private set; }
            public float PreviousValue { get; private set; }
            public float Velocity { get; private set; }

            public void Reset(float value = 0f)
            {
                Value = PreviousValue = value;
                Velocity = 0f;
            }

            public void Update()
            {
                PreviousValue = Value;
                Velocity = (Velocity - Value * 0.045f) * 0.84f;
                Value += Velocity;
                if (Mathf.Abs(Value) + Mathf.Abs(Velocity) < 0.0001f) Value = Velocity = 0f;
            }

            public float Offset(float t, float length, float timeStacker)
            {
                return LaunchOffset(t, length, Mathf.Lerp(PreviousValue, Value, timeStacker));
            }
        }

        // Keep only the impact shape, never the flight spring's velocity. A short
        // monotonic ease pulls it straight without a second oscillation on the bridge.
        internal sealed class LaunchTightening
        {
            private const int Duration = 4;
            private float initialValue, value, previousValue;
            private int ticks;

            public void Begin(LaunchSpring source)
            {
                initialValue = value = previousValue = source.Value;
                ticks = 0;
            }

            public void Update()
            {
                previousValue = value;
                ticks = Math.Min(ticks + 1, Duration);
                float remaining = 1f - (float)ticks / Duration;
                value = initialValue * remaining * remaining;
            }

            public float Offset(float t, float length, float timeStacker) =>
                LaunchOffset(t, length, Mathf.Lerp(previousValue, value, timeStacker));
        }

        private static float LaunchOffset(float t, float length, float amplitude)
        {
            if (t <= 0f || t >= 1f) return 0f;
            return Mathf.Sin(t * Mathf.PI * 6f) * Mathf.Min(12f, length * 0.06f) * amplitude;
        }

        internal sealed class Node
        {
            public Vector2 Position;
            public Vector2 Previous;
            public Vector2 FramePosition;
            public Vector2 Force;
            public readonly float InverseMass;

            public Node(Vector2 position, float inverseMass)
            {
                Position = Previous = FramePosition = position;
                InverseMass = inverseMass;
            }

            public void Pin(Vector2 position)
            {
                Position = Previous = position;
            }
        }

        internal sealed class Rope
        {
            public readonly Node[] Nodes;
            public readonly float SegmentLength;
            private float[] tensions;
            private float[] upper;
            private Vector2[] predicted;
            // Stable material coordinates keep saved segment/t attachments valid
            // when a junction splits an edge into two physical edges.
            public Node[] Particles { get; private set; }
            public float[] Coordinates { get; private set; }
            public bool HasJunctions => junctions.Count != 0;
            private readonly Dictionary<Node, float> junctions = new Dictionary<Node, float>();

            public Rope(Vector2 start, Vector2 end, int count, bool fixedStart, bool fixedEnd)
            {
                count = Math.Max(2, count);
                float distance = Vector2.Distance(start, end);
                // Store the natural length ONCE: moving anchors stretch silk;
                // they must not redefine its unstretched length each frame.
                SegmentLength = Mathf.Max(0.1f, distance * RestLengthFactor / (count - 1));
                Nodes = new Node[count];
                float inverseMass = 1f / Mathf.Max(0.08f, SegmentLength * 0.006f);
                for (int i = 0; i < count; i++)
                {
                    bool pinned = (i == 0 && fixedStart) || (i == count - 1 && fixedEnd);
                    Nodes[i] = new Node(Vector2.Lerp(start, end, (float)i / (count - 1)), pinned ? 0f : inverseMass);
                }
                RebuildChain();
            }

            public Node RegisterJunction(Node endpoint, int segment, float t)
            {
                float coordinate = Mathf.Clamp(segment + Mathf.Clamp01(t), 0f, Nodes.Length - 1);
                // Subpixel material resolution avoids nearly zero-length edges
                // when two shots land at practically the same location.
                coordinate = Mathf.Round(coordinate * 4096f) / 4096f;
                if (!junctions.TryGetValue(endpoint, out float old) || old != coordinate)
                {
                    junctions[endpoint] = coordinate;
                    RebuildChain();
                }
                int index = Array.BinarySearch(Coordinates, coordinate);
                return Particles[index];
            }

            public void RemoveJunction(Node endpoint)
            {
                if (junctions.Remove(endpoint)) RebuildChain();
            }

            private void RebuildChain()
            {
                var coordinates = new SortedSet<float>();
                for (int i = 0; i < Nodes.Length; i++) coordinates.Add(i);
                foreach (float coordinate in junctions.Values) coordinates.Add(coordinate);
                var chain = new Node[coordinates.Count];
                var material = new float[coordinates.Count];
                int index = 0;
                foreach (float coordinate in coordinates)
                {
                    material[index] = coordinate;
                    int original = (int)coordinate;
                    int existing = Coordinates == null ? -1 : Array.BinarySearch(Coordinates, coordinate);
                    if (coordinate == original) chain[index] = Nodes[original];
                    else if (existing >= 0) chain[index] = Particles[existing];
                    else
                    {
                        Locate(coordinate, out int edge, out float t);
                        Node a = Particles[edge], b = Particles[edge + 1];
                        chain[index] = new Node(Vector2.Lerp(a.Position, b.Position, t),
                            1f / Mathf.Max(0.08f, SegmentLength * 0.006f))
                        {
                            Previous = Vector2.Lerp(a.Previous, b.Previous, t),
                            FramePosition = Vector2.Lerp(a.FramePosition, b.FramePosition, t)
                        };
                    }
                    index++;
                }
                Particles = chain;
                Coordinates = material;
                tensions = new float[chain.Length - 1];
                upper = new float[chain.Length];
                predicted = new Vector2[chain.Length];
            }

            private void Locate(float coordinate, out int edge, out float t)
            {
                coordinate = Mathf.Clamp(coordinate, 0f, Nodes.Length - 1);
                int found = Array.BinarySearch(Coordinates, coordinate);
                edge = Mathf.Clamp(found >= 0 ? found : ~found - 1, 0, Particles.Length - 2);
                t = (coordinate - Coordinates[edge]) / (Coordinates[edge + 1] - Coordinates[edge]);
            }

            public Vector2 Sample(float coordinate, float timeStacker)
            {
                Locate(coordinate, out int edge, out float t);
                return Vector2.Lerp(Vector2.Lerp(Particles[edge].FramePosition, Particles[edge].Position, timeStacker),
                    Vector2.Lerp(Particles[edge + 1].FramePosition, Particles[edge + 1].Position, timeStacker), t);
            }

            public Vector2 ClosestPoint(Vector2 position, out int segment, out float t)
            {
                float best = float.MaxValue, material = 0f;
                Vector2 result = Particles[0].Position;
                for (int i = 0; i < Particles.Length - 1; i++)
                {
                    Vector2 a = Particles[i].Position, delta = Particles[i + 1].Position - a;
                    float fraction = delta.sqrMagnitude > 0.000001f ? Mathf.Clamp01(Vector2.Dot(position - a, delta) / delta.sqrMagnitude) : 0f;
                    Vector2 point = a + delta * fraction;
                    float distance = (point - position).sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance; result = point;
                    material = Mathf.Lerp(Coordinates[i], Coordinates[i + 1], fraction);
                }
                segment = Mathf.Min((int)material, Nodes.Length - 2);
                t = material - segment;
                return result;
            }

            public void BeginFrame()
            {
                foreach (var node in Particles) node.FramePosition = node.Position;
            }

            public void Predict(float dt, float gravityScale)
            {
                Array.Clear(tensions, 0, tensions.Length);
                float damping = (float)Math.Pow(Damping, dt);
                foreach (var node in Particles)
                {
                    if (node.InverseMass == 0f) continue;
                    Vector2 velocity = (node.Position - node.Previous) * damping;
                    node.Previous = node.Position;
                    node.Position += velocity + (node.Force * node.InverseMass + Vector2.down * (Gravity * gravityScale)) * (dt * dt);
                }
                SolvePretension(dt);
            }

            private void SolvePretension(float dt)
            {
                // Implicit taut-string wave equation. Axial distance constraints
                // alone cannot supply transverse tension to a straight, unloaded
                // strand. Solve the tension across the entire chain in O(n), so
                // short/long threads do not need thousands of relaxation passes.
                // Implicit integration remains stable for dense short segments.
                for (int i = 0; i < Particles.Length; i++)
                {
                    float weight = Pretension * Particles[i].InverseMass * dt * dt / SegmentLength;
                    float left = i > 0 ? -weight / (Coordinates[i] - Coordinates[i - 1]) : 0f;
                    float right = i + 1 < Particles.Length ? -weight / (Coordinates[i + 1] - Coordinates[i]) : 0f;
                    float diagonal = 1f - left - right;
                    Vector2 rhs = Particles[i].Position;
                    if (i > 0)
                    {
                        diagonal -= left * upper[i - 1];
                        rhs -= left * predicted[i - 1];
                    }
                    upper[i] = right / diagonal;
                    predicted[i] = rhs / diagonal;
                }
                for (int i = Particles.Length - 1; i >= 0; i--)
                    Particles[i].Position = predicted[i] - (i + 1 < Particles.Length ? upper[i] * Particles[i + 1].Position : Vector2.zero);
            }

            public void Solve(float dt, bool reverse)
            {
                // Taut branches transmit a point load along each whole span.
                // Do not smooth across a junction: it is a free hinge, not a bend.
                SolveJunctionSpans();
                // Allow a small additional elastic extension under external loads.
                for (int n = 0; n < tensions.Length; n++)
                {
                    int i = reverse ? tensions.Length - 1 - n : n;
                    Node a = Particles[i], b = Particles[i + 1];
                    float rest = SegmentLength * (Coordinates[i + 1] - Coordinates[i]);
                    float alpha = rest * StretchCompliance / (dt * dt);
                    float weight = a.InverseMass + b.InverseMass;
                    Vector2 delta = b.Position - a.Position;
                    float distance = delta.magnitude;
                    if (weight == 0f || distance < 0.0001f) continue;
                    float change = (-(distance - rest) - alpha * tensions[i]) / (weight + alpha);
                    float tension = Mathf.Min(0f, tensions[i] + change);
                    change = tension - tensions[i];
                    tensions[i] = tension;
                    Vector2 correction = delta * (change / distance);
                    a.Position -= correction * a.InverseMass;
                    b.Position += correction * b.InverseMass;
                }
            }

            private void SolveJunctionSpans()
            {
                if (junctions.Count == 0) return;
                int start = 0;
                for (int end = 1; end < Particles.Length; end++)
                {
                    bool boundary = end == Particles.Length - 1 || junctions.ContainsValue(Coordinates[end]);
                    if (!boundary) continue;
                    Vector2 a = Particles[start].Position, b = Particles[end].Position;
                    Vector2 direction = (b - a).normalized;
                    for (int i = start + 1; i < end; i++)
                    {
                        // Correct only transverse curvature. Axial extension,
                        // junction displacement and load exchange remain physical.
                        Vector2 offset = Particles[i].Position - a;
                        Vector2 transverse = offset - direction * Vector2.Dot(offset, direction);
                        Particles[i].Position -= transverse * 0.25f;
                    }
                    start = end;
                }
            }

            public void EndFrame()
            {
                foreach (var node in Particles) node.Force = Vector2.zero;
            }

            public Vector2 Point(int segment, float t)
            {
                segment = Mathf.Clamp(segment, 0, Nodes.Length - 2);
                return Sample(segment + Mathf.Clamp01(t), 1f);
            }

            public void AddForce(int segment, float t, Vector2 force)
            {
                segment = Mathf.Clamp(segment, 0, Nodes.Length - 2);
                t = Mathf.Clamp01(t);
                // Total force is independent of node count and radius. Fixed
                // anchors absorb their share instead of injecting it elsewhere.
                Locate(segment + t, out int edge, out float fraction);
                Particles[edge].Force += force * (1f - fraction);
                Particles[edge + 1].Force += force * fraction;
            }
        }

        public static void Join(Node end, Rope parent, int segment, float t)
        {
            Node junction = parent.RegisterJunction(end, segment, t);
            float weight = end.InverseMass + junction.InverseMass;
            if (weight < 0.0001f) return;
            Vector2 correction = (end.Position - junction.Position) / weight;
            end.Position -= correction * end.InverseMass;
            junction.Position += correction * junction.InverseMass;
        }

        public static void ResolveContact(Node node, Vector2 surface, Vector2 normal)
        {
            Vector2 velocity = node.Position - node.Previous;
            // Keep tangential movement; discard only velocity entering terrain.
            velocity -= normal * Mathf.Min(0f, Vector2.Dot(velocity, normal));
            node.Position = surface;
            node.Previous = surface - velocity * 0.8f;
        }
    }
}
