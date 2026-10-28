using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using RoR2.Audio;
using UnityEngine;

namespace JakSurvivor
{
    // Jak's sounds live in Jak.bnk, which Build-Soundbank.ps1 generates.
    // Its buses resolve to the game's Init.bnk, so the volume sliders, pause and
    // focus mute apply as they do to every other RoR2 sound.
    internal static class JakAudio
    {
        internal sealed class Voice
        {
            public readonly uint PlayingId;
            public readonly string Key;
            public readonly GameObject Owner;
            public readonly bool Menu;
            public bool Ended { get; internal set; }

            internal Voice(uint playingId, string key, GameObject owner, bool menu)
            {
                PlayingId = playingId; Key = key; Owner = owner; Menu = menu;
            }
        }

        internal const string BankFile = "Jak.bnk";
        internal static readonly string[] Keys = { "blast-charge", "blast-swing", "blast-flight", "blast-hit", "blast-wall", "spin-kick", "blue-whoosh", "gun-spin-hit", "jump", "jump-double", "blaster", "scatter", "board-start", "board-loop", "board-jump", "board-q-jump", "board-bounce", "dark-enter", "dark-exit", "claws", "bomb-charge", "bomb-impact", "gun-draw", "gun-twirl", "arc-loop", "arc-spin", "blue-gun-whine", "wave-charge", "wave-shot", "roll-stn", "roll-drt", "roll-grs", "roll-san", "roll-wod", "roll-mtl", "roll-wtr", "roll-flip", "transform-zap", "stretched-zap" };
        internal static bool IsLoop(string key) => key == "blast-charge" || key == "blast-flight" || key == "board-loop" || key == "arc-loop" || key == "arc-spin" || key == "blue-gun-whine" || key == "wave-charge";
        internal static string EventName(string key, bool menu = false) => (menu ? "Play_JakMenu_" : "Play_Jak_") + key.Replace('-', '_');
        private static readonly List<Voice> voices = new List<Voice>();
        private static uint pitchParameter, volumeParameter;
        internal static bool Loaded { get; private set; }

        public static void LoadBank()
        {
            if (Loaded) return;
            if (!AkSoundEngine.IsInitialized())
            {
                Plugin.Log.LogWarning("Wwise is not running; Jak plays without sound.");
                return;
            }
            byte[] bank = File.ReadAllBytes(Path.Combine(JakAssets.PluginDirectory, BankFile));
            GCHandle pinned = GCHandle.Alloc(bank, GCHandleType.Pinned);
            AKRESULT result;
            try { result = AkSoundEngine.LoadBankMemoryCopy(pinned.AddrOfPinnedObject(), (uint)bank.Length, out _); }
            finally { pinned.Free(); }
            if (result != AKRESULT.AK_Success && result != AKRESULT.AK_BankAlreadyLoaded)
                throw new InvalidDataException("Wwise rejected " + BankFile + ": " + result);
            pitchParameter = AkSoundEngine.GetIDFromString("Jak_Pitch");
            volumeParameter = AkSoundEngine.GetIDFromString("Jak_Volume");
            Loaded = true;
            Plugin.Log.LogInfo("Loaded " + BankFile + " with " + Keys.Length + " original Jak 3 sound effects.");
        }

        // pitchMod and volume1024 are 989snd's units (util.cpp:132-140): 1536 per
        // octave and 1024 for full volume. Gain is relative to DefaultGain, which
        // the bank already normalizes each clip to. Looping keys update the
        // owner's playing voice instead of layering another.
        public static Voice Play(string key, GameObject owner, float gain = JakSoundLevels.DefaultGain, float pitchMod = 0f, float volume1024 = 1024f)
        {
            if (!owner || !Loaded) return null;
            Voice voice = IsLoop(key) ? voices.Find(v => v.Owner == owner && v.Key == key) : null;
            voice = voice ?? Post(key, owner, false);
            if (voice == null) return null;
            SetParameters(voice.PlayingId, gain / JakSoundLevels.DefaultGain * volume1024 / 1024f, pitchMod * 1200f / 1536f);
            return voice;
        }

        // The selection display's 2D sounds take a raw linear gain.
        public static Voice PlayMenu(string key, GameObject owner, float gain)
        {
            if (!owner || !Loaded) return null;
            Voice voice = Post(key, owner, true);
            if (voice != null) SetParameters(voice.PlayingId, gain, 0f);
            return voice;
        }

        // A fixed world position on one of the game's pooled point emitters.
        public static Voice PlayAt(string key, Vector3 point)
        {
            if (!Loaded) return null;
            uint playingId = PointSoundManager.EmitSoundLocal(AkSoundEngine.GetIDFromString(EventName(key)), point);
            return playingId == AkSoundEngine.AK_INVALID_PLAYING_ID ? null : new Voice(playingId, key, null, false);
        }

        public static void Fade(Voice voice, float seconds)
        {
            if (voice == null) return;
            AkSoundEngine.StopPlayingID(voice.PlayingId, (int)(seconds * 1000f), AkCurveInterpolation.AkCurveInterpolation_Linear);
            End(voice);
        }

        public static void Stop(Voice voice)
        {
            if (voice == null) return;
            AkSoundEngine.StopPlayingID(voice.PlayingId);
            End(voice);
        }

        // The end callback arrives frames after a stop; a loop restarted on the
        // same owner before then must post a new voice, not reuse the stopped one.
        private static void End(Voice voice)
        {
            voice.Ended = true;
            voices.Remove(voice);
        }

        internal static IReadOnlyList<Voice> Playing => voices;

        private static Voice Post(string key, GameObject owner, bool menu)
        {
            // Destroying an emitter stops its sounds, which may not report an end.
            voices.RemoveAll(v => !v.Owner);
            uint playingId = AkSoundEngine.PostEvent(EventName(key, menu), owner, (uint)AkCallbackType.AK_EndOfEvent, OnEnd, null);
            if (playingId == AkSoundEngine.AK_INVALID_PLAYING_ID)
            {
                Plugin.Log.LogWarning("Wwise could not post " + EventName(key, menu) + ".");
                return null;
            }
            var voice = new Voice(playingId, key, owner, menu);
            voices.Add(voice);
            return voice;
        }

        private static void SetParameters(uint playingId, float gain, float cents)
        {
            AkSoundEngine.SetRTPCValueByPlayingID(volumeParameter, Mathf.Clamp(20f * Mathf.Log10(Mathf.Max(gain, 1e-5f)), -96f, 24f), playingId);
            AkSoundEngine.SetRTPCValueByPlayingID(pitchParameter, Mathf.Clamp(cents, -2400f, 2400f), playingId);
        }

        private static void OnEnd(object cookie, AkCallbackType type, AkCallbackInfo info)
        {
            if (!(info is AkEventCallbackInfo end)) return;
            Voice voice = voices.Find(v => v.PlayingId == end.playingID);
            if (voice != null) End(voice);
        }
    }
}
