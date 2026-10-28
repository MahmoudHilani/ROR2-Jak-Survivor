using System;
using System.Linq;

namespace JakSurvivor
{
    [Serializable] internal sealed class JakLightningAppearance
    {
        public string name = "", texture = "";
        public int strands = 0, innerPoints = 0;
        public float jitter = 0f, widthMin = 0f, widthMax = 0f, regenerateMin = 0f, regenerateMax = 0f, uvRepeat = 0f, uvSpeed = 0f, fade = 0f;
    }

    // One native lightning-spec (lightning-h.gc:56-79). Times are 300 Hz ticks, lengths metres, colours GS bytes.
    [Serializable] internal sealed class JakShockSpec
    {
        public string name = "", texture = "", sound = "";
        public int[] startColor = Array.Empty<int>(), endColor = Array.Empty<int>(), fadeToColor = Array.Empty<int>();
        public float fadeStartFactor = 0f, fadeTime = 0f, reduction = 0f, boxSize = 0f, mergeFactor = 0f, radius = 0f, duration = 0f, durationRand = 0f, adjustDistance = 0f;
        public int numPoints = 0, mergeCount = 0;
        public bool distanceScaling = false;
    }

    [Serializable] internal sealed class JakBlastAppearance
    {
        public string texture = "";
        public int spans = 0, innerPoints = 0;
        public float travel = 0f, reduction = 0f, endpointOffset = 0f, innerOffset = 0f, width = 0f, uvRepeat = 0f, regenerateMin = 0f, regenerateMax = 0f, fade = 0f, uvSpeed = 0f;
        public int[] colors = Array.Empty<int>();
    }

    // Shared by source import, bundled load and offline checks. No count/order shortcuts.
    internal static class JakWeaponEffectContract
    {
        internal static readonly string[] RequiredGroups = {
            "group-darkjak-smack-charge", "group-darkjak-smack-hit", "group-darkjak-smack-wall-explode",
            "group-darkjak-hadouken", "group-darkjak-hadouken-trail", "group-spin-hit", "group-darkjak-bomb", "group-darkjak-transform", "group-punch-hit",
            "group-gun-red-shot-fired", "group-gun-yellow-shot-hit", "group-gun-yellow-shot-hit-object",
            "group-gun-blue-2-muzzle-flare", "group-red-2-charge", "group-gun-red-3-scorched-earth"
        };
        internal static readonly int[] WeaponParticles = { 624, 625, 626, 627, 628, 629, 226, 227, 228, 229, 353, 354, 355, 360, 361, 362, 363, 369, 370, 371, 372, 373, 374, 168, 664, 173, 187, 188 };
        internal static readonly string[] RequiredModels = { "vfx-collectables-bomb-blast", "vfx-collectables-generic-blast", "vfx-gun-red-cone" };
        internal static readonly string[] RequiredLightning = { "blue-light-test", "blue-light-test-big" };
        internal static readonly string[] RequiredShockSpecs = {
            "lightning-darkjak-transform", "lightning-darkjak-attack", "lightning-blue-shot-attack-explode", "lightning-blue-shot-terminal"
        };
        // Shock geometry is allocated per bank slot at this capacity.
        internal const int MaxShockPoints = 32;

