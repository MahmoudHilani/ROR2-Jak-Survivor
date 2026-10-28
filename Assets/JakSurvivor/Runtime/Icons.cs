using UnityEngine;

namespace JakSurvivor
{
    internal static class Icons
    {
        public static Sprite Blaster => JakAssets.Get<Sprite>("blaster");
        public static Sprite Scatter => JakAssets.Get<Sprite>("scatter");
        public static Sprite ArcWielder => JakAssets.Get<Sprite>("arc_wielder");
        public static Sprite WaveConcussor => JakAssets.Get<Sprite>("wave_concussor");
        public static Sprite Spin => JakAssets.Get<Sprite>("spin");
        public static Sprite Roll => JakAssets.Get<Sprite>("roll");
        public static Sprite Board => JakAssets.Get<Sprite>("board");
        public static Sprite Dark => JakAssets.Get<Sprite>("jak3-icon-dark");
        public static Sprite Claws => JakAssets.Get<Sprite>("jak3-icon-claws");
        public static Sprite DarkBlast => JakAssets.Get<Sprite>("jak3-icon-blast");
        public static Sprite Bomb => JakAssets.Get<Sprite>("jak3-icon-bomb");
        public static Sprite Passive => JakAssets.Get<Sprite>("passive");
        // White silhouette; the HUD tints it with the buff colour.
        public static Sprite DarkBuff => JakAssets.Get<Sprite>("buff_dark");
        public static Sprite Skin => JakAssets.Get<Sprite>("skin_default");
        public static Sprite Portrait => JakAssets.Get<Sprite>("jak3-portrait");
    }
}
