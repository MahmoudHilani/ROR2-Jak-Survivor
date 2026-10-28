using UnityEngine;

namespace JakSurvivor
{
    [DefaultExecutionOrder(10000)]
    internal sealed class JakGunBarrel : MonoBehaviour
    {
        private Transform cylinders, tip;
        private Quaternion applied = Quaternion.identity;
        private bool twisted;
        private float spin;

        private void Awake()
        {
            foreach (Transform bone in GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "Jak3Bone_gun_8_cylinders") cylinders = bone;
                if (bone.name == "Jak3Bone_gun_10_barrelTip") tip = bone;
            }
        }

        public void SetSpin(float spinUnits) => spin = spinUnits;

        private void Update() => RemoveTwist();

        private void LateUpdate()
        {
            // gun-util.gc:315-324; joint-mod.gc:956-966 twists the nose axis. In the exported rig
            // the barrel runs along these bones' local y (barrelTip sits at +y of barrelCenter).
            // import_jak3.py reflects x: axial rotation changes sign.
            applied = Quaternion.AngleAxis(-spin * (360f / 65536f), Vector3.up);
            if (cylinders) cylinders.localRotation = applied * cylinders.localRotation;
            if (tip) tip.localRotation = applied * tip.localRotation;
            twisted = true;
        }

        private void RemoveTwist()
        {
            if (!twisted) return;
            Quaternion inverse = Quaternion.Inverse(applied);
            if (cylinders) cylinders.localRotation = inverse * cylinders.localRotation;
            if (tip) tip.localRotation = inverse * tip.localRotation;
            twisted = false;
        }

        private void OnDisable() => RemoveTwist();
    }
}
