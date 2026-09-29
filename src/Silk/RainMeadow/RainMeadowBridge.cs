using System;
using System.Linq;
using Tinker.Silk.Bridge;
using UnityEngine;

namespace tinker.Silk.RainMeadow
{
    // This facade must contain no Rain Meadow types in fields, signatures or method bodies.
    // Its implementation lives behind non-inlined calls so the same DLL loads without RM.
    public static class RainMeadowBridge
    {
        private static bool available;
        static RainMeadowBridge()
        {
            available = AppDomain.CurrentDomain.GetAssemblies().Any(a => IsMeadowAssembly(a.GetName().Name));
            // A negative result during another plugin's OnEnable must not stick forever.
            AppDomain.CurrentDomain.AssemblyLoad += (_, args) =>
            {
                if (IsMeadowAssembly(args.LoadedAssembly.GetName().Name)) available = true;
            };
        }
        private static bool IsMeadowAssembly(string name) =>
            name.Replace(" ", "").Equals("RainMeadow", StringComparison.OrdinalIgnoreCase);
        public static bool IsRainMeadowLoaded => available;
        public static bool IsOnline
        {
            get
            {
#if RAINMEADOW
                return IsRainMeadowLoaded && MeadowCompatibility.IsOnline();
#else
                return false;
#endif
            }
        }

        public static bool IsOnlineAndRemote(Player player) => !CanSimulate(player);
        public static bool CanSimulate(PhysicalObject obj)
        {
#if RAINMEADOW
            if (IsRainMeadowLoaded) return MeadowCompatibility.CanSimulate(obj);
#endif
            return true;
        }

        public static bool CanMoveObject(PhysicalObject obj)
        {
#if RAINMEADOW
            if (IsRainMeadowLoaded) return MeadowCompatibility.CanMoveObject(obj);
#endif
            return true;
        }

        public static void AttachSilkData(Player player)
        {
#if RAINMEADOW
            if (IsRainMeadowLoaded) MeadowCompatibility.AttachSilkData(player);
#endif
        }

        public static void PushSilkState(Player player, SilkPhysics silk)
        {
#if RAINMEADOW
            if (IsRainMeadowLoaded) MeadowCompatibility.PushSilkState(player, silk);
#endif
        }

        public static bool PullSilkState(Player player, SilkPhysics silk)
        {
#if RAINMEADOW
            if (IsRainMeadowLoaded) return MeadowCompatibility.PullSilkState(player, silk);
#endif
            return false;
        }

        public static bool HasSilkData(Player player)
        {
#if RAINMEADOW
            if (IsRainMeadowLoaded) return MeadowCompatibility.HasSilkData(player);
#endif
            return false;
        }

        public static bool UpdateRoom(Room room)
        {
#if RAINMEADOW
            if (IsRainMeadowLoaded) return MeadowCompatibility.UpdateRoom(room);
#endif
            return false;
        }

        public static void UnloadRoom(Room room)
        {
#if RAINMEADOW
            if (IsRainMeadowLoaded) MeadowCompatibility.UnloadRoom(room);
#endif
        }

        public static bool CanCreateBridge(Player player)
        {
#if RAINMEADOW
            if (IsRainMeadowLoaded) return MeadowCompatibility.CanCreateBridge(player);
#endif
            return true;
        }

        public static bool SubmitBridge(Player player, SilkBridge bridge, Action onRejected)
        {
#if RAINMEADOW
            if (IsRainMeadowLoaded) return MeadowCompatibility.SubmitBridge(player, bridge, onRejected);
#endif
            return false;
        }

        public static bool RelayForce(SilkBridge bridge, Vector2 point, Vector2 force)
        {
#if RAINMEADOW
            if (IsRainMeadowLoaded) return MeadowCompatibility.RelayForce(bridge, point, force);
#endif
            return false;
        }

        public static bool RelayDamage(SilkBridge bridge, float amount, Vector2 point)
        {
#if RAINMEADOW
            if (IsRainMeadowLoaded) return MeadowCompatibility.RelayDamage(bridge, amount, point);
#endif
            return false;
        }
    }
}
