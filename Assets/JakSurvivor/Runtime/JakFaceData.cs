using UnityEngine;

namespace JakSurvivor
{
    public sealed class JakFaceData : ScriptableObject
    {
        public bool companion;
        public JakFaceTrack[] tracks;
        public AnimationClip[] clips;
        public Texture2D iris, pupil, lid;
        public Material compositor;
    }
}
