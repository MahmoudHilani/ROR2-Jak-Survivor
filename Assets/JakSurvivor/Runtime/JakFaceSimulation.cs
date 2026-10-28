using System;

namespace JakSurvivor
{
    [Serializable] public sealed class JakFaceTrack
    {
        public string name;
        public int frames, eyeFrames;
        public float speed, artistBase, artistStep;
        public float[] blendWeights = Array.Empty<float>();
        public int[] eyes = Array.Empty<int>();
    }

    public struct JakEyeState
    {
        public float X, Y, Lid, IrisScale, PupilScale, LidScale;
    }

    public sealed class JakFaceSimulation
    {
        public const int TargetCount = 13;
        // eye.gc:56,103,720-738: decrement before indexing, reset at zero.
        private static readonly float[] BlinkTable = { 0f, 0.667f, 0.9f, 1f, 1f, 1f, 1f, 0.333f, 0.1f, 0f };
        private int randomTime = 60;
        private double remainder;
        private uint random;
        public float Blink { get; private set; }
        public JakEyeState Left { get; private set; }
        public JakEyeState Right { get; private set; }

        public JakFaceSimulation(uint seed) => random = (seed & 0x7fffffu) == 0 ? 1u : seed & 0x7fffffu;

        public void Evaluate(JakFaceTrack clip, float frame, float darkInterp, float scarfInterp, float gogglesInterp, float elapsed, float[] weights,
            JakFaceTrack eyeClip = null, float eyeFrame = float.NaN)
        {
            if (weights == null || weights.Length != TargetCount) throw new ArgumentException("Expected thirteen blend weights.");
            Array.Clear(weights, 0, weights.Length);
            // powerups.gc:901-917; foreground.gc:1570-1615: overrides replace the entire anim track.
            if (darkInterp != 0f || scarfInterp != 0f || gogglesInterp != 0f)
            {
                weights[1] = weights[3] = weights[5] = weights[6] = darkInterp;
                weights[4] = gogglesInterp;
                weights[7] = scarfInterp;
            }
            else if (clip != null && clip.blendWeights.Length != 0)
            {
                Samples(frame, clip.frames, out int first, out int second, out float fraction);
                for (int i = 0; i < TargetCount; i++) weights[i] = Lerp(clip.blendWeights[first * TargetCount + i], clip.blendWeights[second * TargetCount + i], fraction);
            }
            // powerups.gc:919-921; logic-target.gc:2951: the remaining boxed overrides start at zero.
            JakEyeState left, right;
            if (darkInterp != 0f) left = right = new JakEyeState { PupilScale = 2f };
            else
            {
                bool separateEyes = !float.IsNaN(eyeFrame);
                left = SampleEye(separateEyes ? eyeClip : clip, separateEyes ? eyeFrame : frame, 0);
                right = SampleEye(separateEyes ? eyeClip : clip, separateEyes ? eyeFrame : frame, 1);
            }
            AdvanceBlink(elapsed, left.Lid >= 0f && right.Lid >= 0f);
            left.Lid = ResolveLid(left.Lid, Blink);
            right.Lid = ResolveLid(right.Lid, Blink);
            Left = left;
            Right = right;
        }

        private void AdvanceBlink(float elapsed, bool explicitLids)
        {
            remainder += Math.Max(0f, elapsed) * 60.0;
            int frames = (int)(remainder + 1e-5);
            remainder = Math.Max(0.0, remainder - frames);
            if (frames == 0) return;
            if (explicitLids)
            {
                randomTime = 60;
                Blink = 0f;
                return;
            }
            for (int i = 0; i < frames; i++)
            {
                if (--randomTime >= 10) continue;
                Blink = BlinkTable[randomTime];
                if (randomTime != 0) continue;
                // math.gc:686-723: VU random mantissa recurrence, with per-model rather than shared entropy.
                random = ((random << 1) ^ ((random >> 4 ^ random >> 22) & 1u)) & 0x7fffffu;
                randomTime = 60 + (int)(random / 8388608.0 * 180.0);
            }
        }

        private static JakEyeState SampleEye(JakFaceTrack track, float frame, int side)
        {
            // eye.gc:1045-1057: native no-track fallback.
            if (track == null || track.eyeFrames == 0) return new JakEyeState { Lid = -1f, IrisScale = 0.55f, PupilScale = 0.45f, LidScale = 1f };
            Samples(frame, track.eyeFrames, out int first, out int second, out float fraction);
            return Interpolate(DecodeEye(track.eyes, first * 12 + side * 6), DecodeEye(track.eyes, second * 12 + side * 6), fraction);
        }

        // eye.gc:925-945 convert-eye-data: signed translations/lid /128, zero-extended scale bytes /64.
        public static JakEyeState DecodeEye(int[] values, int offset) => new JakEyeState
        {
            X = values[offset] / 128f, Y = values[offset + 1] / 128f, Lid = values[offset + 2] / 128f,
            IrisScale = (values[offset + 3] & 255) / 64f, PupilScale = (values[offset + 4] & 255) / 64f, LidScale = (values[offset + 5] & 255) / 64f
        };

        private static JakEyeState Interpolate(JakEyeState a, JakEyeState b, float t) => new JakEyeState
        {
            X = Lerp(a.X, b.X, t), Y = Lerp(a.Y, b.Y, t), Lid = Lerp(a.Lid, b.Lid, t),
            IrisScale = Lerp(a.IrisScale, b.IrisScale, t), PupilScale = Lerp(a.PupilScale, b.PupilScale, t), LidScale = Lerp(a.LidScale, b.LidScale, t)
        };

        public static float ResolveLid(float lid, float blink) => lid < 0f ? Lerp(1f + lid, 1f, blink) : lid;

        public static float DarkInterpolation(string name, float artistFrame, bool dark, float previous, float elapsed)
        {
            // target-darkjak.gc:1027,1124-1171,788: artist frames differ from compressed track frames.
            if (name == "jakb-darkjak-get-on-fast" && dark) return Range(artistFrame, 5f, 15f);
            if (name == "jakb-darkjak-get-off" || name == "jakb-darkjak-get-off-end") return 1f - Range(artistFrame, 10f, 60f);
            if (name == "jakb-darkjak-bomb-end" && !dark) return 1f - Range(artistFrame, 90f, 115f);
            if (dark) return 1f;
            return Math.Max(0f, previous - 2f * Math.Max(0f, elapsed));
        }

        public static string ExitAnimation(string currentClip)
        {
            // target-darkjak.gc:1122-1171: entries 451/466/467/468 revert through entry 461 (bomb-end).
            switch (currentClip)
            {
                case "jakb-darkjak-get-on-fast": return currentClip;
                case "jakb-darkjak-bomb-pre":
                case "jakb-darkjak-bomb":
                case "jakb-darkjak-bomb-loop":
                case "jakb-darkjak-bomb-land":
                case "jakb-darkjak-bomb-end": return "jakb-darkjak-bomb-end";
                default: return "jakb-darkjak-get-off";
            }
        }

        private static float Range(float value, float begin, float end) => Math.Max(0f, Math.Min(1f, (value - begin) / (end - begin)));
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static void Samples(float frame, int count, out int first, out int second, out float fraction)
        {
            frame = Math.Max(0f, Math.Min(frame, count - 1));
            first = Math.Min((int)frame, count - 1);
            second = Math.Min(first + 1, count - 1);
            fraction = first == second ? 0f : frame - (int)frame;
        }
    }
}
