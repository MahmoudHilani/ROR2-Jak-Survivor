using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EntityStates;
using JakSurvivor.States;
using R2API;
using RoR2;
using RoR2.ContentManagement;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace JakSurvivor
{
    public sealed class JakContent : IContentPackProvider
    {
        private readonly ContentPack content = new ContentPack();
        private readonly List<SkillDef> skills = new List<SkillDef>();
        private readonly List<SkillFamily> families = new List<SkillFamily>();
        private static readonly FieldInfo SkillFamilyField = typeof(GenericSkill).GetField("_skillFamily", BindingFlags.Instance | BindingFlags.NonPublic);
        public string identifier => Plugin.Guid;
        public static GameObject BodyPrefab { get; private set; }
        public static GameObject MasterPrefab { get; private set; }
        public static SurvivorDef Survivor { get; private set; }
        public static BuffDef DarkBuff { get; private set; }
        public static SkillDef ArcWielder { get; private set; }
        public static SkillDef WaveConcussor { get; private set; }
        public static SkillDef JetBoard { get; private set; }
        public static SkillDef Spin { get; private set; }
        public static JakSpinCatalog SpinCatalog { get; private set; }
        public static SkillDef Roll { get; private set; }
        public static SkillDef DarkClaws { get; private set; }
        public static SkillDef DarkBomb { get; private set; }
        public static SkillDef DarkBlast { get; private set; }
        public static GameObject ClawSlashEffect { get; private set; }
        public static GameObject ClawFinisherSlashEffect { get; private set; }

        public IEnumerator LoadStaticContentAsync(LoadStaticContentAsyncArgs args)
        {
            AssetBundleCreateRequest bundleRequest = JakAssets.BeginLoad();
            var bodyHandle = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoBody.prefab");
            var masterHandle = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoMonsterMaster.prefab");
            var materialHandle = Addressables.LoadAssetAsync<Material>(JakModel.MaterialTemplateKey);
            var slashHandle = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Croco/CrocoSlash.prefab");
            var finisherSlashHandle = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Croco/CrocoComboFinisherSlash.prefab");
            yield return bodyHandle;
            yield return masterHandle;
            yield return materialHandle;
            yield return slashHandle;
            yield return finisherSlashHandle;
            ClawSlashEffect = slashHandle.Result;
            ClawFinisherSlashEffect = finisherSlashHandle.Result;
            while (!bundleRequest.isDone) yield return null;
            JakAssets.EndLoad(bundleRequest);
            SpinCatalog = JakSpinCatalog.Parse(JakAssets.Get<JakSpinData>(JakAnimator.Spin.DataAsset).tracks);
            if (!bodyHandle.Result || !masterHandle.Result || !materialHandle.Result)
                throw new InvalidOperationException("Required base-game Commando assets could not be loaded.");
            JakEffects.LoadData();
            Plugin.Log.LogInfo("Using locally extracted Jak 3 assets (OpenGOAL v0.3.8).");
            JakModel.MaterialTemplate = materialHandle.Result;
            JakAudio.LoadBank();

            BodyPrefab = bodyHandle.Result.InstantiateClone("JakBody", true);
            var body = BodyPrefab.GetComponent<CharacterBody>();
            body.baseNameToken = "JAK_NAME";
            body.subtitleNameToken = "JAK_SUBTITLE";
            body.baseMaxHealth = 120f;
            body.levelMaxHealth = 36f;
            body.baseRegen = 1.5f;
            body.levelRegen = 0.3f;
            body.baseDamage = 12f;
            body.levelDamage = 2.4f;
            body.baseArmor = 0f;
            body.baseMoveSpeed = 7f;
            body.baseJumpCount = 2;
            body.baseJumpPower = JakJump.MaxGroundVelocity;
            BodyPrefab.GetComponent<SfxLocator>().jumpSound = "";
            BodyPrefab.GetComponents<EntityStateMachine>().First(m => m.customName == "Body").mainStateType = new SerializableEntityStateType(typeof(JakMain));
            BodyPrefab.GetComponent<CharacterDeathBehavior>().deathState = new SerializableEntityStateType(typeof(JakDeath));
            body.portraitIcon = Icons.Portrait.texture;
            body.bodyColor = new Color(0.24f, 0.65f, 0.84f);
            body.preferredInitialStateType = new SerializableEntityStateType(typeof(JakMain));

            JakItemDisplays.Load();
            JakModel.Install(BodyPrefab, JakAssets.Get<GameObject>(JakAnimator.ModelPrefab), JakItemDisplays.RuleSet);
            BodyPrefab.AddComponent<JakController>();
            BodyPrefab.AddComponent<JakWeaponCombat>();
            BodyPrefab.AddComponent<JakDarkCombat>();
            BodyPrefab.AddComponent<JakWeaponPresentation>();

            BuildBuff();
            BuildSkills(BodyPrefab.GetComponent<SkillLocator>());
            MasterPrefab = masterHandle.Result.InstantiateClone("JakMonsterMaster", true);
            MasterPrefab.GetComponent<CharacterMaster>().bodyPrefab = BodyPrefab;
            var display = JakAssets.Get<GameObject>(JakAnimator.DisplayPrefab).InstantiateClone("JakDisplay", false);
            JakModel.PrepareMaterials(display);
            display.GetComponentInChildren<CharacterModel>().itemDisplayRuleSet = JakItemDisplays.RuleSet;
            Survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            Survivor.cachedName = "JakSurvivor";
            Survivor.bodyPrefab = BodyPrefab;
            Survivor.displayPrefab = display;
            Survivor.displayNameToken = "JAK_NAME";
            Survivor.descriptionToken = "JAK_DESCRIPTION";
            Survivor.outroFlavorToken = "JAK_OUTRO";
            Survivor.mainEndingEscapeFailureFlavorToken = "JAK_FAILURE";
            Survivor.primaryColor = body.bodyColor;
            Survivor.desiredSortPosition = 25f;

            GameObject[] effects = JakEffects.Build();
            content.effectDefs.Add(effects.Select(prefab => new EffectDef(prefab)).ToArray());
            Plugin.Log.LogInfo("Using extracted Jak 3 VFX: " + effects.Length + " named registered effects.");

            content.bodyPrefabs.Add(new[] { BodyPrefab });
            content.masterPrefabs.Add(new[] { MasterPrefab });
            content.survivorDefs.Add(new[] { Survivor });
            content.buffDefs.Add(new[] { DarkBuff });
            content.skillDefs.Add(skills.ToArray());
            content.skillFamilies.Add(families.ToArray());
            content.entityStateTypes.Add(new[] { typeof(JakMain), typeof(JakDeath), typeof(Blaster), typeof(ScatterGun),
                typeof(States.ArcWielder), typeof(States.WaveConcussor), typeof(WaveRecovery),
                typeof(JetBoard), typeof(DodgeRoll), typeof(RollLeap), typeof(SpinAttack), typeof(SpinRecovery), typeof(EnterDarkJak), typeof(DarkClaws), typeof(DarkBomb), typeof(States.DarkBlast) });
            content.projectilePrefabs.Add(new[] { JakBlastProjectile.BuildPrefab() });
            Plugin.Log.LogInfo("Jak content built: body, master, survivor, four skill families with Arc Wielder, Wave Concussor and Jet-Board alternatives, Dark Jak buff.");
            args.ReportProgress(1f);
        }

        public IEnumerator GenerateContentPackAsync(GetContentPackAsyncArgs args)
        {
            ContentPack.Copy(content, args.output);
            args.ReportProgress(1f);
            yield break;
        }

        public IEnumerator FinalizeAsync(FinalizeAsyncArgs args)
        {
            args.ReportProgress(1f);
            yield break;
        }

        private void BuildBuff()
        {
            DarkBuff = ScriptableObject.CreateInstance<BuffDef>();
            DarkBuff.name = "JakDarkForm";
            DarkBuff.buffColor = new Color(0.64f, 0.24f, 0.94f);
            DarkBuff.iconSprite = Icons.DarkBuff;
            DarkBuff.canStack = false;
            DarkBuff.isDebuff = false;
        }

        private void BuildSkills(SkillLocator locator)
        {
            var blaster = MakeSkill<SkillDef>("Blaster", typeof(Blaster), "Weapon", Icons.Blaster, 0f, 1, 0);
            // Holding primary must not interrupt an active weapon attack.
            // Other weapon skills can still cancel its recovery phase.
            blaster.interruptPriority = InterruptPriority.Any;
            var scatter = MakeSkill<SkillDef>("Scatter", typeof(ScatterGun), "Weapon", Icons.Scatter, Balance.ScatterCooldown, 2, 1);
            scatter.mustKeyPress = true;
            ArcWielder = MakeSkill<ArcWielderSkillDef>("ArcWielder", typeof(States.ArcWielder), "Weapon", Icons.ArcWielder, 0f, 1, 0);
            ArcWielder.interruptPriority = InterruptPriority.Any;
            WaveConcussor = MakeSkill<WaveConcussorSkillDef>("WaveConcussor", typeof(States.WaveConcussor), "Weapon", Icons.WaveConcussor,
                Balance.WaveCooldown, Balance.WaveStocks, 0);
            // Charging neither reserves a stock nor announces an attack to items.
            // The originating skill is spent/announced exactly once on release.
            WaveConcussor.requiredStock = 1;
            WaveConcussor.mustKeyPress = true;
            WaveConcussor.suppressSkillActivation = true;
            var roll = MakeSkill<JakMovementSkillDef>("Roll", typeof(DodgeRoll), "Body", Icons.Roll, Balance.RollCooldown, 1, 1);
            Roll = roll;
            roll.mustKeyPress = true;
            roll.cancelSprintingOnActivation = false;
            roll.isCombatSkill = false;
            var board = MakeSkill<JakMovementSkillDef>("Board", typeof(JetBoard), "Body", Icons.Board, Balance.BoardCooldown, 1, 1);
            JetBoard = board;
            board.cancelSprintingOnActivation = false;
            board.forceSprintDuringState = true;
            board.isCombatSkill = false;
            board.beginSkillCooldownOnSkillEnd = true;
            var dark = MakeSkill<DarkJakSkillDef>("Dark", typeof(EnterDarkJak), "Weapon", Icons.Dark, Balance.DarkCooldown, 1, 1);
            var claws = MakeSkill<SteppedSkillDef>("Claw", typeof(States.DarkClaws), "Weapon", Icons.Claws, 0f, 1, 0);
            claws.stepCount = Balance.ClawCombo.Length;
            claws.stepGraceDuration = Balance.ClawStepGrace;
            DarkClaws = claws;
            DarkClaws.interruptPriority = InterruptPriority.Any;
            DarkBomb = MakeSkill<DarkBombSkillDef>("Bomb", typeof(States.DarkBomb), "Weapon", Icons.Bomb, 0f, 1, 1);
            DarkBlast = MakeSkill<DarkBlastSkillDef>("DarkBlast", typeof(States.DarkBlast), "Body", Icons.DarkBlast, Balance.DarkBlastCooldown, 1, 0);
            DarkBlast.requiredStock = 1;
            DarkBlast.suppressSkillActivation = true;
            DarkBlast.resetCooldownTimerOnUse = false;
            DarkBlast.beginSkillCooldownOnSkillEnd = true;
            DarkBlast.mustKeyPress = true;
            DarkBlast.cancelSprintingOnActivation = false;
            DarkBlast.interruptPriority = InterruptPriority.Any;
            AssignFamily(locator.primary, "Primary", blaster, ArcWielder);
            Spin = MakeSkill<SkillDef>("Spin", typeof(SpinAttack), "Body", Icons.Spin, Balance.SpinCooldown, 1, 1);
            Spin.mustKeyPress = true;
            Spin.cancelSprintingOnActivation = false;
            Spin.interruptPriority = InterruptPriority.Any;
            AssignFamily(locator.secondary, "Secondary", scatter, WaveConcussor, Spin);
            AssignFamily(locator.utility, "Utility", roll, board);
            AssignFamily(locator.special, "Special", dark);
            locator.passiveSkill.enabled = true;
            locator.passiveSkill.skillNameToken = "JAK_SUBTITLE";
            locator.passiveSkill.skillDescriptionToken = "JAK_PASSIVE_DESCRIPTION";
            locator.passiveSkill.icon = Icons.Passive;
        }

        private T MakeSkill<T>(string key, Type state, string machine, Sprite icon, float cooldown, int stock, int consume) where T : SkillDef
        {
            var def = ScriptableObject.CreateInstance<T>();
            ((ScriptableObject)def).name = "Jak" + key;
            def.skillName = "Jak" + key;
            def.skillNameToken = "JAK_" + key.ToUpperInvariant() + "_NAME";
            def.skillDescriptionToken = "JAK_" + key.ToUpperInvariant() + "_DESCRIPTION";
            def.activationState = new SerializableEntityStateType(state);
            def.activationStateMachineName = machine;
            def.interruptPriority = InterruptPriority.Skill;
            def.icon = icon;
            def.baseRechargeInterval = cooldown;
            def.baseMaxStock = stock;
            def.rechargeStock = 1;
            def.stockToConsume = consume;
            def.requiredStock = consume;
            // Contextual form changes must not refill charges or erase cooldown progress.
            def.fullRestockOnAssign = false;
            def.dontAllowPastMaxStocks = false;
            def.keywordTokens = Array.Empty<string>();
            skills.Add(def);
            return def;
        }

        private void AssignFamily(GenericSkill slot, string name, params SkillDef[] variants)
        {
            if (SkillFamilyField == null) throw new MissingFieldException(typeof(GenericSkill).FullName, "_skillFamily");
            var family = ScriptableObject.CreateInstance<SkillFamily>();
            ((ScriptableObject)family).name = "Jak" + name + "Family";
            family.variants = variants.Select(def => new SkillFamily.Variant
            {
                skillDef = def,
                viewableNode = new ViewablesCatalog.Node(def.skillNameToken, false)
            }).ToArray();
            SkillFamilyField.SetValue(slot, family);
            families.Add(family);
        }
    }
}
