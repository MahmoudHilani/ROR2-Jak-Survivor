using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Rendering;
using F = JakSurvivor.JakParticleSimulation.Field;

namespace JakSurvivor
{
    [DefaultExecutionOrder(10000)]
    internal sealed class JakEffectPlayer : MonoBehaviour
    {
        internal const float WaveVisualDuration = 1.1f;
        private const string LauncherMode = "launcher";
        private static readonly JakParticleGroup LauncherGroup = new JakParticleGroup { duration = 0f, linger = 1f };
        public string Mode = "", GroupName = "", Sound = "";
        public int Direct;
        private JakParticleSimulation simulation;
        private JakEffects.Snapshot snapshot;
        private bool initialized, returning, leased;
        private uint leaseId;
        internal uint LeaseId => leaseId;
        internal bool IsLease(uint id) => leaseId == id && !returning;
        private int actorCursor;
        private readonly List<JakEffectModel.JakEffectActor> actors = new List<JakEffectModel.JakEffectActor>();
        private readonly Dictionary<string, Material> batches = new Dictionary<string, Material>();
        private readonly Dictionary<Camera, JakParticleMesh> cameraMeshes = new Dictionary<Camera, JakParticleMesh>();
        private readonly List<Camera> deadCameras = new List<Camera>();
        private bool alphaParticles;
        private MaterialPropertyBlock particleTint;
        private MaterialPropertyBlock tint;
        private float age, lifetime, visualScale = 1f, travel;
        private Vector3 beamStart, beamDirection;
        private string beam;
        private JakParticleSimulation.Particle beamStyle;
        private CharacterBody owner;
        internal bool UseUnscaledTime = false;
        // Short muzzle flashes retain the fired ray, including spin volley headings.
        internal Vector3 ShotDirection;
        // Menu bursts can shrink an effect; gameplay spawns keep the source size.
        internal float Size = 1f;
        internal bool Driven;
        internal bool HasParticles => simulation != null && simulation.Particles.Count != 0;
        internal void Retire(float tail) { Driven = false; lifetime = age + Mathf.Max(0f, tail); }
        private readonly HashSet<int> launchedParts = new HashSet<int>();
        internal float Charge, Radius = 3f, WaveElapsed;
        internal bool HasGround;
        internal Vector3 GroundPosition, GroundNormal = Vector3.up;
        private JakParticleSimulation.Particle ringStyle;
        private float sampledWaveTime;
        internal JakScatterVisual ScatterVisual { get; private set; }

        internal void SampleWave(float radius, float elapsed, bool grounded, Vector3 groundPosition, Vector3 groundNormal)
        {
            Driven = true;
            Radius = Mathf.Clamp(radius, 0f, 18f);
            WaveElapsed = Mathf.Max(0f, elapsed);
            HasGround = grounded; GroundPosition = groundPosition; GroundNormal = groundNormal;
            // gun-red-shot.gc:985-1000,1157: shockwave particles launch from the probed ground, not the muzzle.
            // The ring sprite lies in the same ground plane, so both mark one radius.
            if (Mode == "wave-burst" && grounded)
                transform.SetPositionAndRotation(groundPosition, Quaternion.FromToRotation(Vector3.up, groundNormal));
            if (initialized && simulation != null && Mode == "wave-burst")
            {
                simulation.WaveRadius = Radius;
                simulation.Advance(Mathf.Max(0f, WaveElapsed - sampledWaveTime));
                sampledWaveTime = WaveElapsed;
            }
        }

        // A long-lived world-space player for direct parts launched at arbitrary points, such as shock tips.
        internal static JakEffectPlayer CreateLauncher(int layer)
        {
            var player = new GameObject("JakParticleLauncher") { layer = layer }.AddComponent<JakEffectPlayer>();
            player.Mode = LauncherMode;
            player.Driven = true;
            player.InitializeLauncher();
            return player;
        }

