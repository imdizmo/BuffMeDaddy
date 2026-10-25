using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using Jatify.AdminHammer;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.BuffMeDaddy
{
    // Add-on for AdminHammer: a "Buffs" tab that gives you any status effect in the game and, per
    // effect, removes its time limit. Separate and optional: AdminHammer works without it.
    // Everything acts on your own character (status effects are client-side); no console cheats used.
    [BepInPlugin(Guid, "BuffMeDaddy", "1.1.0")]
    [BepInDependency(Jatify.AdminHammer.Plugin.Guid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "jatify.buffmedaddy";
        internal static ConfigEntry<string> UnlimitedCfg, ProfilesCfg;

        private void Awake()
        {
            Log.Bind(Logger);
            AppDomain.CurrentDomain.AssemblyResolve += NewestAdminHammer;   // before any code that touches AdminHammer runs
            Setup();
        }

        // ScriptEngine (the dev profile's hot reload) loads each script renamed "Name-<ticks>", so "AdminHammer" alone would
        // not be found: point it at the newest copy. A normal install loads AdminHammer under its own name; this never runs.
        private static System.Reflection.Assembly NewestAdminHammer(object sender, ResolveEventArgs e)
        {
            if (new System.Reflection.AssemblyName(e.Name).Name != "AdminHammer") return null;
            return AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name.StartsWith("AdminHammer-")).OrderByDescending(a => a.GetName().Name).FirstOrDefault();
        }

        private void OnDestroy() => AppDomain.CurrentDomain.AssemblyResolve -= NewestAdminHammer;

        private void Setup()
        {
            UnlimitedCfg = Config.Bind("Buffs", "Unlimited", "", "Status effects (prefab names, comma separated) whose time limit is removed. Set from the Buffs tab.");
            ProfilesCfg = Config.Bind("Buffs", "Profiles", "", "Saved buff profiles (made from the Buffs tab): Name=effect:1,effect:0;... where 1 = no time limit.");
            AdminHammerApi.AddTab(new AdminHammerApi.Tab
            {
                Name = "Buffs", Targets = true,
                Build = BuffTab.Build, Show = BuffTab.Show, Tick = BuffTab.Tick, Footer = BuffTab.Footer,
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
            AdminHammerApi.Status($"{e.Name}: on{(IsUnlimited(e) ? ", no time limit" : "")}");
        }

        internal static void Remove(Entry e)
        {
            var p = Player.m_localPlayer; if (p == null) return;
            p.GetSEMan().RemoveStatusEffect(e.Hash);
            AdminHammerApi.Status($"{e.Name}: off");
        }
    }

    // The tab, from the Jatify design (AdminHammer view, Buffs): profiles and categories on the left, the effects as
    // tiles, the picked one's details and actions on the right. Built from AdminHammerUi, so it matches the panel.
    internal static class BuffTab
    {
        private const float W = 1100f, Body = 559f, RailW = 232f, InsW = 288f, WorkW = W - RailW - InsW;
        private static RectTransform _area, _rail, _toolbar, _grid, _ins;
        private static TMP_InputField _search;
        private static string _cat = "Meads", _query = "";
        private static Buffs.Entry _sel;
        private static bool _naming;
        private static float _refreshAt;

        internal static void Build(RectTransform area)
        {
            _area = area;
            Line(RailW, 0f, 1f, Body);
            _search = AdminHammerUi.Search(area, "Search buffs", 12f, 14f, RailW - 24f, q => { _query = (q ?? "").Trim().ToLowerInvariant(); PaintRail(); PaintToolbar(); PaintGrid(); });
            _rail = AdminHammerUi.Scroll(area, "Rail", 12f, 60f, RailW - 24f, Body - 74f);
            _toolbar = AdminHammerUi.Place(AdminHammerUi.Node(area, "Toolbar"), RailW + 1f, 0f, WorkW - 1f, 58f);
            var tl = AdminHammerUi.Box(area, "ToolbarLine", AdminHammerUi.LineSoft, AdminHammerUi.NoColor, 1); tl.raycastTarget = false; AdminHammerUi.Place(tl.rectTransform, RailW + 1f, 58f, WorkW - 1f, 1f);
            _grid = AdminHammerUi.Scroll(area, "Grid", RailW + 1f, 59f, WorkW - 1f, Body - 60f);
            Line(RailW + WorkW, 0f, 1f, Body);
            _ins = AdminHammerUi.Place(AdminHammerUi.Node(area, "Inspector"), RailW + WorkW + 1f, 0f, InsW - 1f, Body);
            AdminHammerApi.TargetChanged += () => { if (_area != null && _area.gameObject.activeInHierarchy) Paint(); };
        }

        internal static void Show() { _sel = _sel ?? Buffs.All.FirstOrDefault(e => e.Category == _cat); Paint(); }

        internal static void Tick()
        {
            if (Time.unscaledTime > _refreshAt) { _refreshAt = Time.unscaledTime + 1f; PaintInspector(); }   // timers count down
            if (Input.GetKeyDown(KeyCode.Slash) && _search != null && !_search.isFocused && !_naming) _search.ActivateInputField();
        }

        internal static void Footer(RectTransform foot)
        {
            float x = 16f, h = foot.sizeDelta.y;
            x = AdminHammerUi.Key(foot, "Click", "select", x, h); x = AdminHammerUi.Key(foot, "Right click", "turn on or off", x, h);
            var t = AdminHammerUi.Text(foot, "A profile gives all of its effects at once", "medium", 12.5f, AdminHammerUi.C("mute"), x, 0f, 300f, h); x += AdminHammerUi.Width(t) + 18f;
            AdminHammerUi.Key(foot, "/", "search", x, h);
        }

        // the target: me, or an online player by name (the header's picker)
        private static string TargetName => AdminHammerApi.Target;
        private static bool Me => TargetName.Length == 0;
        private static ZDOID TargetId => Buffs.Others().Where(p => p.m_name == TargetName).Select(p => p.m_characterID).DefaultIfEmpty(ZDOID.None).First();

        private static void Paint() { PaintRail(); PaintToolbar(); PaintGrid(); PaintInspector(); }

        private static IEnumerable<Buffs.Entry> Visible() =>
            _query.Length > 0 ? Buffs.All.Where(e => e.Name.ToLowerInvariant().Contains(_query) || e.Se.name.ToLowerInvariant().Contains(_query))
                              : Buffs.All.Where(e => e.Category == _cat);

        // ---- rail: profiles, then categories ----
        private static void PaintRail()
        {
            AdminHammerUi.Clear(_rail);
            float w = RailW - 24f, y = 0f;
            AdminHammerUi.Section(_rail, "Profiles", 2f, y, w); y += 22f;
            foreach (var p in Profiles.All)
            {
                var prof = p; int inf = p.Fx.Count(f => f.inf);
                var row = AdminHammerUi.Box(_rail, "Profile", AdminHammerUi.C("raised"), AdminHammerUi.LineSoft, 8); AdminHammerUi.Place(row.rectTransform, 0f, y, w - 32f, 34f);
                AdminHammerUi.Text(row.transform, p.Name, "medium", 13.5f, Color.white, 10f, 0f, w - 32f - 80f, 34f).overflowMode = TextOverflowModes.Ellipsis;
                AdminHammerUi.Text(row.transform, $"{p.Fx.Count}{(inf > 0 ? $" · {inf}∞" : "")}", "mono", 11.5f, AdminHammerUi.C("mute"), w - 32f - 70f, 0f, 60f, 34f, TextAlignmentOptions.MidlineRight);
                AdminHammerUi.On(row, () => Apply(prof));
                var del = AdminHammerUi.Box(_rail, "Delete", AdminHammerUi.NoColor, AdminHammerUi.NoColor, 8); AdminHammerUi.Place(del.rectTransform, w - 28f, y, 28f, 34f);
                AdminHammerUi.Fill(AdminHammerUi.Text(del.transform, "×", "medium", 16f, AdminHammerUi.C("mute"), 0f, 0f, 28f, 34f, TextAlignmentOptions.Center).rectTransform);
                AdminHammerUi.On(del, () => { Profiles.Delete(prof); AdminHammerApi.Status($"Deleted {prof.Name}"); PaintRail(); });
                y += 38f;
            }
            if (_naming)
            {
                var name = AdminHammerUi.Search(_rail, "Profile name", 0f, y, w - 66f, _ => { });
                name.characterLimit = 24; name.onSubmit.AddListener(Save); name.ActivateInputField();
                AdminHammerUi.Primary(_rail, "Save", w - 60f, y, 60f, 34f, () => Save(name.text));
                y += 40f;
            }
            else
            {
                int on = Me ? Buffs.All.Count(e => Buffs.Active(e) != null) : 0;
                var b = AdminHammerUi.Box(_rail, "SaveProfile", AdminHammerUi.NoColor, AdminHammerUi.C("line2"), 8); AdminHammerUi.Place(b.rectTransform, 0f, y, w, 32f);
                AdminHammerUi.Fill(AdminHammerUi.Text(b.transform, on > 0 ? $"Save current as profile ({on})" : "Turn effects on to save a profile", "medium", 12.5f, on > 0 ? AdminHammerUi.C("dim") : AdminHammerUi.C("mute"), 0f, 0f, w, 32f, TextAlignmentOptions.Center).rectTransform);
                if (on > 0) AdminHammerUi.On(b, () => { _naming = true; PaintRail(); });
                y += 38f;
            }
            var sep = AdminHammerUi.Box(_rail, "Sep", AdminHammerUi.LineSoft, AdminHammerUi.NoColor, 1); sep.raycastTarget = false; AdminHammerUi.Place(sep.rectTransform, 0f, y + 4f, w, 1f); y += 14f;
            foreach (var c in Buffs.Categories)
            {
                int n = Buffs.All.Count(e => e.Category == c); if (n == 0) continue;
                string cc = c;
                AdminHammerUi.CatRow(_rail, c, n, _query.Length == 0 && c == _cat, y, w, () => { _cat = cc; _query = ""; if (_search != null) _search.text = ""; _sel = Buffs.All.FirstOrDefault(e => e.Category == cc); Paint(); });
                y += 38f;
            }
            _rail.sizeDelta = new Vector2(0f, y);
        }

        private static void Save(string name)
        {
            name = (name ?? "").Trim(); if (name.Length == 0) return;
            var fx = Buffs.All.Where(e => Buffs.Active(e) != null).Select(e => (e.Se.name, Buffs.IsUnlimited(e))).ToList();
            Profiles.Save(name, fx); _naming = false;
            AdminHammerApi.Status($"Saved {name}: {fx.Count} effects"); PaintRail();
        }

        // one click: every effect of the profile, each with its own no-time-limit switch
        private static void Apply(Profiles.Profile p)
        {
            var list = p.Fx.Select(f => (e: Buffs.All.FirstOrDefault(x => x.Se.name == f.se), f.inf)).Where(f => f.e != null).ToList();
            if (Me) foreach (var (e, inf) in list) { Buffs.SetUnlimited(e, inf); Player.m_localPlayer?.GetSEMan().AddStatusEffect(e.Hash, resetTime: true); Buffs.KeepUnlimited(); }
            else { var who = TargetId; if (who.IsNone()) { AdminHammerApi.Status($"{TargetName} is not online"); return; } foreach (var (e, inf) in list) { Buffs.GiveTo(who, e); if (inf) Buffs.SetKept(who, e, true); } }
            AdminHammerApi.Status(Me ? $"{p.Name}: {list.Count} effects on" : $"Gave {p.Name} to {TargetName}: {list.Count} effects");
            Paint();
        }

        private static void PaintToolbar()
        {
            AdminHammerUi.Clear(_toolbar);
            var t = AdminHammerUi.Text(_toolbar, _query.Length > 0 ? "Results" : _cat, "bold", 15f, Color.white, 16f, 0f, 200f, 58f);
            AdminHammerUi.Text(_toolbar, Visible().Count().ToString(), "mono", 12f, AdminHammerUi.C("mute"), 16f + AdminHammerUi.Width(t) + 6f, 1f, 60f, 58f);
            var who = AdminHammerUi.Text(_toolbar, Me ? "your effects" : $"effects for {TargetName}", "medium", 12f, AdminHammerUi.C("dim"), 0f, 0f, 260f, 58f, TextAlignmentOptions.MidlineRight);
            AdminHammerUi.Place(who.rectTransform, WorkW - 1f - 16f - 260f, 0f, 260f, 58f);
            var lbl = AdminHammerUi.Text(_toolbar, "Showing", "medium", 12f, AdminHammerUi.C("mute"), 0f, 0f, 60f, 58f, TextAlignmentOptions.MidlineRight);
            AdminHammerUi.Place(lbl.rectTransform, WorkW - 1f - 16f - AdminHammerUi.Width(who) - 8f - 60f, 0f, 60f, 58f);
        }

        private static void PaintGrid()
        {
            AdminHammerUi.Clear(_grid);
            var list = Visible().ToList(); var who = Me ? ZDOID.None : TargetId;
            float tw = (WorkW - 1f - 32f - 16f) / 3f;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                var tile = AdminHammerUi.Tile(_grid, e.Se.m_icon, e.Name, e == _sel, 16f + (i % 3) * (tw + 8f), 14f + (i / 3) * 68f, tw,
                    () => { _sel = e; PaintGrid(); PaintInspector(); },
                    () => { _sel = e; DoGive(e); Paint(); },
                    () => { _sel = e; if (Me && Buffs.Active(e) != null) DoRemove(e); else DoGive(e); Paint(); });
                if (Me && Buffs.Active(e) != null) AdminHammerUi.Tag(tile, "ON", "on");
                if (Me ? Buffs.IsUnlimited(e) : Buffs.Kept(who, e)) AdminHammerUi.Tag(tile, "∞", "inf");
            }
            _grid.sizeDelta = new Vector2(0f, 14f + Mathf.CeilToInt(list.Count / 3f) * 68f + 10f);
        }

        private static void PaintInspector()
        {
            if (_ins == null) return; AdminHammerUi.Clear(_ins);
            float x = 18f, w = InsW - 1f - 36f;
            if (_sel == null) { AdminHammerUi.Para(_ins, "Pick an effect to give it or take it off.", 14f, AdminHammerUi.C("mute"), x, 18f, w, 60f); return; }
            AdminHammerUi.IconBox(_ins, _sel.Se.m_icon, _sel.Name, x, 18f, 56f, 12);
            var n = AdminHammerUi.Text(_ins, _sel.Name, "bold", 18f, Color.white, x + 68f, 18f, w - 68f, 44f, TextAlignmentOptions.TopLeft); n.textWrappingMode = TextWrappingModes.Normal; n.maxVisibleLines = 2; n.lineSpacing = -10f;
            AdminHammerUi.Text(_ins, _sel.Se.name, "mono", 12f, AdminHammerUi.C("mute"), x + 68f, 58f, w - 68f, 18f);
            var a = Me ? Buffs.Active(_sel) : null; var who = Me ? ZDOID.None : TargetId;
            string state = !Me ? $"For {TargetName}" : a == null ? "Off" : a.m_ttl <= 0f ? "On, no time limit" : $"On, {Mathf.CeilToInt(a.GetRemaningTime())} s left";
            float y = 92f + AdminHammerUi.Meta(_ins, x, 92f, w, ("Category", _sel.Category), ("Duration", _sel.Se.m_ttl > 0f ? $"{Mathf.RoundToInt(_sel.Se.m_ttl)} s" : "Set by the game"), ("State", state)) + 10f;
            bool unl = Me ? Buffs.IsUnlimited(_sel) : Buffs.Kept(who, _sel);
            AdminHammerUi.Text(_ins, Me ? "No time limit" : "Keep topping up while I'm online", "medium", 13f, AdminHammerUi.C("dim"), x, y, w - 50f, 22f);
            AdminHammerUi.Switch(_ins, x + w - 38f, y, unl, on =>
            {
                if (Me) Buffs.SetUnlimited(_sel, on);
                else { var id = TargetId; if (!id.IsNone()) Buffs.SetKept(id, _sel, on); }
                PaintGrid(); PaintInspector();
            });
            y += 32f;
            AdminHammerUi.Para(_ins, Me ? "Right click a tile to turn an effect on or off." : "Effects can't be taken off another player. Switching this off lets the effect run out.", 12.5f, AdminHammerUi.C("mute"), x, y, w, 40f);
            float by = Body - 18f - 22f - 50f - 46f;
            AdminHammerUi.Primary(_ins, "Give", x, by, (w - 8f) / 2f, 42f, () => { DoGive(_sel); Paint(); });
            AdminHammerUi.Secondary(_ins, "Remove", x + (w - 8f) / 2f + 8f, by + 3f, (w - 8f) / 2f, 36f, () => { DoRemove(_sel); Paint(); }, !Me || a != null);
            AdminHammerUi.Secondary(_ins, "Remove all", x, by + 50f, w, 36f, () => { RemoveAll(); Paint(); });
        }

        private static void DoGive(Buffs.Entry e)
        {
            if (Me) { Buffs.Give(e); return; }
            var who = TargetId; if (who.IsNone()) { AdminHammerApi.Status($"{TargetName} is not online"); return; }
            Buffs.GiveTo(who, e); AdminHammerApi.Status($"Gave {e.Name} to {TargetName}");
        }

        private static void DoRemove(Buffs.Entry e)
        {
            if (Me) { Buffs.Remove(e); return; }
            var who = TargetId; if (!who.IsNone()) Buffs.SetKept(who, e, false);
            AdminHammerApi.Status($"Stopped topping up {e.Name} on {TargetName}; it runs out normally");
        }

        private static void RemoveAll()
        {
            if (!Me)
            {
                var who = TargetId; if (Buffs.Keep.TryGetValue(who, out var s)) s.Clear();
                AdminHammerApi.Status($"Stopped topping up everything on {TargetName}"); return;
            }
            var p = Player.m_localPlayer; if (p == null) return;
            foreach (var e in Buffs.All.Where(e => Buffs.Active(e) != null)) p.GetSEMan().RemoveStatusEffect(e.Hash, quiet: true);
            AdminHammerApi.Status("Removed all effects");
        }

        private static void Line(float x, float y, float w, float h)
        {
            var l = AdminHammerUi.Box(_area, "Line", AdminHammerUi.C("glassLine"), AdminHammerUi.NoColor, 1); l.raycastTarget = false; AdminHammerUi.Place(l.rectTransform, x, y, w, h);
        }
    }

    // Buff profiles (user): saved sets of effects, each with its own no-time-limit switch, applied in one click.
    // In the config as "Name=effect:1,effect:0;Name2=..." (effect prefab names; 1 = no time limit).
    internal static class Profiles
    {
        internal sealed class Profile { public string Name; public List<(string se, bool inf)> Fx = new List<(string, bool)>(); }

        internal static List<Profile> All
        {
            get
            {
                var list = new List<Profile>();
                foreach (var part in Plugin.ProfilesCfg.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = part.IndexOf('='); if (eq <= 0) continue;
                    var p = new Profile { Name = part.Substring(0, eq).Trim() };
                    foreach (var fx in part.Substring(eq + 1).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var bits = fx.Split(':'); if (bits[0].Trim().Length > 0) p.Fx.Add((bits[0].Trim(), bits.Length > 1 && bits[1].Trim() == "1"));
                    }
                    list.Add(p);
                }
                return list;
            }
        }

        internal static void Save(string name, List<(string se, bool inf)> fx)
        {
            name = new string(name.Where(c => c != ';' && c != '=' && c != ',' && c != ':').ToArray()).Trim(); if (name.Length == 0) return;
            var list = All.Where(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            list.Add(new Profile { Name = name, Fx = fx });
            Write(list);
        }

        internal static void Delete(Profile p) => Write(All.Where(x => x.Name != p.Name).ToList());

        private static void Write(List<Profile> list) =>
            Plugin.ProfilesCfg.Value = string.Join(";", list.Select(p => p.Name + "=" + string.Join(",", p.Fx.Select(f => f.se + ":" + (f.inf ? "1" : "0")))));
    }
}
