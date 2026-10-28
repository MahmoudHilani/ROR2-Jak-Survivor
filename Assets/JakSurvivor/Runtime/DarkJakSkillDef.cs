using RoR2;
using RoR2.Skills;

namespace JakSurvivor
{
    public sealed class DarkJakSkillDef : SkillDef
    {
        public override bool IsReady(GenericSkill slot) => base.IsReady(slot) && !slot.characterBody.HasBuff(JakContent.DarkBuff);
    }
}
