using System;

namespace SpellBrigadeGainMultipliers
{
    /// <summary>
    /// Runtime counters so the panel can prove whether each hook is actually being hit.
    /// </summary>
    public static class Stats
    {
        public static int XpHits;
        public static double XpRawSum;
        public static double XpScaledSum;

        public static int GoldCalcHits;
        public static int GoldLastRaw;
        public static int GoldLastScaled;

        public static int WalletHits;
        public static long WalletSum;

        public static int WizardHits;
        public static double WizardRawMinutes;
        public static double WizardScaledMinutes;

        public static string LastMessage = "";

        public static void Reset()
        {
            XpHits = 0;
            XpRawSum = 0;
            XpScaledSum = 0;
            GoldCalcHits = 0;
            GoldLastRaw = 0;
            GoldLastScaled = 0;
            WalletHits = 0;
            WalletSum = 0;
            WizardHits = 0;
            WizardRawMinutes = 0;
            WizardScaledMinutes = 0;
            LastMessage = "";
        }

        public static void Say(string msg)
        {
            LastMessage = msg;
            MelonLoader.MelonLogger.Msg("[GainMults] " + msg);
        }
    }
}
