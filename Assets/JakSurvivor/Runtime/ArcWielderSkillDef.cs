using EntityStates;
using RoR2;
using RoR2.Skills;

namespace JakSurvivor
{
    public sealed class ArcWielderSkillDef : SkillDef
    {
        public override bool CanExecute(GenericSkill slot) => !IsAlreadyInState(slot) &&
            JakWeaponCombat.CanUseWeapons(slot.characterBody) && base.CanExecute(slot);
    }
}
