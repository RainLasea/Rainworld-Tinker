using RainMeadow;
using UnityEngine;

namespace tinker.Silk.RainMeadow
{
    /// <summary>
    /// Custom EntityData for syncing Tinker's silk state over Rain Meadow multiplayer.
    /// Automatically synced as part of OnlineEntity's EntityState.entityDataStates.
    /// Auto-registered by Rain Meadow's InitializeBuiltinTypes() which scans all assemblies.
    /// </summary>
    public class TinkerSilkEntityData : OnlineEntity.EntityData
    {
        /// <summary>
        /// State class with [OnlineField] attributes for automatic serialization.
        /// Non-abstract + extends EntityDataState → auto-registered by Rain Meadow.
        /// </summary>
        public class TinkerSilkEntityDataState : EntityDataState
        {
            // ── Synced fields ──────────────────────────────────
            // SilkMode as int: 0=Retracted, 1=ShootingOut, 2=AttachedToTerrain, 3=AttachedToObject, 4=Retracting
            [OnlineField("default", false, false, true)]
            public int mode;

            [OnlineField("default", false, false, true)]
            public bool attached;

            // Current silk tip position (world space)
            [OnlineField("default", false, false, true)]
            public float posX;

            [OnlineField("default", false, false, true)]
            public float posY;

            // Terrain anchor position (if attached to terrain)
            [OnlineField("default", false, false, true)]
            public float terrainAttachX;

            [OnlineField("default", false, false, true)]
            public float terrainAttachY;

            // Rope length for rendering
            [OnlineField("default", false, false, true)]
            public float ropeLength;

            // Visual state — green pull indicator + blue super-jump glow
            [OnlineField("default", false, false, true)]
            public bool pullingObject;

            [OnlineField("default", false, false, true)]
            public int superJumpTimer;

            // ── Constructors ──────────────────────────────────
            public TinkerSilkEntityDataState() { }

            public TinkerSilkEntityDataState(TinkerSilkEntityData data)
            {
                mode = (int)data.Mode;
                attached = data.Attached;
                posX = data.PosX;
                posY = data.PosY;
                terrainAttachX = data.TerrainAttachX;
                terrainAttachY = data.TerrainAttachY;
                ropeLength = data.RopeLength;
                pullingObject = data.PullingObject;
                superJumpTimer = data.SuperJumpTimer;
            }

            // ── EntityDataState implementation ────────────────
            public override void ReadTo(OnlineEntity.EntityData data, OnlineEntity onlineEntity)
            {
                var d = (TinkerSilkEntityData)data;
                d.Mode = (SilkMode)mode;
                d.Attached = attached;
                d.PosX = posX;
                d.PosY = posY;
                d.TerrainAttachX = terrainAttachX;
                d.TerrainAttachY = terrainAttachY;
                d.RopeLength = ropeLength;
                d.PullingObject = pullingObject;
                d.SuperJumpTimer = superJumpTimer;
                // NOTE: SilkPhysics is NOT updated here to avoid premature creation.
                // PlayerUpdate → PullSilkState() handles EntityData → SilkPhysics copy
                // at the right time when isRemote is properly set.
            }

            public override System.Type GetDataType() => typeof(TinkerSilkEntityData);
        }

        // ── Runtime data on the entity ────────────────────────
        public SilkMode Mode = SilkMode.Retracted;
        public bool Attached;
        public float PosX, PosY;
        public float TerrainAttachX, TerrainAttachY;
        public float RopeLength;
        public bool PullingObject;
        public int SuperJumpTimer;

        public override OnlineEntity.EntityData.EntityDataState MakeState(OnlineEntity entity, OnlineResource inResource)
            => new TinkerSilkEntityDataState(this);
    }
}
