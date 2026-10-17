using System;
using System.Collections.Generic;

namespace SpellBrigadeGainMultipliers
{
    internal sealed class StatDef
    {
        public Il2Cpp.StatType Type;
        public string Label;
        public string LabelZh;
        public float Step;
    }

    /// <summary>
    /// Applies player stat bonuses through our own StaticStatModifier objects.
    ///
    /// Deliberately lazy: no PlayerStats lifecycle hook. Instances are discovered by scanning for
    /// them from the panel/update loop, so nothing touches networked state during spawning (which
    /// is what the game does right before a run's loading screen - poking a NetworkBehaviour's
    /// stats at Awake time is a good way to stall the load).
    /// </summary>
    internal static class StatEditor
    {
        public static readonly StatDef[] Defs =
        {
            new StatDef { Type = Il2Cpp.StatType.Damage, Label = "Damage", LabelZh = "伤害", Step = 0.1f },
            new StatDef { Type = Il2Cpp.StatType.MaxHealth, Label = "Max HP", LabelZh = "最大生命", Step = 10f },
            new StatDef { Type = Il2Cpp.StatType.MovementSpeed, Label = "Move speed", LabelZh = "移动速度", Step = 0.1f },
            new StatDef { Type = Il2Cpp.StatType.Luck, Label = "Luck", LabelZh = "幸运", Step = 0.05f },
            new StatDef { Type = Il2Cpp.StatType.Dodge, Label = "Dodge", LabelZh = "闪避", Step = 0.05f },
            new StatDef { Type = Il2Cpp.StatType.CriticalChance, Label = "Crit chance", LabelZh = "暴击率", Step = 0.05f },
            new StatDef { Type = Il2Cpp.StatType.CriticalDamage, Label = "Crit damage", LabelZh = "暴击伤害", Step = 0.1f },
            new StatDef { Type = Il2Cpp.StatType.PickupRadius, Label = "Pickup radius", LabelZh = "拾取范围", Step = 0.1f },
            new StatDef { Type = Il2Cpp.StatType.Armor, Label = "Armor", LabelZh = "护甲", Step = 1f },
            new StatDef { Type = Il2Cpp.StatType.HealthRegeneration, Label = "HP regen", LabelZh = "生命回复", Step = 0.1f },
            new StatDef { Type = Il2Cpp.StatType.Rerolls, Label = "Rerolls", LabelZh = "重掷次数", Step = 1f },
            new StatDef { Type = Il2Cpp.StatType.Revives, Label = "Revives", LabelZh = "复活次数", Step = 1f },
            new StatDef { Type = Il2Cpp.StatType.GoldGain, Label = "Gold gain", LabelZh = "金币获取", Step = 0.1f },
            new StatDef { Type = Il2Cpp.StatType.XPGain, Label = "Mana gain", LabelZh = "法力获取", Step = 0.1f },
            new StatDef { Type = Il2Cpp.StatType.DropChance, Label = "Drop chance", LabelZh = "掉落率", Step = 0.05f },
            new StatDef { Type = Il2Cpp.StatType.Projectiles, Label = "Projectiles", LabelZh = "投射物", Step = 1f },
        };

        private static readonly List<Il2Cpp.PlayerStats> Players = new List<Il2Cpp.PlayerStats>();
        private static readonly Dictionary<long, Il2Cpp.StaticStatModifier> Mods =
            new Dictionary<long, Il2Cpp.StaticStatModifier>();

        private static float _nextScan;
        private static bool _reportedScanFailure;
        private static int _lastReportedCount = -1;

        public static int LivePlayers
        {
            get
            {
                Scan(false);
                return Players.Count;
            }
        }

        public static void ResetAll()
        {
            foreach (var def in Defs) Settings.SetStatDelta(def.Type, 0f);
        }

        public static void Apply(float delta, Il2Cpp.StatType type)
        {
            if (!Settings.Enabled) return;

            Scan(true);

            foreach (var player in Players)
            {
                if (player != null) ApplyTo(player, type, delta);
            }
        }

        /// <summary>Finds live PlayerStats instances (throttled) and re-applies pending bonuses.</summary>
        public static void Scan(bool force)
        {
            var now = UnityEngine.Time.realtimeSinceStartup;
            if (!force && now < _nextScan) return;
            _nextScan = now + 2f;

            try
            {
                Players.Clear();

                var found = UnityEngine.Object.FindObjectsOfType<Il2Cpp.PlayerStats>();
                if (found != null)
                {
                    for (var i = 0; i < found.Length; i++)
                    {
                        var player = found[i];
                        if (player != null) Players.Add(player);
                    }
                }

                if (Players.Count != _lastReportedCount)
                {
                    _lastReportedCount = Players.Count;
                    MelonLoader.MelonLogger.Msg($"[GainMults] stats: {Players.Count} player(s) found");
                }
            }
            catch (Exception e)
            {
                if (!_reportedScanFailure)
                {
                    _reportedScanFailure = true;
                    MelonLoader.MelonLogger.Warning("[GainMults] player scan failed: " + e.GetType().Name);
                }
            }

            foreach (var def in Defs)
            {
                var delta = Settings.GetStatDelta(def.Type);
                if (Math.Abs(delta) < 0.0001f) continue;

                foreach (var player in Players)
                {
                    if (player != null) ApplyTo(player, def.Type, delta);
                }
            }
        }

        public static string Describe(Il2Cpp.StatType type)
        {
            Scan(false);

            foreach (var player in Players)
            {
                if (player == null) continue;

                try { return player.GetCharacterValue(type).ToString("0.##"); }
                catch { return "?"; }
            }

            return "n/a";
        }

        private static void ApplyTo(Il2Cpp.PlayerStats stats, Il2Cpp.StatType type, float delta)
        {
            try
            {
                var key = Key(stats, type);

                if (Mods.TryGetValue(key, out var existing) && existing != null)
                {
                    existing.Value = delta;
                    return;
                }

                var modifier = new Il2Cpp.StaticStatModifier();
                modifier.StatType = type;
                modifier.Value = delta;
                stats.AddCharacterStatModifier(modifier);
                Mods[key] = modifier;
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning($"[GainMults] stat {type} failed: {e.GetType().Name} {e.Message}");
            }
        }

        private static long Key(Il2Cpp.PlayerStats stats, Il2Cpp.StatType type)
        {
            return (stats.Pointer.ToInt64() * 397) ^ (int)type;
        }
    }
}

