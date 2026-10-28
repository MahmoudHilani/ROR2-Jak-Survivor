using EntityStates;
using RoR2;
using RoR2.Skills;

namespace JakSurvivor
{
    public sealed class JakMovementSkillDef : SkillDef
    {
        private sealed class Instance : BaseSkillInstanceData
        {
            public EntityStateMachine Weapon;
        }

        public override BaseSkillInstanceData OnAssigned(GenericSkill slot) =>
            new Instance { Weapon = EntityStateMachine.FindByCustomName(slot.gameObject, "Weapon") };

        public override bool CanExecute(GenericSkill slot) => base.CanExecute(slot) &&
            slot.skillInstanceData is Instance data && data.Weapon && data.Weapon.CanInterruptState(InterruptPriority.Skill);
    }
}
