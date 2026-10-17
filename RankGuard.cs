using System;
using HarmonyLib;

namespace SpellBrigadeGainMultipliers
{
    internal static class RankMath
    {
        private static int _maxRank;

        /// <summary>The highest rank the game considers valid (cached, 0 when unknown).</summary>
        public static int MaxRank(Il2Cpp.CharacterRankProgression ranks)
        {
            try { return MaxRank(ranks?.rankCalculator); }
            catch { return 0; }
        }

        public static int MaxRank(Il2Cpp.CharacterRankCalculator calculator)
        {
            if (_maxRank > 0) return _maxRank;
            if (calculator == null) return 0;

            try
            {
                for (var rank = 1; rank < 500; rank++)
                {
                    if (calculator.IsMaxRank(rank))
                    {
                        _maxRank = rank;
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[GainMults] max rank probe failed: " + e.GetType().Name);
            }

            return _maxRank;
        }

        /// <summary>
        /// Minutes that can still be consumed before the rank hits the maximum, so a run can never
        /// push the game's rank math past its table.
        /// </summary>
        public static float RemainingMinutes(Il2Cpp.CharacterRankCalculator calculator, int startRank, float startProgress, int prestige)
        {
            try
            {
                var max = MaxRank(calculator);
                if (max <= 0) return float.MaxValue;

                if (startRank >= max) return 0.5f;

                double budget = 0;
                for (var rank = startRank; rank < max; rank++)
                    budget += calculator.CalculateMinutesRequiredForNextRank(rank, prestige);

                var current = calculator.CalculateMinutesRequiredForNextRank(startRank, prestige);
                var progress = Math.Max(0f, Math.Min(1f, startProgress));
                budget -= progress * current;

                return (float)Math.Max(0.5, budget);
            }
            catch
            {
                return float.MaxValue;
            }
        }

        public static void Reset()
        {
            _maxRank = 0;
        }
    }

    /// <summary>Reads / writes the out-of-run wizard rank of the selected character.</summary>
    internal static class RankEditor
    {
        public static int GetRank()
        {
            try
            {
                var lp = Il2Cpp.LocalPlayer.Instance;
                if (lp == null) return -1;

                var ranks = lp.GetCharacterRankManager();
                if (ranks == null) return -1;

                var progress = ranks.GetOrCreateProgressForCharacter(lp.GetSelectedCharacter());
                return progress == null ? -1 : progress.CurrentRank;
            }
            catch
            {
                return -1;
            }
        }

        public static void SetRank(int rank)
        {
            try
            {
                var lp = Il2Cpp.LocalPlayer.Instance;
                if (lp == null)
                {
                    Stats.Say("rank: LocalPlayer not ready");
                    return;
                }

                var ranks = lp.GetCharacterRankManager();
                if (ranks == null)
                {
                    Stats.Say("rank: rank manager not ready");
                    return;
                }

                var max = RankMath.MaxRank(ranks);
                if (rank < 1) rank = 1;
                if (max > 0 && rank > max) rank = max;

                ranks.SetRankProgression(lp.GetSelectedCharacter(), rank, 0f);
                Stats.Say($"wizard rank set to {rank}");
            }
            catch (Exception e)
            {
                Stats.Say("set wizard rank failed: " + e.GetType().Name);
            }
        }
    }

    /// <summary>
    /// The wizard multiplier scales minutes played, and the game's rank math is not bounds safe:
    /// once the computed rank goes past the maximum it can store garbage (we have seen negative
    /// values like -1998380864). Every rank that reaches the progression object goes through
    /// SetRankProgression, so clamping the argument there keeps the save sane.
    /// </summary>
    [HarmonyPatch(typeof(Il2Cpp.CharacterRankProgression), "SetRankProgression")]
    internal static class RankWriteClampPatch
    {
        private static bool _reported;

        private static void Prefix(Il2Cpp.CharacterRankProgression __instance, ref int rank, ref float rankProgress)
        {
            try
            {
                var max = RankMath.MaxRank(__instance);
                var rawRank = rank;

                if (rank < 1) rank = 1;
                if (max > 0 && rank > max) rank = max;

                if (float.IsNaN(rankProgress) || rankProgress < 0f) rankProgress = 0f;
                if (rankProgress > 0.999f) rankProgress = 0.999f;

                if (rank != rawRank && !_reported)
                {
                    _reported = true;
                    MelonLoader.MelonLogger.Warning(
                        $"[GainMults] rank write {rawRank} clamped to {rank} (max {max}). " +
                        "Save data contained an invalid rank; use the panel row to set it.");
                }
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[GainMults] rank write clamp failed: " + e.GetType().Name);
            }
        }
    }
}



