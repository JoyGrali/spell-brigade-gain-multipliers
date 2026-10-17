using HarmonyLib;

namespace SpellBrigadeGainMultipliers
{
    /// <summary>
    /// Experience is granted through PartyLevelManager.TryAddExperience(float).
    ///
    /// The multiplier is only applied on the outermost call. MelonLoader auto-applies every
    /// [HarmonyPatch] class in the assembly on top of any manual PatchAll, so the same method
    /// can end up with two Prefix instances; the depth guard makes that (and genuine recursive
    /// calls inside the game) apply the multiplier exactly once.
    /// </summary>
    [HarmonyPatch(typeof(Il2Cpp.PartyLevelManager), "TryAddExperience")]
    internal static class XpGainPatch
    {
        [System.ThreadStatic] private static int _depth;

        private static void Prefix(ref float experience)
        {
            if (_depth > 8) _depth = 0; // never leak after an exception

            var outermost = _depth == 0;
            _depth++;

            if (!outermost || experience <= 0f) return;

            if (!Settings.Enabled) return;

            var mult = Settings.XpMultiplier;
            var raw = experience;
            experience *= mult;

            Stats.XpHits++;
            Stats.XpRawSum += raw;
            Stats.XpScaledSum += experience;

            Log.Info($"XP: {raw:0.##} -> {experience:0.##} (x{mult})");
        }

        private static void Postfix()
        {
            if (_depth > 0) _depth--;
        }
    }
}


