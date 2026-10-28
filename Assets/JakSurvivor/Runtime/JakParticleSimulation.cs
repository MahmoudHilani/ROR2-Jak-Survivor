using System;
using System.Collections.Generic;
using System.Linq;

namespace JakSurvivor
{
    [Serializable] internal sealed class JakParticleField
    {
        public string key = "", copy = "";
        public float value = 0f, range = 0f, multiplier = 1f;
        public bool integerRange = false;
    }

    [Serializable] internal sealed class JakParticleDefinition
    {
        public int id = 0;
        public string texture = "", behavior = "";
        public string[] textureFrames = Array.Empty<string>();
        public bool motionBlur = false, glow = false;
        public JakParticleField[] fields = Array.Empty<JakParticleField>();
    }

    [Serializable] internal sealed class JakParticleItem
    {
        public int part = 0;
        public float period = 0f, length = 0f, offset = 0f;
        public bool rawCount = false, disabled = false;
        public string[] flags = Array.Empty<string>();
    }

    [Serializable] internal sealed class JakParticleGroup
    {
        public string name = "";
        public int id = 0;
        public float duration, linger;
        public JakParticleItem[] items = Array.Empty<JakParticleItem>();
    }

    // GOAL's linear sprite updates use 300 Hz units, integrated on 60 Hz frames.
    // Keep this independent of Unity so source stages and emission can be checked offline.
    internal sealed class JakParticleSimulation
    {
        internal static HashSet<int> ReferencedIds(JakParticleDefinition[] parts, IEnumerable<int> initial)
        {
            var ids = new HashSet<int>(initial);
            var pending = new Queue<int>(ids);
            while (pending.Count > 0)
            {
                // Dequeue once per pass; inside the predicate it would consume
                // one queued ID per candidate scanned.
                int id = pending.Dequeue();
                var definition = parts.FirstOrDefault(p => p.id == id);
                if (definition == null) continue;
                foreach (var field in definition.fields.Where(f => f.key == "binding" || f.key == "next-launcher"))
                    if (ids.Add((int)field.value)) pending.Enqueue((int)field.value);
            }
            return ids;
        }

        private const float BirthBound = 64f;
        // target-part.gc:3682-3684: group-darkjak-smack-wall-explode bursts defpart 179 at :num 100 for its 0.035 s window.
        private const int WallExplodePart = 179;
        private const float WallExplodeBirths = 100f;

