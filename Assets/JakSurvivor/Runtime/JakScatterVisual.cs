using System.Collections.Generic;
using System.Linq;
using RoR2;
using UnityEngine;

namespace JakSurvivor
{
    // Presentation only: decorative rays never participate in damage or item procs.
    internal sealed class JakScatterVisual
    {
        private readonly Vector3 origin, contact;
        private readonly bool impact;
        private readonly float size;
        private readonly Vector3[] endpoints;
        private readonly Dictionary<Camera, JakParticleMesh> meshes = new Dictionary<Camera, JakParticleMesh>();
        private readonly List<Camera> deadCameras = new List<Camera>();
        internal Vector3 Origin => origin;
        internal Vector3 Contact => contact;
        internal IReadOnlyList<Vector3> Endpoints => endpoints;

        internal JakScatterVisual(Vector3 origin, Quaternion rotation, float size, bool impact, Vector3 start, bool blocked)
        {
            this.origin = origin;
            this.size = size;
            this.impact = impact;
            contact = origin;
            if (impact)
            {
                this.origin = start;
                endpoints = new[] { contact };
                return;
            }
            endpoints = new Vector3[9];
            bool covered = blocked || Physics.CheckSphere(origin, 0.02f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
            // A stable horizontal fan with a little height; no random holes or camera-dependent cone.
            for (int i = 0; i < endpoints.Length; i++)
            {
                float yaw = Mathf.Lerp(-42f, 42f, i / (float)(endpoints.Length - 1));
                float pitch = (i % 3 - 1) * 7f;
                Vector3 direction = rotation * (Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward);
                float distance = ScatterShot.Range * size;
                if (Physics.Raycast(origin, direction, out RaycastHit hit, distance, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                    distance = hit.distance;
                // A muzzle inside terrain must not paint a fan on the far side of the wall.
                if (covered) distance = 0f;
                endpoints[i] = origin + direction * distance;
            }
        }

        internal void Draw(Camera camera, int layer, float age)
        {
            deadCameras.Clear();
            foreach (var pair in meshes) if (!pair.Key) { pair.Value.Dispose(); deadCameras.Add(pair.Key); }
            foreach (var dead in deadCameras) meshes.Remove(dead);
            if (!meshes.TryGetValue(camera, out JakParticleMesh mesh)) meshes.Add(camera, mesh = new JakParticleMesh());
            mesh.Clear();
            Material material = JakEffects.Material("jak3-vfx-hotdot.png", true);
            // A radial dot stretched along a 20m ray only lights its midpoint; the Blaster's
            // source beam texture stays bright along its length.
            Material streak = JakEffects.Material(JakEffects.Data.particles.First(p => p.id == 264).texture, true);
            Vector3 right = camera.transform.right, up = camera.transform.up;
            float fade = JakScatterBurst.Fade(age, JakScatterBurst.Duration);
            Color red = new Color(1.6f, 0.19f, 0.025f, fade);
            foreach (Vector3 endpoint in endpoints)
            {
                Vector3 delta = endpoint - origin;
                float distance = delta.magnitude;
                if (distance < 0.001f) continue;
                Vector3 direction = delta / distance;
                Vector3 tail = origin + direction * JakScatterBurst.Tail(distance, age);
                Vector3 center = (tail + endpoint) * 0.5f;
                Vector3 side = Vector3.Cross(direction, camera.transform.position - center).normalized;
                if (side.sqrMagnitude < 0.001f) side = right;
                if (age < JakScatterBurst.TrailDuration)
                {
                    mesh.Quad(streak, center, side * (0.12f * size), (endpoint - tail) * 0.5f, red);
                    mesh.Quad(streak, center, side * (0.04f * size), (endpoint - tail) * 0.5f,
                        new Color(2f, 0.85f, 0.3f, fade));
                }
            }
            if (impact)
            {
                float radius = (0.2f + (1f - fade) * 0.3f) * size;
                mesh.Quad(material, contact, right * radius, up * radius, new Color(2f, 0.5f, 0.08f, fade));
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * Mathf.PI / 3f;
                    Vector3 direction = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                    mesh.Quad(material, contact + direction * radius * 1.6f, Vector3.Cross(camera.transform.forward, direction) * (0.04f * size),
                        direction * radius * 0.7f, red);
                }
            }
            else
            {
                float flash = JakScatterBurst.Fade(age, JakScatterBurst.FlashDuration);
                float radius = (0.25f + flash * 0.45f) * size;
                mesh.Quad(material, origin, right * radius, up * radius, new Color(2f, 0.6f, 0.12f, flash));
            }
            mesh.Draw(camera, layer);
        }

        internal void Dispose()
        {
            foreach (var mesh in meshes.Values) mesh.Dispose();
            meshes.Clear();
        }
    }
}
