// Live Match: everything the app learns about the match you're in, as it happens, from the game's log: who you're
// playing and on what, every stock taken and lost on a timeline, the connection (ping, jitter, rollbacks), your
// chance to win, how each game ended, and a running list of what happened. A red dot means it's recording live.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    class LiveMatchPanel : Panel
    {
        readonly LiveView _view = new LiveView { Dock = DockStyle.Fill };
        public LiveMatchPanel()
        {
            Dock = DockStyle.Fill; Padding = new Padding(28, 18, 16, 8); BackColor = Color.Transparent;
            var title = Theme.Label("Live Match", Theme.Title); title.Dock = DockStyle.Top; title.Height = 46;
            Controls.Add(_view); Controls.Add(title);
        }

        public void Show(LiveState s, List<(DateTime When, string Text, int Kind)> events, MatchHistory h, bool following)
        {
            _view.State = s; _view.Events = events; _view.History = h; _view.Following = following;
            _view.Invalidate();
        }
    }

    class LiveView : ScrollView
    {
        public LiveState State; public List<(DateTime When, string Text, int Kind)> Events; public MatchHistory History; public bool Following;

        static string Clock(int frame) { var t = TimeSpan.FromSeconds(Math.Max(0, frame) / 60.0); return $"{(int)t.TotalMinutes}:{t.Seconds:00}"; }

        protected override void Draw(Graphics g, int w)
        {
            var s = State; var art = StatsArt.Cached;
            int y = 0;
            bool live = s != null && s.Phase == MatchPhase.Playing;
            bool any = s != null && s.OpponentName != null && s.Phase != MatchPhase.None && s.Phase != MatchPhase.Searching;
            // the status line, with the recording dot
            var top = new RectangleF(0, y, w, 46);
            Card(g, top);
            double pulse = 0.5 + 0.5 * Math.Sin(DateTime.Now.TimeOfDay.TotalSeconds * 4);
            using (var b = new SolidBrush(live ? Color.FromArgb((int)(140 + 115 * pulse), 235, 64, 52) : Color.FromArgb(90, 160, 160, 170))) g.FillEllipse(b, 16, y + 15, 16, 16);
            string status = !Following ? "Not following a game: turn on the Stat Tracker in Settings (Add-ons), and make sure Brawl Online is set up on this PC."
                : s == null || s.Phase == MatchPhase.None ? "Waiting for a match. Start one in Brawl Online and this fills in live."
                : s.Phase == MatchPhase.Searching ? "Searching for a match…"
                : s.Phase == MatchPhase.Found ? "Match found, connecting…"
                : s.Phase == MatchPhase.Loading ? $"Game {s.Game} loading…"
                : s.Phase == MatchPhase.Playing ? $"LIVE · game {s.Game}" + (s.Frame > 0 ? " · " + Clock(s.Frame) : "")
                : s.Phase == MatchPhase.GameOver ? $"Between games · set {s.MyWins}–{s.TheirWins}"
                : "Set over";
            Text(g, status, Theme.BodyBold, live ? Color.FromArgb(255, 120, 110) : Theme.Text, new RectangleF(42, y + 13, w - 56, 22));
            y += 56;
            if (!any)
            {
                var r = new RectangleF(0, y, w, 150);
                Card(g, r);
                Caps(g, "What shows up here during a match", 16, y + 12, w - 32);
                string[] what =
                {
                    "Who you're playing (name and code), the mode, and which player you are",
                    "Both characters and the stage, the game number and the set score",
                    "Every stock taken and lost, as it happens, on a timeline of the game",
                    "Your connection: ping, jitter and how much rollback is going on",
                    "Your chance to win, and how each game ended (stocks and percent left)",
                };
                float yy = y + 36;
                foreach (var l in what) { Text(g, "•  " + l, Theme.Small, Theme.Text, new RectangleF(18, yy, w - 36, 18)); yy += 21; }
                y += 160;
                DrawEvents(g, w, ref y);
                ContentHeight = y; return;
            }

            // who and where
            var head = new RectangleF(0, y, w, 120);
            Card(g, head);
            var stage = art?.Stage(s.Stage);
            if (stage != null) { StatsArt.Cover(g, stage, head, 8); using (var p = StatsArt.Round(head, 8)) { g.SetClip(p); using (var b = new SolidBrush(Color.FromArgb(170, 12, 13, 18))) g.FillRectangle(b, head); g.ResetClip(); } }
            if (art?.Head(s.MyChar) != null) StatsArt.Cover(g, art.Head(s.MyChar), new RectangleF(12, y + 12, 80, 96), 6, false, 0.5f, 0f);
            if (art?.Head(s.TheirChar) != null) StatsArt.Cover(g, art.Head(s.TheirChar), new RectangleF(w - 92, y + 12, 80, 96), 6, true, 0.5f, 0f);
            Text(g, "you", Theme.Small, Theme.Muted, new RectangleF(100, y + 14, 120, 18));
            Text(g, s.MyChar != null ? Fighters.Name(s.MyChar.Value) : "…", Theme.Heading, You, new RectangleF(100, y + 30, w / 2f - 110, 26));
            Text(g, s.OpponentName, Theme.Small, Theme.Muted, new RectangleF(w / 2f, y + 14, w / 2f - 100, 18), Right);
            Text(g, s.TheirChar != null ? Fighters.Name(s.TheirChar.Value) : "…", Theme.Heading, Them, new RectangleF(w / 2f, y + 30, w / 2f - 100, 26), Right);
            string mode = s.Mode == "ranked" ? "Ranked" : s.Mode == "unranked" ? "Unranked" : s.Mode == "direct" ? "Direct" : s.Mode;
            Text(g, $"{s.OpponentCode} · {mode} · you're player {s.MyPort}" + (s.Stage != null && History != null ? " · " + History.StageName(s.Stage.Value) : ""), Theme.Small, Theme.Text, new RectangleF(100, y + 64, w - 200, 18), Center);
            Text(g, $"KOs {s.StocksTaken}–{s.StocksLost}", Theme.Bold(18f), s.StocksTaken > s.StocksLost ? Theme.Good : s.StocksTaken < s.StocksLost ? Theme.Bad : Theme.Text, new RectangleF(100, y + 82, w / 2f - 110, 32));
            Text(g, $"set {s.MyWins}–{s.TheirWins}", Theme.Bold(14f), Theme.Text, new RectangleF(w / 2f, y + 86, w / 2f - 100, 28), Right);
            // a stock just went: flash the card
            if (s.LastKo != default && (DateTime.Now - s.LastKo).TotalSeconds < 2.5)
            {
                float t = (float)((DateTime.Now - s.LastKo).TotalSeconds / 2.5);
                using (var pen = new Pen(Color.FromArgb((int)(220 * (1 - t)), s.LastKoMine ? Theme.Good : Theme.Bad), 3)) using (var p = StatsArt.Round(RectangleF.Inflate(head, -1.5f, -1.5f), 8)) g.DrawPath(pen, p);
            }
            y += 130;

            // the stocks on the game's timeline
            var tl = new RectangleF(0, y, w, 104);
            Card(g, tl);
            Caps(g, "Stocks this game", 16, y + 10, 200);
            int end = Math.Max(60 * 60, Math.Max(s.Frame, s.Kos.Count > 0 ? s.Kos.Max(k => k.Frame) : 0)) + 60 * 15;
            var bar = new RectangleF(20, y + 52, w - 40, 6);
            using (var b = new SolidBrush(Color.FromArgb(50, 255, 255, 255))) using (var p = StatsArt.Round(bar, 3)) g.FillPath(b, p);
            if (s.Frame > 0) using (var b = new SolidBrush(Color.FromArgb(120, 255, 255, 255))) using (var p = StatsArt.Round(new RectangleF(bar.X, bar.Y, Math.Max(6, bar.Width * s.Frame / end), bar.Height), 3)) g.FillPath(b, p);
            foreach (var k in s.Kos)
            {
                float x = bar.X + bar.Width * k.Frame / end;
                var c = k.Mine ? Theme.Good : Theme.Bad;
                using (var b = new SolidBrush(c)) g.FillEllipse(b, x - 7, bar.Y - 4, 14, 14);
                Text(g, k.Mine ? "▲" : "▼", Theme.Small, c, new RectangleF(x - 10, k.Mine ? bar.Y - 24 : bar.Y + 14, 20, 16), Center);
                Text(g, Clock(k.Frame), Theme.Small, Theme.Muted, new RectangleF(x - 24, k.Mine ? bar.Y - 38 : bar.Y + 26, 48, 16), Center);
            }
            if (s.Kos.Count == 0) Text(g, s.Phase == MatchPhase.Playing ? "No stocks lost yet" : "Stocks show up here as they go", Theme.Small, Theme.Muted, new RectangleF(20, y + 84, w - 300, 18));
            Text(g, "▲ you took one   ▼ you lost one", Theme.Small, Theme.Muted, new RectangleF(w - 260, y + 84, 244, 18), Right);
            y += 114;

            // tiles: connection, chance, last game
            var tiles = new List<(string Name, string Value, Color Color, string Note)>();
            if (s.Ping >= 0) tiles.Add(("Ping", $"{s.Ping:0} ms", s.Ping < 90 ? Theme.Good : s.Ping < 160 ? Theme.Text : Theme.Bad, $"jitter {s.Jitter:0} ms"));
            if (s.Rollbacks >= 0) tiles.Add(("Rollbacks", $"{s.Rollbacks:0.#}%", s.Rollbacks < 15 ? Theme.Good : s.Rollbacks < 30 ? Theme.Text : Theme.Bad, "of frames re-run lately"));
            if (History != null && s.MyChar != null && s.TheirChar != null)
            {
                string oppId = s.OpponentCode != null ? History.UserIdOf(s.OpponentCode) : null;
                var proj = History.Project(s.MyChar, s.TheirChar, oppId, s.Stage);
                if (proj != null) tiles.Add(("Chance to win", $"{proj.Chance * 100:0}%", RecordTable.RateColor(proj.Chance), proj.Evidence >= 15 ? "from your history" : "few games yet"));
            }
            if (s.EndStocksMine != null) tiles.Add(("Last game", s.LastGameWon == true ? "Won" : "Lost", s.LastGameWon == true ? Theme.Good : Theme.Bad,
                $"stocks {s.EndStocksMine}–{s.EndStocksTheirs} · {s.EndDamageMine:0}% / {s.EndDamageTheirs:0}%"));
            if (tiles.Count > 0)
            {
                int n = tiles.Count; float tw = (w - (n - 1) * 10f) / n;
                for (int i = 0; i < n; i++)
                {
                    var r = new RectangleF(i * (tw + 10), y, tw, 82);
                    Card(g, r);
                    Caps(g, tiles[i].Name, r.X + 14, y + 10, tw - 28);
                    Text(g, tiles[i].Value, Theme.Bold(16f), tiles[i].Color, new RectangleF(r.X + 12, y + 28, tw - 24, 30));
                    Text(g, tiles[i].Note, Theme.Small, Theme.Muted, new RectangleF(r.X + 14, y + 58, tw - 28, 18));
                }
                y += 92;
            }
            DrawEvents(g, w, ref y);
            ContentHeight = y;
        }

        void DrawEvents(Graphics g, int w, ref int y)
        {
            var ev = Events ?? new List<(DateTime, string, int)>();
            var shown = ev.AsEnumerable().Reverse().Take(40).ToList();
            var r = new RectangleF(0, y, w, 40 + Math.Max(1, shown.Count) * 22);
            Card(g, r);
            Caps(g, "What happened", 16, y + 10, 200);
            float yy = y + 34;
            if (shown.Count == 0) Text(g, "Nothing yet.", Theme.Small, Theme.Muted, new RectangleF(16, yy, w - 32, 18));
            foreach (var e in shown)
            {
                Text(g, e.When.ToString("h:mm:ss tt"), Theme.Small, Theme.Muted, new RectangleF(16, yy, 90, 18));
                Text(g, e.Text, Theme.Small, e.Kind == 1 ? Theme.Good : e.Kind == 2 ? Theme.Bad : Theme.Text, new RectangleF(108, yy, w - 124, 18));
                yy += 22;
            }
            y += (int)r.Height + 10;
        }
    }
}
