using RainMeadow;
using System;
using System.Linq;
using UnityEngine;

namespace tinker.Silk.RainMeadow
{
    /// <summary>
    /// Bridge between Tinker's silk system and Rain Meadow's OnlineEntity sync system.
    /// Handles attaching TinkerSilkEntityData to the OnlineEntity and keeping it in sync.
    /// All references to Rain Meadow types are conditionally compiled.
    /// </summary>
    public static class RainMeadowBridge
    {
        private static bool? _available;
        private static bool _checked;

        /// <summary>
        /// Checks if Rain Meadow is loaded in the current AppDomain.
        /// Safe to call without try-catch — just checks assembly names.
        /// </summary>
        public static bool IsRainMeadowLoaded
        {
            get
            {
                if (!_checked)
                {
                    _checked = true;
                    try
                    {
                        _available = AppDomain.CurrentDomain.GetAssemblies()
                            .Any(a => a.GetName().Name.Replace(" ", "").Equals("RainMeadow", StringComparison.OrdinalIgnoreCase));
                    }
                    catch
                    {
                        _available = false;
                    }
                }
                return _available ?? false;
            }
        }

        /// <summary>
        /// Attach TinkerSilkEntityData to the Player's OnlineEntity.
        /// </summary>
        public static void AttachSilkData(Player player)
        {
            if (!IsRainMeadowLoaded) return;

            var opo = GetOnlinePhysicalObject(player);
            if (opo == null) return;

            if (opo.TryGetData<TinkerSilkEntityData>(out _)) return;

            var data = new TinkerSilkEntityData();
            opo.AddData(data);
        }

        /// <summary>
        /// Push local SilkPhysics state into the OnlineEntity's TinkerSilkEntityData.
        /// </summary>
        public static void PushSilkState(Player player, SilkPhysics silk)
        {
            if (!IsRainMeadowLoaded) return;

            var opo = GetOnlinePhysicalObject(player);
            if (opo == null) return;

            if (!opo.TryGetData<TinkerSilkEntityData>(out var data))
            {
                data = new TinkerSilkEntityData();
                opo.AddData(data);
            }

            data.Mode = silk.mode;
            data.Attached = silk.Attached;
            data.PosX = silk.pos.x;
            data.PosY = silk.pos.y;
            data.TerrainAttachX = silk.terrainStuckPos.x;
            data.TerrainAttachY = silk.terrainStuckPos.y;
            data.RopeLength = silk.idealRopeLength;
            data.PullingObject = silk.pullingObject;
            data.SuperJumpTimer = silk.superJumpTimer;
        }

        /// <summary>
        /// Pull synced silk state from the OnlineEntity into local SilkPhysics.
        /// </summary>
        public static bool PullSilkState(Player player, SilkPhysics silk)
        {
            if (!IsRainMeadowLoaded) return false;

            var opo = GetOnlinePhysicalObject(player);
            if (opo == null) return false;

            if (!opo.TryGetData<TinkerSilkEntityData>(out var data)) return false;

            silk.mode = data.Mode;
            silk.pos = new Vector2(data.PosX, data.PosY);
            silk.lastPos = silk.pos;
            silk.terrainStuckPos = new Vector2(data.TerrainAttachX, data.TerrainAttachY);
            silk.idealRopeLength = data.RopeLength;
            silk.requestedRopeLength = data.RopeLength;
            silk.pullingObject = data.PullingObject;
            silk.superJumpTimer = data.SuperJumpTimer;
            return true;
        }

        public static bool HasSilkData(Player player)
        {
            if (!IsRainMeadowLoaded) return false;
            var opo = GetOnlinePhysicalObject(player);
            if (opo == null) return false;
            return opo.TryGetData<TinkerSilkEntityData>(out _);
        }

        private static OnlinePhysicalObject GetOnlinePhysicalObject(Player player)
        {
            if (player?.abstractPhysicalObject == null) return null;
            OnlinePhysicalObject.map.TryGetValue(player.abstractPhysicalObject, out var opo);
            return opo;
        }

        public static bool IsOnlineAndRemote(Player player)
        {
            if (!IsRainMeadowLoaded) return false;
            var opo = GetOnlinePhysicalObject(player);
            return opo != null && !opo.isMine;
        }
    }
}
