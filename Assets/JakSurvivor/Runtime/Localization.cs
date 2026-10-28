using System.Globalization;
using R2API;

namespace JakSurvivor
{
    internal static class Localization
    {
        public static void Register()
        {
            Add("NAME", "Jak");
            Add("SUBTITLE", "The Eco Warrior");
            Add("PASSIVE_DESCRIPTION", "Jak can <style=cIsUtility>double jump</style>; hold jump to leap higher. Dark Jak's attacks share the same item scaling and charges as his selected secondary.");
            Add("DESCRIPTION", "Jak is a versatile eco warrior who trades precise Morph Gun fire for the ferocity of Dark Jak.\n\n" +
                "<style=cSub>Blaster</style> keeps distant threats in check. Close the gap to sweep nearby enemies with <style=cSub>Scatter Gun</style>. " +
                "Choose <style=cSub>Arc Wielder</style> to channel lightning between enemies, or <style=cSub>Wave Concussor</style> to charge a radial shockwave.\n\n" +
                "<style=cSub>Dodge Roll</style> bursts in your movement direction. Jump near the end to carry momentum into a leap. Select <style=cSub>Jet-Board</style> as an alternate Utility to cruise over rough ground.\n\n" +
                "<style=cSub>Dark Jak</style> grants armor, speed, sweeping claws and a devastating Dark Bomb for " + Number(Balance.DarkDuration) + " seconds.");
            Add("OUTRO", "...and so he left, still searching for a way home.");
            Add("FAILURE", "...and so he vanished, another echo in the dark.");
            Add("LORE", "The planet was wrong.\n\nNo Haven walls. No wasteland. Just a sky fractured by a moon that felt too close, and something ancient breathing beneath the stone.\n\nDaxter tightened his grip on Jak's shoulder. For once, he had nothing to say.\n\nJak checked the Morph Gun. Blue eco sparked in the chamber.\n\nSomewhere beyond the horizon, a teleporter began to hum.\n\nHe had crossed worlds before. They could do it again.");
            Add("SKIN", "Haven Wanderer");
            Add("BLASTER_NAME", "Blaster");
            Add("BLASTER_DESCRIPTION", "Fire an accurate eco bolt for <style=cIsDamage>240% damage</style>.");
            Add("SCATTER_NAME", "Scatter Gun");
            Add("SCATTER_DESCRIPTION", "Blast every enemy in a wide <style=cIsUtility>20m fan</style> for <style=cIsDamage>1000% damage within 6m</style>, or <style=cIsDamage>667% damage</style> farther away. Holds <style=cIsUtility>2 charges</style>.");
            Add("ARCWIELDER_NAME", "Arc Wielder");
            Add("ARCWIELDER_DESCRIPTION", "Hold to channel a lightning stream that bends to and arcs through enemies for <style=cIsDamage>" + Percent(Balance.ArcDamage) +
                " damage</style> every <style=cIsUtility>" + Number(Balance.ArcInterval) + " seconds</style>. Each victim retains " + Percent(Balance.ArcDamageRetention) + " damage.");
            Add("WAVECONCUSSOR_NAME", "Wave Concussor");
            Add("WAVECONCUSSOR_DESCRIPTION", "Hold to charge a <style=cIsUtility>360-degree shockwave</style>. Release for up to <style=cIsDamage>" +
                Percent(Balance.WaveFullDamage) + " damage</style> across <style=cIsUtility>" +
                Number(Balance.WaveBaseRadius + Balance.WaveRadiusGain) + "m</style>. Automatically releases before the charging sound repeats. Damage decreases with distance. Holds <style=cIsUtility>" +
                Balance.WaveStocks + " charges</style>.");
            Add("SPIN_NAME", "Spin Attack");
            Add("SPIN_DESCRIPTION", "Spin kick enemies within <style=cIsUtility>" + Number(Balance.SpinRadius) + "m</style> for <style=cIsDamage>" + Percent(Balance.SpinDamage) +
                " damage</style>. <style=cSub>Fire while spinning to spray shots along the Morph Gun.</style> Air spins hop once per airborne period.");
            Add("ROLL_NAME", "Dodge Roll");
            Add("ROLL_DESCRIPTION", "Roll in your movement direction for <style=cIsUtility>about 10m</style> while sprinting, scaling with movement speed. <style=cSub>Jump near the end to leap.</style>");
            Add("BOARD_NAME", "Jet-Board");
            Add("BOARD_DESCRIPTION", "Cruise on a steerable jet-board for about <style=cIsUtility>4 seconds</style>. Hold jump to ollie higher. <style=cSub>Use the skill again to dismount.</style>");
            Add("DARK_NAME", "Dark Jak");
            Add("DARK_DESCRIPTION", "Transform for <style=cIsUtility>" + Number(Balance.DarkDuration) + " seconds</style>, gaining <style=cIsHealing>" + Number(Balance.DarkArmor) +
                " armor</style> and <style=cIsUtility>" + Percent(Balance.DarkMoveBonus) + " movement speed</style>. Replace your primary with <style=cIsDamage>Dark Claws</style>, secondary with <style=cIsDamage>Dark Bomb</style> and special with <style=cIsDamage>Dark Blast</style>.");
            Add("CLAW_NAME", "Dark Claws");
            Add("CLAW_DESCRIPTION", "Chain three claw strikes through nearby enemies for <style=cIsDamage>" + Percent(Balance.ClawCombo[0].Damage) + "</style>, <style=cIsDamage>" +
                Percent(Balance.ClawCombo[1].Damage) + "</style> and <style=cIsDamage>" + Percent(Balance.ClawCombo[2].Damage) + " damage</style>.");
            Add("BOMB_NAME", "Dark Bomb");
            Add("BOMB_DESCRIPTION", "Slam down and detonate dark eco in a <style=cIsUtility>" + Number(Balance.DarkBombRadius) + "m radius</style> for <style=cIsDamage>" +
                Percent(Balance.DarkBombDamage) + " damage</style>, stunning enemies. Uses your selected secondary's charges and cooldown.");
            Add("DARKBLAST_NAME", "Dark Blast");
            Add("DARKBLAST_DESCRIPTION", "Hold to charge dark energy in your hand. Release to launch an expanding, piercing blast for <style=cIsDamage>" + Percent(Balance.DarkBlastDamage) + " damage</style>. Stops at walls. Holding does not increase damage.");
            Add("DARK_BUFF", "Dark Jak");
        }

        private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Percent(float coefficient) => Number(coefficient * 100f) + "%";
        private static void Add(string key, string value) => LanguageAPI.Add("JAK_" + key, value);
    }
}