        // sp-launch-particles-var with *launch-matrix*: the part's own fields, placed by a launch frame.
        // The source timer override (spt-timer initial value) replaces the sampled lifetime when positive.
        internal void Launch(int part, Vector3 point, Quaternion rotation, float timer = 0f)
        {
            InitializeLauncher();
            if (launchedParts.Add(part))
                EnsureBatches(new[] { part });
            int first = simulation.Particles.Count;
            simulation.Launch(part);
            for (int i = first; i < simulation.Particles.Count; i++)
            {
                var particle = simulation.Particles[i];
                Place(particle, F.X, F.Y, F.Z, rotation, point);
                Place(particle, F.VelX, F.VelY, F.VelZ, rotation, Vector3.zero);
                Place(particle, F.AccelX, F.AccelY, F.AccelZ, rotation, Vector3.zero);
                if (timer > 0f) particle.Lifetime = timer;
            }
        }

        private static void Place(JakParticleSimulation.Particle particle, F x, F y, F z, Quaternion rotation, Vector3 offset)
        {
            Vector3 value = rotation * new Vector3(particle.Get(x), particle.Get(y), particle.Get(z)) + offset;
            particle.Set(x, value.x); particle.Set(y, value.y); particle.Set(z, value.z);
        }

        private void InitializeLauncher()
        {
            if (initialized) return;
            PrepareSimulation(LauncherGroup);
            initialized = true;
        }

        // Local driven effects use the same pool as catalog effects. Configuration is
        // supplied before the next Update/Start initializes the simulation.
        internal static JakEffectPlayer Spawn(string name, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            GameObject prefab = JakEffects.Get(name);
            if (!EffectManager.UsePools)
            {
                var standalone = Instantiate(prefab, position, rotation, parent).GetComponent<JakEffectPlayer>();
                standalone.BeginLease();
                return standalone;
            }
            // RoR2's four-argument overload ignores its parent and writes local coordinates.
            EffectManagerHelper helper = EffectManager.GetPooledEffect(prefab, position, rotation);
            var player = helper.GetComponent<JakEffectPlayer>();
            var component = helper.GetComponent<EffectComponent>();
            if (component) component.effectData = null;
            player.transform.SetParent(parent, true);
            player.BeginLease();
            helper.Reset(true);
            helper.UnparentOnReturnToPool = parent;
            player.transform.localScale = prefab.transform.localScale;
            player.gameObject.layer = prefab.layer;
            player.gameObject.SetActive(true);
            helper.ResetPostActivate();
            return player;
        }
        internal static JakEffectPlayer Spawn(string name, Transform parent) =>
            Spawn(name, parent.position, parent.rotation, parent);

        private void Awake()
        {
            var component = GetComponent<EffectComponent>();
            if (component)
            {
                component.OnEffectComponentReset -= EffectReset;
                component.OnEffectComponentReset += EffectReset;
            }
        }
        private void EffectReset(bool hasData)
        {
            // Native catalog spawns assign EffectData and reset the helper BEFORE
            // activation. Internal pool reactivation is not a new effect lease.
            if (hasData && !initialized) BeginLease();
        }
        private void BeginLease() { unchecked { leaseId++; } returning = false; leased = true; }
        private void OnEnable()
        {
            if (returning) return;
            initialized = false;
            var component = GetComponent<EffectComponent>();
            leased |= component && component.effectData != null;
        }
        private void Start() => EnsureStarted();
        private void EnsureStarted()
        {
            if (initialized || returning) return;
            if (Mode == LauncherMode) InitializeLauncher();
            else InitializeEffect();
        }
        private void PrepareSimulation(JakParticleGroup group)
        {
            if (!leased) BeginLease();
            if (particleTint == null) particleTint = new MaterialPropertyBlock();
            if (tint == null) tint = new MaterialPropertyBlock();
            int seed = UnityEngine.Random.Range(1, int.MaxValue);
            if (simulation != null && simulation.Matches(JakEffects.Data.simulation, group)) simulation.Reset(seed);
            else simulation = new JakParticleSimulation(JakEffects.Data.simulation, group, seed);
            if (!ReferenceEquals(snapshot, JakEffects.Data))
            {
                batches.Clear(); launchedParts.Clear(); alphaParticles = false;
                if (snapshot != null && !ReferenceEquals(snapshot.models, JakEffects.Data.models))
                {
                    foreach (var actor in actors) if (actor.Root) Destroy(actor.Root.gameObject);
                    actors.Clear();
                }
                snapshot = JakEffects.Data;
            }
            Camera.onPreCull += DrawParticles;
        }

