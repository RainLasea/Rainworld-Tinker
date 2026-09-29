using System;
using System.Collections.Generic;
using UnityEngine;

namespace tinker.Silk
{
    // Fast span vibration plus small travelling disturbances, separate from the
    // load-bearing rope. Constant weight cannot change its equilibrium sag.
    // Time is in simulation ticks (40 Hz), distance in world pixels.
    internal sealed class SilkWaves
    {
        private const int Substeps = 7;
        private const float Speed = 14f;
        private const float Response = 1.6f;
        private const float Attenuation = 0.994f; // per substep, not per mesh node
        // These short packets only carry contact disturbances between spans.
        // The main visual response is each span's fast damped vibration.
        internal const float StepDuration = 3.5f;
        internal const float ImpactDuration = 5f;
        private readonly List<Strand> strands = new List<Strand>();
        private readonly List<Junction> junctions = new List<Junction>();

        internal sealed class Junction
        {
            internal Junction Parent;
            internal bool Fixed;
            internal Vector2 Drive;
            internal readonly List<Port> Ports = new List<Port>();
            internal Junction Root => Parent == null ? this : (Parent = Parent.Root);
        }

        internal sealed class Port
        {
            internal Span Span;
            internal bool Start;
            internal Vector2 Incoming => Start ? Span.Left[0] : Span.Right[Span.Last];
            internal void Send(Vector2 value)
            {
                if (Start) Span.Right[0] = value;
                else Span.Left[Span.Last] = value;
            }
        }

        internal sealed class Span
        {
            internal readonly float From, To, Length;
            internal readonly Junction Start, End;
            internal readonly Vector2[] Right, Left, Previous;
            internal Vector2 RingPosition, RingVelocity, RingPrevious;
            internal float RingLimit = 1.2f, RingDecay = 0.20f;
            private Vector2 previousBoundary;
            internal int Last => Right.Length - 1;
            private float Frequency => 2f * Mathf.PI / Mathf.Lerp(5.5f, 8f, Mathf.Clamp01(Length / 500f));

            internal Span(float from, float to, float unitLength, Junction start, Junction end)
            {
                From = from; To = to; Length = Mathf.Max(0.001f, (to - from) * unitLength);
                Start = start; End = end;
                // Two-pixel cells retain short crests; seven substeps keep the
                // characteristic step near one cell, limiting numerical smearing.
                int count = Mathf.Max(1, Mathf.CeilToInt(Length / 2f)) + 1;
                Right = new Vector2[count]; Left = new Vector2[count]; Previous = new Vector2[count];
            }

            internal void Advance()
            {
                // Outgoing junction fields enter this span; incoming fields
                // belong to the disturbance leaving it and must not re-excite it.
                Vector2 boundary = (Start.Root.Ports.Count > 2 && !Start.Root.Fixed ? Right[0] : Vector2.zero) +
                    (End.Root.Ports.Count > 2 && !End.Root.Fixed ? Left[Last] : Vector2.zero);
                RingVelocity += (boundary - previousBoundary) * (Frequency * 0.45f);
                previousBoundary = boundary;

                // Upwind characteristic transport is stable even when a new
                // junction creates a subpixel edge. No spatial smoothing pass.
                float travel = Mathf.Min(1f, Speed / Substeps * Last / Length);
                for (int i = Last; i > 0; i--)
                    Right[i] = Vector2.Lerp(Right[i], Right[i - 1], travel) * Attenuation;
                for (int i = 0; i < Last; i++)
                    Left[i] = Vector2.Lerp(Left[i], Left[i + 1], travel) * Attenuation;

                // Exact damped oscillator step: the span changes direction
                // quickly, without drawing a train of travelling crests.
                float dt = 1f / Substeps, omega = Frequency;
                float decay = Mathf.Exp(-RingDecay * dt);
                float sine = Mathf.Sin(omega * dt), cosine = Mathf.Cos(omega * dt);
                Vector2 position = RingPosition;
                RingPosition = (position * cosine + (RingVelocity + position * RingDecay) / omega * sine) * decay;
                RingVelocity = (RingVelocity * cosine -
                    (position * (omega * omega + RingDecay * RingDecay) + RingVelocity * RingDecay) / omega * sine) * decay;
                RingLimit = Mathf.Max(1.2f, RingLimit * decay);
                if (RingLimit <= 1.2f) RingDecay = 0.20f;
                BoundRing();
            }

