using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace JakSurvivor.States
{
    public sealed class ArcWielder : BaseSkillState
    {
        private JakWeaponCombat combat;
        private EntityStateMachine body;
        private JakVisuals visuals;
        private uint id;
        private float sampledRate;
        private bool accepted;

        public override void OnEnter()
        {
            base.OnEnter();
            body = EntityStateMachine.FindByCustomName(gameObject, "Body");
            combat = GetComponent<JakWeaponCombat>();
            visuals = JakVisuals.Find(GetModelTransform());
            if (isAuthority)
            {
                id = combat.AllocateActionId();
                sampledRate = JakWeaponRules.SampleRate(attackSpeedStat);
            }
            accepted = combat.Begin(JakWeaponCombat.Kind.Arc, id, sampledRate, fixedAge);
            if (!accepted) { if (isAuthority) outer.SetNextStateToMain(); return; }
            if (visuals)
            {
                visuals.SetWeaponMode(JakWeaponMode.ArcWielder);
                if (fixedAge > 0f) JakAnimator.ArcLoop(visuals.Animator, sampledRate);
                else JakAnimator.ArcStart(visuals.Animator, sampledRate);
            }
            JakShot shot = JakAim.Shot(body, GetAimRay());
            if (!shot.Spinning) StartAimMode(2f);
        }
        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!accepted) return;
            if (!JakWeaponCombat.CanUseWeapons(characterBody) || !activatorSkillSlot || activatorSkillSlot.skillDef != JakContent.ArcWielder ||
                isAuthority && !IsKeyDownAuthority())
            {
                combat.End(id);
                if (isAuthority) outer.SetNextStateToMain();
                return;
            }
            JakShot shot = JakAim.Shot(body, GetAimRay());
            if (!shot.Spinning) StartAimMode(2f);
        }
        public override void OnExit()
        {
            if (combat) combat.End(id);
            if (visuals) JakAnimator.StopWeaponAction(visuals.Animator);
            JakController controller = GetComponent<JakController>();
            if (controller) controller.RestorePrimaryWeaponMode();
            base.OnExit();
        }
        public override void OnSerialize(NetworkWriter writer)
        {
            base.OnSerialize(writer);
            writer.Write(id); writer.Write(sampledRate); writer.Write(fixedAge);
        }
        public override void OnDeserialize(NetworkReader reader)
        {
            base.OnDeserialize(reader);
            id = reader.ReadUInt32(); sampledRate = reader.ReadSingle(); fixedAge = reader.ReadSingle();
        }
        // Primary activation uses Any, so held primary cannot re-enter this state;
        // secondary and transformation still interrupt the channel.
        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }
}
