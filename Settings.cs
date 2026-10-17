using System;
using System.Collections.Generic;

namespace SpellBrigadeGainMultipliers
{
    /// <summary>
    /// All tunables are MelonPreferences entries: the panel and the config file share them,
    /// so an edit in-game is persisted to UserData/MelonPreferences.cfg immediately.
    /// </summary>
    public static class Settings
    {
        public static MelonLoader.MelonPreferences_Entry<float> Xp;
        public static MelonLoader.MelonPreferences_Entry<float> Gold;
        public static MelonLoader.MelonPreferences_Entry<float> GoldGrant;
        public static MelonLoader.MelonPreferences_Entry<bool> Verbose;
        public static MelonLoader.MelonPreferences_Entry<bool> ChineseEntry;
        public static MelonLoader.MelonPreferences_Entry<bool> EnabledEntry;

        private static readonly Dictionary<Il2Cpp.StatType, MelonLoader.MelonPreferences_Entry<float>> StatEntries =
            new Dictionary<Il2Cpp.StatType, MelonLoader.MelonPreferences_Entry<float>>();

        public static float XpMultiplier => Xp?.Value ?? 1f;
        public static float GoldMultiplier => Gold?.Value ?? 1f;

        public static float GoldGrantMultiplier => GoldGrant?.Value ?? 1f;
        public static bool VerboseLogging => Verbose?.Value ?? false;
        public static bool Enabled => EnabledEntry?.Value ?? true;
        public static bool ChineseLabels => ChineseEntry?.Value ?? true;

        public static void Load()
        {
            var cat = MelonLoader.MelonPreferences.CreateCategory("GainMultipliers", "Gain Multipliers");

            Xp = cat.CreateEntry("XpMultiplier", 2f,
                "Mana multiplier (in-run XP)",
                "Multiplies the in-run experience (the game calls it mana) added to the party level manager.");
            Gold = cat.CreateEntry("GoldMultiplier", 2f,
                "Gold multiplier",
                "Multiplies the gold reward calculated at the end of a run (base gold and every bonus).");
            EnabledEntry = cat.CreateEntry("Enabled", true,
                "Enable modifications",
                "Master switch. When off, the XP/gold/stat patches do nothing; only the save-repair clamps stay active.");
            GoldGrant = cat.CreateEntry("GoldGrantMultiplier", 1f,
                "Gold grant multiplier (fallback)",
                "Extra multiplier where gold is added to the wallet. Keep 1.0: the run reward is already scaled by GoldMultiplier. Raise it only if GoldMultiplier has no effect.");
            Verbose = cat.CreateEntry("Verbose", false,
                "Verbose logging",
                "Log every hook hit, including calls where the multiplier is 1.");
            ChineseEntry = cat.CreateEntry("ChineseLabels", true,
                "Chinese labels",
                "Use a Windows CJK font for the in-game panel. Falls back to English if the font cannot be loaded.");

            foreach (var def in StatEditor.Defs)
            {
                StatEntries[def.Type] = cat.CreateEntry("Stat_" + def.Type.ToString(), 0f,
                    def.Label + " bonus",
                    "Flat bonus added to this player stat while a run is active.");
            }

            MelonLoader.MelonPreferences.Save();
        }

        public static float GetStatDelta(Il2Cpp.StatType type)
        {
            return StatEntries.TryGetValue(type, out var entry) ? entry.Value : 0f;
        }

        public static void SetStatDelta(Il2Cpp.StatType type, float value)
        {
            if (!StatEntries.TryGetValue(type, out var entry)) return;
            entry.Value = value;
            StatEditor.Apply(value, type);
            Save();
        }

        private static bool _dirty;
        private static float _nextSave;

        public static void Save()
        {
            _dirty = true;
        }

        /// <summary>Coalesces writes so holding a +/- button does not hammer the cfg file.</summary>
        public static void Tick()
        {
            if (!_dirty) return;

            var now = UnityEngine.Time.realtimeSinceStartup;
            if (now < _nextSave) return;

            _dirty = false;
            _nextSave = now + 1f;

            try { MelonLoader.MelonPreferences.Save(); } catch { /* ignore */ }
        }

        public static void ResetDefaults()
        {
            try
            {
                Xp?.ResetToDefault();
                Gold?.ResetToDefault();
                GoldGrant?.ResetToDefault();
                Verbose?.ResetToDefault();

                foreach (var pair in StatEntries)
                {
                    pair.Value.ResetToDefault();
                    StatEditor.Apply(pair.Value.Value, pair.Key);
                }

                MelonLoader.MelonPreferences.Save();
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Error(e.ToString());
            }
        }
    }
}




