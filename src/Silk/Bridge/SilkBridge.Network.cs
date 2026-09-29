using tinker.Silk;
using UnityEngine;

namespace Tinker.Silk.Bridge
{
    public partial class SilkBridge
    {
        internal ushort NetworkOwner = 0;
        internal int NetworkNumber = 0;
        internal float NetworkLength => maxBridgeLength;
        private SilkNetworkCurve networkCurve;

        internal void CaptureNetworkGeometry(out float[] coordinates, out Vector2[] basis, out Vector2[] visual)
        {
            coordinates = (float[])rope.Coordinates.Clone();
            basis = new Vector2[coordinates.Length];
            visual = new Vector2[coordinates.Length];
            for (int i = 0; i < coordinates.Length; i++)
            {
                basis[i] = rope.Particles[i].Position;
                visual[i] = basis[i] + WaveOffset(coordinates[i], 1f);
            }
        }

        internal void ReceiveNetworkGeometry(float[] coordinates, Vector2[] basis, Vector2[] visual)
        {
            if (!SilkNetworkCurve.Valid(coordinates, basis, visual, SegmentCount)) return;
            networkCurve ??= new SilkNetworkCurve();
            networkCurve.Receive(coordinates, basis, visual);
        }

        internal void UpdateNetworkReplica()
        {
            networkCurve?.Advance();
            for (int i = 0; networkCurve != null && i < RenderPoints.Length; i++)
                RenderPoints[i] = networkCurve.Sample(i, 1f, false);
            // Each peer only moves the physical objects that it owns.
            CheckAndAttachNearbyObjects();
            UpdateAttachedObjects();
        }

        internal void BecomeNetworkOwner()
        {
            if (networkCurve == null) return;
            for (int i = 0; i < rope.Particles.Length; i++)
            {
                var node = rope.Particles[i];
                node.Position = node.Previous = node.FramePosition = networkCurve.Sample(rope.Coordinates[i], 1f, false);
                node.Force = Vector2.zero;
            }
            networkCurve = null;
            UpdateRenderPoints();
        }

        internal void RemoveNetworkBridge(bool animate)
        {
            if (animate && !slatedForDeletetion)
                BrokenSilkManager.TriggerBreakAnimation(GetRenderPath(), room,
                    GetPointOnSegment(SegmentCount / 2, 0.5f), startAnchor, endAnchor);
            slatedForDeletetion = true;
            startAnchor.attachedBridge?.rope.RemoveJunction(physicsNodes[0]);
            endAnchor.attachedBridge?.rope.RemoveJunction(physicsNodes[physicsNodes.Length - 1]);
            ReleaseAllAttachedObjects();
        }
    }
}
