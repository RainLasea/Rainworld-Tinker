using RWCustom;
using SlugBase.DataTypes;
using System;
using UnityEngine;

namespace Tinker.PlayerGraphics_Hooks
{
    public class TailModule : IDisposable
    {
        public PlayerGraphics self;
        public TailSegment[] tail;
        private readonly TailSegment[] originalTail;
        private readonly LeaserSprites sprites = new();

        public TailModule(PlayerGraphics self)
        {
            this.self = self;
            // Keep the original body-part registration valid for other graphics mods.
            originalTail = self.tail;
            self.tail = new TailSegment[4];
            self.tail[0] = new TailSegment(self, 8f, 8f, null, 0.85f, 1f, 1f, true);
            self.tail[1] = new TailSegment(self, 8f, 10f, self.tail[0], 0.85f, 1f, 0.5f, true);
            self.tail[2] = new TailSegment(self, 6f, 10f, self.tail[1], 0.85f, 1f, 0.5f, true);
            self.tail[3] = new TailSegment(self, 4f, 8f, self.tail[2], 0.85f, 1f, 0.5f, true);
            tail = self.tail;
            ReplaceBodyParts(originalTail, tail);
            foreach (var segment in tail) segment.Reset(self.owner.bodyChunks[1].pos);
        }

        public void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            TriangleMesh.Triangle[] tris = new TriangleMesh.Triangle[]
            {
                new TriangleMesh.Triangle(0, 1, 2),
                new TriangleMesh.Triangle(1, 2, 3),
                new TriangleMesh.Triangle(2, 3, 4),
                new TriangleMesh.Triangle(3, 4, 5),
                new TriangleMesh.Triangle(4, 5, 6)
            };

            var meshA = new TriangleMesh("tinker_facea", tris, true, true);
            var meshB = new TriangleMesh("tinker_faceb", tris, true, true);

            meshA.shader = rCam.game.rainWorld.Shaders["Basic"];
            meshB.shader = rCam.game.rainWorld.Shaders["Basic"];

            int totalVerts = meshA.UVvertices.Length;
            int pairs = (totalVerts - 1) / 2;

            for (int j = 0; j < totalVerts; j++)
            {
                float t;
                float u;

                if (j == totalVerts - 1)
                {
                    t = 1f;
                    u = 0.5f;
                }
                else
                {
                    t = (j / 2) / (float)pairs;
                    u = (j % 2 == 0) ? 0f : 1f;
                }

                meshA.UVvertices[j] = new Vector2(u, t * 0.5f);
                meshB.UVvertices[j] = new Vector2(u, 0.5f + t * 0.5f);
            }

            sprites.Add(sLeaser, meshA, meshB);
            AddToContainer(sLeaser, rCam, null);
        }

        public void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            if (!sprites.TryGet(sLeaser, out var meshes)) return;
            var meshA = (TriangleMesh)meshes[0];
            var meshB = (TriangleMesh)meshes[1];
            meshA.isVisible = meshB.isVisible = sLeaser.sprites[2].isVisible;
            meshA.alpha = meshB.alpha = sLeaser.sprites[2].alpha;

            Color colA = PlayerColor.GetCustomColor(self, "AntennaBase");
            Color colB = PlayerColor.GetCustomColor(self, "AntennaTip");

            Color connectionColor = Color.Lerp(colA, Color.black, 0.4f);

            Vector2 lastP = Vector2.Lerp(self.owner.bodyChunks[1].lastPos, self.owner.bodyChunks[1].pos, timeStacker);

            for (int i = 0; i < tail.Length; i++)
            {
                Vector2 p = Vector2.Lerp(tail[i].lastPos, tail[i].pos, timeStacker);
                Vector2 dir = Custom.DirVec(lastP, p);
                Vector2 perp = Custom.PerpendicularVector(dir);
                float rad = tail[i].StretchedRad * 0.8f;

                if (i == tail.Length - 1)
                {
                    meshA.MoveVertice(i * 2, p - camPos);
                    meshB.MoveVertice(i * 2, p - camPos);
                }
                else
                {
                    meshA.MoveVertice(i * 2, p - perp * rad - camPos);
                    meshA.MoveVertice(i * 2 + 1, p + perp * rad - camPos);
                    meshB.MoveVertice(i * 2, p - perp * rad - camPos);
                    meshB.MoveVertice(i * 2 + 1, p + perp * rad - camPos);
                }

                Color finalColorA = colA;
                Color finalColorB = colB;

                if (i == 0)
                {
                    finalColorA = Color.Lerp(connectionColor, colA, 0.2f);
                    finalColorB = Color.Lerp(connectionColor, colB, 0.2f);
                }
                else if (i == 1)
                {
                    finalColorA = Color.Lerp(connectionColor, colA, 0.7f);
                    finalColorB = Color.Lerp(connectionColor, colB, 0.7f);
                }

                int v = i * 2;
                meshA.verticeColors[v] = finalColorA;
                meshB.verticeColors[v] = finalColorB;

                if (i < tail.Length - 1)
                {
                    meshA.verticeColors[v + 1] = finalColorA;
                    meshB.verticeColors[v + 1] = finalColorB;
                }

                lastP = p;
            }

            meshA.Refresh();
            meshB.Refresh();
        }
        public void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContainer)
        {
            if (!sprites.TryGet(sLeaser, out var meshes)) return;
            FContainer c = newContainer ?? sLeaser.sprites[2].container ?? rCam.ReturnFContainer("Midground");
            foreach (var mesh in meshes)
            {
                mesh.RemoveFromContainer();
                c.AddChild(mesh);
            }
        }

        private void ReplaceBodyParts(TailSegment[] previous, TailSegment[] replacement)
        {
            var parts = new System.Collections.Generic.List<BodyPart>(self.bodyParts);
            for (int i = 0; i < previous.Length; i++)
            {
                int index = parts.IndexOf(previous[i]);
                if (index < 0) continue;
                if (i < replacement.Length) parts[index] = replacement[i];
                else parts.RemoveAt(index);
            }
            foreach (var part in replacement)
                if (!parts.Contains(part)) parts.Add(part);
            self.bodyParts = parts.ToArray();
        }

        public void Cleanup()
        {
            sprites.Cleanup();
            if (ReferenceEquals(self.tail, tail))
            {
                self.tail = originalTail;
                ReplaceBodyParts(tail, originalTail);
                foreach (var segment in originalTail) segment.Reset(self.owner.bodyChunks[1].pos);
            }
        }
        public void Dispose() => Cleanup();
    }
}
