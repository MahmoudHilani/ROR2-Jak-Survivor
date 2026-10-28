using System;
using System.Collections.Generic;
using UnityEngine;

namespace JakSurvivor
{
    [DefaultExecutionOrder(200)]
    public sealed class JakFace : MonoBehaviour
    {
        public JakFaceData data;
        public SkinnedMeshRenderer[] renderers;
        public Renderer leftEye, rightEye;
        public Transform horns;
        public float ScarfInterp, GogglesInterp;
        public float DarkInterp { get; private set; }
        public string CurrentClip { get; private set; }
        public float CurrentFrame { get; private set; }
        public JakEyeState Left => simulation.Left;
        public JakEyeState Right => simulation.Right;
        private JakVisuals visuals;
        private JakFaceSimulation simulation;
        private readonly float[] weights = new float[JakFaceSimulation.TargetCount];
        private readonly List<AnimatorClipInfo> clips = new List<AnimatorClipInfo>(32);
        private struct Binding
        {
            public JakFaceTrack Body, Eyes;
            public float Trim, TimeScale;
            public bool FreezeEyes, Held;
        }
        private Dictionary<AnimationClip, Binding> bindings;
        private Dictionary<string, JakFaceTrack> tracks;
        private int[][] targets;
        private float[][] appliedWeights;
        private struct EyeRenderState { internal bool Valid; internal Vector4 Eye, Lid; }
        private EyeRenderState leftRendered, rightRendered;
        private readonly Plane[] frustum = new Plane[6];
        private int renderedFrame = -1;
        private RenderTexture leftTexture, rightTexture;
        private Material compositor;
        private MaterialPropertyBlock properties;
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");

        private void Awake()
        {
            visuals = GetComponent<JakVisuals>();
            simulation = new JakFaceSimulation((uint)GetInstanceID());
            tracks = new Dictionary<string, JakFaceTrack>(StringComparer.Ordinal);
            foreach (JakFaceTrack track in data.tracks) tracks.Add(track.name, track);
            bindings = new Dictionary<AnimationClip, Binding>();
            foreach (AnimationClip clip in data.clips)
            {
                string source = SourceClip(clip.name, out float trim);
                if (!tracks.TryGetValue(source, out JakFaceTrack track)) continue;
                // Match the importer using actual raw clip duration and internal joint
                // frame count, including exported rounding, rather than a bake rate.
                if (source == JakAnimator.Weapon.WaveBody && clip.name != source)
                {
                    float frame = clip.name.EndsWith(JakAnimator.Weapon.Release, StringComparison.Ordinal) ? JakAnimator.Weapon.WaveReleaseFrame :
                        clip.name.EndsWith(JakAnimator.Weapon.ChargeLoop, StringComparison.Ordinal) ? JakAnimator.Weapon.WaveLoopStartFrame : 0f;
                    AnimationClip raw = Array.Find(data.clips, c => c.name == source);
                    trim = JakAnimator.Weapon.FrameTime(raw.length, track.frames, frame);
                }
                JakFaceTrack eyes = track;
                bool armedJump = clip.name.EndsWith("@armed", StringComparison.Ordinal);
                bool freeze = clip.name == "jakb-jump-fall@armed";
                if (armedJump) eyes = tracks[freeze ? "jakb-gun-stance-yellow" : "jakb-gun-front-jump"];
                bool arc = clip.name.EndsWith("@arc", StringComparison.Ordinal);
                if (arc) eyes = tracks[JakAnimator.Weapon.ArcIdle];
                float timeScale = clip.name == JakAnimator.Weapon.WaveBody + JakAnimator.Weapon.Release ?
                    (Array.Find(data.clips, c => c.name == source).length - trim) / clip.length : 1f;
                if (clip.name.StartsWith("jakb-gun-front-run@back", StringComparison.Ordinal))
                {
                    trim = clip.length * 0.5f;
                    timeScale = -1f;
                }
                bindings.Add(clip, new Binding { Body = track, Eyes = eyes, Trim = trim, TimeScale = timeScale, FreezeEyes = freeze || arc,
                    Held = clip.name == "jakb-jump-fall@armed" || clip.name == "jakb-jump-fall@arc" || clip.name == "jakb-darkjak-jump-fall" || clip.name == "jakb-board-jump-fall" });
            }
            targets = new int[renderers.Length][];
            appliedWeights = new float[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                Mesh mesh = renderers[i].sharedMesh;
                targets[i] = new int[mesh.blendShapeCount];
                appliedWeights[i] = new float[targets[i].Length];
                for (int j = 0; j < targets[i].Length; j++)
                {
                    targets[i][j] = int.Parse(mesh.GetBlendShapeName(j).Substring("Jak3Target_".Length));
                    appliedWeights[i][j] = float.NaN;
                }
            }
        }

