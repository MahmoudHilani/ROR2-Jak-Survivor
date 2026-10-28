using RoR2;
using RoR2.Skills;

namespace JakSurvivor
{
    // Dark Bomb spends the selected secondary's pool, so the slot sizes and recharges the
    // override exactly as GenericSkill.GetBaseMaxStock/RechargeBaseSkill size the base pool.
    public sealed class DarkBombSkillDef : SkillDef
    {
        public override int GetMaxStock(GenericSkill slot) => slot.baseSkill.GetMaxStock(slot);
        public override int GetRechargeStock(GenericSkill slot) => slot.baseSkill.GetRechargeStock(slot);
        public override float GetRechargeInterval(GenericSkill slot) => slot.baseSkill.GetRechargeInterval(slot);
        public override float GetRechargeRate(GenericSkill slot) => slot.baseSkill.GetRechargeRate(slot);
    }
}
