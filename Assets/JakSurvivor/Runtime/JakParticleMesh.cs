using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace JakSurvivor
{
    // Owned by one effect and one camera: deferred draws never share mutable geometry.
    // Alpha-blended effects keep their individual submissions and native depth sorting.
    internal sealed class JakParticleMesh
    {
        private sealed class Batch
        {
            internal readonly Mesh Mesh = new Mesh { name = "JakParticleBatch" };
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            internal readonly List<Vector2> Uv = new List<Vector2>();
            internal readonly List<Color> Colors = new List<Color>();
            internal readonly List<Vector4> Probes = new List<Vector4>();
            internal readonly List<Vector2> Depths = new List<Vector2>();
            internal readonly List<int> Triangles = new List<int>();
            internal Batch() => Mesh.MarkDynamic();
            internal void Clear() { Vertices.Clear(); Uv.Clear(); Colors.Clear(); Probes.Clear(); Depths.Clear(); Triangles.Clear(); }
        }
        private readonly Dictionary<Material, Batch> batches = new Dictionary<Material, Batch>();
        private readonly MaterialPropertyBlock white = new MaterialPropertyBlock();
        internal JakParticleMesh() => white.SetVector("_TintColor", Vector4.one);

        internal void Clear() { foreach (Batch batch in batches.Values) batch.Clear(); }
        internal void Quad(Material material, Vector3 center, Vector3 right, Vector3 up, Color color, Vector4 probe = default, float depth = 0f)
        {
            if (!batches.TryGetValue(material, out Batch batch)) batches.Add(material, batch = new Batch());
            int first = batch.Vertices.Count;
            batch.Vertices.Add(center - right - up); batch.Vertices.Add(center + right - up);
            batch.Vertices.Add(center + right + up); batch.Vertices.Add(center - right + up);
            batch.Uv.Add(Vector2.zero); batch.Uv.Add(Vector2.right); batch.Uv.Add(Vector2.one); batch.Uv.Add(Vector2.up);
            // Float vertex colors retain source channels above 1; Color32 would clamp them.
            for (int i = 0; i < 4; i++) { batch.Colors.Add(color); batch.Probes.Add(probe); batch.Depths.Add(new Vector2(depth, 0f)); }
            batch.Triangles.Add(first); batch.Triangles.Add(first + 1); batch.Triangles.Add(first + 2);
            batch.Triangles.Add(first); batch.Triangles.Add(first + 2); batch.Triangles.Add(first + 3);
        }
        internal void Draw(Camera camera, int layer)
        {
            foreach (var pair in batches)
            {
                Batch batch = pair.Value;
                if (batch.Vertices.Count == 0) continue;
                batch.Mesh.Clear();
                batch.Mesh.SetVertices(batch.Vertices); batch.Mesh.SetUVs(0, batch.Uv); batch.Mesh.SetColors(batch.Colors); batch.Mesh.SetUVs(1, batch.Probes); batch.Mesh.SetUVs(2, batch.Depths);
                batch.Mesh.SetTriangles(batch.Triangles, 0);
                Graphics.DrawMesh(batch.Mesh, Matrix4x4.identity, pair.Key, layer, camera, 0, white, ShadowCastingMode.Off, false);
            }
        }
        internal void Dispose()
        {
            foreach (Batch batch in batches.Values)
                if (batch.Mesh)
                {
                    if (Application.isPlaying) Object.Destroy(batch.Mesh);
                    else Object.DestroyImmediate(batch.Mesh);
                }
            batches.Clear();
        }
    }
}