        private void EnsureEyeResources()
        {
            if (compositor) return;
            properties = new MaterialPropertyBlock();
            compositor = new Material(data.compositor);
            compositor.SetTexture("_Iris", data.iris);
            compositor.SetTexture("_Pupil", data.pupil);
            compositor.SetTexture("_Lid", data.lid);
            leftTexture = CreateEye("JakLeftEye");
            rightTexture = CreateEye("JakRightEye");
        }

        private static RenderTexture CreateEye(string name)
        {
            var texture = new RenderTexture(32, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false };
            texture.Create();
            return texture;
        }

        private void LateUpdate() => Apply(Time.deltaTime, false);

        // Explicit animation sampling (captures and editor tools) also refreshes its render state.
        public void Apply(float elapsed) => Apply(elapsed, true);
        private void Apply(float elapsed, bool forceRender)
        {
            if (simulation == null || !visuals.Animator) return;
            Animator animator = visuals.Animator;
            SampleAnimator(animator, false, out Binding body, out float bodyTime);
            SampleAnimator(animator, true, out Binding eyes, out float eyeTime);
            JakFaceTrack track = body.Body;
            CurrentClip = track == null ? "" : track.name;
            CurrentFrame = Frame(track, bodyTime);
            float artistFrame = track == null ? 0f : track.artistBase + CurrentFrame * track.artistStep;
            DarkInterp = JakFaceSimulation.DarkInterpolation(CurrentClip, artistFrame, visuals.IsDark, DarkInterp, elapsed);
            // foreground.gc:1562 and eye.gc:951-972 select root and highest float channel independently.
            simulation.Evaluate(track, CurrentFrame, data.companion ? 0f : DarkInterp, ScarfInterp, GogglesInterp, elapsed, weights,
                eyes.Eyes, Frame(eyes.Eyes, eyes.FreezeEyes ? 0f : eyeTime));
            // Keep the logical face and Dark interpolation advancing even when no camera sees it.
            if (horns) horns.localScale = Vector3.one * DarkInterp;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            bool visible = leftEye && leftEye.isVisible || rightEye && rightEye.isVisible;
            for (int i = 0; i < renderers.Length; i++) visible |= renderers[i] && renderers[i].isVisible;
            if (forceRender || visible) RenderFace();
        }

        private void RenderFace()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            renderedFrame = Time.frameCount;
            for (int i = 0; i < renderers.Length; i++)
                for (int j = 0; j < targets[i].Length; j++)
                {
                    float value = weights[targets[i][j]] * 100f;
                    if (appliedWeights[i][j] == value) continue;
                    renderers[i].SetBlendShapeWeight(j, value);
                    appliedWeights[i][j] = value;
                }
            EnsureEyeResources();
            RenderEye(leftEye, leftTexture, simulation.Left, false, ref leftRendered);
            RenderEye(rightEye, rightTexture, simulation.Right, true, ref rightRendered);
        }

        private void OnEnable()
        {
            Camera.onPreCull += PrepareCamera;
            renderedFrame = -1;
            leftRendered.Valid = rightRendered.Valid = false;
            if (appliedWeights != null)
                foreach (float[] sent in appliedWeights)
                    for (int j = 0; j < sent.Length; j++) sent[j] = float.NaN;
        }

        private void OnDisable() => Camera.onPreCull -= PrepareCamera;
        private void PrepareCamera(Camera camera)
        {
            // isVisible describes the previous render. Refresh before the first camera
            // that sees a returning character, avoiding a stale expression for one frame.
            if (renderedFrame == Time.frameCount || simulation == null || !camera) return;
            GeometryUtility.CalculateFrustumPlanes(camera, frustum);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                // CharacterModel can re-enable renderers later in this camera's pre-render.
                if (!renderer || !renderer.gameObject.activeInHierarchy ||
                    (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0 || !GeometryUtility.TestPlanesAABB(frustum, renderer.bounds)) continue;
                RenderFace(); return;
            }
        }

        private static float Frame(JakFaceTrack track, float time) => track == null ? 0f : Mathf.Min(time * track.speed * 60f, track.frames - 1);

