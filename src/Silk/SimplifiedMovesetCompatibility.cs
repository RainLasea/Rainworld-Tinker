using System;
using System.Reflection;
using HarmonyLib;

namespace tinker.Silk
{
    internal static class SimplifiedMovesetCompatibility
    {
        internal static void Init(Harmony harmony)
        {
            // The soft dependency loads the optional plugin before our OnEnable,
            // so this guard is patched before its gameplay methods are JITted.
            var playerMod = AccessTools.TypeByName("SimplifiedMoveset.PlayerMod");
            if (playerMod == null) return;
            Apply(harmony, playerMod);
        }

        internal static void Apply(Harmony harmony, Type playerMod)
        {
            var method = playerMod.GetMethod("Is_Blacklisted", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(Player) }, null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(playerMod.FullName, "bool Is_Blacklisted(Player)");
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(SimplifiedMovesetCompatibility), nameof(UseSilkLocomotion)));
        }

        private static bool UseSilkLocomotion(Player __0, ref bool __result)
        {
            // SimplifiedMoveset's IL dispatchers already fall through to vanilla
            // for blacklisted players. Its replacement beam functions query real
            // room tiles and cannot see our Player-only virtual beam adapter.
            // Use that existing fallback only while this player is on silk; do
            // not edit its blacklist, attached fields, settings or room tiles.
            if (!SilkClimb.IsClimbing(__0) || !SilkClimb.BeamAnimation(__0.animation)) return true;
            __result = true;
            return false;
        }
    }
}
