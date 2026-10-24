using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using Jatify.AdminHammer;
using SeneaLUI.Components;
using SeneaLUI.Core;
using SeneaLUI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.BuffMeDaddy
{
    // Add-on for AdminHammer: a "Buffs" tab that gives you any status effect in the game and, per
    // effect, removes its time limit. Separate and optional: AdminHammer works without it.
    // Everything acts on your own character (status effects are client-side); no console cheats used.
    [BepInPlugin(Guid, "BuffMeDaddy", "1.0.1")]
    [BepInDependency(Jatify.AdminHammer.Plugin.Guid)]
    [BepInDependency("seneaL.valheim.ui")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "jatify.buffmedaddy";
        internal static ConfigEntry<string> UnlimitedCfg;

        private void Awake()
        {
            Log.Bind(Logger);
            UnlimitedCfg = Config.Bind("Buffs", "Unlimited", "", "Status effects (prefab names, comma separated) whose time limit is removed. Set from the Buffs tab.");
            AdminHammerApi.AddTab(new AdminHammerApi.Tab
            {
                Name = "Buffs", Icon = "spark", Title = "Buffs",
                Build = BuffTab.Build, Show = BuffTab.Show, Tick = BuffTab.Tick,
            });
        }

        // Runs always (not only with the panel open): keeps unlimited effects from running out.
        private float _next;
        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            Buffs.KeepUnlimited();
            Buffs.KeepOthers();
        }
    }

    internal static class Buffs
    {
        internal sealed class Entry { public StatusEffect Se; public string Name, Category; public int Hash; }

        internal static readonly string[] Categories = { "Meads", "Forsaken powers", "Food & rest", "Set & item effects", "Ailments", "Other" };
        private static readonly string[] AilmentWords = { "wet", "burning", "poison", "frost", "lightning", "smoked", "tared", "encumbered", "cold", "freezing", "slimed", "spirit", "staggered", "hit", "puke", "fall" };

        private static List<Entry> _all;
        internal static List<Entry> All
        {
            get
            {
                if (_all != null && _all.Count > 0) return _all;
                _all = new List<Entry>();
                if (ObjectDB.instance == null) return _all;
                foreach (var se in ObjectDB.instance.m_StatusEffects)
                {
                    if (se == null) continue;
                    var loc = string.IsNullOrEmpty(se.m_name) ? "" : Localization.instance.Localize(se.m_name);
                    var name = string.IsNullOrEmpty(loc) || loc.StartsWith("[") ? Words(se.name) : loc;
                    _all.Add(new Entry { Se = se, Name = name, Hash = se.NameHash(), Category = Classify(se) });
                }
                _all = _all.OrderBy(e => e.Name).ToList();
                return _all;
            }
        }

        private static string Classify(StatusEffect se)
        {
            var n = se.name.ToLowerInvariant(); var c = (se.m_category ?? "").ToLowerInvariant();
            if (n.StartsWith("potion_") || c.Contains("potion") || c.Contains("mead")) return "Meads";
            if (n.StartsWith("gp_")) return "Forsaken powers";
            if (n.StartsWith("seteffect_") || n.Contains("equip") || n.StartsWith("se_") && c.Contains("set")) return "Set & item effects";
            if (n.Contains("rested") || n.Contains("resting") || n.Contains("shelter") || n.Contains("campfire") || n.Contains("cozy") || n.Contains("food") || n.Contains("feast") || c.Contains("food")) return "Food & rest";
            if (AilmentWords.Any(w => n.Contains(w))) return "Ailments";
            return "Other";
        }

        private static string Words(string s) => System.Text.RegularExpressions.Regex.Replace(s.Replace('_', ' '), "(?<=[a-z])([A-Z])", " $1").Trim();

        // ---- unlimited set (by prefab name, saved in the config) --------------------------------
        private static HashSet<string> _unl;
        private static HashSet<string> Unl => _unl ?? (_unl = new HashSet<string>(Plugin.UnlimitedCfg.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)));
        internal static bool IsUnlimited(Entry e) => Unl.Contains(e.Se.name);

        internal static void SetUnlimited(Entry e, bool on)
        {
            if (on) Unl.Add(e.Se.name); else Unl.Remove(e.Se.name);
            Plugin.UnlimitedCfg.Value = string.Join(",", Unl);
            var active = Active(e);
            // Turning it off: give the effect a fresh normal duration from now.
            if (!on && active != null) active.m_ttl = e.Se.m_ttl > 0f ? active.GetDuration() + e.Se.m_ttl : 0f;
            KeepUnlimited();
        }

        internal static void KeepUnlimited()
        {
            var p = Player.m_localPlayer;
            if (p == null || Unl.Count == 0) return;
            foreach (var se in p.GetSEMan().GetStatusEffects())
                if (se != null && se.m_ttl > 0f && Unl.Contains(se.name.Replace("(Clone)", "").Trim())) se.m_ttl = 0f;   // 0 = never expires
        }

        // ---- other players ---------------------------------------------------------------------------
        // Giving uses Valheim's own RPC on their character (works without this mod on their side).
        // No remote remove: a routed RPC's sender id is set by the sending client, so another player's
        // game cannot tell a real admin from a forged one. "No time limit" for them: we re-send the effect before it
        // runs out, while we are both online.
        internal static readonly Dictionary<ZDOID, HashSet<int>> Keep = new Dictionary<ZDOID, HashSet<int>>();
        private static readonly Dictionary<(ZDOID, int), float> _nextSend = new Dictionary<(ZDOID, int), float>();

        internal static List<ZNet.PlayerInfo> Others()
        {
            var me = Player.m_localPlayer != null ? Player.m_localPlayer.GetZDOID() : ZDOID.None;
            return ZNet.instance == null ? new List<ZNet.PlayerInfo>() : ZNet.instance.GetPlayerList().Where(p => !p.m_characterID.IsNone() && p.m_characterID != me).ToList();
        }

        internal static void GiveTo(ZDOID who, Entry e) =>
            ZRoutedRpc.instance?.InvokeRoutedRPC(who.UserID, who, "RPC_AddStatusEffect", e.Hash, true, 0, 0f, -1);


        internal static bool Kept(ZDOID who, Entry e) => Keep.TryGetValue(who, out var s) && s.Contains(e.Hash);

        internal static void SetKept(ZDOID who, Entry e, bool on)
        {
            if (!Keep.TryGetValue(who, out var s)) Keep[who] = s = new HashSet<int>();
            if (on) { s.Add(e.Hash); GiveTo(who, e); } else s.Remove(e.Hash);
            _nextSend[(who, e.Hash)] = Time.unscaledTime + Interval(e.Hash);
        }

        private static float Interval(int hash)
        {
            var se = ObjectDB.instance?.GetStatusEffect(hash);
            return se == null || se.m_ttl <= 0f ? 60f : Mathf.Clamp(se.m_ttl * 0.5f, 2f, 60f);
        }

        // Re-send kept effects before they run out; forget players who left.
        internal static void KeepOthers()
        {
            if (Keep.Count == 0 || ZNet.instance == null) return;
            var online = new HashSet<ZDOID>(Others().Select(p => p.m_characterID));
            foreach (var who in Keep.Keys.ToList())
            {
                if (!online.Contains(who)) { Keep.Remove(who); continue; }
                foreach (var hash in Keep[who])
                    if (!_nextSend.TryGetValue((who, hash), out var t) || Time.unscaledTime >= t)
                    {
                        ZRoutedRpc.instance?.InvokeRoutedRPC(who.UserID, who, "RPC_AddStatusEffect", hash, true, 0, 0f, -1);
                        _nextSend[(who, hash)] = Time.unscaledTime + Interval(hash);
                    }
            }
        }

        // ---- give / remove ------------------------------------------------------------------------
        internal static StatusEffect Active(Entry e) => Player.m_localPlayer?.GetSEMan().GetStatusEffect(e.Hash);

        internal static void Give(Entry e)
        {
            var p = Player.m_localPlayer; if (p == null) return;
            p.GetSEMan().AddStatusEffect(e.Hash, resetTime: true);
            KeepUnlimited();
            AdminHammerApi.Toast($"{e.Name}: on{(IsUnlimited(e) ? ", no time limit" : "")}");
        }

        internal static void Remove(Entry e)
        {
            var p = Player.m_localPlayer; if (p == null) return;
            p.GetSEMan().RemoveStatusEffect(e.Hash);
            AdminHammerApi.Toast($"{e.Name}: off");
        }
    }

    // The tab: categories and search on the left, the effects as a slot grid, actions for the selected one.
    internal static class BuffTab
    {
        private static RectTransform _area, _cats, _strip;
        private static ScrollBox _grid;
        private static SearchBox _search;
        private static SwitchBox _unlimited;
        private static TextMeshProUGUI _selName, _selInfo;
        private static Slot _selSlot;
        private static string _cat = "Meads", _query = "";
        private static RectTransform _targets;
        private static ZDOID _target = ZDOID.None;   // None = me
        private static string _targetName = "Me";
        private static TextMeshProUGUI _unlLabel;
        private static Buffs.Entry _sel;
        private static float _refreshAt;

        internal static void Build(RectTransform area)
        {
            _area = area;
            _search = new SearchBox(area, "search", 220f);
            _search.Root.anchoredPosition = new Vector2(0f, -68f);
            _search.Changed += q => { _query = (q ?? "").Trim().ToLowerInvariant(); Paint(); };
            _cats = Ui.TL(area, "cats", 0f, 112f, 220f, 400f);

            // selected effect + actions (where AdminHammer's strip sits)
            _strip = Ui.TL(area, "sel", 0f, 0f, 1052f, 56f);
            var holder = Ui.TL(_strip, "slot", 0f, 0f, 220f, 56f);
            _selSlot = new Slot(holder, "s", 0f, 0f, 56, 40, null);
            _selName = Ui.Label(holder, "n", TextStyles.RowName, 0f, 68f, 22f, 152f, TextAlignmentOptions.TopLeft);
            _selName.overflowMode = TextOverflowModes.Ellipsis;
            _selInfo = Ui.Label(holder, "i", TextStyle.Body(12f, Palette.Ash), 0f, 68f, 42f, 152f, TextAlignmentOptions.TopLeft);
            _selInfo.overflowMode = TextOverflowModes.Ellipsis;
            Btn(_strip, "Give", 240f, () => { if (_sel != null) { DoGive(_sel); Paint(); } }, false);
            Btn(_strip, "Remove", 350f, () => { if (_sel != null) { DoRemove(_sel); Paint(); } }, true);
            Btn(_strip, "Remove all", 460f, RemoveAll, true, 110f);
            _unlimited = new SwitchBox(_strip, "unl", on =>
            {
                if (_sel == null) return;
                if (Me) Buffs.SetUnlimited(_sel, on);
                else { Buffs.SetKept(_target, _sel, on); AdminHammerApi.Toast($"{_sel.Name} for {_targetName}: {(on ? "kept topped up while you are online" : "normal time")}"); }
                Paint();
            });
            Ui.Place(_unlimited.Root, 0f, 1f, 600f, -17f, SwitchBox.W, SwitchBox.H);
            _unlLabel = Ui.Label(_strip, "ul", TextStyles.RowName, 0f, 650f, 33f, 400f, TextAlignmentOptions.TopLeft);
            _targets = Ui.TL(area, "targets", 240f, 66f, 800f, 30f);

            _grid = new ScrollBox(area, "grid", 10 * 80f - 8f, 404f, 160f);
            _grid.Place(240f, 104f, 10 * 80f - 8f, 404f, 8f);
            Ui.Label(area, "hint", TextStyles.Hint, 0f, 240f, 540f, 800f, TextAlignmentOptions.TopLeft).text =
                "Click: select   ·   Right click: give / remove   ·   gold edge = active   ·   ∞ = no time limit";
        }

        internal static void Show() { _sel = _sel ?? Buffs.All.FirstOrDefault(e => e.Category == _cat); Paint(); }

        internal static void Tick()
        {
            _search.Tick(true);
            _grid.Tick(Hud.instance);
            _unlimited.Tick();
            if (Time.unscaledTime > _refreshAt) { _refreshAt = Time.unscaledTime + 1f; PaintSelected(); }   // timers count down
        }

        private static bool Me => _target.IsNone();

        private static void Paint() { PaintTargets(); PaintCats(); PaintGrid(); PaintSelected(); }

        private static void PaintTargets()
        {
            Clear(_targets);
            var others = Buffs.Others();
            if (!Me && !others.Any(p => p.m_characterID == _target)) { _target = ZDOID.None; _targetName = "Me"; }
            Ui.Label(_targets, "l", TextStyles.CardLabel, 0f, 0f, 20f, 60f, TextAlignmentOptions.TopLeft).text = "FOR";
            float x = 44f;
            foreach (var (id, name) in new[] { (ZDOID.None, "Me") }.Concat(others.Select(p => (p.m_characterID, p.m_name))))
            {
                bool on = id == _target;
                float w = Mathf.Max(60f, 24f + name.Length * 8f);
                var b = new GoldButton(_targets, "t" + name, w, () => { _target = id; _targetName = name; Paint(); }, 28, !on, on ? null : TextStyles.BtnSmallDark);
                b.SetLabel(name);
                b.Root.anchoredPosition = new Vector2(x, 0f);
                x += w + 6f;
            }
        }

        private static void DoGive(Buffs.Entry e)
        {
            if (Me) { Buffs.Give(e); return; }
            Buffs.GiveTo(_target, e);
            AdminHammerApi.Toast($"{e.Name} given to {_targetName}");
        }

        private static void DoRemove(Buffs.Entry e)
        {
            if (Me) { Buffs.Remove(e); return; }
            Buffs.SetKept(_target, e, false);
            AdminHammerApi.Toast($"Stopped topping up {e.Name} on {_targetName}; it runs out normally");
        }

        private static IEnumerable<Buffs.Entry> Visible() =>
            _query.Length > 0 ? Buffs.All.Where(e => e.Name.ToLowerInvariant().Contains(_query) || e.Se.name.ToLowerInvariant().Contains(_query))
                              : Buffs.All.Where(e => e.Category == _cat);

        private static void PaintCats()
        {
            Clear(_cats);
            float y = 0f;
            foreach (var c in Buffs.Categories)
            {
                int n = Buffs.All.Count(e => e.Category == c); if (n == 0) continue;
                bool on = _query.Length == 0 && c == _cat;
                var row = Ui.TL(_cats, c, 0f, y, 220f, 40f);
                var band = Ui.Img(row, on ? Skin.Sliced(40, Styles.RowOn) : null, on ? Color.white : Palette.Clear);
                band.type = Image.Type.Sliced; band.raycastTarget = true;
                if (on) Ui.Img(Ui.Place(Ui.Node(row, "bar"), 0f, 0.5f, 0f, 0f, 3f, 26f), null, Palette.Gold);
                var st = TextStyle.Medium(15f, on ? Palette.Parchment : Palette.Linen);
                Ui.Label(row, "n", st, 0f, 14f, (40f - st.Size * 1.3f) * 0.5f + st.Baseline(1.3f), 160f, TextAlignmentOptions.TopLeft).text = c;
                var cs = TextStyle.SemiBold(12f, Palette.Ash);
                Ui.Label(row, "c", cs, 1f, -12f, (40f - cs.Size * 1.3f) * 0.5f + cs.Baseline(1.3f), 44f, TextAlignmentOptions.TopRight).text = n.ToString();
                var cc = c;
                Click(row, () => { _cat = cc; _query = ""; _search.Clear(); _sel = Buffs.All.FirstOrDefault(e => e.Category == cc); Paint(); });
                y += 43f;
            }
        }

        private static void PaintGrid()
        {
            Clear(_grid.Content);
            int i = 0;
            foreach (var e in Visible())
            {
                var entry = e;
                bool active = Me && Buffs.Active(e) != null, unl = Me ? Buffs.IsUnlimited(e) : Buffs.Kept(_target, e);
                var slot = new Slot(_grid.Content, "b" + i, (i % 10) * 80f, (i / 10) * 80f, 72, 52, null, false);
                slot.SetIcon(e.Se.m_icon != null ? e.Se.m_icon : Icons.Get("spark"), e == _sel);
                if (active) Ui.Img(Ui.Stretch(slot.Root, "on", -2f), Skin.Sliced(40, Styles.BuildReady), Color.white).type = Image.Type.Sliced;
                if (unl) Ui.Label(slot.Root, "inf", TextStyle.Display(18f, Palette.Gold, 0f), 1f, -6f, 20f, 24f, TextAlignmentOptions.TopRight).text = "∞";
                var hit = Ui.Img(slot.Root, null, Palette.Clear); hit.raycastTarget = true;
                var h = slot.Root.gameObject.AddComponent<UIInputHandler>();
                h.m_onLeftClick = _ => { _sel = entry; Paint(); };
                h.m_onRightClick = _ => { _sel = entry; if (Me && Buffs.Active(entry) != null) DoRemove(entry); else DoGive(entry); Paint(); };
                Tooltip.Bind(h, slot.Root, () => new Tip().Reset(entry.Name, 1).AddRow(entry.Category, entry.Se.name));
                i++;
            }
            _grid.SetContentHeight(Mathf.CeilToInt(i / 10f) * 80f);
        }

        private static void PaintSelected()
        {
            if (_sel == null) { _selName.text = "Pick a buff"; _selInfo.text = ""; _selSlot.SetIcon(Icons.Get("spark"), false); return; }
            _selSlot.SetIcon(_sel.Se.m_icon != null ? _sel.Se.m_icon : Icons.Get("spark"), true);
            _selName.text = _sel.Name;
            _unlLabel.text = Me ? "No time limit" : "No time limit (while I'm online)";
            if (!Me)
            {
                _selInfo.text = Buffs.Kept(_target, _sel) ? $"kept topped up on {_targetName}" : $"for {_targetName}";
                _unlimited.Set(Buffs.Kept(_target, _sel));
                return;
            }
            var a = Buffs.Active(_sel);
            _selInfo.text = a == null ? (_sel.Se.m_ttl > 0f ? $"lasts {Mathf.RoundToInt(_sel.Se.m_ttl)} s" : "no time limit of its own")
                          : a.m_ttl <= 0f ? "active · no time limit" : $"active · {Mathf.CeilToInt(a.GetRemaningTime())} s left";
            _unlimited.Set(Buffs.IsUnlimited(_sel));
        }

        private static void RemoveAll()
        {
            if (!Me)
            {
                if (Buffs.Keep.TryGetValue(_target, out var s)) s.Clear();
                AdminHammerApi.Toast($"Stopped topping up everything on {_targetName}");
                Paint(); return;
            }
            var p = Player.m_localPlayer; if (p == null) return;
            foreach (var e in Buffs.All.Where(e => Buffs.Active(e) != null)) p.GetSEMan().RemoveStatusEffect(e.Hash, quiet: true);
            AdminHammerApi.Toast("All buffs removed");
            Paint();
        }

        private static void Btn(RectTransform parent, string label, float x, Action a, bool dark, float w = 100f)
        {
            var b = new GoldButton(parent, "b" + label, w, a, 36, dark, dark ? TextStyles.BtnSmallDark : null);
            b.SetLabel(label);
            b.Root.anchoredPosition = new Vector2(x, -10f);
        }

        private static void Click(RectTransform rt, Action a) => rt.gameObject.AddComponent<UIInputHandler>().m_onLeftClick = _ => a();
        private static void Clear(Transform t) { for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject); }
    }
}
