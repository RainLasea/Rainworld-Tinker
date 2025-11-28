using UnityEngine;
using RWCustom;
using Weaver.Silk.Bridge;

namespace Weaver.Silk.Collision
{
    public interface ICollisionProvider
    {
        bool RayTraceBridgesReturnFirstIntersection(Room room, Vector2 from, Vector2 to, out SilkBridge bridge, out Vector2 hitPoint, out float t);
        IntVector2? RayTraceTilesForTerrainReturnFirstSolid(Room room, Vector2 from, Vector2 to);
        int RayTracedTilesArray(Vector2 from, Vector2 to, IntVector2[] outArray);
    }
}