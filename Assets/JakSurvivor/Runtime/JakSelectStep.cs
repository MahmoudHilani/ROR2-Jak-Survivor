using UnityEngine;

namespace JakSurvivor
{
    // Times are seconds into the state.
    internal sealed class JakSelectStep : StateMachineBehaviour
    {
        public string sound = "";
        public float gain;
        public bool gun;
        public float darkFrom = float.PositiveInfinity;
        public float darkUntil = float.PositiveInfinity;
        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            JakSelectDisplay display = animator.GetComponentInParent<JakSelectDisplay>();
            if (display) display.EnterStep(sound, gain, gun, darkFrom, darkUntil);
        }
        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            JakSelectDisplay display = animator.GetComponentInParent<JakSelectDisplay>();
            if (display) display.UpdateStep(stateInfo.normalizedTime * stateInfo.length, gun, darkFrom, darkUntil);
        }
    }
}
