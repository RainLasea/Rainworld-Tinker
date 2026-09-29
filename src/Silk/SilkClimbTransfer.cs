using System;
using UnityEngine;

namespace tinker.Silk
{
    // Translate only this player's queried support during a handoff. Starting a
    // transfer never moves the body; subsequent simulation ticks close the gap.
    internal struct SilkClimbTransfer
    {
        internal const float MaxStep = 2f;
        private Vector2 start;
        private int ticks, duration;
        internal Vector2 Offset { get; private set; }
        internal bool Active => ticks < duration;

        internal void Begin(Vector2 displacement)
        {
            start = Offset = -displacement;
            ticks = 0;
            // Smoothstep's maximum derivative is 1.5. Bound physical correction
            // per tick, including the larger feet-to-hands posture changes.
            duration = displacement.sqrMagnitude < 0.000001f ? 0 :
                Math.Max(6, (int)Math.Ceiling(displacement.magnitude * 1.5f / MaxStep));
            if (duration == 0) start = Offset = Vector2.zero;
        }

        internal void Advance()
        {
            if (!Active) return;
            float t = (float)++ticks / duration;
            Offset = start * (1f - t * t * (3f - 2f * t));
        }
    }
}