        internal static void Validate(JakParticleDefinition[] parts, JakParticleGroup[] groups)
        {
            if (parts == null || groups == null) throw new ArgumentException("Particle definitions and groups are required.");
            var ids = new HashSet<int>();
            foreach (var part in parts)
            {
                if (part == null || !ids.Add(part.id) || part.fields == null || part.texture == null)
                    throw new ArgumentException("Invalid or duplicate particle definition.");
                foreach (var field in part.fields)
                {
                    if (field == null || string.IsNullOrEmpty(field.key) || field.copy == null ||
                        !Finite(field.value) || !Finite(field.range) || !Finite(field.multiplier) ||
                        !Finite(field.value + field.range * field.multiplier))
                        throw new ArgumentException("Particle fields must contain finite values.");
                    if (field.key == "num" && (Math.Abs(field.value) + Math.Abs(field.range * field.multiplier) > (part.id == WallExplodePart ? WallExplodeBirths : BirthBound)))
                        throw new ArgumentException("Emitter exceeds its supported source birth bound.");
                    if (field.integerRange && (field.range < 0f || field.range > 100000f))
                        throw new ArgumentException("Integer random range is outside supported bounds.");
                }
            }
            var names = new HashSet<string>();
            foreach (var group in groups)
            {
                if (group == null || string.IsNullOrEmpty(group.name) || !names.Add(group.name) || group.items == null ||
                    !Finite(group.duration) || !Finite(group.linger) || group.duration < 0f || group.linger < 0f || group.duration + group.linger > 60f)
                    throw new ArgumentException("Invalid particle group or tail longer than one minute.");
                foreach (var item in group.items)
                    if (item == null || !ids.Contains(item.part) || !Finite(item.period) || !Finite(item.length) || !Finite(item.offset) ||
                        item.period < 0f || (item.period > 0f && item.period < 1f / 300f) || item.length < 0f || item.offset < 0f)
                        throw new ArgumentException("Invalid particle group emitter.");
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        // Common names are compiled once at definition load; hot updates use these indices.
        internal enum Field { X, Y, Z, VelX, VelY, VelZ, AccelX, AccelY, AccelZ, R, G, B, A, FadeR, FadeG, FadeB, FadeA, ScaleX, ScaleY, ScalevelX, ScalevelY, RotX, RotZ, RotvelZ, ConerotX, ConerotY, RotateX, RotateY, RotateZ, LaunchSpeed, Friction, Omega, Userdata, Timer, NextTime, NextLauncher, Binding, Num, Count }
        internal sealed class FieldLayout
        {
            internal readonly List<string> Names = new List<string> { "x", "y", "z", "vel-x", "vel-y", "vel-z", "accel-x", "accel-y", "accel-z", "r", "g", "b", "a", "fade-r", "fade-g", "fade-b", "fade-a", "scale-x", "scale-y", "scalevel-x", "scalevel-y", "rot-x", "rot-z", "rotvel-z", "conerot-x", "conerot-y", "rotate-x", "rotate-y", "rotate-z", "launch-speed", "friction", "omega", "userdata", "timer", "next-time", "next-launcher", "binding", "num" };
            private readonly Dictionary<string, int> extra = new Dictionary<string, int>(StringComparer.Ordinal);
            internal int Index(string key, bool create = false)
            {
                switch (key)
                {
                    case "x": return (int)Field.X;
                    case "y": return (int)Field.Y;
                    case "z": return (int)Field.Z;
                    case "vel-x": return (int)Field.VelX;
                    case "vel-y": return (int)Field.VelY;
                    case "vel-z": return (int)Field.VelZ;
                    case "accel-x": return (int)Field.AccelX;
                    case "accel-y": return (int)Field.AccelY;
                    case "accel-z": return (int)Field.AccelZ;
                    case "r": return (int)Field.R;
                    case "g": return (int)Field.G;
                    case "b": return (int)Field.B;
                    case "a": return (int)Field.A;
                    case "fade-r": return (int)Field.FadeR;
                    case "fade-g": return (int)Field.FadeG;
                    case "fade-b": return (int)Field.FadeB;
                    case "fade-a": return (int)Field.FadeA;
                    case "scale-x": return (int)Field.ScaleX;
                    case "scale-y": return (int)Field.ScaleY;
                    case "scalevel-x": return (int)Field.ScalevelX;
                    case "scalevel-y": return (int)Field.ScalevelY;
                    case "rot-x": return (int)Field.RotX;
                    case "rot-z": return (int)Field.RotZ;
                    case "rotvel-z": return (int)Field.RotvelZ;
                    case "conerot-x": return (int)Field.ConerotX;
                    case "conerot-y": return (int)Field.ConerotY;
                    case "rotate-x": return (int)Field.RotateX;
                    case "rotate-y": return (int)Field.RotateY;
                    case "rotate-z": return (int)Field.RotateZ;
                    case "launch-speed": return (int)Field.LaunchSpeed;
                    case "friction": return (int)Field.Friction;
                    case "omega": return (int)Field.Omega;
                    case "userdata": return (int)Field.Userdata;
                    case "timer": return (int)Field.Timer;
                    case "next-time": return (int)Field.NextTime;
                    case "next-launcher": return (int)Field.NextLauncher;
                    case "binding": return (int)Field.Binding;
                    case "num": return (int)Field.Num;
                }
                if (extra.TryGetValue(key, out int index)) return index;
                if (!create) return -1;
                index = Names.Count;
                Names.Add(key); extra.Add(key, index);
                return index;
            }
        }
        internal readonly struct CompiledField
        {
            internal readonly JakParticleField Source;
            internal readonly int Key, Copy;
            internal CompiledField(JakParticleField source, FieldLayout layout)
            { Source = source; Key = layout.Index(source.key, true); Copy = string.IsNullOrEmpty(source.copy) ? -1 : layout.Index(source.copy, true); }
        }
        internal sealed class Definition
        {
            internal JakParticleDefinition Source;
            internal CompiledField[] Fields;
        }
        internal sealed class Particle
        {
            private readonly FieldLayout layout;
            private float[] values;
            private bool[] present;
            internal Particle(FieldLayout layout)
            { this.layout = layout; values = new float[layout.Names.Count]; present = new bool[values.Length]; }
            // Enumerated only by diagnostics/exporters; no dictionaries belong to particles.
            public IEnumerable<KeyValuePair<string, float>> Fields
            {
                get { for (int i = 0; i < values.Length; i++) if (present[i]) yield return new KeyValuePair<string, float>(layout.Names[i], values[i]); }
            }
            public JakParticleDefinition Definition;
            public string Texture = "";
            public float Age, Lifetime = 1f, Next = float.PositiveInfinity, ChildRemainder;
            public int NextId, Binding;
            public float Get(string key, float fallback = 0f) => Get(layout.Index(key), fallback);
            public void Set(string key, float value) => Set(layout.Index(key, true), value);
            internal float Get(Field key, float fallback = 0f) => Get((int)key, fallback);
            internal void Set(Field key, float value) => Set((int)key, value);
            internal float Get(int index, float fallback = 0f) => index >= 0 && index < values.Length && present[index] ? values[index] : fallback;
            internal void Set(int index, float value)
            {
                if (index >= values.Length) { Array.Resize(ref values, layout.Names.Count); Array.Resize(ref present, values.Length); }
                values[index] = value; present[index] = true;
            }
            internal void Clear() => Array.Clear(present, 0, present.Length);
        }

        internal sealed class Catalog
        {
            internal readonly FieldLayout Layout = new FieldLayout();
            internal readonly Dictionary<int, Definition> Definitions = new Dictionary<int, Definition>();
            internal Catalog(JakParticleDefinition[] parts)
            {
                foreach (JakParticleDefinition part in parts)
                {
                    var fields = new CompiledField[part.fields.Length];
                    for (int i = 0; i < fields.Length; i++) fields[i] = new CompiledField(part.fields[i], Layout);
                    Definitions.Add(part.id, new Definition { Source = part, Fields = fields });
                }
            }
        }
        private readonly Catalog catalog;
        private FieldLayout layout => catalog.Layout;
        private Dictionary<int, Definition> definitions => catalog.Definitions;
        private readonly JakParticleGroup group;
        private Random random;
        private readonly float[] remainders;
        private double pending;
        public float WaveRadius = 18f;
        public float Age { get; private set; }
        public int Births { get; private set; }
        public int Created { get; private set; }
        public List<Particle> Particles { get; } = new List<Particle>();
        // Retired particles are reused, so per-frame launches do not allocate once warm.
        private readonly Stack<Particle> pool = new Stack<Particle>();
        private const float Frame = 1f / 60f;
        private const int Limit = 512;

        public JakParticleSimulation(JakParticleDefinition[] parts, JakParticleGroup source, int seed)
            : this(new Catalog(parts), source, seed) { }

        internal JakParticleSimulation(Catalog catalog, JakParticleGroup source, int seed)
        {
            this.catalog = catalog;
            group = source;
            random = new Random(seed);
            remainders = new float[source.items.Length];
        }

        internal bool Matches(Catalog sourceCatalog, JakParticleGroup source) => ReferenceEquals(catalog, sourceCatalog) && ReferenceEquals(group, source);
        internal void ClearParticles()
        {
            foreach (Particle particle in Particles) pool.Push(particle);
            Particles.Clear();
        }
        internal void Reset(int seed)
        {
            ClearParticles();
            Array.Clear(remainders, 0, remainders.Length);
            random = new Random(seed);
            pending = 0; Age = 0f; Births = 0; WaveRadius = 18f;
        }

        public float Sample(JakParticleField field)
        {
            double draw = field.integerRange ? random.Next(0, Math.Max(0, (int)field.range) + 1) : random.NextDouble() * field.range;
            return field.value + (float)draw * field.multiplier;
        }

        private void Patch(Particle particle, Definition definition, bool birth)
        {
            if (!string.IsNullOrEmpty(definition.Source.texture)) particle.Texture = definition.Source.texture;
            // A stage consumes the previous countdown; fields omitted by the patch survive.
            particle.Next = float.PositiveInfinity;
            foreach (CompiledField field in definition.Fields)
            {
                float value = field.Copy < 0 ? Sample(field.Source) : particle.Get(field.Copy);
                particle.Set(field.Key, value);
                if (field.Key == (int)Field.Timer) particle.Lifetime = birth ? value : particle.Age + value;
                else if (field.Key == (int)Field.NextTime) particle.Next = value;
                else if (field.Key == (int)Field.NextLauncher) particle.NextId = (int)value;
                else if (field.Key == (int)Field.Binding) particle.Binding = (int)value;
            }
        }

        public Particle Spawn(int id)
        {
            if (Particles.Count >= Limit || !definitions.TryGetValue(id, out Definition compiled)) return null;
            JakParticleDefinition definition = compiled.Source;
            Particle particle;
            if (pool.Count > 0) particle = pool.Pop();
            else { particle = new Particle(layout); Created++; }
            particle.Clear();
            particle.Definition = definition;
            particle.Texture = "";
            particle.Age = particle.ChildRemainder = 0f;
            particle.Lifetime = 1f;
            particle.Next = float.PositiveInfinity;
            particle.NextId = particle.Binding = 0;
            Patch(particle, compiled, true);
            // Cone rotation is a source launch direction, not a billboard rotation.
            double x = particle.Get(Field.ConerotX) * Math.PI / 180.0;
            double y = particle.Get(Field.ConerotY) * Math.PI / 180.0;
            Rotate(particle, Velocities, x, -y, 0);
            // Launch rotation owns both the radial offset and the complete velocity, including forward-launched bursts.
            if (definition.behavior == "spline-align" || definition.behavior == "lightning-align")
            {
                double rx = particle.Get(Field.RotateX) * Math.PI / 180.0;
                double ry = particle.Get(Field.RotateY) * Math.PI / 180.0;
                double rz = particle.Get(Field.RotateZ) * Math.PI / 180.0;
                Rotate(particle, Axes, rx, ry, rz);
                Rotate(particle, Velocities, rx, ry, rz);
                Rotate(particle, Accelerations, rx, ry, rz);
            }
            if (definition.motionBlur)
            {
                float vx = particle.Get(Field.VelX), vy = particle.Get(Field.VelY), vz = particle.Get(Field.VelZ);
                particle.Set(Field.LaunchSpeed, (float)Math.Sqrt(vx * vx + vy * vy + vz * vz));
            }
            if (definition.textureFrames.Length > 0)
                particle.Texture = definition.textureFrames[random.Next(definition.textureFrames.Length)];
            if (definition.behavior == "wave-converge")
            {
                // Adapt the recovered birth-func-converge callback into an isotropic
                // muzzle-local emitter; the attraction remains in source tick units.
                double azimuth = random.NextDouble() * Math.PI * 2.0;
                float vertical = (float)(random.NextDouble() * 2.0 - 1.0);
                float plane = (float)Math.Sqrt(1.0 - vertical * vertical);
                float radius = particle.Get(Field.X, 1f);
                particle.Set(Field.X, (float)Math.Cos(azimuth) * plane * radius);
                particle.Set(Field.Y, vertical * radius);
                particle.Set(Field.Z, (float)Math.Sin(azimuth) * plane * radius);
            }
            Particles.Add(particle);
            Births++;
            return particle;
        }

        private float Count(int id)
        {
            if (!definitions.TryGetValue(id, out Definition definition)) return 0f;
            foreach (CompiledField field in definition.Fields) if (field.Key == (int)Field.Num) return Sample(field.Source);
            return 1f;
        }

        public void Launch(int id)
        {
            int count = (int)Math.Max(0f, Count(id));
            for (int i = 0; i < count && i < Limit; i++) Spawn(id);
        }

        public static float ActiveWindow(float start, float end, JakParticleItem item)
        {
            if (end <= start || end <= item.offset) return 0f;
            start = Math.Max(start, item.offset);
            if (item.period <= 0f) return end - start;
            float active = 0f;
            int first = (int)Math.Floor((start - item.offset) / item.period);
            int last = (int)Math.Floor((end - item.offset) / item.period);
            for (int cycle = first; cycle <= last; cycle++)
            {
                float begin = item.offset + cycle * item.period;
                active += Math.Max(0f, Math.Min(end, begin + item.length) - Math.Max(start, begin));
            }
            return active;
        }

        public void Advance(float delta, bool continuous = false)
        {
            if (delta <= 0f || float.IsNaN(delta) || float.IsInfinity(delta)) return;
            // Effects have finite tails. Cap a suspended-frame catch-up to their remaining life.
            pending += Math.Min(delta, group.duration + group.linger + 3f);
            while (pending + 1e-8 >= Frame)
            {
                Tick(continuous);
                pending -= Frame;
            }
        }

        private void Tick(bool continuous)
        {
            int existing = Particles.Count;
            float end = continuous ? Age + Frame : Math.Min(Age + Frame, group.duration);
            for (int i = 0; i < group.items.Length; i++)
            {
                JakParticleItem item = group.items[i];
                if (item.disabled) continue;
                float active = ActiveWindow(Age, end, item);
                if (active <= 0f) continue;
                remainders[i] += Count(item.part) * (item.rawCount ? 1f : active * 60f);
                float whole = (float)Math.Floor(remainders[i]);
                remainders[i] -= whole;
                int births = (int)Math.Min(Limit, whole);
                for (int birth = 0; birth < births; birth++) Spawn(item.part);
            }
            // Child bindings emit at their parent's position, never as global free emitters.
            // gun-part.gc:3374: the 1.5-tick glow must be drawn on its birth frame.
            int count = group.id == 107 ? existing : Particles.Count;
            for (int i = count - 1; i >= 0; i--)
            {
                Particle particle = Particles[i];
                if (particle.Binding != 0)
                {
                    particle.ChildRemainder += Count(particle.Binding);
                    while (particle.ChildRemainder >= 1f)
                    {
                        Particle child = Spawn(particle.Binding);
                        if (child != null) foreach (Field axis in Axes) child.Set(axis, particle.Get(axis));
                        particle.ChildRemainder -= 1f;
                    }
                }
                float remain = Frame;
                int stages = 0;
                while (remain > 0f)
                {
                    float step = Math.Min(remain, Math.Max(0f, particle.Next));
                    Integrate(particle, step);
                    if (particle.Definition.behavior == "wave-trail")
                    {
                        float x = particle.Get(Field.X), z = particle.Get(Field.Z);
                        float radius = (float)Math.Sqrt(x * x + z * z);
                        // Hold at the expanding front without stopping, so trails ride it out to the final radius.
                        if (radius > WaveRadius && radius > 0f)
                        {
                            particle.Set(Field.X, x * WaveRadius / radius); particle.Set(Field.Z, z * WaveRadius / radius);
                        }
                    }
                    particle.Age += step;
                    particle.Next -= step;
                    remain -= step;
                    if (particle.Next <= 0f)
                    {
                        if (++stages > 8 || !definitions.TryGetValue(particle.NextId, out Definition stage)) particle.Next = float.PositiveInfinity;
                        else Patch(particle, stage, false);
                    }
                }
                if (particle.Age >= particle.Lifetime || (particle.Get(Field.A, 128f) <= 0f && float.IsPositiveInfinity(particle.Next)))
                {
                    Particles.RemoveAt(i);
                    pool.Push(particle);
                }
            }
            Age += Frame;
        }

        private static readonly Field[] Axes = { Field.X, Field.Y, Field.Z };
        private static readonly Field[] Velocities = { Field.VelX, Field.VelY, Field.VelZ }, Accelerations = { Field.AccelX, Field.AccelY, Field.AccelZ };
        private static void Rotate(Particle particle, Field[] fields, double rx, double ry, double rz)
        {
            if (rx == 0 && ry == 0 && rz == 0) return;
            double x = particle.Get(fields[0]), y = particle.Get(fields[1]), z = particle.Get(fields[2]);
            double yy = y * Math.Cos(rx) - z * Math.Sin(rx), zz = y * Math.Sin(rx) + z * Math.Cos(rx);
            double xx = x * Math.Cos(ry) + zz * Math.Sin(ry);
            z = -x * Math.Sin(ry) + zz * Math.Cos(ry);
            particle.Set(fields[0], (float)(xx * Math.Cos(rz) - yy * Math.Sin(rz)));
            particle.Set(fields[1], (float)(xx * Math.Sin(rz) + yy * Math.Cos(rz)));
            particle.Set(fields[2], (float)z);
        }
        private static readonly Field[] Fades = { Field.FadeR, Field.FadeG, Field.FadeB, Field.FadeA };
        private static void Integrate(Particle p, float seconds)
        {
            float ticks = seconds * 300f;
            if (p.Definition.behavior == "wave-converge")
            {
                float x = p.Get(Field.X), y = p.Get(Field.Y), z = p.Get(Field.Z);
                float radius = (float)Math.Sqrt(x * x + y * y + z * z);
                float acceleration = Math.Abs(p.Get(Field.AccelX));
                if (radius > 0.0001f)
                    for (int i = 0; i < Axes.Length; i++)
                        p.Set(Velocities[i], p.Get(Velocities[i]) - p.Get(Axes[i]) / radius * acceleration * ticks / 5f);
                if (radius < 0.04f) p.Lifetime = p.Age;
            }
            for (int i = 0; i < Axes.Length; i++)
            {
                p.Set(Axes[i], p.Get(Axes[i]) + p.Get(Velocities[i]) * ticks);
                float friction = p.Get(Field.Friction);
                float velocity = p.Get(Velocities[i]) + (p.Definition.behavior == "wave-converge" ? 0f : p.Get(Accelerations[i])) * ticks / 5f;
                p.Set(Velocities[i], velocity * (friction == 0f ? 1f : Math.Max(0f, 1f - (1f - friction) * ticks / 5f)));
            }
            p.Set(Field.ScaleX, p.Get(Field.ScaleX, 1f) + p.Get(Field.ScalevelX) * ticks);
            p.Set(Field.ScaleY, p.Get(Field.ScaleY, 1f) + p.Get(Field.ScalevelY) * ticks);
            p.Set(Field.RotZ, p.Get(Field.RotZ) + p.Get(Field.RotvelZ) * ticks);
            for (int i = 0; i < Colors.Length; i++) p.Set(Colors[i], Math.Max(0f, Math.Min(255f, p.Get(Colors[i], 128f) + p.Get(Fades[i]) * ticks)));
        }

        private static readonly Field[] Colors = { Field.R, Field.G, Field.B, Field.A };
    }
}
