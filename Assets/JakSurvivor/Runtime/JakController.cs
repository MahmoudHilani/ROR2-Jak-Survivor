using System;
using System.Reflection;
using RoR2;
using UnityEngine;

namespace JakSurvivor
{
    // Native timed buffs replicate form (including to joining peers). Only the selected
    // secondary's BASE pool is shared with this controller's Dark Bomb override;
    // replacement/network overrides retain their independent native pools.
    public sealed class JakController : MonoBehaviour
    {
        private static readonly FieldInfo OverridesField = typeof(GenericSkill).GetField("skillOverrides", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(GenericSkill).FullName, "skillOverrides");
        private CharacterBody body;
        private SkillLocator skills;
        private JakVisuals visuals;
        private JakBoardRig rig;
        private JakWeaponCombat combat;
        private bool transformed, changingSkills;
        private GenericSkill.SkillOverride bombPool;
        private Run.FixedTimeStamp? darkBlastSince;

        internal Run.FixedTimeStamp SpinHop = Run.FixedTimeStamp.negativeInfinity;
        private void Awake()
        {
            body = GetComponent<CharacterBody>();
            skills = GetComponent<SkillLocator>();
            combat = GetComponent<JakWeaponCombat>();
        }
        private void OnEnable()
        {
            if (skills && skills.primary) skills.primary.onSkillChanged += SkillChanged;
            if (skills && skills.secondary) skills.secondary.onSkillChanged += SkillChanged;
        }
        private void Start()
        {
            Transform model = GetComponent<ModelLocator>().modelTransform;
            visuals = JakVisuals.Find(model);
            rig = model ? model.GetComponent<JakBoardRig>() : null;
            if (rig) rig.PhaseChanged += BoardPhaseChanged;
            // The prefab adds the network component after the controller.
            combat = GetComponent<JakWeaponCombat>();
            UpdateForm();
            RestorePrimaryWeaponMode();
        }
        private void FixedUpdate() => UpdateForm();
        private void BoardPhaseChanged()
        {
            if (rig.Phase == JakBoardPhase.Riding && combat) combat.CancelUncommitted();
        }
        private void SkillChanged(GenericSkill slot)
        {
            if (!changingSkills && combat) combat.CancelUncommitted();
            if (!changingSkills) RestorePrimaryWeaponMode();
        }
        public void RestorePrimaryWeaponMode()
        {
            if (visuals && skills && skills.primary)
                visuals.SetWeaponMode(skills.primary.skillDef == JakContent.ArcWielder ? JakWeaponMode.ArcWielder : JakWeaponMode.Blaster);
        }
        private void UpdateForm()
        {
            bool shouldTransform = body && body.healthComponent && body.healthComponent.alive && JakContent.DarkBuff && body.HasBuff(JakContent.DarkBuff);
            if (shouldTransform != transformed)
            {
                if (combat) combat.CancelUncommitted(); // Before capturing stock or changing overrides.
                transformed = shouldTransform;
                changingSkills = true;
                if (transformed)
                {
                    skills.primary.SetSkillOverride(this, JakContent.DarkClaws, GenericSkill.SkillOverridePriority.Contextual);
                    GenericSkill secondary = skills.secondary;
                    secondary.SetSkillOverride(this, JakContent.DarkBomb, GenericSkill.SkillOverridePriority.Contextual);
                    bombPool = FindPool(secondary, JakContent.DarkBomb);
                    bombPool.stock = secondary.baseStock;
                    bombPool.rechargeStopwatch = secondary.baseRechargeStopwatch;
                    skills.special.SetSkillOverride(this, JakContent.DarkBlast, GenericSkill.SkillOverridePriority.Contextual);
                    // Initialize our pool even when a higher-priority replacement owns the slot.
                    int bonusStock = skills.special.GetBaseMaxStock() - skills.special.baseSkill.GetMaxStock(skills.special);
                    FindPool(skills.special, JakContent.DarkBlast).stock = JakContent.DarkBlast.GetMaxStock(skills.special) + bonusStock;
                    darkBlastSince = Run.FixedTimeStamp.now;
                }
                else
                {
                    if (body.healthComponent && body.healthComponent.alive) JakAudio.Play("dark-exit", gameObject);
                    ReturnBombCharges();
                    RestoreSkills();
                }
                changingSkills = false;
                if (visuals) visuals.SetDark(transformed);
                RestorePrimaryWeaponMode();
            }
            if (transformed) ReturnBombCharges();
        }
        private GenericSkill.SkillOverride FindPool(GenericSkill slot, RoR2.Skills.SkillDef definition)
        {
            // GenericSkill exposes baseStock but no override-pool accessor. Read this one
            // native seam, not slot.stock: PickCurrentOverride changes its index BEFORE
            // OnUnassigned/onSkillChanged, so slot.stock may already belong to Heresy.
            foreach (GenericSkill.SkillOverride pool in (GenericSkill.SkillOverride[])OverridesField.GetValue(slot))
                if (ReferenceEquals(pool.source, this) && pool.skillDef == definition && pool.priority == GenericSkill.SkillOverridePriority.Contextual)
                    return pool;
            throw new InvalidOperationException(definition.skillName + "'s native override pool was not installed.");
        }
        private void ReturnBombCharges()
        {
            // DarkBombSkillDef sizes and times the override pool as the base, so it maps 1:1.
            if (bombPool == null || !skills || !skills.secondary) return;
            skills.secondary.baseStock = bombPool.stock;
            skills.secondary.baseRechargeStopwatch = bombPool.rechargeStopwatch;
        }
        private void RestoreSkills()
        {
            if (!skills) return;
            if (skills.primary) skills.primary.UnsetSkillOverride(this, JakContent.DarkClaws, GenericSkill.SkillOverridePriority.Contextual);
            if (skills.secondary) skills.secondary.UnsetSkillOverride(this, JakContent.DarkBomb, GenericSkill.SkillOverridePriority.Contextual);
            bombPool = null;
            if (skills.special && darkBlastSince.HasValue)
            {
                // RoR2 freezes an overridden base skill's recharge; native SkillStateOverrideData catches it up on clear.
                skills.special.UnsetSkillOverride(this, JakContent.DarkBlast, GenericSkill.SkillOverridePriority.Contextual);
                skills.special.RechargeBaseSkill(darkBlastSince.Value.timeSince);
            }
            darkBlastSince = null;
        }
        private void OnDisable()
        {
            if (skills && skills.primary) skills.primary.onSkillChanged -= SkillChanged;
            if (skills && skills.secondary) skills.secondary.onSkillChanged -= SkillChanged;
            if (combat) combat.CancelUncommitted();
            changingSkills = true;
            ReturnBombCharges();
            RestoreSkills();
            changingSkills = false;
            transformed = false;
            if (visuals) visuals.SetDark(false);
            RestorePrimaryWeaponMode();
        }
        private void OnDestroy() { if (rig) rig.PhaseChanged -= BoardPhaseChanged; }
    }
}
