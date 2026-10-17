using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

[assembly: MelonInfo(typeof(SpellBrigadeGainMultipliers.GainMultipliersMod), "Gain Multipliers (XP & Gold)", "2.5.0", "misob")]
[assembly: MelonGame("BoltBlasterGames", "TheSpellBrigade")]

namespace SpellBrigadeGainMultipliers
{
    public class GainMultipliersMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            Settings.Load();

            LoggerInstance.Msg($"Gain Multipliers loaded. XP x{Settings.XpMultiplier}, Gold x{Settings.GoldMultiplier}. Panel: F8.");

            // Note: MelonLoader already auto-applies every [HarmonyPatch] class in this assembly.
            // Patches.EnsureOnce() verifies that later and only patches manually if it did not happen.
            try
            {
                MelonEvents.OnUpdate.Subscribe(new LemonAction(Ui.Tick), 0, false);
                MelonEvents.OnGUI.Subscribe(new LemonAction(Ui.Draw), 0, false);
                LoggerInstance.Msg("Panel registered (F8).");
            }
            catch (Exception e)
            {
                LoggerInstance.Error("Failed to register panel: " + e);
            }
        }
    }

    /// <summary>
    /// Verifies (a few frames in, once the loader had its chance) that our patches are
    /// installed exactly once, and installs them itself when the loader did not.
    /// </summary>
    internal static class Patches
    {
        private static bool _checked;
        private static readonly HarmonyLib.Harmony Harmony = new HarmonyLib.Harmony("misob.thespellbrigade.gainmultipliers");

        public static void EnsureOnce()
        {
            if (_checked) return;
            if (UnityEngine.Time.frameCount < 30) return;
            _checked = true;

            try
            {
                var xp = AccessTools.Method(typeof(Il2Cpp.PartyLevelManager), "TryAddExperience");
                var gold = AccessTools.Method(typeof(Il2Cpp.RunGoldCalculator), "Calculate");
                var wallet = AccessTools.Method(typeof(Il2Cpp.LocalPlayer), "AddGold");
                var wizard = AccessTools.Method(typeof(Il2Cpp.CharacterRankCalculator), "CalculateProgress");

                var xpCount = CountPrefixes(xp);
                var goldCount = CountPostfixes(gold);
                var walletCount = CountPrefixes(wallet);
                var wizardCount = CountPrefixes(wizard);

                if (xpCount == 0 || goldCount == 0)
                {
                    MelonLogger.Msg($"[GainMults] patches missing -> applying manually");
                    Harmony.PatchAll(Assembly.GetExecutingAssembly());

                    xpCount = CountPrefixes(xp);
                    goldCount = CountPostfixes(gold);
                    walletCount = CountPrefixes(wallet);
                    wizardCount = CountPrefixes(wizard);
                }

                MelonLogger.Msg($"[GainMults] patch state: xp={xpCount} gold={goldCount} wallet={walletCount}");
            }
            catch (Exception e)
            {
                MelonLogger.Error("[GainMults] patch verification failed: " + e);
            }
        }

        private static int CountPrefixes(MethodBase method)
        {
            try { return HarmonyLib.Harmony.GetPatchInfo(method)?.Prefixes?.Count ?? 0; }
            catch { return -1; }
        }

        private static int CountPostfixes(MethodBase method)
        {
            try { return HarmonyLib.Harmony.GetPatchInfo(method)?.Postfixes?.Count ?? 0; }
            catch { return -1; }
        }
    }

    internal static class Log
    {
        public static void Info(string msg)
        {
            if (Settings.VerboseLogging) MelonLogger.Msg("[GainMults] " + msg);
        }
    }
}











