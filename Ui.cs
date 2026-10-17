using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SpellBrigadeGainMultipliers
{
    /// <summary>
    /// In-game panel. F8 toggles it.
    ///
    /// This game's build strips part of the managed IMGUI wrappers (GUILayout.*, sliders) and
    /// never delivers IMGUI button events, so:
    ///  * everything is drawn with Rect based GUI.* calls;
    ///  * clicks are detected by polling the mouse and hit testing the rects drawn last frame;
    ///  * buttons can repeat while held, and values can be typed in directly.
    /// </summary>
    internal static class Ui
    {
        private static bool _visible = true;
        private static bool _dragging;
        private static bool _probed;
        private static int _page;

        private static Rect _rect = new Rect(24f, 24f, 640f, 560f);

        private static string _status = "";
        private static int _statusFrame = -1;

        private static readonly Dictionary<string, bool> Cap = new Dictionary<string, bool>();

        private struct Hit
        {
            public Rect Rect;
            public Action Action;
            public bool Repeat;
        }

        private static readonly List<Hit> _hits = new List<Hit>();

        private static bool _wasDown;
        private static Vector2 _lastMouse;
        private static int _mouseSource;
        private static int _inputMode;

        private static Action _repeatAction;
        private static float _repeatStart;
        private static float _nextRepeat;

        private static string _editLabel;
        private static string _editBuffer;
        private static Action<float> _editApply;

        private static readonly float[] Presets = { 1f, 2f, 3f, 5f, 10f };
        private const float Step = 0.5f;

        // ---------------------------------------------------------------- lifecycle

        public static void Tick()
        {
            Patches.EnsureOnce();
            RankSanitizer.EnsureOnce();

            if (Ugui.EnsureBuilt())
            {
                Ugui.Tick();
                return;
            }

            if (PressedToggle()) _visible = !_visible;

            if (_visible)
            {
                PollMouse();
                PollText();
            }
            else
            {
                _hits.Clear();
                CancelEdit();
            }
        }

        public static void Draw()
        {
            if (Ugui.Built) return;

            Text.SetupFont();
            ProbeOnce();
            _hits.Clear();

            if (!_visible)
            {
                Btn(new Rect(8f, 8f, 46f, 22f), "GM", () => _visible = true);
                return;
            }

            try
            {
                Box(_rect);

                var x = _rect.x + 10f;
                var w = _rect.width - 20f;
                var y = _rect.y + 8f;

                Btn(new Rect(_rect.x + w - 210f, y, 100f, 20f),
                    _page == 0 ? "> " + Text.T("Multipliers", "倍率") : Text.T("Multipliers", "倍率"),
                    () => _page = 0);
                Btn(new Rect(_rect.x + w - 105f, y, 95f, 20f),
                    _page == 1 ? "> " + Text.T("Stats", "属性") : Text.T("Stats", "属性"),
                    () => _page = 1);

                y = Line(y, 20f, r => Label(r, Text.T("Gain Multipliers 1.5.0", "倍率与属性 1.5.0")));

                if (_page == 0) y = DrawMultipliers(x, w, y);
                else DrawStats(x, w, y);

                DrawEditLine(x, w);

                Btn(new Rect(_rect.x + w - 74f, _rect.y + _rect.height - 30f, 74f, 24f), Text.T("Hide", "隐藏"), () => _visible = false);
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Error("[GainMults] panel error: " + e);
                _visible = false;
            }
        }

        // ---------------------------------------------------------------- page: multipliers

        private static float DrawMultipliers(float x, float w, float y)
        {
            y = Multiplier(y, Text.T("Mana multiplier (in-run XP)", "法力倍率（局内经验）"), Settings.Xp, 0.5f, 100f);
            y = Multiplier(y, Text.T("Gold multiplier (run reward)", "金币倍率（结算）"), Settings.Gold, 0.5f, 100f);
            y = Multiplier(y, Text.T("Wallet fallback", "钱包兜底倍率"), Settings.GoldGrant, 0.5f, 100f);

            y = Line(y, 22f, r => Toggle(r, Settings.Verbose.Value,
                Text.T(" Verbose logging", " 详细日志"),
                () =>
                {
                    Settings.Verbose.Value = !Settings.Verbose.Value;
                    Settings.Save();
                    _statusFrame = -1;
                }));

            y += 2f;
            const float statusHeight = 118f;
            Box(new Rect(x, y, w, statusHeight));
            Label(new Rect(x + 8f, y + 4f, w - 16f, statusHeight - 8f), Status());
            y += statusHeight + 6f;

            y = Line(y, 20f, r => Label(r, Text.T("Tests", "测试")));
            y = Line(y, 24f, r => Btn(r, Text.T("Add 10 mana  ->  XP hook", "加 10 法力  ->  XP 钩子"), TestXp));
            y = Line(y, 24f, r => Btn(r, Text.T("Add 10 gold  ->  wallet hook", "加 10 金币  ->  钱包钩子"), TestGold));
            y = Line(y, 24f, r => Btn(r, Text.T("Synthetic run reward  ->  gold hook", "模拟一次结算  ->  金币钩子"), TestSyntheticReward));

            y += 4f;
            var half = (w - 6f) / 2f;
            Btn(new Rect(x, y, half, 24f), Text.T("Reset counters", "清零计数"), Stats.Reset);
            Btn(new Rect(x + half + 6f, y, half, 24f), Text.T("Reset defaults", "恢复默认"), Settings.ResetDefaults);
            y += 28f;

            y = Line(y, 24f, r => Btn(r, Text.T("Repair wizard rank", "修复巫师等级"), RankSanitizer.RepairNow));

            if (!string.IsNullOrEmpty(Stats.LastMessage))
                Label(new Rect(x, y, w, 40f), Text.T("Last: ", "上次：") + Stats.LastMessage);

            return y;
        }

        // ---------------------------------------------------------------- page: stats

        private static void DrawStats(float x, float w, float y)
        {
            var live = StatEditor.LivePlayers;
            y = Line(y, 20f, r => Label(r, live > 0
                ? Text.T($"Players: {live}", $"已连接玩家：{live}")
                : Text.T("No player stats yet - start a run.", "还没有玩家数据 —— 进一局才能改属性。")));

            foreach (var def in StatEditor.Defs)
            {
                var defLocal = def;
                var label = Text.T(defLocal.Label, defLocal.LabelZh);

                Label(new Rect(x, y, 150f, 20f), label);

                // Click the value to type a bonus directly.
                BoxLabel(new Rect(x + 152f, y, 80f, 20f), StatEditor.Describe(defLocal.Type),
                    () => StartEdit(Text.T(defLocal.Label + " bonus", defLocal.LabelZh + " 加成"),
                        Settings.GetStatDelta(defLocal.Type),
                        v => Settings.SetStatDelta(defLocal.Type, v)));

                Btn(new Rect(x + 240f, y, 26f, 20f), "-", () => StepStat(defLocal, -1f), true);
                Btn(new Rect(x + 268f, y, 26f, 20f), "+", () => StepStat(defLocal, +1f), true);
                Btn(new Rect(x + 298f, y, 26f, 20f), "0", () => Settings.SetStatDelta(defLocal.Type, 0f));

                Label(new Rect(x + 334f, y, 200f, 20f),
                    Text.T("bonus ", "加成 ") + Settings.GetStatDelta(defLocal.Type).ToString("0.##"));

                y += 24f;
            }

            y += 4f;
            Btn(new Rect(x, y, 170f, 24f), Text.T("Clear all bonuses", "清空所有加成"), StatEditor.ResetAll);
        }

        private static void StepStat(StatDef def, float direction)
        {
            Settings.SetStatDelta(def.Type, Settings.GetStatDelta(def.Type) + def.Step * direction);
            _statusFrame = -1;
        }

        // ---------------------------------------------------------------- typed input

        private static void StartEdit(string label, float current, Action<float> apply)
        {
            _editLabel = label;
            _editBuffer = current.ToString("0.##", CultureInfo.InvariantCulture);
            _editApply = apply;
        }

        private static void CancelEdit()
        {
            _editLabel = null;
            _editBuffer = null;
            _editApply = null;
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
                _statusFrame = -1;
            }
            else
            {
                Stats.Say(Text.T("not a number: ", "不是数字：") + buffer);
            }
        }

        private static void PollText()
        {
            if (_editApply == null) return;

            try
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
                {
                    CancelEdit();
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
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[GainMults] typed input unavailable: " + e.GetType().Name);
                CancelEdit();
            }
        }

        private static void DrawEditLine(float x, float w)
        {
            if (_editApply == null) return;

            var y = _rect.y + _rect.height - 30f;
            Box(new Rect(x, y, w - 90f, 24f));
            Label(new Rect(x + 6f, y + 3f, w - 100f, 20f),
                Text.T($"{_editLabel}: {_editBuffer}_   Enter=apply  Esc=cancel",
                        $"{_editLabel}：{_editBuffer}_   Enter 确定  Esc 取消"));
        }

        // ---------------------------------------------------------------- mouse polling

        private static void PollMouse()
        {
            var down = MouseDown();

            if (!TryMouseGui(out var p))
            {
                _wasDown = down;
                return;
            }

            var now = Time.realtimeSinceStartup;

            if (down && !_wasDown)
            {
                var header = new Rect(_rect.x, _rect.y, _rect.width - 220f, 26f);

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
                else if (header.Contains(p))
                {
                    _dragging = true;
                }
            }
            else if (down)
            {
                if (_dragging)
                {
                    _rect.x += p.x - _lastMouse.x;
                    _rect.y += p.y - _lastMouse.y;
                }
                else if (_repeatAction != null && now - _repeatStart > 0.35f && now >= _nextRepeat)
                {
                    _repeatAction();
                    _nextRepeat = now + 0.05f;
                }
            }
            else
            {
                _dragging = false;
                _repeatAction = null;
            }

            _lastMouse = p;
            _wasDown = down;
        }

        private static bool TryHit(Vector2 p, out Hit hit)
        {
            for (var i = _hits.Count - 1; i >= 0; i--)
            {
                if (_hits[i].Rect.Contains(p))
                {
                    hit = _hits[i];
                    return true;
                }
            }

            hit = default;
            return false;
        }

        internal static bool TryMouseGui(out Vector2 gui)
        {
            gui = default;

            if (_mouseSource != 2)
            {
                try
                {
                    var mp = UnityEngine.Input.mousePosition;
                    gui = new Vector2(mp.x, Screen.height - mp.y);
                    _mouseSource = 1;
                    return true;
                }
                catch (Exception e)
                {
                    if (_mouseSource == 1) MelonLoader.MelonLogger.Warning("[GainMults] legacy mouse unavailable: " + e.GetType().Name);
                    _mouseSource = 2;
                }
            }

            if (_mouseSource == 2)
            {
                try
                {
                    var mouse = UnityEngine.InputSystem.Mouse.current;
                    if (mouse == null) return false;
                    var v = mouse.position.ReadValue();
                    gui = new Vector2(v.x, Screen.height - v.y);
                    return true;
                }
                catch (Exception e)
                {
                    MelonLoader.MelonLogger.Warning("[GainMults] mouse unavailable: " + e.GetType().Name + " -> panel is display only");
                    _mouseSource = -1;
                }
            }

            return false;
        }

        internal static bool MouseDown()
        {
            if (_mouseSource == 1 || _mouseSource == 0)
            {
                try { return UnityEngine.Input.GetMouseButton(0); } catch { }
            }

            if (_mouseSource == 2)
            {
                try
                {
                    var mouse = UnityEngine.InputSystem.Mouse.current;
                    return mouse != null && mouse.leftButton.isPressed;
                }
                catch { }
            }

            return false;
        }

        // ---------------------------------------------------------------- keyboard

        internal static bool PressedToggle()
        {
            if (_inputMode == 0 || _inputMode == 1)
            {
                try
                {
                    _inputMode = 1;
                    return UnityEngine.Input.GetKeyDown(KeyCode.F8);
                }
                catch (Exception e)
                {
                    if (_inputMode == 1) MelonLoader.MelonLogger.Warning("[GainMults] legacy Input unavailable: " + e.GetType().Name);
                    _inputMode = 2;
                }
            }

            if (_inputMode == 2)
            {
                try
                {
                    var kb = UnityEngine.InputSystem.Keyboard.current;
                    return kb != null && kb.f8Key.wasPressedThisFrame;
                }
                catch (Exception e)
                {
                    MelonLoader.MelonLogger.Warning("[GainMults] Input System unavailable too: " + e.GetType().Name);
                    _inputMode = -1;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------- drawing helpers

        private static float Line(float y, float height, Action<Rect> draw)
        {
            draw(new Rect(_rect.x + 10f, y, _rect.width - 20f, height));
            return y + height + 4f;
        }

        private static float Multiplier(float y, string label, MelonLoader.MelonPreferences_Entry<float> entry, float min, float max)
        {
            var x = _rect.x + 10f;

            Label(new Rect(x, y, 250f, 20f), label);

            BoxLabel(new Rect(x + 252f, y, 54f, 20f), "x" + entry.Value.ToString("0.0"),
                () => StartEdit(label, entry.Value, v => Set(entry, v, min, max)));

            var bx = x + 314f;
            Btn(new Rect(bx, y, 28f, 20f), "-", () => Set(entry, entry.Value - Step, min, max), true);
            bx += 30f;
            Btn(new Rect(bx, y, 28f, 20f), "+", () => Set(entry, entry.Value + Step, min, max), true);
            bx += 34f;

            foreach (var preset in Presets)
            {
                var value = preset;
                Btn(new Rect(bx, y, 34f, 20f), value.ToString("0"), () => Set(entry, value, min, max));
                bx += 36f;
            }

            return y + 24f;
        }

        private static void Set(MelonLoader.MelonPreferences_Entry<float> entry, float value, float min, float max)
        {
            value = (float)Math.Round(value * 10.0) / 10f;
            if (value < min) value = min;
            if (value > max) value = max;
            entry.Value = value;
            Settings.Save();
            _statusFrame = -1;
        }

        private static void Btn(Rect r, string text, Action action, bool repeat = false)
        {
            _hits.Add(new Hit { Rect = r, Action = action, Repeat = repeat });

            if (!Supported("GUI.Button")) return;
            try { GUI.Button(r, text); }
            catch (Exception e) { Fail("GUI.Button", e); }
        }

        private static void BoxLabel(Rect r, string text, Action onClick)
        {
            _hits.Add(new Hit { Rect = r, Action = onClick });

            if (!Supported("GUI.Box")) return;
            try { GUI.Box(r, text); }
            catch (Exception e) { Fail("GUI.Box", e); }
        }

        private static bool Toggle(Rect r, bool value, string text, Action onClick)
        {
            _hits.Add(new Hit { Rect = r, Action = onClick });

            if (!Supported("GUI.Toggle")) return value;
            try { return GUI.Toggle(r, value, text); }
            catch (Exception e) { Fail("GUI.Toggle", e); return value; }
        }

        private static bool Supported(string api)
        {
            return !Cap.TryGetValue(api, out var ok) || ok;
        }

        private static void Fail(string api, Exception e)
        {
            if (Cap.TryGetValue(api, out var ok) && !ok) return;
            Cap[api] = false;
            MelonLoader.MelonLogger.Warning($"[GainMults] GUI api '{api}' is stripped in this build: {e.Message}");
        }

        private static void ProbeOnce()
        {
            if (_probed) return;
            _probed = true;

            var off = new Rect(-4000f, -4000f, 1f, 1f);
            Label(off, "probe");
            Box(off);
            try { GUI.Button(off, "probe"); } catch (Exception e) { Fail("GUI.Button", e); }

            MelonLoader.MelonLogger.Msg(
                "[GainMults] GUI capabilities -> " +
                $"Label={Supported("GUI.Label")} Box={Supported("GUI.Box")} Button={Supported("GUI.Button")}");
        }

        private static void Box(Rect r)
        {
            if (!Supported("GUI.Box")) return;
            try { GUI.Box(r, ""); }
            catch (Exception e) { Fail("GUI.Box", e); }
        }

        private static void Label(Rect r, string text)
        {
            if (!Supported("GUI.Label")) return;
            try { GUI.Label(r, text); }
            catch (Exception e) { Fail("GUI.Label", e); }
        }

        // ---------------------------------------------------------------- status

        private static string Status()
        {
            if (_statusFrame == Time.frameCount && _status.Length > 0) return _status;

            _statusFrame = Time.frameCount;
            var sb = new StringBuilder();

            try
            {
                var mgr = Il2Cpp.PartyLevelManager.Instance;
                if (mgr == null)
                {
                    sb.AppendLine(Text.T("Party: not spawned yet", "队伍：尚未生成"));
                }
                else
                {
                    var host = "?";
                    try { host = mgr.IsServer ? Text.T("yes", "是") : Text.T("no", "否"); } catch { }
                    sb.AppendLine(Text.T($"Party: level {mgr.GetLevel()}   mana {mgr.GetGatheredExperience():0.#}   host: {host}",
                                         $"队伍：等级 {mgr.GetLevel()}   法力 {mgr.GetGatheredExperience():0.#}   主机：{host}"));
                }
            }
            catch (Exception e)
            {
                sb.AppendLine(Text.T("Party: n/a", "队伍：不可用") + " (" + e.GetType().Name + ")");
            }

            try
            {
                var lp = Il2Cpp.LocalPlayer.Instance;
                if (lp == null)
                {
                    sb.AppendLine(Text.T("Wallet gold: n/a", "钱包金币：不可用"));
                }
                else
                {
                    var rank = "?";
                    try
                    {
                        // Read only: never create progression entries from the panel.
                        var rankMgr = lp.GetCharacterRankManager();
                        var progressMap = rankMgr.RankProgressPerCharacter;
                        var character = lp.GetSelectedCharacter();
                        if (progressMap != null && progressMap.ContainsKey(character))
                        {
                            var progress = progressMap[character];
                            rank = Text.T($"rank {progress.CurrentRank}  prog {progress.ProgressTowardsNextRank:0.##}  prestige {progress.Prestige}",
                                          $"等级 {progress.CurrentRank}  进度 {progress.ProgressTowardsNextRank:0.##}  转生 {progress.Prestige}");
                        }
                    }
                    catch { }

                    sb.AppendLine(Text.T($"Wallet gold: {lp.GetGold()}   wizard: {rank}",
                                         $"钱包金币：{lp.GetGold()}   巫师：{rank}"));
                }
            }
            catch (Exception e)
            {
                sb.AppendLine(Text.T("Wallet: n/a", "钱包：不可用") + " (" + e.GetType().Name + ")");
            }

            sb.AppendLine(Text.T($"Hooks: mana {Stats.XpHits} ({Stats.XpRawSum:0.#}->{Stats.XpScaledSum:0.#})   gold {Stats.GoldCalcHits} ({Stats.GoldLastRaw}->{Stats.GoldLastScaled})",
                                 $"钩子：法力 {Stats.XpHits}（{Stats.XpRawSum:0.#}->{Stats.XpScaledSum:0.#}）  金币 {Stats.GoldCalcHits}（{Stats.GoldLastRaw}->{Stats.GoldLastScaled}）"));
            sb.AppendLine(Text.T($"        wallet {Stats.WalletHits} (+{Stats.WalletSum})   wizard {Stats.WizardHits} ({Stats.WizardRawMinutes:0.#}->{Stats.WizardScaledMinutes:0.#} min)",
                                 $"        钱包 {Stats.WalletHits}（+{Stats.WalletSum}）  巫师 {Stats.WizardHits}（{Stats.WizardRawMinutes:0.#}->{Stats.WizardScaledMinutes:0.#} 分钟）"));

            _status = sb.ToString();
            return _status;
        }

        // ---------------------------------------------------------------- tests

        internal static void TestXp()
        {
            try
            {
                var mgr = Il2Cpp.PartyLevelManager.Instance;
                if (mgr == null)
                {
                    Stats.Say(Text.T("Mana test: not in a run.", "法力测试：不在局内。"));
                    return;
                }

                var beforeLevel = mgr.GetLevel();
                var beforeXp = mgr.GetGatheredExperience();
                mgr.TryAddExperience(10f);
                var afterLevel = mgr.GetLevel();
                var afterXp = mgr.GetGatheredExperience();

                Stats.Say(Text.T($"Mana test: +10, {beforeXp:0.#} -> {afterXp:0.#}, level {beforeLevel} -> {afterLevel}",
                                 $"法力测试：+10，{beforeXp:0.#} -> {afterXp:0.#}，等级 {beforeLevel} -> {afterLevel}"));
                _statusFrame = -1;
            }
            catch (Exception e)
            {
                Stats.Say(Text.T("Mana test failed: ", "法力测试失败：") + e.GetType().Name);
            }
        }

        internal static void TestGold()
        {
            try
            {
                var lp = Il2Cpp.LocalPlayer.Instance;
                if (lp == null)
                {
                    Stats.Say(Text.T("Gold test: LocalPlayer not ready.", "金币测试：LocalPlayer 未就绪。"));
                    return;
                }

                var before = lp.GetGold();
                lp.AddGold(10);
                var after = lp.GetGold();
                Stats.Say(Text.T($"Gold test: wallet {before} -> {after}", $"金币测试：钱包 {before} -> {after}"));
                _statusFrame = -1;
            }
            catch (Exception e)
            {
                Stats.Say(Text.T("Gold test failed: ", "金币测试失败：") + e.GetType().Name);
            }
        }

        internal static void TestSyntheticReward()
        {
            try
            {
                var lp = Il2Cpp.LocalPlayer.Instance;
                if (lp == null)
                {
                    Stats.Say(Text.T("Synthetic reward: LocalPlayer not ready.", "模拟结算：LocalPlayer 未就绪。"));
                    return;
                }

                var calculator = new Il2Cpp.RunGoldCalculator(new Il2Cpp.ILocalPlayer(lp.Pointer));
                var cachedStats = new Il2CppSystem.Collections.Generic.List<Il2Cpp.CachedStat>();
                var upgrades = new Il2CppSystem.Collections.Generic.List<Il2Cpp.IFinalUpgrade>();
                var reward = calculator.Calculate(10, 3, 20, cachedStats, upgrades, (Il2Cpp.Difficulty)0);

                Stats.Say(Text.T("Synthetic reward: total ", "模拟结算：总计 ") +
                          (reward == null ? "null" : reward.TotalGold.ToString()) +
                          Text.T($" (gold hook {Stats.GoldCalcHits} calls)", $"（金币钩子 {Stats.GoldCalcHits} 次）"));
                _statusFrame = -1;
            }
            catch (Exception e)
            {
                Stats.Say(Text.T("Synthetic reward failed (expected outside a run): ", "模拟结算失败（不在局内属正常）：") + e.GetType().Name);
            }
        }
    }
}







