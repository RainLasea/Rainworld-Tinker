using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RWCustom;
using UnityEngine;

namespace tinker.Silk
{
    // Adapt only Player's two beam state-machine methods. No room tiles are
    // mutated and no creature, terrain collision or other player sees silk tiles.
    internal static class SilkBeamAdapter
    {
        internal static void Init()
        {
            IL.Player.UpdateAnimation += Adapt;
            try { IL.Player.UpdateBodyMode += Adapt; }
            catch
            {
                IL.Player.UpdateAnimation -= Adapt;
                throw;
            }
        }

        internal static void Cleanup()
        {
            IL.Player.UpdateAnimation -= Adapt;
            IL.Player.UpdateBodyMode -= Adapt;
        }

        internal static void Adapt(ILContext il)
        {
            var cursor = new ILCursor(il);
            int tiles = 0, centers = 0;
            while (cursor.TryGotoNext(MoveType.Before, instruction =>
                (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                instruction.Operand is MethodReference method && method.DeclaringType.FullName == "Room" &&
                (method.Name == "GetTile" || method.Name == "MiddleOfTile")))
            {
                var method = (MethodReference)cursor.Next.Operand;
                bool tile = method.Name == "GetTile";
                string parameter = method.Parameters[0].ParameterType.FullName;
                // Fail explicitly if a future game update adds a query overload.
                if (method.Parameters.Count != 1 && !(method.Parameters.Count == 2 && parameter == "System.Int32"))
                    throw new InvalidOperationException("Unsupported silk beam query: " + method.FullName);
                cursor.Remove();
                cursor.Emit(OpCodes.Ldarg_0);
                if (parameter == "UnityEngine.Vector2")
                {
                    if (tile) cursor.EmitDelegate<Func<Room, Vector2, Player, Room.Tile>>(GetTile);
                    else cursor.EmitDelegate<Func<Room, Vector2, Player, Vector2>>(Middle);
                }
                else if (parameter == "RWCustom.IntVector2")
                {
                    if (tile) cursor.EmitDelegate<Func<Room, IntVector2, Player, Room.Tile>>(GetTile);
                    else cursor.EmitDelegate<Func<Room, IntVector2, Player, Vector2>>(Middle);
                }
                else if (parameter == "System.Int32" && method.Parameters.Count == 2)
                {
                    if (tile) cursor.EmitDelegate<Func<Room, int, int, Player, Room.Tile>>((room, x, y, player) => GetTile(room, new IntVector2(x, y), player));
                    else cursor.EmitDelegate<Func<Room, int, int, Player, Vector2>>((room, x, y, player) => Middle(room, new IntVector2(x, y), player));
                }
                else throw new InvalidOperationException("Unsupported silk beam query: " + method.FullName);
                if (tile) tiles++; else centers++;
            }
            if (tiles == 0 || centers == 0)
                throw new InvalidOperationException("Could not locate vanilla beam queries in " + il.Method.Name);
        }

        private static Room.Tile GetTile(Room room, Vector2 position, Player player) =>
            WithSilk(room.GetTile(position), position, player);

        private static Room.Tile GetTile(Room room, IntVector2 position, Player player) =>
            WithSilk(room.GetTile(position), SilkClimb.IsClimbing(player) ? SilkClimb.TileQuery(player, position) : room.MiddleOfTile(position), player);

        private static Room.Tile WithSilk(Room.Tile original, Vector2 query, Player player)
        {
            if (original.Solid || !SilkClimb.TryGetSurface(player, out bool vertical) ||
                !SilkClimb.TrySample(player, query, vertical, out var sample) ||
                !SilkBeamGeometry.WithinBeam(query, vertical, sample)) return original;
            return new Room.Tile(original.X, original.Y, original.Terrain,
                original.verticalBeam || vertical, original.horizontalBeam || !vertical,
                original.wallbehind, original.shortCut, original.waterInt)
            { wormGrass = original.wormGrass, hive = original.hive };
        }

        private static Vector2 Middle(Room room, Vector2 position, Player player) =>
            SurfaceCenter(room.MiddleOfTile(position), position, player);

        private static Vector2 Middle(Room room, IntVector2 position, Player player) =>
            SurfaceCenter(room.MiddleOfTile(position), SilkClimb.IsClimbing(player) ? SilkClimb.TileQuery(player, position) : room.MiddleOfTile(position), player);

        private static Vector2 SurfaceCenter(Vector2 original, Vector2 query, Player player)
        {
            if (!SilkClimb.TryGetSurface(player, out bool vertical) ||
                !SilkClimb.TrySample(player, query, vertical, out var sample)) return original;
            if (Vector2.Distance(query, sample.Point) > 35f) return original;
            if (vertical && (player.animation == Player.AnimationIndex.GetUpToBeamTip || player.animation == Player.AnimationIndex.BeamTip))
                return sample.Point + Vector2.up * 5f;
            if (vertical && player.animation == Player.AnimationIndex.HangUnderVerticalBeam)
                return sample.Point - Vector2.up * 10f;
            // Only replace the normal axis. Along-beam coordinates stay in world
            // space and are advanced solely by vanilla velocity and collision.
            if (vertical) original.x = sample.Point.x;
            else original.y = sample.Point.y;
            return original;
        }
    }
}
