using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Rendering;

namespace JakSurvivor
{
    internal static class JakEffects
    {
        internal sealed class Snapshot
        {
            public readonly JakParticleDefinition[] particles;
            public readonly JakParticleSimulation.Catalog simulation;
            public readonly JakParticleGroup[] groups;
            public readonly JakEffectModel.ModelFile[] models;

            public Snapshot(JakParticleDefinition[] particles, JakParticleGroup[] groups, JakEffectModel.ModelFile[] models)
            {
                this.particles = particles; this.groups = groups; this.models = models;
                simulation = new JakParticleSimulation.Catalog(particles);
            }
        }

        internal readonly struct Definition
        {
            public readonly string Name, PrefabName, Mode, Group, Sound;
            public readonly int Direct;
            public readonly float Scale;
            public bool FollowsMuzzle => Mode == "muzzle" || Mode == "arc-muzzle" || Mode == "wave-charge";

            public Definition(string name, string prefabName, string mode, string group = "", int direct = 0, string sound = "", float scale = 1f)
            {
                Name = name; PrefabName = prefabName; Mode = mode; Group = group; Direct = direct; Sound = sound; Scale = scale;
            }
        }

        internal static readonly Definition[] Definitions =
        {
            new Definition("blast-charge", "Jak3BlastCharge", "blast-charge", "group-darkjak-smack-charge"),
            new Definition("blast-core", "Jak3BlastCore", "blast-core", "group-darkjak-hadouken"),
            new Definition("blast-trail", "Jak3BlastTrail", "blast-trail", "group-darkjak-hadouken-trail"),
            new Definition("blast-hit", "Jak3BlastHit", "impact", "group-darkjak-smack-hit", sound: "blast-hit"),
            new Definition("blast-wall", "Jak3BlastWall", "impact", "group-darkjak-smack-wall-explode", sound: "blast-wall"),
            new Definition("bomb", "Jak3DarkBomb", "bomb", "group-darkjak-bomb", sound: "bomb-impact"),
            new Definition("transform", "Jak3Transform", "transform", "group-darkjak-transform"),
            new Definition("muzzle", "Jak3BlasterMuzzle", "muzzle", direct: 268),
            new Definition("scatter", "Jak3ScatterMuzzle", "scatter"),
            new Definition("scatter-hit", "JakScatterHit", "scatter-hit"),
            new Definition("tracer", "Jak3BlasterTracer", "tracer"),
            // Blaster impacts draw at half the source size, which reads too large in RoR2.
            new Definition("hit", "Jak3BlasterHit", "impact", "group-gun-yellow-shot-hit-object", scale: 0.5f),
            // target-handler.gc:466-502: gun spin contacts share this group and sound.
            new Definition("spin-hit", "Jak3SpinHit", "impact", "group-spin-hit", sound: "gun-spin-hit"),
            new Definition("claw", "Jak3ClawHit", "impact", "group-punch-hit"),
            new Definition("terrain", "Jak3BlasterTerrainHit", "impact", "group-gun-yellow-shot-hit", scale: 0.5f),
            new Definition("arc-muzzle", "Jak3ArcMuzzle", "arc-muzzle", "group-gun-blue-2-muzzle-flare"),
            new Definition("wave-charge", "Jak3WaveCharge", "wave-charge", "group-red-2-charge"),
            new Definition("wave-burst", "Jak3WaveBurst", "wave-burst", direct: 353),
            new Definition("wave-ring", "Jak3WaveRing", "wave-ring", direct: 229)
        };

        internal static Snapshot Data { get; private set; }
        internal static JakEffectsAsset Assets { get; private set; }
        private static Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

        internal static GameObject Get(string name) => TryGet(name, out GameObject prefab) ? prefab
            : throw new ArgumentException("Unknown or unloaded Jak effect: " + name);
        internal static bool TryGet(string name, out GameObject prefab) => prefabs.TryGetValue(name, out prefab);

        internal static Dictionary<string, GameObject> ResolvePrefabs(GameObject[] assets)
        {
            if (assets == null || assets.Length != Definitions.Length || assets.Any(p => !p) ||
                assets.Select(p => p.name).Distinct().Count() != assets.Length)
                throw new InvalidDataException("Jak requires one uniquely named prefab for each of its " + Definitions.Length + " effects.");
            var byName = assets.ToDictionary(p => p.name, StringComparer.Ordinal);
            var resolved = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            foreach (Definition definition in Definitions)
            {
                if (!byName.TryGetValue(definition.PrefabName, out GameObject prefab))
                    throw new InvalidDataException("Missing Jak effect prefab: " + definition.PrefabName);
                var player = prefab.GetComponent<JakEffectPlayer>();
                var component = prefab.GetComponent<EffectComponent>();
                var attributes = prefab.GetComponent<VFXAttributes>();
                if (!player || player.Mode != definition.Mode || player.GroupName != definition.Group || player.Direct != definition.Direct || player.Sound != definition.Sound ||
                    !component || component.applyScale || component.positionAtReferencedTransform != definition.FollowsMuzzle ||
                    component.parentToReferencedTransform != definition.FollowsMuzzle || !attributes || attributes.DoNotPool)
                    throw new InvalidDataException("Invalid Jak effect configuration: " + definition.PrefabName);
                resolved.Add(definition.Name, prefab);
            }
            return resolved;
        }
        public static void LoadData()
        {
            Assets = JakAssets.Get<JakEffectsAsset>("JakEffectsData");
            ValidateAssets(Assets);
            Data = new Snapshot(Assets.particles, Assets.groups, Assets.models);
        }

