using HarmonyLib;

namespace SpellBrigadeGainMultipliers
{
    /// <summary>
    /// Gold for a run is produced by RunGoldCalculator.Calculate(...) which returns an
    /// EarnedGold holding the base gold plus every bonus. Scaling each component keeps the
    /// reward screen, the total AND the amount that ends up in the wallet identical, without
    /// touching the bonus percentages the UI displays.
    ///
    /// Scaling happens only in the outermost Postfix (depth guard) so a duplicated patch or a
    /// recursive call can never apply the multiplier twice (that produced x4 instead of x2).
    /// </summary>
    [HarmonyPatch(typeof(Il2Cpp.RunGoldCalculator), "Calculate")]
    internal static class GoldCalculatePatch
    {
        [System.ThreadStatic] private static int _depth;

        private static void Prefix()
        {
            if (_depth > 8) _depth = 0;
            _depth++;
        }

        private static void Postfix(Il2Cpp.EarnedGold __result)
        {
            if (_depth > 0) _depth--;
            if (_depth != 0) return; // only the outermost call scales
            if (__result is null) return;

            if (!Settings.Enabled) return;

            var mult = Settings.GoldMultiplier;
            var before = __result.TotalGold;

            if (mult != 1f)
            {
                __result.BaseGold = Scale(__result.BaseGold, mult);
                __result.BonusGoldFromFinalUpgrade = Scale(__result.BonusGoldFromFinalUpgrade, mult);
                __result.BonusGoldFromGoldGainStat = Scale(__result.BonusGoldFromGoldGainStat, mult);
                __result.BonusGoldFromCurses = Scale(__result.BonusGoldFromCurses, mult);
                __result.BonusGoldFromDifficulty = Scale(__result.BonusGoldFromDifficulty, mult);
                __result.BonusGoldFromUnusedCharacter = Scale(__result.BonusGoldFromUnusedCharacter, mult);
            }

            var after = __result.TotalGold;

            Stats.GoldCalcHits++;
            Stats.GoldLastRaw = before;
            Stats.GoldLastScaled = after;

            Log.Info($"Gold: {before} -> {after} (x{mult})");
        }

        private static int Scale(int value, float mult)
        {
            if (value == 0) return 0;
            return (int)System.Math.Round(value * mult);
        }
    }

    /// <summary>
    /// Fallback / observation hook. Gold reaches the wallet through LocalPlayer.AddGold(int).
    /// Scaling here is off by default (GoldGrantMultiplier = 1) because the run reward is
    /// already scaled in GoldCalculatePatch.
    /// </summary>
    [HarmonyPatch(typeof(Il2Cpp.LocalPlayer), "AddGold")]
    internal static class GoldGrantPatch
    {
        [System.ThreadStatic] private static int _depth;

        private static void Prefix(ref int amount)
        {
            if (_depth > 8) _depth = 0;

            var outermost = _depth == 0;
            _depth++;

            if (!outermost || amount <= 0) return;

            if (!Settings.Enabled) return;

            var mult = Settings.GoldGrantMultiplier;
            var raw = amount;

            if (mult != 1f)
            {
                amount = (int)System.Math.Round(amount * mult);
            }

            Stats.WalletHits++;
            Stats.WalletSum += amount;

            Log.Info($"Wallet gold: +{raw} -> +{amount} (x{mult})");
        }

        private static void Postfix()
        {
            if (_depth > 0) _depth--;
        }
    }
}



