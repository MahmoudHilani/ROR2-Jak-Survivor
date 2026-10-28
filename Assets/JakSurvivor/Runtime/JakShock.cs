using System.Collections.Generic;
using System.Linq;
using RoR2;
using UnityEngine;
using UnityEngine.Rendering;
using S = JakSurvivor.JakShockSimulation;

namespace JakSurvivor
{
    // The three native shock entry points (target-part.gc:2874-3180). Each starts a lightning-tracker
    // in the owner's bounded bank; callers choose the lightning spec and the tip particle.
    internal static class JakShock
    {
        // process-drawable-shock-effect (target-part.gc:3012-3110): the bolt starts at a tracked joint
        // and ends where one of six probes hits more than 2 m away.
        internal static void Probe(GameObject owner, JakShockSpec spec, int tipPart, Transform source, GameObject caster, int layer, float range = 10f)
        {
            if (!owner || !source || !Find(source.position, caster, owner, range, out Vector3 end, out _)) return;
            Bank(owner).Launch(spec, tipPart, source, source.position, null, end, layer);
        }

        // process-drawable-shock-effect-replace with joint 256 (target-part.gc:2874-3010): a fixed origin,
        // and the tip lifted 0.1 m off the hit surface and oriented to its normal.
        internal static void ProbeReplace(GameObject owner, JakShockSpec spec, int tipPart, Vector3 origin, GameObject caster, int layer, float range = 10f)
        {
            if (!owner || !Find(origin, caster, owner, range, out Vector3 end, out Vector3 normal)) return;
            Bank(owner).Launch(spec, tipPart, null, origin, null, end, layer, end + normal * 0.1f, Quaternion.FromToRotation(Vector3.up, normal));
        }

        // process-drawable2-shock-effect (target-part.gc:3112-3180): both ends track joints, tips at both.
        internal static void Between(GameObject owner, JakShockSpec spec, int tipPart, Transform from, Transform to, int layer)
        {
            if (owner && from && to) Bank(owner).Launch(spec, tipPart, from, from.position, to, to.position, layer);
        }

        internal static void Clear(GameObject owner)
        {
            var bank = owner ? owner.GetComponent<JakShockBank>() : null;
            if (bank) bank.Clear();
        }

        internal static void Retire(GameObject owner)
        {
            var bank = owner ? owner.GetComponent<JakShockBank>() : null;
            if (bank) bank.Retire();
            else if (owner) Object.Destroy(owner);
        }

        // RoR2 victims have no native node list; a random skinned bone stands in for the random joint.
        internal static Transform VictimJoint(HurtBox victim)
        {
            if (!victim || !victim.healthComponent) return null;
            CharacterBody body = victim.healthComponent.body;
            Transform root = body && body.modelLocator ? body.modelLocator.modelTransform : victim.healthComponent.transform;
            if (!root) return null;
            var bones = root.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(r => r.bones).Where(b => b).Distinct().ToArray();
            if (bones.Length != 0) return bones[Random.Range(0, bones.Length)];
            var hurtboxes = victim.healthComponent.GetComponentsInChildren<HurtBox>();
            return hurtboxes.Length == 0 ? victim.transform : hurtboxes[Random.Range(0, hurtboxes.Length)].transform;
        }

        private static readonly RaycastHit[] hits = new RaycastHit[32];
        private static readonly System.Random random = new System.Random();
        private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private static readonly Vector3[] directions = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left, Vector3.up, Vector3.down };

        // target-part.gc:3035-3075 and lightning-h.gc:261-273: six axes of a random ZYX rotation, last first,
        // 0.1 m spheres against backgnd/crate/obstacle/hit-by-others/pusher, ignoring the caster. Jak is
        // not in that collide set, so the owner is ignored too.
        private static bool Find(Vector3 origin, GameObject caster, GameObject owner, float range, out Vector3 end, out Vector3 normal)
        {
            Quaternion rotation = Quaternion.AngleAxis((float)random.NextDouble() * 360f, Vector3.forward) *
                Quaternion.AngleAxis((float)random.NextDouble() * 360f, Vector3.up) * Quaternion.AngleAxis((float)random.NextDouble() * 360f, Vector3.right);
            int mask = LayerIndex.world.mask | LayerIndex.CommonMasks.characterBodiesOrDefault;
            for (int i = directions.Length - 1; i >= 0; i--)
            {
                int count = Physics.SphereCastNonAlloc(origin, 0.1f, rotation * directions[i], hits, range, mask, QueryTriggerInteraction.Ignore);
                int nearest = -1;
                for (int h = 0; h < count; h++)
                {
                    Transform hit = hits[h].collider.transform;
                    if (hits[h].distance <= 0f || caster && hit.IsChildOf(caster.transform) || hit.IsChildOf(owner.transform)) continue;
                    if (nearest < 0 || hits[h].distance < hits[nearest].distance) nearest = h;
                }
                if (nearest < 0 || !S.AcceptProbe(true, Vector3.Distance(origin, hits[nearest].point), range)) continue;
                end = hits[nearest].point; normal = hits[nearest].normal;
                return true;
            }
            end = normal = default;
            return false;
        }