            private void BoundRing()
            {
                float amplitude = Mathf.Sqrt(RingPosition.sqrMagnitude + RingVelocity.sqrMagnitude / (Frequency * Frequency));
                if (amplitude > RingLimit)
                {
                    RingPosition *= RingLimit / amplitude;
                    RingVelocity *= RingLimit / amplitude;
                }
            }

            internal float Shape(float coordinate)
            {
                float t = Mathf.Clamp01((coordinate - From) / (To - From));
                return t <= 0f || t >= 1f ? 0f : Mathf.Sin(Mathf.PI * t);
            }

            internal Vector2 RingAt(float coordinate, float timeStacker) =>
                Vector2.Lerp(RingPrevious, RingPosition, timeStacker) * Shape(coordinate);

            internal void Pluck(float coordinate, Vector2 impulse, bool gentle)
            {
                float shape = Shape(coordinate);
                if (shape <= 0f) return;
                if (!gentle) { RingLimit = 9f; RingDecay = 0.095f; }
                // Stepping injects much less energy than leaving the support.
                RingVelocity += Vector2.ClampMagnitude(impulse, 12f) *
                    (Frequency * shape * (gentle ? 0.23f : 0.95f));
                BoundRing();
            }

            internal Vector2 At(int index)
            {
                if ((index == 0 && Start.Root.Fixed) || (index == Last && End.Root.Fixed))
                    return Vector2.zero;
                return Right[index] + Left[index];
            }

            internal Vector2 Sample(float coordinate, float timeStacker)
            {
                float at = Mathf.Clamp01((coordinate - From) / (To - From)) * Last;
                int i = Mathf.Min((int)at, Last - 1);
                Vector2 a = Vector2.Lerp(Previous[i], At(i), timeStacker);
                Vector2 b = Vector2.Lerp(Previous[i + 1], At(i + 1), timeStacker);
                return Vector2.Lerp(a, b, at - i) + RingAt(coordinate, timeStacker);
            }

            internal void Inject(float coordinate, Vector2 value)
            {
                float at = Mathf.Clamp01((coordinate - From) / (To - From)) * Last;
                int i = Mathf.Min((int)at, Last - 1);
                float fraction = at - i;
                value *= 0.5f * Mathf.Min(1f, Speed / Substeps * Last / Length);
                InjectCell(i, value * (1f - fraction));
                InjectCell(i + 1, value * fraction);
            }

            private void InjectCell(int index, Vector2 value)
            {
                if (index == 0) Start.Root.Drive += value * 2f;
                else if (index == Last) End.Root.Drive += value * 2f;
                else { Right[index] += value; Left[index] += value; }
            }
        }

        private sealed class Pulse
        {
            internal float Coordinate, Age, Duration;
            internal Vector2 Amplitude;
        }

        private sealed class Pluck
        {
            internal float Coordinate;
            internal Vector2 Impulse;
            internal bool Gentle;
        }

        internal sealed class Strand
        {
            internal readonly float[] Coordinates;
            internal readonly Junction[] Nodes;
            internal Span[] Spans { get; private set; }
            private float[] sampleCoordinates;
            private readonly float unitLength;
            private readonly List<Pulse> pulses = new List<Pulse>();
            private readonly List<Pluck> plucks = new List<Pluck>();
            private Strand previousState;
            internal bool Active { get; private set; }

            internal Strand(float[] coordinates, float unitLength, bool fixedStart, bool fixedEnd, Strand previous)
            {
                Coordinates = (float[])coordinates.Clone();
                sampleCoordinates = Coordinates;
                this.unitLength = unitLength;
                previousState = previous;
                Nodes = new Junction[coordinates.Length];
                Spans = new Span[coordinates.Length - 1];
                for (int i = 0; i < Nodes.Length; i++)
                    Nodes[i] = new Junction { Fixed = (i == 0 && fixedStart) || (i == Nodes.Length - 1 && fixedEnd) };
                for (int i = 0; i < Spans.Length; i++)
                {
                    Spans[i] = new Span(coordinates[i], coordinates[i + 1], unitLength, Nodes[i], Nodes[i + 1]);
                }
                if (previous != null)
                {
                    float scale = previous.unitLength / unitLength;
                    foreach (Pulse pulse in previous.pulses)
                        pulses.Add(new Pulse { Coordinate = pulse.Coordinate * scale, Age = pulse.Age,
                            Duration = pulse.Duration, Amplitude = pulse.Amplitude });
                    foreach (Pluck pluck in previous.plucks)
                        plucks.Add(new Pluck { Coordinate = pluck.Coordinate * scale, Impulse = pluck.Impulse, Gentle = pluck.Gentle });
                    Active = previous.Active;
                }
            }

