#if RAINMEADOW
using RainMeadow;
using UnityEngine;
using static Tinker.Silk.Bridge.BridgeModeState;

namespace tinker.Silk.RainMeadow
{
    public class TinkerSilkEntityData : OnlineEntity.EntityData
    {
        internal TinkerSilkEntityDataState Snapshot;
        internal uint Revision;
        internal uint AppliedRevision;
        internal Player AppliedPlayer;

        public override EntityDataState MakeState(OnlineEntity entity, OnlineResource inResource)
        {
            // Capture at network tick time, after every Player.Update hook has run.
            if (entity.isMine && entity is OnlinePhysicalObject opo &&
                opo.apo.realizedObject is Player player && tinkerSilkData.IsTinkerPlayer(player))
                Snapshot = new TinkerSilkEntityDataState(player, tinkerSilkData.Get(player));
            return Snapshot ?? new TinkerSilkEntityDataState();
        }

        public class TinkerSilkEntityDataState : EntityDataState
        {
            [OnlineField] public string room = "";
            [OnlineField] public int mode;
            [OnlineField] public Vector2 tip;
            [OnlineField] public Vector2 terrain;
            [OnlineField] public float ropeLength;
            [OnlineField] public float requestedLength;
            [OnlineField] public bool pullingObject;
            [OnlineField] public int superJumpTimer;
            [OnlineField] public bool instantDisappear;
            [OnlineField] public int shotSequence;
            [OnlineField] public Vector2[] bends = System.Array.Empty<Vector2>();
            [OnlineField(nullable: true)] public OnlineEntity.EntityId attachedObject;
            [OnlineField] public float energy = 100f;
            [OnlineField] public bool exhausted;
            [OnlineField] public bool building;
            [OnlineField] public bool firingBridge;
            [OnlineField] public Vector2 bridgeStart;
            [OnlineField] public Vector2 bridgeTip;

            public TinkerSilkEntityDataState() { }
            public TinkerSilkEntityDataState(Player player, SilkPhysics silk)
            {
                room = player.room?.abstractRoom.name ?? "";
                mode = (int)silk.mode;
                tip = silk.pos;
                terrain = silk.terrainStuckPos;
                ropeLength = silk.idealRopeLength;
                requestedLength = silk.requestedRopeLength;
                pullingObject = silk.pullingObject;
                superJumpTimer = silk.superJumpTimer;
                instantDisappear = silk.instantDisappear;
                shotSequence = silk.ShotSequence;
                bends = silk.CopyNetworkBends();
                attachedObject = MeadowCompatibility.ObjectId(silk.attachedObject);
                energy = tinkerSilkData.GetEnergy(player);
                exhausted = tinkerSilkData.GetExhausted(player);
                var build = SilkBridgeManager.GetBridgeModeState(player);
                building = build.active;
                firingBridge = build.virtualSilkActive || build.animating;
                bridgeStart = build.point2;
                bridgeTip = build.virtualSilkPos;
            }

            public override void ReadTo(OnlineEntity.EntityData data, OnlineEntity onlineEntity)
            {
                if (onlineEntity.isMine) return;
                var silk = (TinkerSilkEntityData)data;
                silk.Snapshot = this;
                silk.Revision++;
            }
            public override System.Type GetDataType() => typeof(TinkerSilkEntityData);
        }
    }
}
#endif
