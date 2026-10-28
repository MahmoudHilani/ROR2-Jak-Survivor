using System;
using System.IO;

namespace JakSurvivor
{
    public enum JakSpinVariant : byte { GroundYellow, GroundBlue, AirYellow, AirBlue }
    public enum JakSpinForm : byte { Yellow, Blue }
    public enum JakSpinPhase : byte { Active, Unwind, Recovery, Done }
    public enum JakSpinJump : byte { Normal, CancelSpin, Ignored }

    [Serializable] public sealed class JakSpinTrack
    {
        public JakSpinVariant variant;
        public float activeSeconds, poseSeconds;
        public float[] muzzleYaw = Array.Empty<float>();
    }

    public sealed class JakSpinCatalog
    {
        public const int SampleRate = 60;
        internal sealed class Track
        {
            internal readonly float Active, Pose;
            private readonly float[] yaw;
            internal Track(JakSpinTrack source)
            { Active = source.activeSeconds; Pose = source.poseSeconds; yaw = (float[])source.muzzleYaw.Clone(); }
            internal float Sample(float time)
            {
                float index = Math.Max(0, Math.Min(Pose, time)) * SampleRate;
                int left = Math.Min((int)index, yaw.Length - 1), right = Math.Min(left + 1, yaw.Length - 1);
                return yaw[left] + (yaw[right] - yaw[left]) * (index - left);
            }
        }
        private readonly Track[] tracks;
        private JakSpinCatalog(Track[] tracks) => this.tracks = tracks;
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static JakSpinCatalog Parse(JakSpinTrack[] source)
        {
            if (source == null || source.Length != JakSpinRecipe.Count) throw new InvalidDataException("Every spin variant is required.");
            var tracks = new Track[JakSpinRecipe.Count];
            foreach (JakSpinTrack track in source)
            {
                if (track == null || (int)track.variant >= JakSpinRecipe.Count || tracks[(int)track.variant] != null ||
                    !Finite(track.activeSeconds) || !Finite(track.poseSeconds) || track.activeSeconds <= 0 ||
                    track.poseSeconds < track.activeSeconds || track.muzzleYaw == null ||
                    track.muzzleYaw.Length != (int)Math.Round(track.poseSeconds * SampleRate) + 1)
                    throw new InvalidDataException("Invalid spin variant or track dimensions.");
                for (int i = 0; i < track.muzzleYaw.Length; i++)
                    if (!Finite(track.muzzleYaw[i]) || i > 0 && Math.Abs(track.muzzleYaw[i] - track.muzzleYaw[i - 1]) >= 90)
                        throw new InvalidDataException("Spin muzzle samples must be finite with steps below 90 degrees.");
                tracks[(int)track.variant] = new Track(track);
            }
            return new JakSpinCatalog(tracks);
        }
        public JakSpinTimeline Timeline(JakSpinVariant variant, float rate)
        {
            if ((int)variant >= JakSpinRecipe.Count || !Finite(rate) || rate <= 0) throw new ArgumentOutOfRangeException();
            return new JakSpinTimeline(variant, rate, tracks[(int)variant]);
        }
    }

    public readonly struct JakSpinTimeline
    {
        public readonly JakSpinVariant Variant;
        public readonly float Rate, ActiveEnd, PoseEnd, End;
        public readonly bool Airborne;
        private readonly JakSpinCatalog.Track track;
        internal JakSpinTimeline(JakSpinVariant variant, float rate, JakSpinCatalog.Track track)
        {
            Variant = variant; Rate = rate; this.track = track;
            ref readonly JakSpinRecipe.Variant recipe = ref JakSpinRecipe.Get(variant);
            Airborne = recipe.Airborne;
            ActiveEnd = track.Active / rate; PoseEnd = track.Pose / rate;
            End = PoseEnd + JakSpin.RecoverySeconds / rate;
        }
        public bool IsDefault => track == null;
        public JakSpinPhase PhaseAt(float age) => age < ActiveEnd ? JakSpinPhase.Active :
            age < PoseEnd ? JakSpinPhase.Unwind : age < End ? JakSpinPhase.Recovery : JakSpinPhase.Done;
        // target.gc:2084,2269-2273: the gun and the entry heading (dir-targ) hold through the pose.
        public bool OwnsGun(float age) => !IsDefault && age >= 0 && age < PoseEnd;
        public bool CrossesActive(float previousAge, float age) => age > previousAge && age > 0 && previousAge < ActiveEnd;
        public JakSpinJump JumpAt(float age) => OwnsGun(age) ? (Airborne ? JakSpinJump.Ignored : JakSpinJump.CancelSpin) : JakSpinJump.Normal;
        public float MuzzleYaw(float age) => track.Sample(age * Rate);
    }

