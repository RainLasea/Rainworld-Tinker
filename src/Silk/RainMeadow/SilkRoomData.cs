#if RAINMEADOW
using System;
using System.Collections.Generic;
using System.Linq;
using RainMeadow;
using RainMeadow.Generics;
using Tinker.Silk.Bridge;
using UnityEngine;
using static Tinker.Silk.Bridge.BridgeModeState;

namespace tinker.Silk.RainMeadow
{
    [OnlineState.DeltaSupport(level = OnlineState.StateHandler.DeltaSupport.NullableDelta)]
    public class SilkAnchorState : OnlineState
    {
        [OnlineField] public int kind;
        [OnlineField] public Vector2 position;
        [OnlineField(nullable: true)] public OnlineEntity.EntityId target;
        [OnlineField] public int segment;
        [OnlineField] public float fraction;
        [OnlineField] public Vector2 offset;
        public SilkAnchorState() { }
        public SilkAnchorState(BridgeAnchor anchor)
        {
            kind = (int)anchor.type;
            position = anchor.GetWorldPosition();
            target = anchor.type == BridgeAnchor.AnchorType.BridgeSegment
                ? MeadowCompatibility.BridgeId(anchor.attachedBridge) : MeadowCompatibility.ObjectId(anchor.attachedObject);
            segment = anchor.type == BridgeAnchor.AnchorType.PhysicalObject ? anchor.chunkIndex : anchor.segmentIndex;
            fraction = anchor.segmentT;
            offset = anchor.localOffset;
        }
        internal bool Valid => kind >= 0 && kind <= 2 && SilkNetworkCurve.Finite(position) &&
            SilkNetworkCurve.Finite(offset) && SilkNetworkCurve.Finite(fraction) && fraction >= 0f && fraction <= 1f &&
            segment >= 0 && segment <= 255 && (kind == 0 || target != null);
        internal BridgeAnchor Resolve(Room room, List<SilkBridge> bridges)
        {
            return new BridgeAnchor
            {
                type = (BridgeAnchor.AnchorType)kind, terrainPos = position,
                attachedBridge = kind == 1 ? bridges.Find(b => MeadowCompatibility.Matches(b, target)) : null,
                attachedObject = kind == 2 ? MeadowCompatibility.ResolveObject(target, room) : null,
                segmentIndex = segment, segmentT = fraction, chunkIndex = segment, localOffset = offset
            };
        }
    }

    [OnlineState.DeltaSupport(level = OnlineState.StateHandler.DeltaSupport.NullableDelta)]
    public class SilkBridgeState : OnlineState, IIdentifiable<OnlineEntity.EntityId>
    {
        [OnlineField(always: true)] public OnlineEntity.EntityId id;
        [OnlineField("anchors")] public SilkAnchorState start;
        [OnlineField("anchors")] public SilkAnchorState end;
        [OnlineField("anchors")] public float length;
        [OnlineField("anchors")] public int segments;
        [OnlineField("health")] public float health;
        [OnlineField("geometry")] public float[] coordinates;
        [OnlineField("geometry")] public Vector2[] basis;
        [OnlineField("geometry")] public Vector2[] visual;
        public OnlineEntity.EntityId ID => id;
        public SilkBridgeState() { }
        public SilkBridgeState(SilkBridge bridge)
        {
            id = MeadowCompatibility.BridgeId(bridge);
            start = new SilkAnchorState(bridge.startAnchor);
            end = new SilkAnchorState(bridge.endAnchor);
            length = bridge.NetworkLength;
            segments = bridge.SegmentCount;
            health = bridge.health;
            bridge.CaptureNetworkGeometry(out coordinates, out basis, out visual);
        }
        internal bool Valid => id != null && id.type == (byte)OnlineEntity.EntityId.IdType.custom && id.id > 0 &&
            start != null && start.Valid && end != null && end.Valid &&
            SilkNetworkCurve.Finite(length) && length > 0f && length <= 1380.1f &&
            SilkNetworkCurve.Finite(health) && health > 0f && health <= 120f &&
            SilkNetworkCurve.Valid(coordinates, basis, visual, segments);
    }

    // Room data survives player departure and supplies complete snapshots to late joiners.
    public class SilkRoomData : OnlineResource.ResourceData
    {
        internal const int MaxBridges = 64;
        internal SilkRoomState Snapshot;
        internal SilkRoomState Applied;
        internal Room AppliedRoom;
        internal bool WasOwner;
        internal readonly Dictionary<SilkBridge, PendingForce> Forces = new();
        internal sealed class PendingForce { internal Vector2 Point, Force; internal int Samples; }
        internal readonly List<RemoteLoad> Loads = new();
        internal sealed class RemoteLoad
        {
            internal OnlinePlayer Sender;
            internal OnlineEntity.EntityId Bridge;
            internal Vector2 Point, Force;
            internal int Frames;
        }

        public override ResourceDataState MakeState(OnlineResource resource)
        {
            if (resource is RoomSession session && resource.isOwner && session.absroom.realizedRoom is Room room &&
                AppliedRoom == room && WasOwner)
                Snapshot = new SilkRoomState(SilkBridgeManager.GetBridgesInRoom(room));
            return Snapshot ?? new SilkRoomState(new List<SilkBridge>());
        }

        public class SilkRoomState : ResourceDataState
        {
            [OnlineField] public DeltaStates<SilkBridgeState, OnlineEntity.EntityId> bridges;
            public SilkRoomState() { }
            public SilkRoomState(List<SilkBridge> source)
            {
                bridges = new(source.Where(b => b.IsActive && b.health > 0f && b.NetworkNumber != 0)
                    .Take(MaxBridges).Select(b => new SilkBridgeState(b)).ToList());
            }
            public override Type GetDataType() => typeof(SilkRoomData);
            public override void ReadTo(OnlineResource.ResourceData data, OnlineResource resource)
            {
                // Meadow has already authorized this state. During ownership handoff,
                // the final incoming snapshot can be read after isOwner becomes true.
                ((SilkRoomData)data).Snapshot = this;
            }
        }
    }
}
#endif