        private void InitializeEffect()
        {
            initialized = true;
            var component = GetComponent<EffectComponent>();
            if (component) component.Reset();
            var helper = GetComponent<EffectManagerHelper>();
            if (helper && transform.parent) helper.UnparentOnReturnToPool = true;
            // Snapshot-driven local effects bypass EffectManager's data assignment.
            EffectData effect = component && component.effectData != null ? component.effectData : new EffectData();
            if (Mode == "muzzle" || Mode == "arc-muzzle" || Mode == "wave-charge")
            {
                Transform muzzle = effect.ResolveChildLocatorTransformReference();
                owner = muzzle ? muzzle.GetComponentInParent<CharacterBody>() : GetComponentInParent<CharacterBody>();
            }
            JakParticleGroup group = string.IsNullOrEmpty(GroupName)
                ? LauncherGroup
                : JakEffects.Data.groups.Single(g => g.name == GroupName);
            PrepareSimulation(group);
            if (Direct != 0) simulation.Spawn(Direct);
            lifetime = group.duration + group.linger;
            // Impact sounds are fire-and-forget; they outlive the pooled effect.
            if (!string.IsNullOrEmpty(Sound)) JakAudio.PlayAt(Sound, transform.position);
            visualScale = Size * JakEffects.Definitions.First(d => d.Mode == Mode && d.Group == GroupName && d.Direct == Direct && d.Sound == Sound).Scale;
            if (Mode == "wave-ring")
            {
                // red-2-ring's generic-blast is the PS2 screen warp, which is omitted. Drawn as
                // geometry its shells sit at unrelated radii, so the shockwave sprite alone marks range.
                ringStyle = simulation.Spawn(229);
                lifetime = WaveVisualDuration;
            }
            else if (Mode == "wave-burst")
            {
                // Source generate-shockwave-particles emits 150 directions. Keep that bound.
                simulation.ClearParticles();
                for (int i = 0; i < 150; i++)
                {
                    var particle = simulation.Spawn(353);
                    float angle = i * Mathf.PI * 2f / 150f;
                    float speed = particle.Get(F.VelZ);
                    particle.Set(F.VelX, Mathf.Cos(angle) * speed);
                    particle.Set(F.VelZ, Mathf.Sin(angle) * speed);
                }
                simulation.WaveRadius = Radius;
                simulation.Advance(WaveElapsed);
                sampledWaveTime = WaveElapsed;
                lifetime = WaveVisualDuration;
            }
            else if (Mode == "bomb")
            {
                visualScale *= Mathf.Max(0.01f, effect.scale) / 30f;
                foreach (var model in JakEffects.Data.models.Where(m => !m.name.Contains("gun-red-cone"))) AddActor(model, new Vector3(visualScale, 1f, visualScale));
            }
            else if (Mode == "scatter" || Mode == "scatter-hit")
            {
                simulation.ClearParticles();
                ScatterVisual = new JakScatterVisual(transform.position, transform.rotation, visualScale, Mode == "scatter-hit", effect.start, effect.genericBool);
                lifetime = JakScatterBurst.Duration;
            }
            else if (Mode == "tracer")
            {
                Transform muzzle = effect.ResolveChildLocatorTransformReference();
                beamStart = muzzle ? muzzle.position : effect.start;
                Vector3 offset = effect.origin - beamStart;
                travel = offset.magnitude;
                beamDirection = travel > 0.001f ? offset / travel : Vector3.forward;
                beam = AddBatch(JakEffects.Data.particles.First(p => p.id == 264).texture);
                beamStyle = simulation.Spawn(264);
                // The bullet is hitscan, so the beam spans muzzle to impact at once and its
                // tail retracts into the impact, at the source beam speed but within 0.15 s.
                lifetime = Mathf.Min(travel / 200f, 0.15f);
            }
            if (Mode != "tracer" && batches.Count == 0)
            {
                var ids = new HashSet<int>(group.items.Select(i => i.part));
                if (Direct != 0) ids.Add(Direct);
                EnsureBatches(ids);
            }
        }