            private Span Find(float coordinate)
            {
                int index = Array.BinarySearch(sampleCoordinates, coordinate);
                return Spans[Mathf.Clamp(index >= 0 ? index : ~index - 1, 0, Spans.Length - 1)];
            }

            private void CopyTo(Span span)
            {
                float coordinateScale = span.Length / (span.To - span.From) / unitLength;
                // Project existing span vibration onto the new fundamental mode.
                // The residual is carried by travelling fields so topology edits
                // preserve the instantaneous surface instead of snapping it.
                float weight = 0f;
                for (int j = 1; j < span.Last; j++)
                {
                    float at = Mathf.Lerp(span.From, span.To, (float)j / span.Last);
                    float oldAt = at * coordinateScale;
                    Span old = Find(oldAt);
                    float shape = span.Shape(at);
                    span.RingPosition += old.RingAt(oldAt, 1f) * shape;
                    span.RingPrevious += old.RingAt(oldAt, 0f) * shape;
                    span.RingVelocity += old.RingVelocity * (old.Shape(oldAt) * shape);
                    span.RingLimit = Mathf.Max(span.RingLimit, old.RingLimit);
                    span.RingDecay = Mathf.Min(span.RingDecay, old.RingDecay);
                    weight += shape * shape;
                }
                if (weight > 0f)
                {
                    span.RingPosition /= weight; span.RingPrevious /= weight; span.RingVelocity /= weight;
                }
                // Adding/removing a branch preserves both travelling directions.
                for (int j = 0; j <= span.Last; j++)
                {
                    float at = Mathf.Lerp(span.From, span.To, (float)j / span.Last);
                    float oldAt = at * coordinateScale;
                    Span old = Find(oldAt);
                    float x = Mathf.Clamp01((oldAt - old.From) / (old.To - old.From)) * old.Last;
                    int k = Mathf.Min((int)x, old.Last - 1);
                    Vector2 residual = (old.RingAt(oldAt, 1f) - span.RingAt(at, 1f)) * 0.5f;
                    span.Right[j] = Vector2.Lerp(old.Right[k], old.Right[k + 1], x - k) + residual;
                    span.Left[j] = Vector2.Lerp(old.Left[k], old.Left[k + 1], x - k) + residual;
                    span.Previous[j] = old.Sample(oldAt, 0f) - span.RingAt(at, 0f);
                }
            }

            internal void CoalesceSpans()
            {
                var combined = new List<Span>();
                var coordinates = new List<float> { Coordinates[0] };
                int start = 0;
                for (int end = 1; end < Nodes.Length; end++)
                {
                    Junction node = Nodes[end].Root;
                    if (end != Nodes.Length - 1 && node.Ports.Count == 2 && !node.Fixed) continue;
                    var span = new Span(Coordinates[start], Coordinates[end], unitLength, Nodes[start], Nodes[end]);
                    (previousState ?? this).CopyTo(span);
                    combined.Add(span); coordinates.Add(Coordinates[end]); start = end;
                }
                // Ordinary load-bearing nodes are not wave junctions. Keeping
                // them as ports adds a substep delay per node, visibly stretching
                // short packets when the physics mesh is refined.
                Spans = combined.ToArray();
                sampleCoordinates = coordinates.ToArray();
                previousState = null;
            }

            internal Vector2 Sample(float coordinate, float timeStacker)
            {
                // Bound overlapping impacts without introducing a permanent load.
                return Vector2.ClampMagnitude(Find(coordinate).Sample(coordinate, timeStacker), 10f);
            }

            internal void PluckAt(float coordinate, Vector2 impulse, bool gentle)
            {
                if (impulse.sqrMagnitude < 0.0001f) return;
                Active = true;
                if (plucks.Count >= 32) plucks.RemoveAt(0);
                plucks.Add(new Pluck { Coordinate = coordinate, Impulse = impulse, Gentle = gentle });
                // Retain a small contact disturbance for finite-speed junction
                // coupling; the visible response is primarily span vibration.
                Excite(coordinate, impulse * (gentle ? 0.04f : 0.25f), gentle ? StepDuration : ImpactDuration);
            }