    public static class JakSpin
    {
        public const float RecoverySeconds = 0.3f; // target-util.gc:377,1546 attack-timeout.
        public const float VolleyInterval = 30f / 300f; // target-gun.gc:821,834,3284-3296 combo-fire-delay.
        public const float FallingHop = 33775.48f / 4096f; // target.gc:2880.
        public const float RisingHeight = 2048f / 4096f, RisingCorrection = 1024f / 4096f; // target.gc:2921.
        public const float SpinGravity = 122880f / 4096f; // target.gc:2934.
        public const float CenterHeight = 6553.6f / 4096f; // target-util.gc:412.
        public const float LoopDegreesPerSecond = 393216f / 65536f * 360f; // target-anim.gc:1768,1777-1780.
        public static JakSpinVariant Variant(bool airborne, JakSpinForm form)
        {
            for (int i = 0; i < JakSpinRecipe.Count; i++)
            {
                ref readonly JakSpinRecipe.Variant recipe = ref JakSpinRecipe.Get((JakSpinVariant)i);
                if (recipe.Airborne == airborne && recipe.Form == form) return recipe.Id;
            }
            throw new ArgumentOutOfRangeException(nameof(form));
        }
        public static float HopVelocity(float verticalVelocity, float gravity) => verticalVelocity <= 0 ? FallingHop :
            (float)Math.Sqrt(2 * SpinGravity * (RisingHeight + verticalVelocity * verticalVelocity / (2 * gravity))) + RisingCorrection;
        public static bool HopAvailable(float lastGrounded, float lastHop) => lastHop < lastGrounded;
        public static (float X, float Y, float Z) ShotDirection(float headingDegrees, float aimY)
        {
            float y = Math.Max(-(float)Math.Sin(Math.PI / 4), Math.Min((float)Math.Sin(Math.PI / 4), aimY));
            double horizontal = Math.Sqrt(1 - y * y), heading = headingDegrees * Math.PI / 180;
            return ((float)(Math.Sin(heading) * horizontal), y, (float)(Math.Cos(heading) * horizontal));
        }
        public static (float X, float Z) Push(float offsetX, float offsetZ, bool champion)
        {
            double length = Math.Sqrt((double)offsetX * offsetX + (double)offsetZ * offsetZ);
            return champion || length == 0 ? (0, 0) : ((float)(offsetX / length), (float)(offsetZ / length));
        }
    }

