// The Stat Tracker's Progress view: your level and each character's, your grades lately, every badge (the ones
// still to earn greyed out, milestones with how far to the next tier) and your personal bests.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using SkinManager.Core;

namespace SkinManager.App
{
    class ProgressView : ScrollView
    {
        public StatsData Data;

        protected override void Draw(Graphics g, int w)
        {
            var c = Data?.Career;
            if (c == null)
            {
                Card(g, new RectangleF(0, 0, w, 80));
                Text(g, Data?.ReplayNote ?? "Your progress shows once your games have been read.", Theme.Body, Theme.Muted, new RectangleF(16, 28, w - 32, 24));
                ContentHeight = 84; return;
            }
            int y = 0;
            var games = Data.History.Singles().ToList();
            var grades = games.Select(x => c.Games.TryGetValue(x.Game.Id, out var p) ? p : null).Where(p => p != null).ToList();

            // ---- your level
            var top = new RectangleF(0, y, w, 132);
            Card(g, top);
            var (level, into, need) = Progress.LevelOf(c.Xp);
            Caps(g, "Your level", 16, y + 12, 200);
            Text(g, $"{level}", Theme.Bold(34f), Theme.Accent, new RectangleF(14, y + 28, 110, 56));
            var bar = new RectangleF(120, y + 52, w * 0.5f - 130, 12);
            using (var b = new SolidBrush(Color.FromArgb(55, 255, 255, 255))) using (var p = StatsArt.Round(bar, 6)) g.FillPath(b, p);
            using (var b = new SolidBrush(Theme.Accent)) using (var p = StatsArt.Round(new RectangleF(bar.X, bar.Y, Math.Max(12, bar.Width * into / need), bar.Height), 6)) g.FillPath(b, p);
            Text(g, $"{into} / {need} XP to level {level + 1}", Theme.Small, Theme.Muted, new RectangleF(120, y + 30, bar.Width, 18));
            double perGame = grades.Count == 0 ? 0 : grades.Skip(Math.Max(0, grades.Count - 20)).Average(p => p.Xp);
            Text(g, perGame > 0 ? $"about {Math.Ceiling((need - into) / perGame):0} more game{(Math.Ceiling((need - into) / perGame) == 1 ? "" : "s")} · {c.Xp:N0} XP in all" : $"{c.Xp:N0} XP in all", Theme.Small, Theme.Muted, new RectangleF(120, y + 70, bar.Width, 18));
            Text(g, c.WinStreak >= 2 ? $"{c.WinStreak} wins in a row right now · best {c.BestWinStreak}" : $"best win streak: {c.BestWinStreak}", Theme.Small, c.WinStreak >= 2 ? Theme.Good : Theme.Muted, new RectangleF(120, y + 92, bar.Width, 18));
            // grades: how many of each, and the last ones
            float gx = w * 0.55f, gw = w - gx - 16;
            Caps(g, "Your grades", gx, y + 12, gw);
            string[] letters = { "S", "A", "B", "C", "D" };
            int most = Math.Max(1, letters.Max(l => grades.Count(p => p.Grade == l)));
            float colW = gw / 5f;
            for (int i = 0; i < 5; i++)
            {
                int n = grades.Count(p => p.Grade == letters[i]);
                float bh = 44f * n / most, bx = gx + i * colW;
                using (var b = new SolidBrush(ReportArt.GradeColor(letters[i]))) g.FillRectangle(b, bx + colW * 0.25f, y + 78 - bh, colW * 0.5f, bh);
                Text(g, letters[i], Theme.BodyBold, ReportArt.GradeColor(letters[i]), new RectangleF(bx, y + 80, colW, 18), Center);
                Text(g, n.ToString(), Theme.Small, Theme.Muted, new RectangleF(bx, y + 96, colW, 18), Center);
            }
            var last = grades.Skip(Math.Max(0, grades.Count - 12)).ToList();
            float lx = gx;
            Text(g, "last games:", Theme.Small, Theme.Muted, new RectangleF(lx, y + 112, 70, 18)); lx += 70;
            foreach (var p in last) { Text(g, p.Grade, Theme.BodyBold, ReportArt.GradeColor(p.Grade), new RectangleF(lx, y + 110, 16, 20)); lx += Math.Min(16, (gw - 70) / 12f); }
            y += (int)top.Height + 14;

            // ---- each character's level
            var chars = c.CharXp.OrderByDescending(kv => kv.Value).ToList();
            Caps(g, "Character mastery", 2, y, 300); y += 22;
            int cw = 150, ch = 64, per = Math.Max(1, (w + 10) / (cw + 10));
            cw = (w - (per - 1) * 10) / per;
            for (int i = 0; i < chars.Count; i++)
            {
                var r = new RectangleF(i % per * (cw + 10), y + i / per * (ch + 10), cw, ch);
                Card(g, r);
                Head(g, Data.Head(chars[i].Key), new RectangleF(r.X + 8, r.Y + 8, 42, 48));
                var lv = Progress.LevelOf(chars[i].Value);
                Text(g, Fighters.Name(chars[i].Key), Theme.Small, Theme.Text, new RectangleF(r.X + 58, r.Y + 8, r.Width - 64, 18));
                Text(g, $"Lv {lv.Level}", Theme.Bold(12f), Theme.Accent, new RectangleF(r.X + 58, r.Y + 24, r.Width - 64, 22));
                var cb = new RectangleF(r.X + 58, r.Y + 48, r.Width - 68, 5);
                using (var b = new SolidBrush(Color.FromArgb(55, 255, 255, 255))) g.FillRectangle(b, cb);
                using (var b = new SolidBrush(Them)) g.FillRectangle(b, cb.X, cb.Y, cb.Width * lv.Into / lv.Need, cb.Height);
            }
            y += (chars.Count + per - 1) / per * (ch + 10) + 8;

            // ---- badges
            Caps(g, $"Badges · {c.Badges.Sum(b => b.Count)} earned", 2, y, 300); y += 22;
            int bw = 168, bh2 = 86; int bper = Math.Max(1, (w + 10) / (bw + 10)); bw = (w - (bper - 1) * 10) / bper;
            var all = c.Milestones.Select(m => (m.Badge, Count: m.Count, Next: m.Next, Of: m.Of, Milestone: true))
                .Concat(c.Badges.Select(b => (b.Badge, Count: b.Count, Next: 0, Of: (string)null, Milestone: false))).ToList();
            for (int i = 0; i < all.Count; i++)
            {
                var it = all[i];
                var r = new RectangleF(i % bper * (bw + 10), y + i / bper * (bh2 + 10), bw, bh2);
                bool have = it.Milestone ? it.Badge.Tier > 0 : it.Count > 0;
                Card(g, r, have ? (Color?)null : Color.FromArgb(150, 26, 28, 36));
                var medal = new RectangleF(r.X + 10, r.Y + 12, 40, 40);
                if (have) ReportArt.Medal(g, it.Badge, medal);
                else using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 1.5f) { DashStyle = DashStyle.Dash }) g.DrawEllipse(pen, medal);
                float tx = r.X + 58, tw = r.Width - 64;
                Text(g, it.Badge.Name + (it.Milestone && it.Badge.Tier > 0 ? " · " + Badge.Tiers[it.Badge.Tier] : ""), Theme.BodyBold, have ? Theme.Text : Theme.Muted, new RectangleF(tx, r.Y + 10, tw, 20));
                if (it.Milestone)
                {
                    Text(g, it.Next > 0 ? $"{it.Count} / {it.Next} {it.Of}" : $"{it.Count} {it.Of} · maxed", Theme.Small, Theme.Muted, new RectangleF(tx, r.Y + 30, tw, 32), new StringFormat { Trimming = StringTrimming.EllipsisWord });
                    var pb = new RectangleF(tx, r.Y + 66, tw - 6, 5);
                    using (var b = new SolidBrush(Color.FromArgb(55, 255, 255, 255))) g.FillRectangle(b, pb);
                    using (var b = new SolidBrush(ReportArt.TierColor(Math.Min(4, it.Badge.Tier + 1)))) g.FillRectangle(b, pb.X, pb.Y, it.Next > 0 ? pb.Width * Math.Min(1, (float)it.Count / it.Next) : pb.Width, pb.Height);
                }
                else
                {
                    Text(g, it.Badge.What, Theme.Small, Theme.Muted, new RectangleF(tx, r.Y + 30, tw, 34), new StringFormat { Trimming = StringTrimming.EllipsisWord });
                    Text(g, have ? $"×{it.Count}" : "not yet", Theme.Small, have ? Theme.Accent : Theme.Muted, new RectangleF(tx, r.Y + 64, tw, 18));
                }
            }
            y += (all.Count + bper - 1) / bper * (bh2 + 10) + 8;

            // ---- personal bests
            Caps(g, "Personal bests", 2, y, 300); y += 22;
            var bests = c.Bests.Values.ToList();
            var bc = new RectangleF(0, y, w, 16 + bests.Count * 26);
            Card(g, bc);
            float by = y + 8;
            foreach (var b in bests)
            {
                var game = games.FirstOrDefault(x => x.Game.Id == b.GameId);
                Text(g, b.Name, Theme.Body, Theme.Muted, new RectangleF(14, by + 3, w * 0.3f, 20));
                Text(g, b.Text, Theme.BodyBold, Theme.Accent, new RectangleF(w * 0.3f, by + 3, w * 0.14f, 20));
                Text(g, (b.Detail != null ? b.Detail + " · " : "") + (game.Game != null ? $"vs {game.Them.Name}, {game.Game.Start.ToLocalTime():MMM d}" : ""), Theme.Small, Theme.Muted, new RectangleF(w * 0.44f, by + 5, w * 0.56f - 14, 18));
                by += 26;
            }
            y += (int)bc.Height + 8;
            ContentHeight = y;
        }
    }
}
