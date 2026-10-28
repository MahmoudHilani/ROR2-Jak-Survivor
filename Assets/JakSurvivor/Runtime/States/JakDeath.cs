using EntityStates;

namespace JakSurvivor.States
{
    public sealed class JakDeath : GenericCharacterDeath
    {
        public override void OnEnter()
        {
            // target-board.gc:1018-1058,1104; target-handler.gc:1835: death exits board mode and clears draw-offset.
            var model = GetModelTransform();
            if (model) model.GetComponent<JakBoardRig>().SetPhase(JakBoardPhase.Off);
            base.OnEnter();
        }

        protected override void PlayDeathAnimation(float crossfadeDuration = 0.1f)
        {
            var animator = GetModelAnimator();
            if (animator)
            {
                animator.speed = 1f;
                foreach (string layer in JakAnimator.PoseLayers)
                    if (layer != JakAnimator.Layers.Body) animator.Play(JakAnimator.States.Empty, animator.GetLayerIndex(layer));
            }
            base.PlayDeathAnimation(crossfadeDuration);
        }
    }
}