        internal static void ValidateAssets(JakEffectsAsset asset)
        {
            if (!asset || !asset.quad || !asset.sourcePayload || !asset.provenance || asset.materials == null || asset.models == null)
                throw new InvalidDataException("Missing Jak effects/provenance resources.");
            if (!asset.particleShader || asset.particleShader.name != "JakSurvivor/Particle")
                throw new InvalidDataException("Particle shader missing from effects bundle.");
            if (!asset.lightningShader || asset.lightningShader.name != "JakSurvivor/Lightning")
                throw new InvalidDataException("Lightning shader missing from effects bundle.");
            JakWeaponEffectContract.ValidateBlast(asset.blast);
            JakWeaponEffectContract.Validate(asset.particles, asset.groups, asset.lightning, asset.shockSpecs, asset.models.Select(m => m.name).ToArray());
            foreach (var spec in asset.shockSpecs)
                if (spec.sound != "" && Array.IndexOf(JakAudio.Keys, spec.sound) < 0)
                    throw new InvalidDataException("Lightning spec sound is not a recovered Jak sound: " + spec.name);
            ResolvePrefabs(asset.prefabs);
            foreach (string texture in asset.particles.SelectMany(p => new[] { p.texture }.Concat(p.textureFrames))
                .Concat(new[] { asset.blast.texture }).Concat(asset.lightning.Select(a => a.texture)).Concat(asset.shockSpecs.Select(s => s.texture)).Where(t => !string.IsNullOrEmpty(t)).Distinct())
            {
                var material = asset.materials.SingleOrDefault(m => m.texture == texture);
                if (material == null || !material.additive || !material.alpha || !material.additive.mainTexture || !material.alpha.mainTexture ||
                    material.additive.shader != asset.particleShader || material.alpha.shader != asset.particleShader)
                    throw new InvalidDataException("Missing required source effect texture/material " + texture);
            }
            foreach (var model in asset.models)
                if (!model.prefab || model.animations == null || !model.animations.Any(a => a.duration > 0f && a.name.EndsWith("idle", StringComparison.Ordinal)))
                    throw new InvalidDataException("Missing animated source effect mesh " + model.name);
        }

        internal static void ReloadParticles(JakParticleDefinition[] particles, JakParticleGroup[] groups)
        {
            if (Data == null) throw new InvalidOperationException("Extracted effects are not loaded.");
            JakParticleSimulation.Validate(particles, groups);
            if (!particles.Select(p => p.id).OrderBy(i => i).SequenceEqual(Data.particles.Select(p => p.id).OrderBy(i => i)) ||
                !groups.Select(g => g.name + "/" + g.id).OrderBy(n => n).SequenceEqual(Data.groups.Select(g => g.name + "/" + g.id).OrderBy(n => n)))
                throw new InvalidDataException("Live tuning must retain the registered particle/group identities.");
            JakWeaponEffectContract.Validate(particles, groups, Assets.lightning, Assets.shockSpecs, Data.models.Select(m => m.name).ToArray());
            var textures = new HashSet<string>(Data.particles.SelectMany(p => new[] { p.texture }.Concat(p.textureFrames)));
            if (particles.Any(p => !textures.Contains(p.texture) || p.textureFrames.Any(t => !textures.Contains(t)))) throw new InvalidDataException("Live tuning uses the bundled texture set.");
            // Existing players own the old definitions; future replays use this snapshot.
            // Animated mesh caches and catalog prefabs remain valid across particle edits.
            Data = new Snapshot(particles, groups, Data.models);
        }

        public static GameObject[] Build()
        {
            prefabs = ResolvePrefabs(Assets.prefabs).ToDictionary(pair => pair.Key,
                pair => pair.Value.InstantiateClone(pair.Value.name, false), StringComparer.OrdinalIgnoreCase);
            // Catalog registration is deterministic across peers, independent of bundle and dictionary order.
            return Definitions.Select(definition => prefabs[definition.Name]).ToArray();
        }
        internal static JakShockSpec ShockSpec(string name) => Array.Find(Assets.shockSpecs, s => s.name == name)
            ?? throw new ArgumentException("Unknown lightning spec: " + name);

        internal static Material Material(string texture, bool additive = true) {
            var item = Assets.materials.First(m => m.texture == texture);
            return additive ? item.additive : item.alpha;
        }

        private static readonly Dictionary<string, Material> glowMaterials = new Dictionary<string, Material>();
        // Glow sprites are additive and skip the depth test; the shader scales them by a depth probe.
        private const string GlowProbeKeyword = "JAK_GLOW_PROBE";
        internal static Material GlowMaterial(string texture)
        {
            if (glowMaterials.TryGetValue(texture, out Material material) && material) return material;
            material = new Material(Material(texture)) { name = Material(texture).name + "_glow" };
            material.SetFloat("_ZTest", (float)CompareFunction.Always);
            material.EnableKeyword(GlowProbeKeyword);
            return glowMaterials[texture] = material;
        }
    }

}