            internal void Excite(float coordinate, Vector2 amplitude, float duration)
            {
                if (amplitude.sqrMagnitude < 0.0001f) return;
                Active = true;
                if (pulses.Count >= 32) pulses.RemoveAt(0);
                pulses.Add(new Pulse { Coordinate = coordinate, Amplitude = Vector2.ClampMagnitude(amplitude, 10f),
                    Duration = Mathf.Clamp(duration, 3f, 24f) });
            }

            internal void Drive()
            {
                foreach (Pluck pluck in plucks) Find(pluck.Coordinate).Pluck(pluck.Coordinate, pluck.Impulse, pluck.Gentle);
                plucks.Clear();
                for (int i = pulses.Count - 1; i >= 0; i--)
                {
                    Pulse pulse = pulses[i];
                    pulse.Age += 1f / Substeps;
                    if (pulse.Age >= pulse.Duration) { pulses.RemoveAt(i); continue; }
                    float phase = pulse.Age / pulse.Duration;
                    float envelope = Mathf.Sin(Mathf.PI * phase);
                    // A smooth bipolar sine packet: press, recoil, then silence.
                    float value = Mathf.Sin(2f * Mathf.PI * phase) * envelope * envelope;
                    Find(pulse.Coordinate).Inject(pulse.Coordinate, pulse.Amplitude * (value * Response));
                }
            }

            internal void UpdateActivity()
            {
                Active = pulses.Count != 0 || plucks.Count != 0;
                foreach (Span span in Spans)
                {
                    Active |= span.RingPosition.sqrMagnitude + span.RingVelocity.sqrMagnitude + span.RingPrevious.sqrMagnitude > 0.000001f;
                    for (int i = 0; i <= span.Last && !Active; i++)
                        Active = span.Right[i].sqrMagnitude + span.Left[i].sqrMagnitude + span.Previous[i].sqrMagnitude > 0.000001f;
                }
            }
        }

        internal Strand AddStrand(float[] coordinates, float unitLength, bool fixedStart, bool fixedEnd, Strand previous = null)
        {
            var strand = new Strand(coordinates, unitLength, fixedStart, fixedEnd, previous);
            strands.Add(strand);
            return strand;
        }

        internal void Join(Strand a, int aNode, Strand b, int bNode)
        {
            Junction x = a.Nodes[aNode].Root, y = b.Nodes[bNode].Root;
            if (x == y) return;
            y.Fixed |= x.Fixed;
            x.Parent = y;
        }

        internal void Seal()
        {
            var unique = new HashSet<Junction>();
            foreach (Strand strand in strands)
                foreach (Span span in strand.Spans)
                {
                    Junction start = span.Start.Root, end = span.End.Root;
                    start.Ports.Add(new Port { Span = span, Start = true });
                    end.Ports.Add(new Port { Span = span, Start = false });
                    unique.Add(start); unique.Add(end);
                }
            junctions.AddRange(unique);
            foreach (Strand strand in strands) strand.CoalesceSpans();
            foreach (Junction node in junctions) node.Ports.Clear();
            junctions.Clear(); unique.Clear();
            foreach (Strand strand in strands)
                foreach (Span span in strand.Spans)
                {
                    Junction start = span.Start.Root, end = span.End.Root;
                    start.Ports.Add(new Port { Span = span, Start = true });
                    end.Ports.Add(new Port { Span = span, Start = false });
                    unique.Add(start); unique.Add(end);
                }
            junctions.AddRange(unique);
            Scatter();
        }

        private void Scatter()
        {
            foreach (Junction node in junctions)
            {
                Vector2 sum = Vector2.zero;
                foreach (Port port in node.Ports) sum += port.Incoming;
                Vector2 shared = sum * (2f / node.Ports.Count);
                foreach (Port port in node.Ports)
                    // Fixed/free ends absorb outgoing energy. Real silk junctions
                    // share displacement and scatter into all attached branches.
                    port.Send(node.Fixed || node.Ports.Count == 1 ? Vector2.zero :
                        shared - port.Incoming + node.Drive / node.Ports.Count);
                node.Drive = Vector2.zero;
            }
        }

        internal void Update()
        {
            foreach (Strand strand in strands)
                foreach (Span span in strand.Spans)
                {
                    span.RingPrevious = span.RingPosition;
                    for (int i = 0; i <= span.Last; i++) span.Previous[i] = span.At(i);
                }
            for (int step = 0; step < Substeps; step++)
            {
                foreach (Strand strand in strands)
                {
                    foreach (Span span in strand.Spans) span.Advance();
                    strand.Drive();
                }
                Scatter();
            }
            foreach (Strand strand in strands) strand.UpdateActivity();
        }
    }
}
