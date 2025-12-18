using UnityEngine;
using RWCustom;
using Tinker.Silk.Bridge;

namespace Tinker.Silk.Collision
{
    public interface ICollisionProvider
    {
        bool RayTraceBridgesReturnFirstIntersection(Room room, Vector2 from, Vector2 to, out SilkBridge bridge, out Vector2 hitPoint, out float t);
        IntVector2? RayTraceTilesForTerrainReturnFirstSolid(Room room, Vector2 from, Vector2 to);
        int RayTracedTilesArray(Vector2 from, Vector2 to, IntVector2[] outArray);
    }
}