        internal static Material Material(string texture, int points = 0)
        {
            string key = texture + ":" + points;
            if (!materials.TryGetValue(key, out Material material) || !material)
            {
                material = new Material(JakEffects.Assets.lightningShader) { name = "JakShock_" + texture };
                material.SetTexture("_MainTex", JakEffects.Material(texture).GetTexture("_MainTex"));
                materials[key] = material;
            }
            return material;
        }

        private static JakShockBank Bank(GameObject owner) => owner.GetComponent<JakShockBank>() ?? owner.AddComponent<JakShockBank>();
    }

    internal sealed class JakShockBank : MonoBehaviour
    {
        private sealed class Shock
        {
            internal S State;
            internal bool Live;
            internal Transform From, To;
            internal bool TracksFrom, TracksTo;
            internal Vector3 FromPoint, ToPoint;
            internal int Layer, MeshPoints;
            internal Material Material;
            internal Mesh Mesh;
            internal JakAudio.Voice Voice;
            internal readonly Vector3[] Vertices = new Vector3[JakWeaponEffectContract.MaxShockPoints * 4];
            internal readonly Color32[] Colors = new Color32[JakWeaponEffectContract.MaxShockPoints * 4];
        }
        // Lightning tip voices fade over generic-obs.gc:1540-1552's auto-time.
        internal const float VoiceFade = 0.24f;
        private static readonly Dictionary<int, (int[] Triangles, Vector2[] Uv, Vector2[] Ends)> topologies = new Dictionary<int, (int[], Vector2[], Vector2[])>();
        private readonly Shock[] shocks = new Shock[S.OwnerLimit];
        private readonly bool[] occupied = new bool[S.OwnerLimit];
        private readonly float[] ages = new float[S.OwnerLimit];
        private readonly System.Random random = new System.Random();
        private JakEffectPlayer tips;
        private bool retiring;

        internal void Retire() => retiring = true;

        private static S.V V(Vector3 v) => new S.V(v.x, v.y, v.z);
        private static Vector3 V(S.V v) => new Vector3(v.X, v.Y, v.Z);
        private void OnEnable() => Camera.onPreCull += Draw;

        internal void Launch(JakShockSpec spec, int tipPart, Transform from, Vector3 fromPoint, Transform to, Vector3 toPoint, int layer)
            => Launch(spec, tipPart, from, fromPoint, to, toPoint, layer, toPoint, Quaternion.identity);

        internal void Launch(JakShockSpec spec, int tipPart, Transform from, Vector3 fromPoint, Transform to, Vector3 toPoint, int layer, Vector3 tip, Quaternion tipRotation)
        {
            if (JakEffects.Assets == null) return;
            for (int i = 0; i < shocks.Length; i++)
            {
                occupied[i] = shocks[i] != null && shocks[i].Live;
                ages[i] = occupied[i] ? shocks[i].State.Age : 0f;
            }
            int slot = S.Slot(occupied, ages);
            var shock = shocks[slot] ?? (shocks[slot] = new Shock { Mesh = new Mesh { name = "JakNativeShock" } });
            if (shock.Live) Finish(shock);
            shock.From = from; shock.To = to; shock.TracksFrom = from; shock.TracksTo = to;
            shock.FromPoint = fromPoint; shock.ToPoint = toPoint; shock.Layer = layer;
            if (shock.State == null) shock.State = new S(spec, random, V(fromPoint), V(toPoint));
            else shock.State.Restart(spec, V(fromPoint), V(toPoint));
            shock.Material = JakShock.Material(spec.texture, spec.numPoints);
            Topology(shock, spec.numPoints);
            shock.Live = true;
            // target-part.gc:2969-2999, 3092-3106, 3155-3177: the sampled duration replaces the tip timer.
            float timer = shock.State.Duration / (float)JakEffectsFormat.ClockHz;
            if (!tips) tips = JakEffectPlayer.CreateLauncher(layer);
            tips.gameObject.layer = layer;
            tips.Launch(tipPart, tip, tipRotation, timer);
            if (from && to) tips.Launch(tipPart, fromPoint, Quaternion.identity, timer);
            // generic-obs.gc:1570-1590: the spec sound plays once at target1's joint or offset1, the tip.
            if (spec.sound != "") shock.Voice = JakAudio.PlayAt(spec.sound, toPoint);
        }

        private static void Topology(Shock shock, int points)
        {
            if (shock.MeshPoints == points) return;
            if (!topologies.TryGetValue(points, out var topology))
            {
                var uv = new Vector2[points * 4];
                var ends = new Vector2[points * 4];
                var triangles = new int[(points - 1) * 12];
                // lightning.gc:441-479, 566-605: two strips in camera right/up, t = (4096 / n) * i.
                for (int strip = 0; strip < 2; strip++)
                    for (int i = 0; i < points; i++)
                    {
                        int n = strip * points * 2 + i * 2;
                        uv[n] = new Vector2(0f, 4096 / points * i / 4096f);
                        uv[n + 1] = new Vector2(1f, uv[n].y);
                        // JakLightning's end fade, in fade lengths of 15% of the strip.
                        float along = i / (points - 1f);
                        ends[n] = ends[n + 1] = new Vector2(along / 0.15f, (1f - along) / 0.15f);
                        if (i == points - 1) continue;
                        int t = (strip * (points - 1) + i) * 6;
                        triangles[t] = n; triangles[t + 1] = n + 1; triangles[t + 2] = n + 2;
                        triangles[t + 3] = n + 1; triangles[t + 4] = n + 3; triangles[t + 5] = n + 2;
                    }
                topologies.Add(points, topology = (triangles, uv, ends));
            }
            shock.Mesh.Clear();
            shock.Mesh.SetVertices(shock.Vertices, 0, points * 4);
            shock.Mesh.SetUVs(0, topology.Uv);
            shock.Mesh.SetUVs(1, topology.Ends);
            shock.Mesh.SetTriangles(topology.Triangles, 0);
            shock.Mesh.MarkDynamic();
            shock.MeshPoints = points;
        }

        private void Update()
        {
            float ticks = Time.deltaTime * JakEffectsFormat.ClockHz;
            bool live = false;
            foreach (var shock in shocks)
            {
                if (shock == null || !shock.Live) continue;
                // generic-obs.gc:1460-1530: a tracked process that is gone deactivates the tracker.
                if (shock.TracksFrom && !shock.From || shock.TracksTo && !shock.To) { Finish(shock); continue; }
                if (shock.From) shock.FromPoint = shock.From.position;
                if (shock.To) shock.ToPoint = shock.To.position;
                shock.State.Advance(ticks, V(shock.FromPoint), V(shock.ToPoint));
                if (!shock.State.Alive) Finish(shock);
                else live = true;
            }
            // Ending the native victim process clears tint/mask, not its victim-owned trackers.
            if (retiring && !live && (!tips || !tips.HasParticles)) Destroy(gameObject);
        }

        private void Draw(Camera camera)
        {
            if (!camera) return;
            Vector3 right = camera.transform.right, up = camera.transform.up;
            foreach (var shock in shocks)
            {
                if (shock == null || !shock.Live || !shock.State.Drawn || (camera.cullingMask & (1 << shock.Layer)) == 0) continue;
                S state = shock.State;
                int points = state.Points;
                for (int strip = 0; strip < 2; strip++)
                {
                    Vector3 side = (strip == 0 ? right : up) * state.Spec.radius;
                    for (int i = 0; i < points; i++)
                    {
                        int n = strip * points * 2 + i * 2;
                        Vector3 point = V(state.Line[i]);
                        var c = state.ColorAt(i);
                        var color = new Color32(c.R, c.G, c.B, c.A);
                        shock.Vertices[n] = point - side; shock.Vertices[n + 1] = point + side;
                        shock.Colors[n] = color; shock.Colors[n + 1] = color;
                    }
                }
                shock.Mesh.SetVertices(shock.Vertices, 0, points * 4);
                shock.Mesh.SetColors(shock.Colors, 0, points * 4);
                shock.Mesh.RecalculateBounds();
                Graphics.DrawMesh(shock.Mesh, Matrix4x4.identity, shock.Material, shock.Layer, camera, 0, null, ShadowCastingMode.Off, false);
            }
        }

        // The tracker's active :exit (generic-obs.gc:1540-1552) fades its voice instead of cutting it.
        private void Finish(Shock shock)
        {
            shock.Live = false;
            shock.From = shock.To = null;
            JakAudio.Fade(shock.Voice, VoiceFade);
            shock.Voice = null;
        }

        internal void Clear()
        {
            foreach (var shock in shocks) if (shock != null && shock.Live) Finish(shock);
        }

        private void OnDisable() { Camera.onPreCull -= Draw; Clear(); }

        private void OnDestroy()
        {
            Clear();
            foreach (var shock in shocks) if (shock != null && shock.Mesh) Destroy(shock.Mesh);
            if (tips) tips.Release();
        }
    }
}