    public static class JakSpinRecipe
    {
        public enum Turn : byte { Align, Rate, Settle }
        public readonly struct Segment
        {
            public readonly string Clip;
            public readonly float ClipStart, Seconds;
            public readonly Turn Turn;
            public Segment(string clip, float start, float seconds, Turn turn) { Clip = clip; ClipStart = start; Seconds = seconds; Turn = turn; }
        }
        public readonly struct Overlay
        {
            public readonly string Clip;
            public readonly float ClipStart, Speed;
            public Overlay(string clip, float start, float speed) { Clip = clip; ClipStart = start; Speed = speed; }
        }
        public readonly struct Variant
        {
            public readonly JakSpinVariant Id;
            public readonly JakSpinForm Form;
            public readonly bool Airborne;
            public readonly string ActionClip, EndOverlay, Sound, FaceSource;
            public readonly float PreTurn, ActiveSeconds, EntryBlend;
            private readonly Segment[] body;
            public readonly Overlay UpperBody;
            public int SegmentCount => body.Length;
            public Segment SegmentAt(int index) => body[index];
            public float PoseSeconds { get { float sum = 0; foreach (Segment s in body) sum += s.Seconds; return sum; } }
            internal Variant(JakSpinVariant id, JakSpinForm form, bool airborne, string action, string end,
                string sound, string face, float pre, float active, float blend, Segment[] body, Overlay upper)
            {
                Id = id; Form = form; Airborne = airborne; ActionClip = action; EndOverlay = end;
                Sound = sound; FaceSource = face; PreTurn = pre; ActiveSeconds = active; EntryBlend = blend; this.body = body; UpperBody = upper;
            }
        }
        private const string Yellow = "jakb-gun-attack-from-stance", Blue = Yellow + "-blue", Air = "jakb-attack-from-jump";
        // target.gc:2166-2210,2283-2320; target-anim.gc:1725-1810. Air loop is bounded to one cycle.
        private static readonly Variant[] variants = {
            new Variant(JakSpinVariant.GroundYellow, JakSpinForm.Yellow, false, Yellow + "@spin", Yellow + "-end",
                "spin-kick", Yellow, 18, 0.4f, 0.05f, new[] { new Segment(Yellow, 0, 0.4f, Turn.Align) }, default),
            new Variant(JakSpinVariant.GroundBlue, JakSpinForm.Blue, false, Blue + "@spin", Blue + "-end",
                "blue-whoosh", Blue, 0, 0.4f, 0.05f, new[] { new Segment(Blue, 0, 0.4f, Turn.Align) }, default),
            AirVariant(JakSpinVariant.AirYellow, JakSpinForm.Yellow, new Overlay(Yellow, 0, 0.6f)),
            AirVariant(JakSpinVariant.AirBlue, JakSpinForm.Blue, new Overlay(Yellow, 0.2f, 0.5f))
        };
        public static int Count => variants.Length;
        private static Variant AirVariant(JakSpinVariant id, JakSpinForm form, Overlay upper) => new Variant(id, form, true,
            Air + "@spin-" + form.ToString().ToLowerInvariant(), null, "spin-kick", Air, 0,
            25f / 60f, 0.075f, new[] { new Segment(Air, 0, 14f / 60f, Turn.Align),
                new Segment(Air + "-loop", 0, 11f / 60f, Turn.Rate), new Segment(Air + "-end", 0, 0.1f, Turn.Rate),
                new Segment(Air + "-end", 0.1f, 0.2f, Turn.Settle) }, upper);
        public static ref readonly Variant Get(JakSpinVariant id) => ref variants[(int)id];
        public static bool TryFaceSource(string clip, out string source)
        {
            foreach (Variant v in variants)
                if (v.ActionClip == clip || v.EndOverlay == clip) { source = v.ActionClip == clip ? v.FaceSource : v.EndOverlay; return true; }
            source = null; return false;
        }
        public static float Facing(in Variant variant, Func<string, (float[] Yaw, float Fps)> align, float time)
        {
            float yaw = variant.PreTurn;
            for (int i = 0; i < variant.SegmentCount; i++)
            {
                Segment segment = variant.SegmentAt(i);
                float t = Math.Max(0, Math.Min(segment.Seconds, time));
                if (segment.Turn == Turn.Align)
                {
                    var data = align(segment.Clip);
                    if (data.Yaw == null || data.Yaw.Length < 2 || data.Fps <= 0) throw new InvalidDataException("Missing spin align data: " + segment.Clip);
                    float index = Math.Min(data.Yaw.Length - 1, t * data.Fps);
                    int left = (int)index, right = Math.Min(left + 1, data.Yaw.Length - 1);
                    yaw += data.Yaw[left] + (data.Yaw[right] - data.Yaw[left]) * (index - left) - data.Yaw[0];
                }
                else if (segment.Turn == Turn.Rate) yaw += t * JakSpin.LoopDegreesPerSecond;
                else
                {
                    float target = (float)Math.Ceiling(yaw / 360) * 360;
                    float normalized = t / segment.Seconds;
                    yaw += (target - yaw) * (1 - (float)Math.Pow(1 - normalized, 3));
                }
                if (time <= segment.Seconds) break;
                time -= segment.Seconds;
            }
            return yaw;
        }
    }
}