        internal static void Validate(JakParticleDefinition[] particles, JakParticleGroup[] groups, JakLightningAppearance[] lightning, JakShockSpec[] shocks, string[] models)
        {
            JakParticleSimulation.Validate(particles, groups);
            ValidateShocks(shocks);
            if (!RequiredGroups.OrderBy(n => n).SequenceEqual(groups.Select(g => g.name).OrderBy(n => n)) ||
                !RequiredModels.OrderBy(n => n).SequenceEqual(models.OrderBy(n => n)) ||
                lightning == null || !RequiredLightning.OrderBy(n => n).SequenceEqual(lightning.Select(a => a.name).OrderBy(n => n)))
                throw new ArgumentException("Incomplete source-derived Jak effect groups, models or lightning appearances.");
            foreach (int id in WeaponParticles)
                if (!particles.Any(p => p.id == id)) throw new ArgumentException("Missing required Arc/Wave particle stage " + id);
            foreach (var particle in particles.Where(p => WeaponParticles.Contains(p.id)))
                foreach (int id in JakParticleSimulation.ReferencedIds(particles, new[] { particle.id }))
                    if (!particles.Any(p => p.id == id)) throw new ArgumentException("Missing required Arc/Wave particle dependency " + id);
            if (particles.Where(p => p.id >= 624 && p.id <= 627).Any(p => p.behavior != "hadouken-track"))
                throw new ArgumentException("Missing hadouken endpoint callbacks.");
            if (particles.Single(p => p.id == 369).behavior != "wave-converge" || particles.Single(p => p.id == 370).behavior != "wave-converge" ||
                particles.Single(p => p.id == 353).behavior != "wave-trail" || particles.Single(p => p.id == 362).textureFrames.Length != 3 ||
                particles.Single(p => p.id == 664).textureFrames.Length != 3 || !particles.Single(p => p.id == 360).glow ||
                !particles.Single(p => p.id == 187).glow || !particles.Single(p => p.id == 173).motionBlur)
                throw new ArgumentException("Missing source callback/texture-group adaptations.");
            if (lightning.Sum(a => a.strands) > 6)
                throw new ArgumentException("Arc appearances exceed the six-strand renderer bound.");
            foreach (var a in lightning)
                if (string.IsNullOrEmpty(a.texture) || a.strands < 1 || a.strands > 6 || a.innerPoints < 1 || a.innerPoints > 14 ||
                    !Positive(a.widthMin) || !Positive(a.widthMax) || a.widthMax < a.widthMin || !Positive(a.regenerateMin) ||
                    !Positive(a.regenerateMax) || a.regenerateMax < a.regenerateMin || !Positive(a.uvRepeat) || !Positive(a.fade) ||
                    !Positive(a.jitter) || float.IsNaN(a.uvSpeed) || float.IsInfinity(a.uvSpeed))
                    throw new ArgumentException("Invalid or unbounded source lightning appearance: " + a.name);
        }
        internal static void ValidateBlast(JakBlastAppearance b)
        {
            if (b == null || b.texture != "jak3-vfx-pal-lightning.png" || b.spans != 2 || b.innerPoints != 14 ||
                b.travel != .085f || b.reduction != .7f || b.endpointOffset != .5f || b.innerOffset != 2f || b.width != 3f || b.uvRepeat != 7f ||
                b.regenerateMin != .017f || b.regenerateMax != .05f || b.fade != .3f || b.uvSpeed != -.5f ||
                b.colors == null || !b.colors.SequenceEqual(new[] {255,32,128,128, 32,32,255,128, 255,255,255,128}))
                throw new ArgumentException("Incomplete native Dark Blast lightning appearance.");
        }
        private static void ValidateShocks(JakShockSpec[] shocks)
        {
            if (shocks == null || !RequiredShockSpecs.OrderBy(n => n).SequenceEqual(shocks.Select(s => s.name).OrderBy(n => n)))
                throw new ArgumentException("Incomplete source-derived lightning specs.");
            foreach (var s in shocks)
                if (string.IsNullOrEmpty(s.texture) || s.sound == null || !Colour(s.startColor) || !Colour(s.endColor) || !Colour(s.fadeToColor) ||
                    !Within(s.fadeStartFactor, 0f, 1f) || !Within(s.fadeTime, 0f, 300f) || !Positive(s.reduction) || s.reduction >= 1f ||
                    s.numPoints < 2 || s.numPoints > MaxShockPoints || !Positive(s.boxSize) || s.boxSize > 10f ||
                    !Positive(s.mergeFactor) || s.mergeFactor > 1f || s.mergeCount < 1 || s.mergeCount > 60 ||
                    !Positive(s.radius) || s.radius > 2f || !Positive(s.duration) || s.duration > 600f || !Within(s.durationRand, 0f, 600f) ||
                    (s.distanceScaling ? !Positive(s.adjustDistance) || s.adjustDistance > 100f : s.adjustDistance != 0f))
                    throw new ArgumentException("Invalid or unbounded source lightning spec: " + s.name);
        }
        private static bool Colour(int[] rgba) => rgba != null && rgba.Length == 4 && rgba.All(c => c >= 0 && c <= 255);
        private static bool Within(float value, float min, float max) => value >= min && value <= max;
        private static bool Positive(float value) => value > 0f && !float.IsInfinity(value) && !float.IsNaN(value);
    }
}
