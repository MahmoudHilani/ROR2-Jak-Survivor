using RoR2;
using UnityEngine;

namespace JakSurvivor
{
    internal static class JakModel
    {
        public const string MaterialTemplateKey = "RoR2/Base/Commando/matCommandoDualies.mat";

        // Loaded asynchronously by JakContent before any model is built.
        public static Material MaterialTemplate { get; set; }

        public static void Install(GameObject bodyPrefab, GameObject prefab, ItemDisplayRuleSet itemDisplays)
        {
            ModelLocator locator = bodyPrefab.GetComponent<ModelLocator>();
            Transform previous = locator.modelTransform;
            GameObject replacement = UnityEngine.Object.Instantiate(prefab, previous.parent);
            replacement.name = JakAnimator.ModelPrefab;
            replacement.transform.localPosition = previous.localPosition;
            replacement.transform.localRotation = previous.localRotation;
            replacement.transform.localScale = previous.localScale;
            CharacterModel model = replacement.GetComponent<CharacterModel>();
            CharacterBody body = bodyPrefab.GetComponent<CharacterBody>();
            model.body = body;
            model.itemDisplayRuleSet = itemDisplays;
            body.overrideCoreTransform = replacement.GetComponent<ChildLocator>().FindChild(JakItemAnatomy.Core);
            HurtBoxGroup hurtBoxes = replacement.GetComponent<HurtBoxGroup>();
            foreach (HurtBox hurtBox in hurtBoxes.hurtBoxes) hurtBox.healthComponent = bodyPrefab.GetComponent<HealthComponent>();
            locator.modelTransform = replacement.transform;
            CharacterDirection direction = bodyPrefab.GetComponent<CharacterDirection>();
            if (direction)
            {
                direction.modelAnimator = replacement.GetComponent<Animator>();
                if (direction.targetTransform && (direction.targetTransform == previous || direction.targetTransform.IsChildOf(previous))) direction.targetTransform = replacement.transform;
                if (direction.overrideAnimatorForwardTransform && direction.overrideAnimatorForwardTransform.IsChildOf(previous)) direction.overrideAnimatorForwardTransform = replacement.transform;
            }
            foreach (AimAnimator aim in replacement.GetComponents<AimAnimator>())
            {
                aim.inputBank = bodyPrefab.GetComponent<InputBankTest>();
                aim.directionComponent = direction;
            }
            PrepareMaterials(replacement);
            UnityEngine.Object.DestroyImmediate(previous.gameObject);
        }

        public static void PrepareMaterials(GameObject model)
        {
            JakVisuals visuals = model.GetComponentInChildren<JakVisuals>(true);
            foreach (Material material in visuals.armedMaterials) if (material) material.shader = MaterialTemplate.shader;
            foreach (Material material in visuals.darkMaterials) if (material) material.shader = MaterialTemplate.shader;
        }

        public static void SetMaterial(CharacterModel.RendererInfo[] infos, Renderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            for (int i = 0; i < infos.Length; i++) if (infos[i].renderer == renderer) infos[i].defaultMaterial = material;
        }

    }
}
