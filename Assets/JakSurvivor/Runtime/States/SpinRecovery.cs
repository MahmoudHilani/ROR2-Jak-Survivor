using EntityStates;
using UnityEngine.Networking;

namespace JakSurvivor.States
{
    public sealed class SpinRecovery : JakMain
    {
        internal float Rate;
        internal JakSpinVariant Variant;
        private JakSpinTimeline timeline;
        private JakMain continuation;
        public override void OnEnter()
        {
            base.OnEnter();
            timeline = JakContent.SpinCatalog.Timeline(Variant, Rate);
            if (JakSpinRecipe.Get(Variant).EndOverlay != null && !JakAnimator.HasWeaponAction(GetModelAnimator()))
                PlayCrossfade(JakAnimator.Layers.Gesture, JakAnimator.Spin.EndState(Variant), JakAnimator.Parameters.SpinRate, JakSpin.RecoverySeconds / Rate, 0.05f);
        }
        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (isAuthority && timeline.PhaseAt(timeline.PoseEnd + fixedAge) == JakSpinPhase.Done)
            { continuation = new JakMain(); outer.SetNextState(continuation); }
        }
        public override void OnExit() { continuation?.ContinueJump(jump); base.OnExit(); }
        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
        public override void OnSerialize(NetworkWriter writer)
        { base.OnSerialize(writer); writer.Write((byte)Variant); writer.Write(Rate); writer.Write(fixedAge); }
        public override void OnDeserialize(NetworkReader reader)
        { base.OnDeserialize(reader); Variant = (JakSpinVariant)reader.ReadByte(); Rate = reader.ReadSingle(); fixedAge = reader.ReadSingle(); }
    }
}
