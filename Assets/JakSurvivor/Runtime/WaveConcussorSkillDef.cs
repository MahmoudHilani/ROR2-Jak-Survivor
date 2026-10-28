using RoR2;
using RoR2.Skills;

namespace JakSurvivor
{
    // No activation reservation. The starting GenericSkill is debited only when a release
    // commits; interruption needs neither a refund nor a second, non-native resource pool.
    public sealed class WaveConcussorSkillDef : SkillDef
    {
        public override bool CanExecute(GenericSkill slot) =>
            !(slot.stateMachine && (slot.stateMachine.state is States.WaveConcussor || slot.stateMachine.state is States.WaveRecovery)) &&
            JakWeaponCombat.CanUseWeapons(slot.characterBody) && base.CanExecute(slot);
    }
}