        private void AddActor(JakEffectModel.ModelFile model, Vector3 scale)
        {
            JakEffectModel.JakEffectActor actor;
            if (actorCursor < actors.Count) actor = actors[actorCursor];
            else
            {
                Transform created = Instantiate(model.prefab, transform, false).transform;
                actor = new JakEffectModel.JakEffectActor(model, created);
                actors.Add(actor);
            }
            actorCursor++;
            actor.Root.localPosition = Vector3.zero; actor.Root.localRotation = Quaternion.identity; actor.Root.localScale = scale;
            actor.Root.gameObject.SetActive(true);
            actor.SetClip(model.animations.First(a => a.name.EndsWith("idle", StringComparison.Ordinal)).name, false);
            actor.Sample(0f);
            tint.Clear(); tint.SetVector("_TintColor", Color.white);
            foreach (var renderer in actor.Renderers)
            { renderer.enabled = true; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.SetPropertyBlock(tint); }
            foreach (Transform child in actor.Root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = gameObject.layer;
        }

        private void EnsureBatches(IEnumerable<int> parts)
        {
            var ids = JakParticleSimulation.ReferencedIds(JakEffects.Data.particles, parts);
            foreach (var definition in JakEffects.Data.particles.Where(p => ids.Contains(p.id)))
                foreach (string texture in new[] { definition.texture }.Concat(definition.textureFrames))
                    if (!string.IsNullOrEmpty(texture) && !batches.ContainsKey(texture)) AddBatch(texture);
        }

        private string AddBatch(string texture)
        {
            bool blended = texture == "jak3-vfx-common-white.png" && (Mode == "scatter" || Mode.StartsWith("wave-", StringComparison.Ordinal));
            batches[texture] = JakEffects.Material(texture, !blended);
            alphaParticles |= blended;
            return texture;
        }

        private void DrawParticles(Camera camera)
        {
            if (!camera || (camera.cullingMask & (1 << gameObject.layer)) == 0) return;
            if (isActiveAndEnabled && ScatterVisual != null) { ScatterVisual.Draw(camera, gameObject.layer, age); return; }
            if (!isActiveAndEnabled || batches.Count == 0) return;
            if (!cameraMeshes.TryGetValue(camera, out JakParticleMesh meshes))
            {
                PruneCameras();
                cameraMeshes.Add(camera, meshes = new JakParticleMesh());
            }
            meshes.Clear();
            foreach (var batch in batches) Draw(batch.Key, camera, batch.Value, meshes);
            meshes.Draw(camera, gameObject.layer);
        }

        private void Update()
        {
            EnsureStarted();
            if (!initialized || returning || simulation == null) return;
            float delta = UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            age += delta;
            bool sustained = Driven && (Mode == "arc-muzzle" || Mode == "wave-charge" || Mode == "blast-charge" || Mode == "blast-core");
            if (!Driven && age >= lifetime) { Release(); return; }
            if (Mode != "tracer" && Mode != "wave-ring" && !(Driven && Mode == "wave-burst"))
                simulation.Advance(delta, sustained);
            foreach (var actor in actors)
            {
                actor.Sample(0f, sampleAt: age);
                float factor = Mode == "bomb" ? Mathf.Clamp01((0.5f - age) / (1f / 6f)) : Mathf.Clamp01(1f - age / lifetime);
                Color color = Mode == "bomb" ? Color.Lerp(Color.white, new Color(0.5f, 0f, 1f), Mathf.Clamp01((age - 1f / 6f) * 6f)) : Color.white;
                color = new Color(color.r * factor, color.g * factor, color.b * factor, 1f);
                tint.SetVector("_TintColor", color);
                foreach (var renderer in actor.Renderers) renderer.SetPropertyBlock(tint);
                actor.Root.gameObject.SetActive(age < actor.Duration(actor.ClipName));
            }
        }

        private void LateUpdate()
        {
            // GOAL's gun effects use the fire direction rather than the muzzle
            // joint's modeling axes. Follow RoR2's aim after the actor pose updates.
            if (ShotDirection.sqrMagnitude > 0.0001f)
                transform.rotation = Util.QuaternionSafeLookRotation(ShotDirection);
            else if (owner && owner.inputBank && owner.inputBank.aimDirection.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(owner.inputBank.aimDirection);
        }

        private void Draw(string texture, Camera camera, Material material, JakParticleMesh meshes)
        {
            if (simulation == null || !camera) return;
            Vector3 right = camera.transform.right, up = camera.transform.up;
            if (Mode == "tracer" && texture == beam && beamStyle != null)
            {
                float head = travel;
                float tail = lifetime > 0f ? travel * Mathf.Clamp01(age / lifetime) : travel;
                if (head > tail)
                {
                    Vector3 center = beamStart + beamDirection * ((head + tail) * 0.5f);
                    Vector3 side = Vector3.Cross(beamDirection, camera.transform.position - center).normalized;
                    Quad(center, side * (beamStyle.Get(F.ScaleX, 0.5f) * 0.5f), beamDirection * ((head - tail) * 0.5f),
                        new Color(beamStyle.Get(F.R, 128f) / 128f, beamStyle.Get(F.G, 128f) / 128f,
                            beamStyle.Get(F.B, 128f) / 128f, beamStyle.Get(F.A, 128f) / 128f), camera, material, meshes);
                }
            }
            else if (Mode == "wave-ring" && ringStyle != null && texture == ringStyle.Texture)
            {
                if (HasGround)
                {
                    Vector3 normal = GroundNormal.sqrMagnitude > 0.001f ? GroundNormal.normalized : Vector3.up;
                    Vector3 side = Vector3.Cross(normal, Vector3.forward).normalized;
                    if (side.sqrMagnitude < 0.001f) side = Vector3.right;
                    // gun-red-shot.gc:1409-1410: scale-x is the ring diameter; alpha scales source a=128.
                    Quad(GroundPosition + normal * 0.04f, side * Radius, Vector3.Cross(normal, side) * Radius,
                        new Color(ringStyle.Get(F.R, 128f) / 128f, ringStyle.Get(F.G, 128f) / 128f, ringStyle.Get(F.B, 128f) / 128f,
                            ringStyle.Get(F.A, 128f) / 128f * Mathf.Clamp01(1f - WaveElapsed / WaveVisualDuration)), camera, material, meshes);
                }
            }
            else foreach (var particle in simulation.Particles)
            {
                if (particle.Texture != texture) continue;
                float particleScale = visualScale;
                if (Mode == "wave-charge") particleScale *= Mathf.Lerp(0.2f, 0.65f, Mathf.Clamp01(Charge));
                if (particle.Definition.id == 627) continue; // Native screen distortion parameters are not additive RGB.
                Vector3 position = particle.Definition.behavior == "hadouken-track" ? transform.position :
                    transform.TransformPoint(new Vector3(particle.Get(F.X), particle.Get(F.Y), particle.Get(F.Z)) * particleScale);
                Vector3 horizontal = right, vertical = up;
                float stretch = 1f;
                bool beam = Mode == "muzzle";
                if (beam)
                {
                    vertical = transform.forward;
                    horizontal = Vector3.Cross(vertical, camera.transform.position - position).normalized;
                }
                else if (particle.Definition.behavior == "lightning-align" || particle.Definition.behavior == "spline-align")
                {
                    // sparticle-launcher.gc:2302-2332; gun-dark-shot.gc:111-115 adds the launcher's rot-z.
                    Vector3 velocity = transform.TransformDirection(new Vector3(particle.Get(F.VelX), particle.Get(F.VelY), particle.Get(F.VelZ)));
                    float angle = Mathf.Atan2(Vector3.Dot(velocity, up), -Vector3.Dot(velocity, right));
                    if (particle.Definition.behavior == "lightning-align") angle += particle.Get(F.RotZ) * Mathf.Deg2Rad;
                    horizontal = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                    vertical = up * Mathf.Cos(angle) - right * Mathf.Sin(angle);
                }
                else if (particle.Definition.motionBlur)
                {
                    Vector3 velocity = transform.TransformDirection(new Vector3(particle.Get(F.VelX), particle.Get(F.VelY), particle.Get(F.VelZ)));
                    if (velocity.sqrMagnitude > 1e-8f) { horizontal = velocity.normalized; vertical = Vector3.Cross(camera.transform.forward, horizontal).normalized; }
                    // sparticle-motion-blur rederives the streak length from velocity each frame, so
                    // launch scale-x shrinks with the spark as friction slows it.
                    float launch = particle.Get(F.LaunchSpeed);
                    if (launch > 0f) stretch = velocity.magnitude / launch;
                }
                else
                {
                    float angle = particle.Get(F.RotZ) * Mathf.Deg2Rad;
                    horizontal = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                    vertical = up * Mathf.Cos(angle) - right * Mathf.Sin(angle);
                }
                float width = Mathf.Max(0f, particle.Get(F.ScaleX, 1f)) * particleScale * stretch;
                float height = Mathf.Max(0f, particle.Get(F.ScaleY, 1f)) * particleScale;
                if (particle.Get(F.RotX) == 4f) position += horizontal * (width * 0.5f);
                // gun-util.gc:202-205 draw-beam: the flash spans scale-y ahead of the muzzle, not around it.
                if (beam) position += vertical * (height * 0.5f);
                Color color = new Color(particle.Get(F.R, 128f) / 128f, particle.Get(F.G, 128f) / 128f,
                    particle.Get(F.B, 128f) / 128f, particle.Get(F.A, 128f) / 128f);
                if (Mode == "wave-charge" && Charge >= 1f) color.a *= 0.85f + 0.15f * Mathf.Sin(age * Mathf.PI * 4f);
                if (Mode == "wave-burst") color.a *= Mathf.Clamp01(1f - WaveElapsed / WaveVisualDuration);
                Material particleMaterial = material;
                Vector4? probe = null;
                float glowDepth = 0f;
                // sparticle-launcher.gc:652 routes every glow-flagged part to sprite-glow, whatever its effect.
                if (particle.Definition.glow)
                {
                    Vector3 toCamera = camera.transform.position - position;
                    float distance = toCamera.magnitude;
                    // sprite-glow.gc:506-532 and Sprite3_Glow.cpp:48-69: depth fade, alpha into RGB, z offset.
                    float omega = particle.Get(F.Omega) * 65536f / 360f / 4096f;
                    float whole = Mathf.Floor(omega), fraction = omega - whole;
                    glowDepth = Vector3.Dot(position - camera.transform.position, camera.transform.forward);
                    float fade = omega == 0f ? 1f : Mathf.Clamp01((fraction - glowDepth / whole) / (1f - fraction));
                    color.r *= color.a * fade; color.g *= color.a * fade; color.b *= color.a * fade;
                    color.a = 1f;
                    if (distance > 0f) position += toCamera / distance * Mathf.Min(distance, particle.Get(F.Userdata) / 4096f);
                    particleMaterial = JakEffects.GlowMaterial(texture);
                    // Sprite3_Glow.cpp:100-122: the occlusion probe is centered on the offset position and
                    // rot-x, which glow parts do not otherwise use, is its half-size.
                    probe = new Vector4(position.x, position.y, position.z, particle.Get(F.RotX) * 65536f / 360f / 4096f);
                }
                Quad(position, horizontal * (width * 0.5f), vertical * (height * 0.5f), color, camera, particleMaterial, meshes, probe, glowDepth);
            }
        }
        private void Quad(Vector3 center, Vector3 right, Vector3 up, Color color, Camera camera, Material material, JakParticleMesh meshes,
            Vector4? probe = null, float glowDepth = 0f)
        {
            // Glow sprites are additive without a depth test, so batching them cannot change alpha sorting.
            if (!alphaParticles || probe.HasValue) { meshes.Quad(material, center, right, up, color, probe ?? default, glowDepth); return; }
            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.SetColumn(0, new Vector4(right.x, right.y, right.z, 0f));
            matrix.SetColumn(1, new Vector4(up.x, up.y, up.z, 0f));
            Vector3 normal = Vector3.Cross(right, up).normalized;
            matrix.SetColumn(2, new Vector4(normal.x, normal.y, normal.z, 0f));
            matrix.SetColumn(3, new Vector4(center.x, center.y, center.z, 1f));
            // GS vertex color (128 = 1.0) is gamma-space and may exceed 1; SetColor would linearize it.
            particleTint.SetVector("_TintColor", color);
            Graphics.DrawMesh(JakEffects.Assets.quad, matrix, material, gameObject.layer, camera,
                0, particleTint, ShadowCastingMode.Off, false);
        }

        // Muzzle effects are parented to the gun, which is hidden on the board
        // and in Dark form. End them there instead of resuming when it returns.
        internal void Release()
        {
            if (returning) return;
            returning = true;
            ReturnToPool();
        }
        private void ReturnToPool()
        {
            var helper = GetComponent<EffectManagerHelper>();
            if (helper) helper.ReturnToPool();
            else Destroy(gameObject); // Explicitly instantiated diagnostics and standalone launchers.
        }
        // A pooled return during an ancestor's deactivation cannot unparent. The lease ends
        // now; the object returns after the hierarchy change unless something else claimed it.
        private void ReturnAfterHierarchyChange()
        {
            uint lease = leaseId;
            RoR2Application.onNextUpdate += () =>
            {
                if (this && leaseId == lease && returning && gameObject.activeSelf) ReturnToPool();
            };
        }
        // Owners may outlive parent-hidden retirement and the next use of this object.
        internal void Release(uint expectedLease) { if (IsLease(expectedLease)) Release(); }
        private void OnDisable()
        {
            bool wasLeased = leased || initialized;
            leased = false;
            ScatterVisual?.Dispose(); ScatterVisual = null;
            Camera.onPreCull -= DrawParticles;
            initialized = false;
            if (simulation != null) simulation.ClearParticles();
            age = lifetime = travel = sampledWaveTime = 0f; visualScale = Size = 1f;
            UseUnscaledTime = Driven = HasGround = false;
            Charge = WaveElapsed = 0f; Radius = 3f;
            GroundPosition = beamStart = beamDirection = Vector3.zero; GroundNormal = Vector3.up;
            ShotDirection = Vector3.zero;
            ringStyle = beamStyle = null; beam = null; owner = null;
            actorCursor = 0;
            foreach (var actor in actors) if (actor.Root) actor.Root.gameObject.SetActive(false);
            var component = GetComponent<EffectComponent>();
            if (component) component.effectData = null;
            PruneCameras();
            // Deactivation inherited from a hidden muzzle must retire the lease as well.
            if (!wasLeased || returning) return;
            returning = true;
            if (gameObject.activeSelf && GetComponent<EffectManagerHelper>()) ReturnAfterHierarchyChange();
            else ReturnToPool();
        }

        private void PruneCameras()
        {
            deadCameras.Clear();
            foreach (var pair in cameraMeshes) if (!pair.Key) { pair.Value.Dispose(); deadCameras.Add(pair.Key); }
            foreach (Camera camera in deadCameras) cameraMeshes.Remove(camera);
            deadCameras.Clear();
        }
        private void OnDestroy()
        {
            var component = GetComponent<EffectComponent>();
            if (component) component.OnEffectComponentReset -= EffectReset;
            ScatterVisual?.Dispose(); ScatterVisual = null;
            Camera.onPreCull -= DrawParticles;
            foreach (var meshes in cameraMeshes.Values) meshes.Dispose();
            cameraMeshes.Clear();
        }
    }

}
