using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace JakSurvivor
{
    internal static class JakRoll
    {
        public const float Duration = 0.45f;
        public const float InitialSpeed = 2.8f;
        // target-util.gc:368: roll-speed-min is 11.5 m/s at base movement speed.
        public const float FinalSpeed = 11.5f / 7f;
        public const float LeapOpensAt = 0.25f;
        public const float LeapDuration = 0.65f;
        // target.gc:3966-3970: roll-flip can land after aframe 4.
        public const float LeapLandingMinimum = 4f / 60f;

        // effect-control.gc:503-505,14-50: effect-roll selects roll plus the ground material.
        private static readonly Dictionary<string, string> RollSounds = new Dictionary<string, string> {
            { "sdStone", "roll-stn" }, { "sdStoneHalcyonite", "roll-stn" },
            { "sdCrystal", "roll-stn" }, { "sdWormCrystal", "roll-stn" },
            { "sdDirt", "roll-drt" }, { "sdSkymeadowDirt", "roll-drt" },
            { "sdBeachGrass", "roll-grs" }, { "sdSand", "roll-san" }, { "sdSnow", "roll-san" },
            { "sdWood", "roll-wod" }, { "sdMetal", "roll-mtl" }, { "sdMachine", "roll-mtl" },
            { "sdSolus", "roll-mtl" }, { "sdVirtual", "roll-mtl" }, { "sdWater", "roll-wtr" }
        };

        public static string RollSound(Vector3 footPosition)
        {
            if (Physics.Raycast(new Ray(footPosition + Vector3.up * 0.1f, Vector3.down), out RaycastHit hit, 4f,
                LayerIndex.world.mask | LayerIndex.water.mask, QueryTriggerInteraction.Collide))
            {
                SurfaceDef surface = SurfaceDefProvider.GetObjectSurfaceDef(hit.collider, hit.point);
                if (surface && RollSounds.TryGetValue(surface.name, out string sound)) return sound;
            }
            return "roll-stn";
        }

        public static float Speed(float moveSpeed, float age) => moveSpeed * Mathf.Lerp(InitialSpeed, FinalSpeed, age / Duration);
    }
}
