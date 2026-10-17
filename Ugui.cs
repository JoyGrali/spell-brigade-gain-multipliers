using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SpellBrigadeGainMultipliers
{
    /// <summary>
    /// uGUI + TextMeshPro panel.
    ///
    /// IMGUI cannot show Chinese in this build (the dynamic-font API is stripped), so the panel
    /// is rebuilt with the game's own UI stack: a ScreenSpaceOverlay Canvas, Image backgrounds and
    /// TMP texts. Clicks are handled by polling the mouse and hit testing our own screen rects -
    /// no UnityEvent / delegate marshalling involved.
    /// </summary>
    internal static class Ugui
    {
        private const float RootX = 24f;
        private const float RootY = 24f;
        private const float PanelWidth = 780f;
        private const float PanelHeight = 680f;

        private static readonly Color PanelColor = new Color(0.04f, 0.045f, 0.07f, 0.94f);
        private static readonly Color ButtonColor = new Color(0.16f, 0.18f, 0.26f, 1f);
        private static readonly Color ValueColor = new Color(0.10f, 0.30f, 0.42f, 1f);
        private static readonly Color TextColor = new Color(0.92f, 0.93f, 0.96f, 1f);
        private static readonly Color DimColor = new Color(0.70f, 0.73f, 0.80f, 1f);

        private struct BtnHit
        {
            public Rect Rect;
            public Action Action;
            public bool Repeat;
            public int Page; // -1 = always
        }

        private static readonly List<BtnHit> Hits = new List<BtnHit>();
        private static readonly Dictionary<string, Il2CppTMPro.TMP_Text> Texts =
            new Dictionary<string, Il2CppTMPro.TMP_Text>();

        private static bool _built;
        private static bool _failed;
        private static bool _visible = true;
        private static int _page;
        private static int _attempts;

        private static Canvas _canvas;
        private static RectTransform _parent;
        private static RectTransform _root;
        private static RectTransform _multiPage;
        private static RectTransform _statsPage;
        private static Il2CppTMPro.TMP_FontAsset _font;

        private static bool _wasDown;
        private static Action _repeatAction;
        private static float _repeatStart;
        private static float _nextRepeat;
        private static float _nextRefresh;

        private static string _editLabel;
        private static string _editBuffer;
        private static Action<float> _editApply;

        public static bool Built => _built;

        // ---------------------------------------------------------------- lifecycle

        public static bool EnsureBuilt()
        {
            if (_built) return true;
            if (_failed) return false;

            if (++_attempts > 900)
            {
                _failed = true;
                MelonLoader.MelonLogger.Warning("[GainMults] uGUI panel gave up, staying on the IMGUI fallback");
                return false;
            }

            try
            {
                if (!Build()) return false; // fonts not loaded yet: retry next frame
                _built = true;
                MelonLoader.MelonLogger.Msg("[GainMults] uGUI panel built");
                return true;
            }
            catch (Exception e)
            {
                _failed = true;
                MelonLoader.MelonLogger.Warning("[GainMults] uGUI build failed, using IMGUI fallback: " + e.GetType().Name + " " + e.Message);
                return false;
            }
        }

        public static void Tick()
        {
            if (!_built) return;

            if (Ui.PressedToggle()) _visible = !_visible;
            _canvas.enabled = _visible;
            if (!_visible) return;

            PollMouse();
            PollText();

            if (Time.realtimeSinceStartup >= _nextRefresh)
            {
                _nextRefresh = Time.realtimeSinceStartup + 0.1f;
                Refresh();
            }
        }

        // ---------------------------------------------------------------- build

        private static bool Build()
        {
            _font = PickFont();
            if (_font == null) return false;

            var canvasGo = new GameObject("GainMultsCanvas");
            UnityEngine.Object.DontDestroyOnLoad(canvasGo);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32000;

            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();

            _root = NewRect("Panel", _canvas.transform, RootX, RootY, PanelWidth, PanelHeight);
            _root.gameObject.AddComponent<UnityEngine.UI.Image>().color = PanelColor;

            _multiPage = NewRect("MultipliersPage", _root, 0f, 0f, PanelWidth, PanelHeight);
            _statsPage = NewRect("StatsPage", _root, 0f, 0f, PanelWidth, PanelHeight);

            _parent = _root;

            Label("title", 10f, 8f, 300f, 22f, 17f, TextColor);
            Button("tabMulti", 320f, 8f, 110f, 22f, Text.T("> Multipliers", "> 倍率"), () => SetPage(0), -1);
            Button("tabStats", 436f, 8f, 90f, 22f, Text.T("> Stats", "> 属性"), () => SetPage(1), -1);
            Toggle("enabled", 545f, 8f, 140f, 22f, () =>
            {
                Settings.EnabledEntry.Value = !Settings.EnabledEntry.Value;
                Settings.Save();
            });

            Button("hide", PanelWidth - 90f, 8f, 80f, 22f, Text.T("Hide", "隐藏"), () => _visible = false, -1);

            _parent = _multiPage;
            BuildMultipliers();

            _parent = _statsPage;
            BuildStats();

            _parent = _root;

            Box("statusBg", 10f, PanelHeight - 118f, PanelWidth - 20f, 100f, new Color(0f, 0f, 0f, 0.35f));
            var status = Label("status", 18f, PanelHeight - 116f, PanelWidth - 36f, 96f, 13f, DimColor);
            status.enableWordWrapping = true;

            var edit = Label("edit", 18f, PanelHeight - 26f, PanelWidth - 36f, 20f, 14f, ValueColor);
            edit.enableWordWrapping = false;

            SetPage(0);
            Refresh();
            return true;
        }

        private static void BuildMultipliers()
        {
            var y = 40f;

            MultiplierRow("xp", y, Text.T("Mana (in-run XP)", "法力（局内经验）"), Settings.Xp, 0.5f, 100f); y += 28f;
            MultiplierRow("gold", y, Text.T("Gold (run reward)", "金币（结算）"), Settings.Gold, 0.5f, 100f); y += 28f;
            MultiplierRow("wallet", y, Text.T("Wallet fallback", "钱包兜底"), Settings.GoldGrant, 0.5f, 100f); y += 28f;

            RankRow("wizardRank", y, Text.T("Wizard rank (set directly)", "巫师等级（直接设置）")); y += 34f;

            Toggle("verbose", 10f, y, 280f, 24f, () =>
            {
                Settings.Verbose.Value = !Settings.Verbose.Value;
                Settings.Save();
            });
            y += 32f;

            Button("testXp", 10f, y, 250f, 24f, Text.T("+10 mana  ->  XP hook", "加 10 法力 -> XP 钩子"), Ui.TestXp, 0); y += 28f;
            Button("testGold", 10f, y, 250f, 24f, Text.T("+10 gold  ->  wallet hook", "加 10 金币 -> 钱包钩子"), Ui.TestGold, 0); y += 28f;
            Button("testCalc", 10f, y, 250f, 24f, Text.T("Synthetic run reward", "模拟一次结算"), Ui.TestSyntheticReward, 0); y += 28f;
            Button("repair", 10f, y, 250f, 24f, Text.T("Repair wizard rank", "修复巫师等级"), RankSanitizer.RepairNow, 0); y += 28f;
            Button("resetCounters", 10f, y, 250f, 24f, Text.T("Reset counters", "清零计数"), Stats.Reset, 0); y += 28f;
            Button("resetDefaults", 10f, y, 250f, 24f, Text.T("Reset defaults", "恢复默认"), Settings.ResetDefaults, 0);
        }

        private static void MultiplierRow(string key, float y, string label,
            MelonLoader.MelonPreferences_Entry<float> entry, float min, float max)
        {
            Label(key + "_label", 10f, y + 3f, 230f, 20f, 14f, TextColor);
            Box(key + "_valueBg", 244f, y, 58f, 24f, ValueColor);
            Label(key + "_value", 250f, y + 3f, 52f, 20f, 14f, TextColor);

            AddHit(new Rect(RootX + 244f, RootY + y, 58f, 24f),
                () => StartEdit(label, entry.Value, v => Set(entry, v, min, max)), false, 0);

            Button(key + "_minus", 306f, y, 30f, 24f, "-", () => Set(entry, entry.Value - 0.5f, min, max), 0, true);
            Button(key + "_plus", 338f, y, 30f, 24f, "+", () => Set(entry, entry.Value + 0.5f, min, max), 0, true);

            var bx = 376f;
            foreach (var preset in new[] { 1f, 2f, 3f, 5f, 10f })
            {
                var value = preset;
                Button(key + "_p" + value.ToString("0"), bx, y, 34f, 24f, value.ToString("0"), () => Set(entry, value, min, max), 0);
                bx += 38f;
            }
        }

        private static void RankRow(string key, float y, string label)
        {
            Label(key + "_label", 10f, y + 3f, 230f, 20f, 14f, TextColor);
            Box(key + "_valueBg", 244f, y, 58f, 24f, ValueColor);
            Label(key + "_value", 250f, y + 3f, 52f, 20f, 14f, TextColor);

            AddHit(new Rect(RootX + 244f, RootY + y, 58f, 24f),
                () => StartEdit(label, RankEditor.GetRank(), v => RankEditor.SetRank((int)Math.Round(v))), false, 0);

            Button(key + "_minus", 306f, y, 30f, 24f, "-", () => RankEditor.SetRank(RankEditor.GetRank() - 1), 0, true);
            Button(key + "_plus", 338f, y, 30f, 24f, "+", () => RankEditor.SetRank(RankEditor.GetRank() + 1), 0, true);

            Label(key + "_hint", 380f, y + 3f, 260f, 20f, 12f, DimColor);
        }

        private static void BuildStats()
        {
            var y = 40f;

            Label("statsHint", 10f, y, PanelWidth - 20f, 20f, 13f, DimColor);
            y += 24f;

            var defs = StatEditor.Defs;
            for (var i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                var key = "stat" + i;

                Label(key + "_label", 10f, y + 3f, 130f, 20f, 14f, TextColor);
                Box(key + "_valueBg", 146f, y, 76f, 24f, ValueColor);
                Label(key + "_value", 152f, y + 3f, 70f, 20f, 13f, TextColor);

                AddHit(new Rect(RootX + 146f, RootY + y, 76f, 24f),
                    () => StartEdit(Text.T(def.Label + " bonus", def.LabelZh + " 加成"),
                        Settings.GetStatDelta(def.Type), v => Settings.SetStatDelta(def.Type, v)), false, 1);

                Button(key + "_minus", 228f, y, 28f, 24f, "-", () => StepStat(def, -1f), 1, true);
                Button(key + "_plus", 258f, y, 28f, 24f, "+", () => StepStat(def, +1f), 1, true);
                Button(key + "_zero", 288f, y, 28f, 24f, "0", () => Settings.SetStatDelta(def.Type, 0f), 1);

                Box(key + "_bonusBg", 322f, y, 70f, 24f, ButtonColor);
                Label(key + "_bonus", 328f, y + 3f, 64f, 20f, 13f, DimColor);

                y += 27f;
            }
        }

        private static void SetPage(int page)
        {
            _page = page;
            if (_multiPage != null) _multiPage.gameObject.SetActive(page == 0);
            if (_statsPage != null) _statsPage.gameObject.SetActive(page == 1);
        }

        // ---------------------------------------------------------------- widget helpers

        private static RectTransform NewRect(string name, Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = EnsureRectTransform(go);
            if (rt == null) throw new InvalidOperationException("no RectTransform on " + name);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
            return rt;
        }

        /// <summary>
        /// A brand new GameObject only has a Transform. Adding a RectTransform converts it in place;
        /// if that is refused, adding a Graphic (which requires RectTransform) forces the conversion.
        /// </summary>
        private static RectTransform EnsureRectTransform(GameObject go)
        {
            try
            {
                var added = go.AddComponent<RectTransform>();
                if (added != null) return added;
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[GainMults] add RectTransform failed: " + e.GetType().Name);
            }

            try
            {
                var image = go.AddComponent<UnityEngine.UI.Image>();
                if (image != null) image.color = new Color(0f, 0f, 0f, 0f);
            }
            catch { }

            return go.GetComponent<RectTransform>();
        }

        private static Il2CppTMPro.TMP_Text Label(string key, float x, float y, float w, float h, float size, Color color)
        {
            var rt = NewRect(key, _parent != null ? (Transform)_parent : _root, x, y, w, h);
            var text = rt.gameObject.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
            text.font = _font;
            text.fontSize = size;
            text.color = color;
            text.raycastTarget = false;
            text.text = "";

            if (!string.IsNullOrEmpty(key)) Texts[key] = text;
            return text;
        }

        private static void Box(string key, float x, float y, float w, float h, Color color)
        {
            var rt = NewRect(key, _parent != null ? (Transform)_parent : _root, x, y, w, h);
            var image = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static void Button(string key, float x, float y, float w, float h,
            string caption, Action action, int hitPage, bool repeat = false)
        {
            Box(key + "_bg", x, y, w, h, ButtonColor);

            var label = Label(key + "_label", x + 4f, y + 3f, w - 8f, h - 6f, 13f, TextColor);
            label.text = caption;
            label.alignment = Il2CppTMPro.TextAlignmentOptions.Center;

            AddHit(new Rect(RootX + x, RootY + y, w, h), action, repeat, hitPage);
        }

        private static void Toggle(string key, float x, float y, float w, float h, Action action)
        {
            Box(key + "_bg", x, y, w, h, ButtonColor);
            Label(key + "_label", x + 8f, y + 4f, w - 16f, h - 8f, 13f, TextColor);
            AddHit(new Rect(RootX + x, RootY + y, w, h), action, false, 0);
        }

        private static void AddHit(Rect rect, Action action, bool repeat, int page)
        {
            Hits.Add(new BtnHit { Rect = rect, Action = action, Repeat = repeat, Page = page });
        }

        private static void StepStat(StatDef def, float direction)
        {
            Settings.SetStatDelta(def.Type, Settings.GetStatDelta(def.Type) + def.Step * direction);
        }

        private static void Set(MelonLoader.MelonPreferences_Entry<float> entry, float value, float min, float max)
        {
            value = (float)Math.Round(value * 10.0) / 10f;
            if (value < min) value = min;
            if (value > max) value = max;
            entry.Value = value;
            Settings.Save();
        }

        // ---------------------------------------------------------------- refresh

        private static void Refresh()
        {
            SetText("title", Text.T("Gain Multipliers 2.0", "倍率与属性 2.0"));

            SetText("xp_label", Text.T("Mana (in-run XP)", "法力（局内经验）"));
            SetText("gold_label", Text.T("Gold (run reward)", "金币（结算）"));
            SetText("wizard_label", Text.T("Wizard rank (meta XP)", "巫师等级（局外）"));
            SetText("wallet_label", Text.T("Wallet fallback", "钱包兜底"));

            SetText("xp_value", "x" + Settings.Xp.Value.ToString("0.0"));
            SetText("gold_value", "x" + Settings.Gold.Value.ToString("0.0"));
            SetText("wallet_value", "x" + Settings.GoldGrant.Value.ToString("0.0"));

            SetText("verbose_label", (Settings.Verbose.Value ? "[x] " : "[ ] ") + Text.T("Verbose logging", "详细日志"));
            SetText("enabled_label", (Settings.Enabled ? "[x] " : "[ ] ") + Text.T("Enabled", "启用修改"));

            var rank = RankEditor.GetRank();
            SetText("wizardRank_label", Text.T("Wizard rank (set directly)", "巫师等级（直接设置）"));
            SetText("wizardRank_value", rank > 0 ? rank.ToString() : "-");
            SetText("wizardRank_hint", Text.T("type a value to set it", "可直接输入数值"));

            SetText("statsHint", StatEditor.LivePlayers > 0
                ? Text.T($"Players: {StatEditor.LivePlayers}", $"已连接玩家：{StatEditor.LivePlayers}")
                : Text.T("No player data yet - start a run.", "还没有玩家数据 —— 进一局才能改属性。"));

            var defs = StatEditor.Defs;
            for (var i = 0; i < defs.Length; i++)
            {
                SetText("stat" + i + "_label", Text.T(defs[i].Label, defs[i].LabelZh));
                SetText("stat" + i + "_value", StatEditor.Describe(defs[i].Type));
                SetText("stat" + i + "_bonus", Settings.GetStatDelta(defs[i].Type).ToString("0.##"));
            }

            SetText("status", StatusText());
            SetText("edit", _editApply == null
                ? Text.T("Click a value box to type a number.", "点数值框可直接输入数字。")
                : Text.T($"{_editLabel}: {_editBuffer}_   Enter=apply  Esc=cancel",
                         $"{_editLabel}：{_editBuffer}_   Enter 确定  Esc 取消"));
        }

        private static void SetText(string key, string value)
        {
            if (Texts.TryGetValue(key, out var text) && text != null) text.text = value;
        }

        private static string StatusText()
        {
            var sb = new System.Text.StringBuilder();

            try
            {
                var mgr = Il2Cpp.PartyLevelManager.Instance;
                sb.Append(mgr == null
                    ? Text.T("Party: not spawned   ", "队伍：未生成   ")
                    : Text.T($"Party: lv {mgr.GetLevel()}  mana {mgr.GetGatheredExperience():0.#}  host {(mgr.IsServer ? "yes" : "no")}   ",
                             $"队伍：等级 {mgr.GetLevel()}  法力 {mgr.GetGatheredExperience():0.#}  主机 {(mgr.IsServer ? "是" : "否")}   "));
            }
            catch { sb.Append(Text.T("Party: n/a   ", "队伍：不可用   ")); }

            try
            {
                var lp = Il2Cpp.LocalPlayer.Instance;
                sb.Append(lp == null ? Text.T("wallet n/a   ", "钱包不可用   ") : Text.T($"wallet {lp.GetGold()}   ", $"钱包 {lp.GetGold()}   "));
            }
            catch { sb.Append(Text.T("wallet n/a   ", "钱包不可用   ")); }

            sb.AppendLine();
            sb.Append(Text.T($"hooks: mana {Stats.XpHits} ({Stats.XpRawSum:0.#}->{Stats.XpScaledSum:0.#})  gold {Stats.GoldCalcHits}   ",
                             $"钩子：法力 {Stats.XpHits}（{Stats.XpRawSum:0.#}->{Stats.XpScaledSum:0.#}）  金币 {Stats.GoldCalcHits}   "));
            sb.Append(Text.T($"wallet {Stats.WalletHits}  wizard {Stats.WizardHits} ({Stats.WizardRawMinutes:0.#}->{Stats.WizardScaledMinutes:0.#} min)",
                             $"钱包 {Stats.WalletHits}  巫师 {Stats.WizardHits}（{Stats.WizardRawMinutes:0.#}->{Stats.WizardScaledMinutes:0.#} 分钟）"));

            if (!string.IsNullOrEmpty(Stats.LastMessage))
            {
                sb.AppendLine();
                sb.Append(Text.T("last: ", "上次：") + Stats.LastMessage);
            }

            return sb.ToString();
        }

        // ---------------------------------------------------------------- font

        private static Il2CppTMPro.TMP_FontAsset PickFont()
        {
            try
            {
                var fonts = UnityEngine.Resources.FindObjectsOfTypeAll<Il2CppTMPro.TMP_FontAsset>();
                if (fonts != null && fonts.Length > 0)
                {
                    Il2CppTMPro.TMP_FontAsset best = null;
                    var cjk = false;

                    for (var i = 0; i < fonts.Length; i++)
                    {
                        var font = fonts[i];
                        if (font == null) continue;

                        try
                        {
                            // Dynamic TMP atlases report "no glyph" until one is requested,
                            // so ask with tryAddCharacter: it adds the glyph and tells us
                            // whether the source font really has it.
                            if (font.HasCharacter('中', true, true))
                            {
                                best = font;
                                cjk = true;
                                break;
                            }
                        }
                        catch { }

                        best ??= font;
                    }

                    if (best != null)
                    {
                        Text.SetChinese(cjk);
                        MelonLoader.MelonLogger.Msg($"[GainMults] TMP fonts found: {fonts.Length} (cjk={cjk})");
                        return best;
                    }
                }
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[GainMults] font scan failed: " + e.GetType().Name);
            }

            try
            {
                var fallback = Il2CppTMPro.TMP_Settings.defaultFontAsset;
                if (fallback != null)
                {
                    MelonLoader.MelonLogger.Msg("[GainMults] TMP font: using TMP_Settings default");
                    return fallback;
                }
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[GainMults] TMP default font failed: " + e.GetType().Name);
            }

            return null;
        }

        // ---------------------------------------------------------------- input

        private static void PollMouse()
        {
            if (!Ui.TryMouseGui(out var p))
            {
                _wasDown = false;
                return;
            }

            var down = Ui.MouseDown();
            var now = Time.realtimeSinceStartup;

            if (down && !_wasDown)
            {
                if (TryHit(p, out var hit))
                {
                    hit.Action?.Invoke();
                    if (hit.Repeat)
                    {
                        _repeatAction = hit.Action;
                        _repeatStart = now;
                        _nextRepeat = now + 0.35f;
                    }
                }
            }
            else if (down && _repeatAction != null && now - _repeatStart > 0.35f && now >= _nextRepeat)
            {
                _repeatAction();
                _nextRepeat = now + 0.05f;
            }
            else if (!down)
            {
                _repeatAction = null;
            }

            _wasDown = down;
        }

        private static bool TryHit(Vector2 p, out BtnHit hit)
        {
            for (var i = Hits.Count - 1; i >= 0; i--)
            {
                var candidate = Hits[i];
                if (candidate.Page != -1 && candidate.Page != _page) continue;

                if (candidate.Rect.Contains(p))
                {
                    hit = candidate;
                    return true;
                }
            }

            hit = default;
            return false;
        }

        private static void StartEdit(string label, float current, Action<float> apply)
        {
            _editLabel = label;
            _editBuffer = current.ToString("0.##", CultureInfo.InvariantCulture);
            _editApply = apply;
            Refresh();
        }

        private static void PollText()
        {
            if (_editApply == null) return;

            try
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
                {
                    CancelEdit();
                    Refresh();
                    return;
                }

                var typed = UnityEngine.Input.inputString;
                if (string.IsNullOrEmpty(typed)) return;

                foreach (var c in typed)
                {
                    if (c == '\b')
                    {
                        if (_editBuffer.Length > 0) _editBuffer = _editBuffer.Substring(0, _editBuffer.Length - 1);
                    }
                    else if (c == '\n' || c == '\r')
                    {
                        CommitEdit();
                        return;
                    }
                    else if (char.IsDigit(c) || c == '.' || c == '-')
                    {
                        if (_editBuffer.Length < 12) _editBuffer += c;
                    }
                }

                SetText("edit", Text.T($"{_editLabel}: {_editBuffer}_   Enter=apply  Esc=cancel",
                                       $"{_editLabel}：{_editBuffer}_   Enter 确定  Esc 取消"));
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[GainMults] typed input unavailable: " + e.GetType().Name);
                CancelEdit();
            }
        }

        private static void CommitEdit()
        {
            var apply = _editApply;
            var buffer = _editBuffer;
            CancelEdit();

            if (apply == null || string.IsNullOrEmpty(buffer)) return;

            if (float.TryParse(buffer, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                apply(value);
            }
            else
            {
                Stats.Say(Text.T("not a number: ", "不是数字：") + buffer);
            }

            Refresh();
        }

        private static void CancelEdit()
        {
            _editLabel = null;
            _editBuffer = null;
            _editApply = null;
        }
    }
}








