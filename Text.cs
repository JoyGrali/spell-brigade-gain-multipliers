using System;

namespace SpellBrigadeGainMultipliers
{
    /// <summary>
    /// IMGUI uses Unity's built-in font, which has no CJK glyphs. A dynamic OS font
    /// (Microsoft YaHei on Windows) fixes that; if it is unavailable the panel falls back
    /// to English labels instead of drawing empty boxes.
    /// </summary>
    internal static class Text
    {
        private static bool _tried;
        public static bool Chinese { get; private set; }

        public static void SetChinese(bool on)
        {
            Chinese = on;
        }

        public static string T(string en, string zh)
        {
            return Chinese ? zh : en;
        }

        /// <summary>Must be called from inside OnGUI (GUI.skin only exists there).</summary>
        public static void SetupFont()
        {
            if (_tried) return;
            _tried = true;

            if (!Settings.ChineseLabels) return;

            var candidates = new[]
            {
                "Microsoft YaHei UI",
                "Microsoft YaHei",
                "微软雅黑",
                "SimHei",
                "SimSun",
                "Noto Sans CJK SC"
            };

            foreach (var name in candidates)
            {
                try
                {
                    var font = UnityEngine.Font.CreateDynamicFontFromOSFont(name, 14);
                    if (font == null) continue;

                    UnityEngine.GUI.skin.font = font;
                    Chinese = true;
                    MelonLoader.MelonLogger.Msg($"[GainMults] CJK font loaded: {name} -> Chinese labels");
                    return;
                }
                catch (Exception e)
                {
                    /* keep quiet: a single summary line below is enough */
                }
            }

            MelonLoader.MelonLogger.Warning("[GainMults] no CJK font available -> English labels");
        }
    }
}


