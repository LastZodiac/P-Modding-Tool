// The opponent card: a small panel over the game while a match is found and loading (and between the games of a set):
// who you're playing, your record against them, your matchup and stage records, your chance to win and, between games,
// your best picks for the next one. It never takes focus away from the game.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    class LiveCard : OverlayForm
    {
        public LiveState State;
        public MatchHistory History;
        public StatsArt Art;
        public bool Dismissed;
        public Func<string, List<(string Habit, string Tip)>> Scout;   // a player's habits from your games against them
        public bool Pill;              // during the fight: a slim bar
        public Rectangle? Game;        // where the game is on screen
        public bool Left;              // in the top-left corner (normally top right: Brawl Online's chat is on the left)
        public string CoachLine;       // between games: the coach's main note on the last one

        public bool Preview;           // Settings' preview: made-up numbers
        public bool Expanded;          // the whole card (normally just the top and one line: it shouldn't cover the game)
        public event Action<bool> ExpandedChanged;
        readonly Timer _anim = new Timer { Interval = 30 };
        public LiveCard()
        {
            Size = new Size(430, 250);
            _anim.Tick += (s, e) => { if (!Flashing && !Animating) _anim.Stop(); Invalidate(); };
            Cursor = Cursors.Hand;
            MouseClick += (s, e) => { if (Pill) return; Expanded = !Expanded; _openedAt = DateTime.Now; Refresh2(); ExpandedChanged?.Invoke(Expanded); };
        }
        DateTime _openedAt;
        bool Animating => (DateTime.Now - _openedAt).TotalSeconds < 0.3;

        // this card can be clicked (to open or close it); the slim bar during the fight lets clicks through
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; if (!Pill) cp.ExStyle &= ~0x20; return cp; } }

        bool Flashing => State != null && State.LastKo != default && (DateTime.Now - State.LastKo).TotalSeconds < 3;

        public void Place()
        {
            // a top corner of the game (the damage is along the bottom); during the fight the slim bar sits a little lower
            // on the right, under the timer
            var area = Game ?? Screen.PrimaryScreen.WorkingArea;
            int x = Left ? area.X + 14 : area.Right - Width - 14;
            int y = area.Y + 14 + (Pill && !Left ? (int)(area.Height * 0.12) : 0);
            if (Location != new Point(x, y)) Location = new Point(x, y);
        }

        static Color RateColor(double r) => RecordTable.RateColor(r);

        /// <summary>The slim bar during the fight: their head, name, set score, matchup and your chance.</summary>
        void PaintPill(Graphics g)
        {
            var s = State; var h = History;
            using (var border = new Pen(Color.FromArgb(160, OverlayLook.Accent), 1.5f)) g.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
            float x = 8;
            var head = Art?.Head(s?.TheirChar);
            if (head != null) { StatsArt.Cover(g, head, new RectangleF(6, 5, 38, 44), 5, false, 0.5f, 0f); x = 52; }
            bool kos = s != null && s.StocksTaken + s.StocksLost > 0;
            Text(g, "vs " + (s?.OpponentName ?? "") + (kos ? "" : s != null && s.MyWins + s.TheirWins > 0 ? $"  ·  set {s.MyWins}–{s.TheirWins}" : $"  ·  game {s?.Game}"), Theme.BodyBold, Theme.Text, new RectangleF(x, 6, Width - x - (kos ? 140 : 70), 20));
            if (kos) Text(g, $"KOs {s.StocksTaken}–{s.StocksLost}", Theme.BodyBold, s.StocksTaken > s.StocksLost ? Theme.Good : s.StocksTaken < s.StocksLost ? Theme.Bad : Theme.Text, new RectangleF(Width - 140, 6, 68, 20));
            if (Flashing)
            {
                // a stock just went: a quick flash across the bar
                float t = (float)((DateTime.Now - s.LastKo).TotalSeconds / 3);
                var c = s.LastKoMine ? Theme.Good : Theme.Bad;
                int a = (int)(200 * (1 - t));
                using (var b = new SolidBrush(Color.FromArgb(Math.Max(0, a / 3), c))) g.FillRectangle(b, 2, 2, Width - 4, Height - 4);
                using (var pen = new Pen(Color.FromArgb(Math.Max(0, a), c), 2.5f)) g.DrawRectangle(pen, 2, 2, Width - 5, Height - 5);
                float slide = 30 * (1 - ReportArt.EaseOut(Math.Min(1, t * 4)));
                Text(g, s.LastKoMine ? "STOCK TAKEN!" : "Stock lost", Theme.Bold(11f), Color.FromArgb(Math.Max(0, Math.Min(255, a + 55)), c), new RectangleF(x + slide, 28, Width - x - 70, 20));
                if (_proj != null) Text(g, $"{_proj.Chance * 100:0}%", Theme.Bold(15f), RateColor(_proj.Chance), new RectangleF(Width - 70, 14, 62, 28), true);
                return;
            }
            Text(g, _pillLine ?? "", Theme.Small, _pillColor, new RectangleF(x, 28, Width - x - 70, 18));
            if (_proj != null) Text(g, $"{_proj.Chance * 100:0}%", Theme.Bold(15f), RateColor(_proj.Chance), new RectangleF(Width - 70, 14, 62, 28), true);
        }
        string _pillLine; Color _pillColor = Theme.Muted;

        List<(string Text, Color Color)> _lines = new List<(string, Color)>();
        MatchHistory.Projection _proj;

        /// <summary>Works out what to say (and how tall the card is) before it's drawn.</summary>
        public void Refresh2()
        {
            var s = State; var h = History;
            double op = Pill ? 0.8 : 0.88;
            SetOpacity(op);
            _lines = new List<(string, Color)>(); _proj = null;
            if (s != null && h != null)
            {
                string oppId = s.OpponentCode != null ? h.UserIdOf(s.OpponentCode) : null;
                var vsThem = h.Singles().Where(x => oppId != null && x.Them.UserId == oppId).ToList();
                var lines = _lines;
                if (vsThem.Count == 0) lines.Add(("First time playing them (or no games kept yet)", Theme.Muted));
                else
                {
                    var r = MatchHistory.Count(vsThem);
                    lines.Add(($"Head-to-head: {r} ({r.Percent}) in {r.Games} games", RateColor(r.Rate)));
                    var mains = vsThem.GroupBy(x => x.Them.Character).OrderByDescending(x => x.Count()).Take(3).Select(x => $"{Fighters.Name(x.Key)} ×{x.Count()}");
                    lines.Add(("They usually play: " + string.Join(", ", mains), Theme.Text));
                }
                if (Scout != null && oppId != null)
                    foreach (var l in Scout(oppId).Take(2)) lines.Add(("Scouting: " + l.Habit.Replace(" of the time", ""), OverlayLook.Accent));
                if (s.MyChar != null && s.TheirChar != null)
                {
                    var mu = MatchHistory.Count(h.Singles().Where(x => x.Me.Character == s.MyChar && x.Them.Character == s.TheirChar));
                    lines.Add((mu.Games == 0 ? $"{Fighters.Name(s.MyChar.Value)} vs {Fighters.Name(s.TheirChar.Value)}: no games yet"
                        : $"{Fighters.Name(s.MyChar.Value)} vs {Fighters.Name(s.TheirChar.Value)}: {mu} lifetime ({mu.Percent})", mu.Games == 0 ? Theme.Muted : RateColor(mu.Rate)));
                }
                else if (s.TheirChar != null) lines.Add(($"They picked {Fighters.Name(s.TheirChar.Value)}", Theme.Text));
                if (s.Stage != null)
                {
                    var st = MatchHistory.Count(h.Singles().Where(x => x.Game.Stage == s.Stage));
                    lines.Add(($"On {h.StageName(s.Stage.Value)}: {(st.Games == 0 ? "no games yet" : $"{st} ({st.Percent})")}", st.Games == 0 ? Theme.Muted : RateColor(st.Rate)));
                }
                if (s.Phase == MatchPhase.GameOver && s.TheirChar != null)
                {
                    var picks = h.Counterpicks(s.TheirChar.Value, 2);
                    var stages = h.BestStages(s.LegalStages.Count > 0 ? s.LegalStages : null, s.TheirChar, oppId, 2);
                    lines.Add(($"You {(s.LastGameWon == true ? "won" : "lost")} game {s.Game - 1}. For game {s.Game}:", OverlayLook.Accent));
                    if (picks.Count > 0) lines.Add(("Best picks: " + string.Join(", ", picks.Select(p => $"{Fighters.Name(p.Character)} {p.Versus}")), Theme.Text));
                    if (stages.Count > 0) lines.Add(("Best stages: " + string.Join(", ", stages.Select(p => $"{h.StageName(p.Stage)} {p.Record}")), Theme.Text));
                    if (CoachLine != null) foreach (var part in Wrap("Coach: " + CoachLine, 62)) lines.Add((part, OverlayLook.Accent));
                }
    
                _proj = h.Project(s.MyChar, s.TheirChar, oppId, s.Stage);
            }
            if (Preview && _proj == null)
            {
                _lines = new List<(string, Color)> { ("Head-to-head: 7–5 (58%) in 12 games", Theme.Good), ("They usually play: Bowser ×9, Wario ×3", Theme.Text) };
                foreach (var part in Wrap("Coach: Bowser's Forward tilt at 112%: DI up and in and you'd have lived.", 62)) _lines.Add((part, OverlayLook.Accent));
                _proj = new MatchHistory.Projection { Chance = 0.58, Evidence = 20 };
            }
            if (Pill)
            {
                if (Size != new Size(360, 54)) Size = new Size(360, 54);
                _pillLine = null; _pillColor = Theme.Muted;
                if (s != null && h != null && s.MyChar != null && s.TheirChar != null)
                {
                    var mu = MatchHistory.Count(h.Singles().Where(x => x.Me.Character == s.MyChar && x.Them.Character == s.TheirChar));
                    _pillLine = mu.Games == 0 ? $"{Fighters.Name(s.MyChar.Value)} vs {Fighters.Name(s.TheirChar.Value)}: first time" : $"{Fighters.Name(s.MyChar.Value)} vs {Fighters.Name(s.TheirChar.Value)}: {mu} ({mu.Percent})";
                    if (mu.Games > 0) _pillColor = RateColor(mu.Rate);
                }
            }
            else if (!Expanded)
            {
                int want = 72 + Compact().Take(2).Sum(l => l.Text.StartsWith("Coach: ") ? 36 : 20) + 20;
                if (Size != new Size(340, want)) Size = new Size(340, want);
            }
            else
            {
                if (Width != 430) Width = 430;
                int want = Math.Max(130, 84 + Math.Max(1, _lines.Count) * 22 + 30);
                if (Height != want) Height = want;
            }
            RecreateIfNeeded();
            Place();
            if ((Flashing || Animating) && !_anim.Enabled) _anim.Start();
            Invalidate();
        }

        /// <summary>The lines the small card keeps: how you do against them, and between games the coach or your best pick.</summary>
        List<(string Text, Color Color)> Compact()
        {
            var keep = new List<(string Text, Color Color)>();
            if (_lines.Count > 0) keep.Add(_lines[0]);
            var coach = _lines.Where(l => l.Text.StartsWith("Coach: ") || l.Text.StartsWith("   ") && l.Color == OverlayLook.Accent).Select(l => l.Text.Trim()).ToList();
            if (coach.Count > 0) { keep.Add((string.Join(" ", coach), OverlayLook.Accent)); return keep; }
            foreach (var l in _lines.Skip(1))
                if (l.Text.StartsWith("Best picks") || s_matchup(l.Text)) { keep.Add((l.Text, l.Color)); break; }
            return keep;
        }
        static List<string> Wrap(string t, int max)
        {
            var parts = new List<string>(); var line = "";
            foreach (var w in t.Split(' '))
            {
                if (line.Length > 0 && line.Length + 1 + w.Length > max) { parts.Add(line); line = "   " + w; }
                else line = line.Length == 0 ? w : line + " " + w;
            }
            if (line.Length > 0) parts.Add(line);
            return parts;
        }
        bool s_matchup(string t) => State?.MyChar != null && t.StartsWith(Fighters.Name(State.MyChar.Value) + " vs ");

        bool _clickable;
        void RecreateIfNeeded()
        {
            // the window style (clicks or not) follows the slim bar / card switch
            if (IsHandleCreated && _clickable != !Pill) { _clickable = !Pill; RecreateHandle(); }
            else _clickable = !Pill;
        }

        void PaintCompact(Graphics g)
        {
            var s = State; var h = History;
            using (var border = new Pen(OverlayLook.Accent, 2)) g.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
            var top = new RectangleF(2, 2, Width - 5, 66);
            if (Art?.Stage(s.Stage) != null) { StatsArt.Cover(g, Art.Stage(s.Stage), top, 0); StatsArt.Shade(g, top, 235, 120, LinearGradientMode.Horizontal); }
            float left = 12;
            var head = Art?.Head(s.TheirChar);
            if (head != null) { var hb = new RectangleF(10, 9, 42, 50); using (var bg = new SolidBrush(Color.FromArgb(60, 255, 255, 255))) using (var path = StatsArt.Round(hb, 6)) g.FillPath(bg, path); StatsArt.Cover(g, head, hb, 6, false, 0.5f, 0f); left = 60; }
            string mode = s.Mode == "ranked" ? "Ranked" : s.Mode == "unranked" ? "Unranked" : s.Mode == "direct" ? "Direct" : s.Mode == "room" ? "Room" : s.Mode;
            Text(g, "vs " + (s.OpponentName ?? "…"), Theme.BodyBold, Theme.Text, new RectangleF(left, 10, Width - left - 78, 22));
            Text(g, $"{mode} · game {s.Game}" + (s.MyWins + s.TheirWins > 0 ? $" · set {s.MyWins}–{s.TheirWins}" : ""), Theme.Small, Theme.Muted, new RectangleF(left, 32, Width - left - 78, 18));
            if (s.Stage != null && h != null) Text(g, h.StageName(s.Stage.Value), Theme.Small, Theme.Text, new RectangleF(left, 48, Width - left - 78, 18));
            if (_proj != null)
            {
                Text(g, $"{_proj.Chance * 100:0}%", Theme.Bold(17f), RateColor(_proj.Chance), new RectangleF(Width - 80, 8, 72, 30), true);
                Text(g, "to win", Theme.Small, Theme.Muted, new RectangleF(Width - 80, 40, 72, 18), true);
            }
            float y = 72;
            if ((h == null && !Preview) || _proj == null) Text(g, "Reading your match history…", Theme.Small, Theme.Muted, new RectangleF(12, y, Width - 24, 18));
            else foreach (var (t, c) in Compact().Take(2))
            {
                bool two = t.StartsWith("Coach: ");
                using (var b = new SolidBrush(c)) g.DrawString(t, Theme.Small, b, new RectangleF(12, y, Width - 24, two ? 34 : 18), two ? new StringFormat { Trimming = StringTrimming.EllipsisWord } : new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                y += two ? 36 : 20;
            }
            Text(g, "▾ more", Theme.Small, OverlayLook.Accent, new RectangleF(Width - 64, Height - 20, 56, 16));
        }

        protected override void PaintContent(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (Pill) { PaintPill(g); return; }
            if (State != null && !Expanded) { PaintCompact(g); return; }
            if (State != null) using (var m = new SolidBrush(OverlayLook.Accent)) g.DrawString("▴ less", Theme.Small, m, Width - 60, Height - 20);
            using (var border = new Pen(OverlayLook.Accent, 2)) g.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
            var s = State; var h = History;
            if (s == null) return;
            // the top: the stage behind (faded), their character's head, their name
            var top = new RectangleF(2, 2, Width - 5, 74);
            if (Art?.Stage(s.Stage) != null) { StatsArt.Cover(g, Art.Stage(s.Stage), top, 0); StatsArt.Shade(g, top, 235, 120, LinearGradientMode.Horizontal); }
            int left = 16;
            var head = Art?.Head(s.TheirChar);
            if (head != null) { var hb = new RectangleF(12, 10, 50, 58); using (var bg = new SolidBrush(Color.FromArgb(60, 255, 255, 255))) using (var path = StatsArt.Round(hb, 6)) g.FillPath(bg, path); StatsArt.Cover(g, head, hb, 6, false, 0.5f, 0f); left = 74; }
            int y = 12;
            void Text(string t, Font f, Color c, int x = 16, int right = 16) { using (var b = new SolidBrush(c)) g.DrawString(t, f, b, new RectangleF(x, y, Width - x - right, 40), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap }); }
            string mode = s.Mode == "ranked" ? "Ranked" : s.Mode == "unranked" ? "Unranked" : s.Mode == "direct" ? "Direct" : s.Mode;
            Text("vs " + (s.OpponentName ?? "…"), Theme.Heading, Theme.Text, left, 130); y += 26;
            Text($"{s.OpponentCode} · {mode} · game {s.Game}" + (s.MyWins + s.TheirWins > 0 ? $" · set {s.MyWins}–{s.TheirWins}" : ""), Theme.Small, Theme.Muted, left, 130); y += 18;
            if (s.Stage != null && h != null) Text(h.StageName(s.Stage.Value), Theme.Small, Theme.Text, left, 130);
            y = 84;
            if ((h == null && !Preview) || _proj == null) { Text("Reading your match history…", Theme.Body, Theme.Muted); return; }

            var lines = _lines;
            // the chance to win, big, on the right
            var proj = _proj;
            using (var c = new SolidBrush(RateColor(proj.Chance)))
                g.DrawString($"{proj.Chance * 100:0}%", Theme.Bold(24f), c, Width - 118, 8);
            using (var m = new SolidBrush(Theme.Muted)) g.DrawString(proj.Evidence >= 15 ? "to win" : "to win (few games)", Theme.Small, m, Width - 116, 48);

            foreach (var (t, c) in lines)
            {
                // the matchup line gets both characters' heads in front of it
                if (s.MyChar != null && s.TheirChar != null && t.StartsWith(Fighters.Name(s.MyChar.Value) + " vs ") && Art != null)
                {
                    float x = 16;
                    foreach (var id in new[] { s.MyChar, s.TheirChar }) { var hd = Art.Head(id); if (hd != null) { StatsArt.Cover(g, hd, new RectangleF(x, y - 1, 18, 21), 3, false, 0.5f, 0f); x += 21; } }
                    Text(t, Theme.Body, c, (int)x + 4); y += 22; continue;
                }
                Text(t, Theme.Body, c, 16); y += 22;
            }
        }
    }
}
