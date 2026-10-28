using System.IO;
using System.Linq;
using UnityEngine;

namespace JakSurvivor
{
    internal static class JakAssets
    {
        public const string BundleName = "jaksurvivor";
        public static string PluginDirectory => Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
        public static AssetBundle Bundle { get; private set; }
        internal static float ScatterRecovery { get; private set; }

        public static AssetBundleCreateRequest BeginLoad()
        {
            string path = Path.Combine(PluginDirectory, BundleName);
            if (!File.Exists(path)) throw new FileNotFoundException("Jak's AssetBundle is missing. Stage the plugin with scripts/Build-Unity.ps1.", path);
            return AssetBundle.LoadFromFileAsync(path);
        }

        public static void EndLoad(AssetBundleCreateRequest request)
        {
            Bundle = request.assetBundle;
            if (!Bundle) throw new InvalidDataException("Jak's AssetBundle could not be loaded.");
            ScatterRecovery = Get<RuntimeAnimatorController>(JakAnimator.BodyController).animationClips
                .Single(c => c.name == "jakb-gun-red-fire-fast").length;
        }

        public static T Get<T>(string name) where T : Object =>
            Bundle.LoadAsset<T>(name) ?? throw new MissingReferenceException("Jak's AssetBundle has no " + typeof(T).Name + " named " + name + ".");
    }
}
