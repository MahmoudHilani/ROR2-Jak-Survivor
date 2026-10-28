using System;

namespace JakSurvivor
{
    // One lightning-tracker (generic-obs.gc:1450-1720) driving its lightning-control (lightning.gc:291-409)
    // from a native lightning-spec. Every ported caller passes lightning-probe-callback (target-part.gc:2730-2744).
    internal sealed class JakShockSimulation
    {
        internal struct V
        {
            internal float X, Y, Z;
            internal V(float x, float y, float z) { X = x; Y = y; Z = z; }
            public static V operator +(V a, V b) => new V(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static V operator -(V a, V b) => new V(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static V operator *(V a, float b) => new V(a.X * b, a.Y * b, a.Z * b);
            internal float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        }
        internal struct Rgba
        {
            internal byte R, G, B, A;
            internal Rgba(byte r, byte g, byte b, byte a) { R = r; G = g; B = b; A = a; }
            internal static Rgba Of(int[] c) => new Rgba((byte)c[0], (byte)c[1], (byte)c[2], (byte)c[3]);
            // math.gc:609-640 and lightning.gc:263-288: a + (b - a) * t, truncated by ftoi.
            internal static Rgba Lerp(Rgba a, Rgba b, float t) => new Rgba(
                (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t), (byte)(a.A + (b.A - a.A) * t));
        }
        internal const int OwnerLimit = 64;
        private const int Dead = 0, Initial = 1, Updating = 2, Fading = 3;
        internal readonly V[] Line = new V[JakWeaponEffectContract.MaxShockPoints], Meet = new V[JakWeaponEffectContract.MaxShockPoints];
        internal JakShockSpec Spec { get; private set; }
        internal int Points { get; private set; }
        internal int Duration { get; private set; }
        internal int Mode { get; private set; }
        internal float Age { get; private set; }
        internal float Counter { get; private set; }
        internal float BoxSize { get; private set; }
        internal Rgba StartColor { get; private set; }
        internal Rgba EndColor { get; private set; }
        internal bool Alive => Mode != Dead;
        // lightning.gc:784-786 draws modes 2 and 3 only.
        internal bool Drawn => Mode == Updating || Mode == Fading;
        private readonly Random random;
        private readonly Func<double> draw;
        private Rgba start, end, fadeTo;
        private int callbacks;

        internal JakShockSimulation(JakShockSpec spec, Random random, V start, V end)
        {
            this.random = random;
            draw = random.NextDouble;
            Restart(spec, start, end);
        }

        internal void Restart(JakShockSpec spec, V from, V to)
        {
            Spec = spec;
            Points = spec.numPoints;
            // generic-obs.gc:1708-1727: a zero duration argument samples the spec range and truncates to ticks.
            Duration = (int)(spec.duration + random.NextDouble() * spec.durationRand);
            // lightning-h.gc:208-219: trackers construct at distance 0, so lsf4 keeps the spec point count and box.
            BoxSize = spec.boxSize;
            start = Rgba.Of(spec.startColor); end = Rgba.Of(spec.endColor); fadeTo = Rgba.Of(spec.fadeToColor);
            StartColor = start; EndColor = end;
            Mode = Initial; Age = 0f; Counter = 0f; callbacks = 0;
            Meet[0] = from; Meet[Points - 1] = to;
        }

        // gs-packed-rgba-lerp along the strip, lightning.gc:449-470.
        internal Rgba ColorAt(int point) => Rgba.Lerp(StartColor, EndColor, point * (1f / (Points - 1)));

        internal static bool AcceptProbe(bool hit, float distance, float range) => hit && distance > 2f && distance <= range;
        internal static int Slot(bool[] occupied, float[] ages)
        {
            int oldest = 0;
            for (int i = 0; i < occupied.Length; i++)
            {
                if (!occupied[i]) return i;
                if (ages[i] > ages[oldest]) oldest = i;
            }
            return oldest;
        }
        // lightning.gc:36-72.
        internal static void Fractal(V[] points, int first, int last, float size, float reduction, Func<double> draw)
        {
            if (last - first <= 1) return;
            int middle = (first + last) / 2;
            points[middle] = (points[first] + points[last]) * 0.5f +
                new V((float)(draw() - 0.5) * size, (float)(draw() - 0.5) * size, (float)(draw() - 0.5) * size);
            Fractal(points, first, middle, size * reduction, reduction, draw);
            Fractal(points, middle, last, size * reduction, reduction, draw);
        }

        // One game frame: the tracker's code and update, then the lightning engine's update or fade.
        internal void Advance(float ticks, V from, V to)
        {
            if (Mode == Dead || ticks <= 0f) return;
            if (Mode != Fading)
            {
                // generic-obs.gc:1608-1650: after the duration, fade only when fade-time is positive.
                if (Age >= Duration) { Mode = Spec.fadeTime > 0f ? Fading : Dead; Counter = 0f; }
                else
                {
                    Callback();
                    Meet[0] = from; Meet[Points - 1] = to;
                    Update(ticks);
                }
            }
            if (Mode == Fading) Fade(ticks);
            Age += ticks;
        }

        private void Callback()
        {
            // target-part.gc:2730-2744 counts invocations against (seconds 0.007), i.e. the second update.
            if (++callbacks != 2) return;
            byte rg = (byte)random.Next(64, 129);
            StartColor = EndColor = new Rgba(rg, rg, 128, (byte)random.Next(32, 65));
        }

        private void Update(float ticks)
        {
            int last = Points - 1;
            // timer.gc:185: time-adjust-ratio counts 60 Hz frames.
            float ratio = ticks * 0.2f;
            if (Spec.distanceScaling)
                BoxSize = Spec.boxSize * Math.Max(1f, Math.Min(5f, (Meet[0] - Meet[last]).Length / Spec.adjustDistance));
            Counter = Math.Max(0f, Counter - ratio);
            bool initial = Mode == Initial;
            if (initial) { Mode = Updating; Counter = 0f; }
            if (Counter <= 0f)
            {
                Fractal(Meet, 0, last, BoxSize, Spec.reduction, draw);
                if (initial) Array.Copy(Meet, Line, Points);
                Counter = Spec.mergeCount;
            }
            float merge = Math.Max(0f, Math.Min(1f, Spec.mergeFactor * ratio));
            for (int i = 0; i < Points; i++) Line[i] += (Meet[i] - Line[i]) * merge;
        }

        private void Fade(float ticks)
        {
            float time = Spec.fadeTime;
            if (Counter >= time) { Mode = Dead; return; }
            Counter += ticks;
            float t = Math.Max(0f, Math.Min(1f, (1f - Counter / time) * Spec.fadeStartFactor));
            StartColor = Rgba.Lerp(fadeTo, start, t);
            EndColor = Rgba.Lerp(fadeTo, end, t);
        }
    }
}