        private void SampleAnimator(Animator animator, bool includeGesture, out Binding binding, out float time)
        {
            binding = default;
            time = 0f;
            foreach (string name in JakAnimator.PoseLayers)
            {
                if (binding.Body != null) break;
                if (!includeGesture && name == JakAnimator.Layers.Gesture) continue;
                int layer = animator.GetLayerIndex(name);
                if (layer < 0) continue;
                bool next = animator.IsInTransition(layer);
                clips.Clear();
                if (next) animator.GetNextAnimatorClipInfo(layer, clips);
                else animator.GetCurrentAnimatorClipInfo(layer, clips);
                if (next && clips.Count == 0)
                {
                    next = false;
                    animator.GetCurrentAnimatorClipInfo(layer, clips);
                }
                float dominant = -1f;
                for (int i = 0; i < clips.Count; i++)
                {
                    AnimationClip clip = clips[i].clip;
                    if (!bindings.TryGetValue(clip, out Binding candidate) || clips[i].weight <= dominant) continue;
                    dominant = clips[i].weight;
                    binding = candidate;
                    AnimatorStateInfo state = next ? animator.GetNextAnimatorStateInfo(layer) : animator.GetCurrentAnimatorStateInfo(layer);
                    float normalized = clip.isLooping ? state.normalizedTime - Mathf.Floor(state.normalizedTime) : Mathf.Clamp01(state.normalizedTime);
                    time = candidate.Held ? candidate.Trim : normalized * clip.length * candidate.TimeScale + candidate.Trim;
                    if (candidate.TimeScale < 0f) time = Mathf.Repeat(time, clip.length);
                    if (clip.name == JakAnimator.Bomb.Clip)
                    {
                        JakFaceTrack windup = tracks["jakb-darkjak-bomb"], landing = tracks["jakb-darkjak-bomb-land"];
                        JakAnimator.Bomb.Sample(time, (windup.frames - 1) / (windup.speed * 60f), (landing.frames - 1) / (landing.speed * 60f),
                            out float windupTime, out float landingTime, out float weight);
                        binding.Body = binding.Eyes = weight < 0.5f ? windup : landing;
                        time = weight < 0.5f ? windupTime : landingTime;
                    }
                }
            }
        }

        private static string SourceClip(string name, out float trim)
        {
            trim = 0f;
            if (name.StartsWith("jakb-gun-front-run@", StringComparison.Ordinal)) return "jakb-gun-front-run";
            if (JakSpinRecipe.TryFaceSource(name, out string spinSource)) return spinSource;
            foreach (string locomotion in JakAnimator.Weapon.ArcLocomotion)
                if (name == JakAnimator.Weapon.ArcClip(locomotion)) return SourceClip(locomotion, out trim);
            if (name == JakAnimator.Weapon.WaveBody + JakAnimator.Weapon.ChargeStart ||
                name == JakAnimator.Weapon.WaveBody + JakAnimator.Weapon.ChargeLoop || name == JakAnimator.Weapon.WaveBody + JakAnimator.Weapon.Release)
                return JakAnimator.Weapon.WaveBody;
            switch (name)
            {
                case "jakb-jump@armed": return "jakb-jump";
                case "jakb-jump-forward@armed": return "jakb-jump-forward";
                case "jakb-jump-fall@armed": trim = JakAnimator.JumpAirTime; return "jakb-jump";
                case "jakb-jump-double@armed": trim = 5f / 60f; return "jakb-jump";
                case "jakb-jump-forward-double@armed": trim = 5f / 60f; return "jakb-jump-forward";
                case "jakb-darkjak-jump-double": trim = 7f / 60f; return "jakb-darkjak-jump";
                case "jakb-darkjak-jump@takeoff": return "jakb-darkjak-jump";
                case "jakb-board-jump@takeoff": return "jakb-board-jump";
                case "jakb-darkjak-jump-fall": trim = JakAnimator.JumpAirTime; return "jakb-darkjak-jump";
                case "jakb-board-jump-fall": trim = JakAnimator.BoardAirTime; return "jakb-board-jump";
                case JakAnimator.Bomb.Clip: return "jakb-darkjak-bomb";
                case "jakb-darkjak-attack-combo1@strike": return "jakb-darkjak-attack-combo1";
                case "jakb-darkjak-attack-combo2@strike": return "jakb-darkjak-attack-combo2";
                case "jakb-darkjak-attack-combo3c@strike": return "jakb-darkjak-attack-combo3c";
                default: return name;
            }
        }

        private void RenderEye(Renderer renderer, RenderTexture texture, JakEyeState eye, bool mirror, ref EyeRenderState rendered)
        {
            if (!renderer) return;
            var eyeValue = new Vector4(eye.X, eye.Y, eye.IrisScale, eye.PupilScale);
            // eye.gc:387-398: the right lid also uses the left lid-scale.
            var lidValue = new Vector4(eye.Lid, simulation.Left.LidScale, mirror ? 1f : 0f, 0f);
            if (!rendered.Valid || !texture.IsCreated() || !rendered.Eye.Equals(eyeValue) || !rendered.Lid.Equals(lidValue))
            {
                if (!texture.IsCreated()) texture.Create();
                compositor.SetVector("_Eye", eyeValue);
                compositor.SetVector("_LidState", lidValue);
                bool previous = GL.sRGBWrite;
                RenderTexture previousTarget = RenderTexture.active;
                try
                {
                    GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                    Graphics.Blit(null, texture, compositor);
                }
                finally { GL.sRGBWrite = previous; RenderTexture.active = previousTarget; }
                rendered.Eye = eyeValue; rendered.Lid = lidValue; rendered.Valid = true;
            }
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(MainTex, texture);
            renderer.SetPropertyBlock(properties);
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= PrepareCamera;
            if (leftTexture) { leftTexture.Release(); Destroy(leftTexture); }
            if (rightTexture) { rightTexture.Release(); Destroy(rightTexture); }
            if (compositor) Destroy(compositor);
        }
    }
}
