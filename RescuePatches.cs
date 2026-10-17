using System;
using HarmonyLib;

namespace SpellBrigadeGainMultipliers
{
    /// <summary>
    /// Rescue patch.
    ///
    /// The stored wizard rank can end up outside the badge sprite range (the observed broken
    /// value was a negative garbage int). CharacterBadgeDisplay.Show then does rankImages[rank]
    /// and throws ArgumentOutOfRangeException inside a Netcode RPC handler, the main menu never
    /// finishes updating player seats, and the game looks like it "cannot load characters".
    ///
    /// Clamping the index keeps the menu usable no matter what is stored, and the first clamped
    /// call reports the raw numbers so the data can be repaired.
    /// </summary>
    [HarmonyPatch(typeof(Il2Cpp.CharacterBadgeDisplay), "Show")]
    internal static class BadgeRescuePatch
    {
        private static bool _reported;
        private static bool _loggedOnce;

        private static void Prefix(Il2Cpp.CharacterBadgeDisplay __instance, ref int rank, ref int prestige)
        {
            try
            {
                var images = __instance.rankImages;
                var count = images?.Count ?? 0;

                if (!_loggedOnce)
                {
                    _loggedOnce = true;
                    MelonLoader.MelonLogger.Msg($"[GainMults] badge display: rank={rank} prestige={prestige} rankImages={count}");
                }

                try
                {
                    var prestigeBadges = __instance.prestigeBadges;
                    var prestigeCount = prestigeBadges?.Count ?? 0;
                    if (prestigeCount > 0 && (prestige < 0 || prestige >= prestigeCount))
                        prestige = Math.Max(0, Math.Min(prestigeCount - 1, prestige));
                }
                catch { }

                if (count <= 0) return;

                if (rank < 0 || rank > count)
                {
                    var raw = rank;
                    rank = Math.Max(0, Math.Min(count - 1, rank));

                    if (!_reported)
                    {
                        _reported = true;
                        MelonLoader.MelonLogger.Warning(
                            $"[GainMults] wizard rank {raw} is outside the badge range 0..{count - 1}; " +
                            $"clamped to {rank}. Click 'Repair wizard rank' in the panel.");
                    }
                }
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[GainMults] badge clamp failed: " + e.GetType().Name);
            }
        }

        /// <summary>Never let a broken badge take the whole menu / RPC handler down.</summary>
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception != null && !_reported)
            {
                _reported = true;
                MelonLoader.MelonLogger.Warning("[GainMults] badge display failed, suppressed: " +
                                                __exception.GetType().Name + " " + __exception.Message);
            }

            return null;
        }
    }

    /// <summary>
    /// Repairs a wizard rank that is outside the valid range (negative garbage, or above the
    /// maximum rank). The value is written back through the game's own progression object and
    /// through SetRankProgression, so the next save persists the fixed value.
    /// </summary>
    internal static class RankSanitizer
    {
        private static float _nextCheck;

        public static void EnsureOnce()
        {
            if (UnityEngine.Time.frameCount < 60) return;

            var now = UnityEngine.Time.realtimeSinceStartup;
            if (now < _nextCheck) return;
            _nextCheck = now + 5f;

            Repair(quietWhenFine: true);
        }

        public static void RepairNow()
        {
            Repair(quietWhenFine: false);
        }

        private static void Repair(bool quietWhenFine)
        {
            try
            {
                var lp = Il2Cpp.LocalPlayer.Instance;
                if (lp == null) return;

                var ranks = lp.GetCharacterRankManager();
                if (ranks == null) return;

                var character = lp.GetSelectedCharacter();
                var progress = ranks.GetOrCreateProgressForCharacter(character);
                if (progress == null) return;

                var rank = progress.CurrentRank;
                var max = RankMath.MaxRank(ranks);



                var valid = rank >= 1 && (max <= 0 || rank <= max);
                if (valid)
                {
                    if (!quietWhenFine)
                        MelonLoader.MelonLogger.Msg($"[GainMults] wizard rank already fine: {rank} (max {max})");
                    return;
                }

                var target = rank < 1 ? 1 : max;

                progress.CurrentRank = target;
                progress.ProgressTowardsNextRank = 0f;
                ranks.SetRankProgression(character, target, 0f);

                MelonLoader.MelonLogger.Warning($"[GainMults] repaired wizard rank {rank} -> {target} (max {max})");
                Stats.Say($"wizard rank repaired: {rank} -> {target}");
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[GainMults] rank repair failed: " + e.GetType().Name + " " + e.Message);
            }
        }

        /// <summary>Number of rank sprites the badge UI actually owns (0 when no badge exists yet).</summary>
        private static int BadgeCount()
        {
            try
            {
                var badges = UnityEngine.Object.FindObjectsOfType<Il2Cpp.CharacterBadgeDisplay>();
                if (badges == null) return 0;

                for (var i = 0; i < badges.Length; i++)
                {
                    var badge = badges[i];
                    if (badge == null) continue;

                    var images = badge.rankImages;
                    if (images != null && images.Count > 0) return images.Count;
                }
            }
            catch { }

            return 0;
        }

        private static int MaxRank(Il2Cpp.CharacterRankProgression ranks)
        {
            try
            {
                var calculator = ranks.rankCalculator;
                if (calculator == null) return 0;

                for (var rank = 1; rank < 500; rank++)
                {
                    if (calculator.IsMaxRank(rank)) return rank;
                }
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[GainMults] max rank probe failed: " + e.GetType().Name);
            }

            return 0;
        }
    }
}



