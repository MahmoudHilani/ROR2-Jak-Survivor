using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace JakSurvivor
{
    internal sealed class JakEffectsAsset : ScriptableObject
    {
        public JakParticleDefinition[] particles;
        public JakParticleGroup[] groups;
        public JakEffectModel.ModelFile[] models;
        public Mesh quad;
        public Shader particleShader, lightningShader;
        public JakEffectMaterial[] materials;
        public GameObject[] prefabs;
        public JakLightningAppearance[] lightning;
        public JakShockSpec[] shockSpecs;
        public JakBlastAppearance blast;
        public TextAsset sourcePayload, provenance;
    }

    [Serializable] internal sealed class JakEffectMaterial
    {
        public string texture;
        public Material additive, alpha;
    }

    internal static class JakEffectModel
    {
        [Serializable] internal sealed class ModelFile
        {
            public string name;
            public BoneFile[] bones;
            public PartFile[] parts;
            public ClipFile[] animations;
            public GameObject prefab;
        }

        [Serializable] internal sealed class BoneFile
        {
            public string name;
            public int parent;
            public float[] position, rotation, scale;
        }

        [Serializable] internal sealed class PartFile { public string name; }

        [Serializable] internal sealed class ClipFile
        {
            public string name;
            public float duration;
            public TrackFile[] tracks;
        }

        [Serializable] internal sealed class TrackFile
        {
            public int bone, kind;
            public float[] times, values;
        }

        internal sealed class JakEffectActor
        {
            public readonly Transform Root;
            public readonly Transform[] Bones;
            public readonly SkinnedMeshRenderer[] Renderers;
            private readonly ModelFile data;
            private readonly Dictionary<string, ClipFile> clips;
            private ClipFile clip;
            private float time;
            private bool loop;
            public string ClipName => clip?.name ?? "";

            public JakEffectActor(ModelFile source, Transform root)
            {
                data = source;
                Root = root;
                clips = source.animations.ToDictionary(c => c.name);
                var children = root.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
                Bones = source.bones.Select((b, i) => children[BoneName(source.name, b, i)]).ToArray();
                Renderers = source.parts.Select(p => children["Jak3Mesh_" + p.name].GetComponent<SkinnedMeshRenderer>()).ToArray();
            }

            public float Duration(string name) => clips[name].duration;

            public void SetClip(string name, bool repeat)
            {
                clip = clips[name];
                loop = repeat;
                time = 0f;
            }

            public void Sample(float delta, float sampleAt = -1f)
            {
                time = sampleAt >= 0f ? sampleAt : time + delta;
                float at = clip.duration > 0f && loop ? time % clip.duration : Mathf.Min(time, clip.duration);
                for (int i = 0; i < Bones.Length; i++)
                {
                    Bones[i].localPosition = Vec(data.bones[i].position);
                    Bones[i].localRotation = Quat(data.bones[i].rotation);
                    Bones[i].localScale = Vec(data.bones[i].scale);
                }
                foreach (TrackFile track in clip.tracks)
                {
                    int first = Array.BinarySearch(track.times, at);
                    if (first < 0) first = Mathf.Max(0, ~first - 1);
                    first = Mathf.Min(first, track.times.Length - 1);
                    int second = Mathf.Min(first + 1, track.times.Length - 1);
                    float amount = first == second ? 0f : Mathf.Clamp01((at - track.times[first]) / (track.times[second] - track.times[first]));
                    if (track.kind == 1)
                        Bones[track.bone].localRotation = Quaternion.Slerp(Quat(track.values, first * 4), Quat(track.values, second * 4), amount);
                    else
                    {
                        Vector3 value = Vector3.Lerp(Vec(track.values, first * 3), Vec(track.values, second * 3), amount);
                        if (track.kind == 0) Bones[track.bone].localPosition = value;
                        else Bones[track.bone].localScale = value;
                    }
                }
            }
        }

        internal static string BoneName(string model, BoneFile bone, int index) => "Jak3Bone_" + model + "_" + index + "_" + bone.name;
        internal static Vector3 Vec(float[] values, int offset = 0) => new Vector3(values[offset], values[offset + 1], values[offset + 2]);
        internal static Quaternion Quat(float[] values, int offset = 0) => new Quaternion(values[offset], values[offset + 1], values[offset + 2], values[offset + 3]);
    }
}
