using RoR2;
using UnityEngine;

namespace JakSurvivor
{
    [DefaultExecutionOrder(205)]
    public sealed class JakDaxter : MonoBehaviour
    {
        // The importer names each actor root "Jak3_" + its model name.
        public const string ActorName = "Jak3_daxter";
        public Transform actor, shoulder, board;
        public JakBoardRig rig;
        private Animator animator;
        private AimAnimator aim;
        private JakFace face;
        // sidekick.gc:356-360: native meters, reflected across X like the actor.
        private static readonly Vector3 DarkShoulderOffset = new Vector3(-.07f, .05f, .1f);

        public bool OnBoard
        {
            get
            {
                if (!rig || rig.Phase != JakBoardPhase.Riding) return false;
                if (!animator) animator = GetComponent<Animator>();
                int layer = animator.GetLayerIndex(JakAnimator.Layers.FullBody);
                if (layer < 0)
                {
                    AnimatorStateInfo preview = animator.GetCurrentAnimatorStateInfo(0);
                    return !preview.IsName("jakb-board-get-on") && !preview.IsName("jakb-board-get-off");
                }
                AnimatorStateInfo state = animator.IsInTransition(layer) ? animator.GetNextAnimatorStateInfo(layer) : animator.GetCurrentAnimatorStateInfo(layer);
                return !state.IsName(JakAnimator.States.BoardOn) || state.normalizedTime >= 1f;
            }
        }

        private void LateUpdate() => Apply();

        public void Apply()
        {
            if (!animator) animator = GetComponent<Animator>();
            // sidekick.gc uses node 34's translation while on Jak's shoulder,
            // and node 37's normalized matrix while riding. board-states.gc
            // switches after get-on, and back before playing get-off.
            bool riding = OnBoard;
            Transform anchor = riding ? board : shoulder;
            if (!actor || !anchor) return;
            float yaw = 0f;
            if (!aim) aim = GetComponent<AimAnimator>();
            int layer = animator.GetLayerIndex(JakAnimator.Layers.AimYaw);
            if (!riding && aim && aim.enabled && layer >= 0)
            {
                // sidekick.gc copies Jak's upper-body yaw into the sidekick's root.
                // AimAnimator encodes its smoothed angle over a 0..0.999 cycle.
                yaw = Mathf.Lerp(aim.yawRangeMin, aim.yawRangeMax, animator.GetFloat("aimYawCycle") / .999f) *
                    Mathf.Clamp01(1f - animator.GetFloat(JakAnimator.Parameters.Form)) * animator.GetLayerWeight(layer);
            }
            actor.rotation = riding ? board.rotation : actor.parent.rotation * Quaternion.AngleAxis(yaw, Vector3.up);
            if (!face) face = GetComponent<JakFace>();
            // Face interpolation runs at 200. Use this frame's value, and the
            // actor's unit normalization, for the native rotated clearance.
            actor.position = anchor.position + (!riding && face ? actor.TransformVector(DarkShoulderOffset * face.DarkInterp) : Vector3.zero);
        }
    }
}
