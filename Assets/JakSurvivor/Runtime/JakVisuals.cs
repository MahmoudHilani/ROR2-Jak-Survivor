using RoR2;
using UnityEngine;

namespace JakSurvivor
{
    [DefaultExecutionOrder(100)]
    public sealed class JakVisuals : MonoBehaviour
    {
        public Renderer[] bodyRenderers, hornRenderers;
        public Material[] armedMaterials, darkMaterials;
        public GameObject gun, board;
        private Animator animator;
        private CharacterModel model;
        private ModelSkinController skinController;
        private JakBoardRig rig;
        private JakFace face;
        private bool dark;
        private JakWeaponMode weaponMode;
        private bool locomotionDirectionInitialized;
        public bool IsDark => dark;
        public JakWeaponMode WeaponMode => weaponMode;
        public void SetWeaponMode(JakWeaponMode mode)
        {
            weaponMode = mode;
            if (Animator && Animator.GetLayerIndex(JakAnimator.Layers.Gun) >= 0)
                Animator.SetFloat(JakAnimator.Parameters.WeaponMode, (float)mode);
        }
        public Animator Animator => animator ? animator : (animator = GetComponent<Animator>());
        public int ExtractedClipCount => Animator && Animator.runtimeAnimatorController ? Animator.runtimeAnimatorController.animationClips.Length : 0;
        public string CurrentAnimation
        {
            get
            {
                if (!Animator) return "";
                foreach (string name in JakAnimator.PoseLayers)
                {
                    int layer = Animator.GetLayerIndex(name);
                    if (layer < 0) continue;
                    AnimatorClipInfo[] clips = Animator.GetCurrentAnimatorClipInfo(layer);
                    if (clips.Length > 0)
                    {
                        AnimatorClipInfo dominant = clips[0];
                        foreach (AnimatorClipInfo clip in clips) if (clip.weight > dominant.weight) dominant = clip;
                        return dominant.clip.name;
                    }
                }
                return "";
            }
        }
        public Transform ClawSlashAnchor(int step) => transform.Find("JakClawSlash" + step);
        public static JakVisuals Find(Transform model) => model ? model.GetComponent<JakVisuals>() : null;
        private void Awake()
        {
            animator = GetComponent<Animator>();
            model = GetComponent<CharacterModel>();
            rig = GetComponent<JakBoardRig>();
            face = GetComponent<JakFace>();
            rig.PhaseChanged += ApplyAppearance;
            skinController = GetComponent<ModelSkinController>();
            if (skinController) skinController.onSkinApplied += OnSkinApplied;
        }
        private void Start() => ApplyAppearance();
        private void Update() => UpdateLocomotionDirection(Time.deltaTime);
        internal void UpdateLocomotionDirection(float deltaTime = 0f)
        {
            Animator value = Animator;
            if (!value || value.GetLayerIndex(JakAnimator.Layers.Gun) < 0) return;
            // Preserve heading through the neutral frame between opposite keys.
            // Normalize the target so analog magnitude does not alter a steady gait.
            Vector2 direction;
            CharacterModel valueModel = model ? model : (model = GetComponent<CharacterModel>());
            CharacterBody body = valueModel ? valueModel.body : null;
            if (body && body.inputBank)
            {
                // RoR2 smooths its animation direction as an angle. W -> S therefore
                // visits lateral poses even though the player never pressed A or D.
                Vector3 forward = body.characterDirection ? body.characterDirection.animatorForward : transform.forward;
                Vector3 movement = body.inputBank.moveVector;
                direction = new Vector2(Vector3.Dot(movement, Vector3.Cross(Vector3.up, forward)),
                    Vector3.Dot(movement, forward));
            }
            else direction = new Vector2(value.GetFloat("rightSpeed"), value.GetFloat("forwardSpeed"));
            if (direction.sqrMagnitude < 0.0001f)
            {
                if (locomotionDirectionInitialized) return;
                direction = Vector2.up;
            }
            else direction.Normalize();
            if (!locomotionDirectionInitialized || deltaTime <= 0f)
            {
                value.SetFloat(JakAnimator.Parameters.MoveRight, direction.x);
                value.SetFloat(JakAnimator.Parameters.MoveForward, direction.y);
            }
            else
            {
                // Ease pose weights through reversals instead of snapping unit vectors.
                value.SetFloat(JakAnimator.Parameters.MoveRight, direction.x, 0.12f, deltaTime);
                value.SetFloat(JakAnimator.Parameters.MoveForward, direction.y, 0.12f, deltaTime);
            }
            locomotionDirectionInitialized = true;
        }
        private void OnSkinApplied(int index) => ApplyAppearance();
        public void SetDark(bool value)
        {
            bool exiting = dark && !value && model && model.body && model.body.healthComponent.alive;
            dark = value;
            if (value && JakAnimator.HasWeaponAction(Animator)) JakAnimator.StopWeaponAction(Animator);
            ApplyAppearance();
            if (exiting && Animator)
            {
                string exitClip = JakFaceSimulation.ExitAnimation(face.CurrentClip);
                if (exitClip == "jakb-darkjak-get-on-fast") return;
                Animator.SetFloat(JakAnimator.Parameters.TransformRate, 1f);
                Animator.CrossFadeInFixedTime(exitClip == "jakb-darkjak-bomb-end" ? JakAnimator.States.DarkExitBomb : JakAnimator.States.DarkExit,
                    0.1f, Animator.GetLayerIndex(JakAnimator.Layers.Transformation));
            }
        }
        public void SetSelectionAppearance(bool value, bool showGun) { SetDark(value); if (gun) gun.SetActive(showGun && rig.Phase == JakBoardPhase.Off); }
        private void ApplyAppearance()
        {
            // board/board-states.gc:3104-3105: close the board at the end of get-off.
            bool onBoard = rig.Phase != JakBoardPhase.Off;
            if (onBoard && JakAnimator.HasWeaponAction(Animator)) JakAnimator.StopWeaponAction(Animator);
            SetWeaponMode(weaponMode);
            if (Animator)
                foreach (AnimatorControllerParameter parameter in Animator.parameters)
                    if (parameter.name == JakAnimator.Parameters.Form)
                    {
                        Animator.SetFloat(JakAnimator.Parameters.Form, onBoard ? JakAnimator.Board : dark ? JakAnimator.Dark : JakAnimator.Armed);
                        break;
                    }
            if (gun) gun.SetActive(!dark && !onBoard);
            if (board) board.SetActive(onBoard);
            if (hornRenderers != null) foreach (Renderer horn in hornRenderers) if (horn) horn.gameObject.SetActive(true);
            Material[] materials = dark ? darkMaterials : armedMaterials;
            if (bodyRenderers != null && materials != null)
                for (int i = 0; i < bodyRenderers.Length && i < materials.Length; i++)
                    if (bodyRenderers[i] && materials[i])
                    {
                        bodyRenderers[i].sharedMaterial = materials[i];
                        if (model) JakModel.SetMaterial(model.baseRendererInfos, bodyRenderers[i], materials[i]);
                    }
            if (model) model.forceUpdate = true;
        }
        private void OnDisable()
        {
            if (Animator && Animator.isActiveAndEnabled && JakAnimator.HasWeaponAction(Animator)) JakAnimator.StopWeaponAction(Animator);
        }
        private void OnDestroy() { if (skinController) skinController.onSkinApplied -= OnSkinApplied; if (rig) rig.PhaseChanged -= ApplyAppearance; }
    }
}
