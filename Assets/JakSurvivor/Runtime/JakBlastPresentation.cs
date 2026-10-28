using RoR2;
using UnityEngine;
using UnityEngine.Rendering;

namespace JakSurvivor
{
    [DefaultExecutionOrder(10010)]
    internal sealed class JakBlastPresentation : MonoBehaviour
    {
        private JakBlastProjectile shot;
        private JakBlastAppearance style;
        private readonly JakEffectPlayer[] lobes = new JakEffectPlayer[2];
        private readonly uint[] lobeLeases = new uint[2];
        private readonly Mesh[] strips = new Mesh[6];
        private readonly Vector3[][] points = { new Vector3[17], new Vector3[17], new Vector3[17] };
        private readonly Vector3[][] current = { new Vector3[17], new Vector3[17], new Vector3[17] };
        private readonly Vector3[][] desired = { new Vector3[17], new Vector3[17], new Vector3[17] };
        private readonly Vector3[] vertices = new Vector3[34];
        private readonly Vector2[] uv = new Vector2[34];
        private readonly Color32[] colors = new Color32[34];
        private readonly int[] triangles = new int[96];
        private JakEffectPlayer trail;
        private JakAudio.Voice loop;
        private float retired = -1f, nextBolt, generated, emission, elapsed;
        private bool sampled, trailed;
        private Vector3 trailFrom;
        internal void Begin(JakBlastProjectile projectile)
        {
            shot = projectile; style = JakEffects.Assets.blast;
            transform.position = projectile.transform.position;
            for (int i = 0; i < lobes.Length; i++)
            {
                lobes[i] = JakEffectPlayer.Spawn("blast-core", transform);
                lobeLeases[i] = lobes[i].LeaseId;
                lobes[i].Driven = true;
            }
            trail = JakEffectPlayer.CreateLauncher(gameObject.layer);
            loop = JakAudio.Play("blast-flight", gameObject);
            for (int i = 0; i < 16; i++)
            {
                int t = i * 6, v = i * 2;
                triangles[t] = v; triangles[t+1] = v+1; triangles[t+2] = v+2;
                triangles[t+3] = v+1; triangles[t+4] = v+3; triangles[t+5] = v+2;
            }
            for (int i = 0; i < strips.Length; i++)
            {
                strips[i] = new Mesh { name = "DarkBlastNativeBolt" + i };
                strips[i].MarkDynamic();
            }
            Camera.onPreCull += Draw;
        }
        private void Midpoint(Vector3[] offsets, int first, int last, float scale)
        {
            if (last-first <= 1) return;
            int mid = (first+last)/2;
            offsets[mid] = (offsets[first]+offsets[last])*.5f + Random.insideUnitSphere*scale;
            Midpoint(offsets, first, mid, scale*style.reduction);
            Midpoint(offsets, mid, last, scale*style.reduction);
        }
        private void LateUpdate()
        {
            if (retired >= 0f)
            {
                if (Time.time-retired >= style.fade) Destroy(gameObject);
                return;
            }
            if (!shot) { Retire(); return; }
            for (int i = 0; i < lobes.Length; i++)
                if (!lobes[i] || !lobes[i].IsLease(lobeLeases[i])) { Retire(); return; }
            transform.position = shot.transform.position;
            float phase = Run.FixedTimeStamp.now.t * (Mathf.PI*2f/.75f);
            Vector3 offset = new Vector3(Mathf.Cos(phase), 0f, -Mathf.Sin(phase))*(.75f*shot.Radius);
            lobes[0].transform.position = transform.position+offset;
            lobes[1].transform.position = transform.position-offset;
            float previous = elapsed; elapsed += Time.deltaTime;
            bool regenerate = !sampled || elapsed >= nextBolt;
            if (regenerate)
            {
                generated = previous;
                nextBolt = elapsed+Random.Range(style.regenerateMin, style.regenerateMax);
                for (int b = 0; b < 3; b++)
                {
                    System.Array.Copy(current[b], desired[b], 17);
                    desired[b][0] = Random.insideUnitSphere*style.endpointOffset;
                    desired[b][15] = Random.insideUnitSphere*style.endpointOffset;
                    Midpoint(desired[b], 0, 15, style.innerOffset);
                    desired[b][16] = Vector3.zero; // The second native span has no inner points.
                }
            }
            float old = Mathf.Clamp01((previous-generated)/style.travel), next = Mathf.Clamp01((elapsed-generated)/style.travel);
            float fraction = old < 1f ? (next-old)/(1f-old) : 0f;
            for (int b = 0; b < 3; b++)
                for (int i = 0; i < 17; i++)
                {
                    current[b][i] = !sampled ? desired[b][i] : Vector3.Lerp(current[b][i], desired[b][i], fraction);
                    points[b][i] = Vector3.Lerp(lobes[0].transform.position, lobes[1].transform.position, Mathf.Min(i,15)/15f)+current[b][i];
                }
            sampled = true;
            Vector3 ground = transform.position;
            if (Physics.Raycast(ground, Vector3.down, out RaycastHit hit, 30f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) ground.y = hit.point.y+.4f;
            Vector3 forward = Vector3.ProjectOnPlane(shot.transform.forward, Vector3.up).normalized;
            Quaternion heading = forward.sqrMagnitude > 0f ? Quaternion.LookRotation(forward) : Quaternion.identity;
            if (!trailed) { trailFrom = ground; trailed = true; }
            float carried = emission; emission += Time.deltaTime;
            for (int i = 0; emission >= JakBlast.TrailStep; i++, emission -= JakBlast.TrailStep)
                trail.Launch(628, Vector3.Lerp(trailFrom, ground, JakBlast.TrailFraction(carried, Time.deltaTime, i)), heading);
            trailFrom = ground;
        }
        private void Draw(Camera camera)
        {
            if (!sampled || !camera || (camera.cullingMask & (1 << gameObject.layer)) == 0) return;
            float fade = retired < 0f ? 1f : Mathf.Clamp01(1f-(Time.time-retired)/style.fade);
            for (int b = 0; b < 3; b++)
                for (int cross = 0; cross < 2; cross++)
                {
                    float distance = 0f;
                    for (int i = 0; i < 17; i++)
                    {
                        Vector3 center = points[b][i];
                        if (i > 0) distance += Vector3.Distance(center, points[b][i-1]);
                        Vector3 tangent = points[b][Mathf.Min(16,i+1)]-points[b][Mathf.Max(0,i-1)];
                        if (tangent.sqrMagnitude < .0001f) tangent = Vector3.right;
                        Vector3 side = Vector3.Cross(tangent.normalized, camera.transform.forward).normalized;
                        if (side.sqrMagnitude < .0001f) side = camera.transform.up;
                        if (cross != 0) side = Vector3.Cross(tangent.normalized, side).normalized;
                        side *= style.width*.5f;
                        vertices[i*2] = center-side; vertices[i*2+1] = center+side;
                        float v = distance/style.uvRepeat + Time.time/style.uvSpeed;
                        uv[i*2] = new Vector2(0,v); uv[i*2+1] = new Vector2(1,v);
                        int c = b*4;
                        var color = new Color32((byte)style.colors[c], (byte)style.colors[c+1], (byte)style.colors[c+2],
                            (byte)(style.colors[c+3]*fade*Mathf.Clamp01(1f-distance/64f)));
                        colors[i*2] = colors[i*2+1] = color;
                    }
                    Mesh mesh = strips[b*2+cross];
                    mesh.vertices = vertices; mesh.uv = uv; mesh.colors32 = colors; mesh.triangles = triangles; mesh.RecalculateBounds();
                    Graphics.DrawMesh(mesh, Matrix4x4.identity, JakShock.Material(style.texture), gameObject.layer, camera, 0, null, ShadowCastingMode.Off, false);
                }
        }
        internal void Retire()
        {
            if (retired >= 0f) return;
            retired = Time.time; shot = null;
            ReleaseLobes();
            JakAudio.Stop(loop); loop = null;
            if (trail) { trail.Retire(4.1f); trail = null; }
        }
        private void OnDestroy()
        {
            Camera.onPreCull -= Draw;
            JakAudio.Stop(loop);
            ReleaseLobes();
            foreach (var strip in strips) if (strip) Destroy(strip);
            if (trail) trail.Release();
        }
        private void ReleaseLobes()
        {
            for (int i = 0; i < lobes.Length; i++)
            {
                if (lobes[i]) lobes[i].Release(lobeLeases[i]);
                lobes[i] = null; // A returned lobe can be leased by another projectile before our fade ends.
            }
        }
    }
}
