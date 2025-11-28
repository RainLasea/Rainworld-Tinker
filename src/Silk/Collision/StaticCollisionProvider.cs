using UnityEngine;
using RWCustom;
using Weaver.Silk.Bridge;

namespace Weaver.Silk.Collision
{
    public sealed class StaticCollisionProvider : ICollisionProvider
    {
        public static readonly StaticCollisionProvider Instance = new StaticCollisionProvider();
        private StaticCollisionProvider() { }

        public bool RayTraceBridgesReturnFirstIntersection(Room room, Vector2 from, Vector2 to, out SilkBridge bridge, out Vector2 hitPoint, out float t)
        {
            return SilkBridgeManager.RayTraceBridgesReturnFirstIntersection(room, from, to, out bridge, out hitPoint, out t);
        }

        public IntVector2? RayTraceTilesForTerrainReturnFirstSolid(Room room, Vector2 from, Vector2 to)
        {
            return SharedPhysics.RayTraceTilesForTerrainReturnFirstSolid(room, from, to);
        }

        public int RayTracedTilesArray(Vector2 from, Vector2 to, IntVector2[] outArray)
        {
            return SharedPhysics.RayTracedTilesArray(from, to, outArray);
        }
    }
}