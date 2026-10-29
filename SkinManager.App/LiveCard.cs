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

        public LiveCard()
        {
            Size = new Size(430, 250);
        }

        public void Place()
        {
            // the top-left of the game (Brawl's timer is top right, the damage along the bottom)
            var area = Game ?? Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.X + 14, area.Y + 14);
        }

        static Color RateColor(double r) => RecordTable.RateColor(r);

        /// <summary>The slim bar during the fight: their head, name, set score, matchup and your chance.</summary>
        void PaintPill(Graphics g)
        {
            var s = State; var h = History;
            using (var border = new Pen(Color.FromArgb(160, Theme.Accent), 1.5f)) g.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
            float x = 8;
            var head = Art?.Head(s?.TheirChar);
            if (head != null) { StatsArt.Cover(g, head, new RectangleF(6, 5, 38, 44), 5, false, 0.5f, 0f); x = 52; }
            Text(g, "vs " + (s?.OpponentName ?? "") + (s != null && s.MyWins + s.TheirWins > 0 ? $"  ·  set {s.MyWins}–{s.TheirWins}" : $"  ·  game {s?.Game}"), Theme.BodyBold, Theme.Text, new RectangleF(x, 6, Width - x - 70, 20));
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
            Opacity = Pill ? 0.8 : 0.88;
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
                    foreach (var l in Scout(oppId).Take(2)) lines.Add(("Scouting: " + l.Habit.Replace(" of the time", ""), Theme.Accent));
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
                    lines.Add(($"You {(s.LastGameWon == true ? "won" : "lost")} game {s.Game - 1}. For game {s.Game}:", Theme.Accent));
                    if (picks.Count > 0) lines.Add(("Best picks: " + string.Join(", ", picks.Select(p => $"{Fighters.Name(p.Character)} {p.Versus}")), Theme.Text));
                    if (stages.Count > 0) lines.Add(("Best stages: " + string.Join(", ", stages.Select(p => $"{h.StageName(p.Stage)} {p.Record}")), Theme.Text));
                }
    
                _proj = h.Project(s.MyChar, s.TheirChar, oppId, s.Stage);
            }
            if (Pill)
            {
                Size = new Size(360, 54);
                _pillLine = null; _pillColor = Theme.Muted;
                if (s != null && h != null && s.MyChar != null && s.TheirChar != null)
                {
                    var mu = MatchHistory.Count(h.Singles().Where(x => x.Me.Character == s.MyChar && x.Them.Character == s.TheirChar));
                    _pillLine = mu.Games == 0 ? $"{Fighters.Name(s.MyChar.Value)} vs {Fighters.Name(s.TheirChar.Value)}: first time" : $"{Fighters.Name(s.MyChar.Value)} vs {Fighters.Name(s.TheirChar.Value)}: {mu} ({mu.Percent})";
                    if (mu.Games > 0) _pillColor = RateColor(mu.Rate);
                }
            }
            else
            {
                if (Width != 430) Width = 430;
                int want = Math.Max(130, 84 + Math.Max(1, _lines.Count) * 22 + 12);
                if (Height != want) Height = want;
            }
            Place();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (Pill) { PaintPill(g); return; }
            using (var border = new Pen(Theme.Accent, 2)) g.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
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
            if (h == null || _proj == null) { Text("Reading your match history…", Theme.Body, Theme.Muted); return; }

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
