// "How accurate": for the fighter on show, which rows are read straight from P+'s scripts, which needed an assumption
// (and which), and what isn't drawn because the game's code decides it. "Check every fighter" does the same for the
// whole cast, so a weak spot can be found without clicking through every move.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    class AccuracyView : GlassFlow
    {
        public event Action WholeCastRequested;
        public event Action<Character> FighterPicked;

        public AccuracyView()
        {
            FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoScroll = true; Padding = new Padding(16, 12, 16, 12);
            BackColor = Color.Transparent;
        }

        int TextWidth => Math.Max(300, ClientSize.Width - 50);

        Label Line(string text, Font font, Color color, int gap = 2)
        {
            var size = TextRenderer.MeasureText(text ?? "", font, new Size(TextWidth, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            var l = new Label { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent, AutoSize = false, Size = new Size(TextWidth, size.Height + 4), Margin = new Padding(0, 0, 0, gap), UseMnemonic = false };
            Controls.Add(l);
            return l;
        }

        void Start()
        {
            SuspendLayout();
            foreach (Control c in Controls.Cast<Control>().ToList()) c.Dispose();
            Controls.Clear();
        }

        Button CastButton()
        {
            var b = Theme.Secondary("Check every fighter");
            b.Margin = new Padding(0, 4, 0, 12);
            b.Click += (s, e) => WholeCastRequested?.Invoke();
            Controls.Add(b);
            return b;
        }

        public void Status(string text) { Start(); Line(text, Theme.Body, Theme.Muted); ResumeLayout(); }

        /// <summary>One fighter: every row, grouped by how sure it is.</summary>
        public void Show(FighterBook b)
        {
            Start();
            var lines = FrameDataAccuracy.For(b);
            int exact = lines.Count(l => l.Level == Sureness.Exact), approx = lines.Count(l => l.Level == Sureness.Approximate), none = lines.Count(l => l.Level == Sureness.NotDrawn);
            Line($"How accurate {b.Character.Name}'s frame data is", Theme.Heading, Theme.Text, 4);
            Line($"{exact} rows read straight from P+'s scripts  ·  {approx} needed an assumption  ·  {none} with something the game's code decides that isn't drawn", Theme.Body, Theme.Muted, 6);
            var note = FrameDataAccuracy.FighterNote(b);
            if (note != null) Line(note, Theme.Small, Theme.Warn, 6);
            CastButton();
            foreach (var (level, title, color) in new[] { (Sureness.NotDrawn, "Not drawn (the game's code decides)", Theme.Bad), (Sureness.Approximate, "Approximate (an assumption the app has to make)", Theme.Warn), (Sureness.Exact, "Exact", Theme.Good) })
            {
                var these = lines.Where(l => l.Level == level).ToList();
                if (these.Count == 0) continue;
                Line(title.ToUpperInvariant(), Theme.Small, color, 4);
                foreach (var l in these)
                {
                    if (level == Sureness.Exact) continue;
                    Line(l.Move, Theme.BodyBold, Theme.Text, 0);
                    Line(l.Why, Theme.Small, Theme.Muted, 8);
                }
                if (level == Sureness.Exact) Line(string.Join(", ", these.Select(l => l.Move)), Theme.Small, Theme.Muted, 8);
            }
            ResumeLayout();
        }

        /// <summary>The whole cast: each fighter's counts; click one to open it.</summary>
        public void ShowCast(List<(Character Who, int Exact, int Approx, int NotDrawn)> cast, int of)
        {
            Start();
            Line("How accurate the frame data is, fighter by fighter", Theme.Heading, Theme.Text, 4);
            Line(cast.Count < of ? $"Reading the fighters... {cast.Count} of {of}" : $"All {of} fighters: {cast.Sum(c => c.Exact)} rows exact, {cast.Sum(c => c.Approx)} approximate, {cast.Sum(c => c.NotDrawn)} with something not drawn. Click a fighter to see which.", Theme.Body, Theme.Muted, 8);
            foreach (var c in cast.OrderByDescending(x => x.NotDrawn).ThenByDescending(x => x.Approx).ThenBy(x => x.Who.Name))
            {
                var l = Line($"{c.Who.Name}:  {c.Exact} exact  ·  {c.Approx} approximate  ·  {c.NotDrawn} not drawn", Theme.Body, c.NotDrawn > 0 ? Theme.Warn : Theme.Text, 4);
                l.Cursor = Cursors.Hand; var who = c.Who;
                l.Click += (s, e) => FighterPicked?.Invoke(who);
            }
            ResumeLayout();
        }
    }
}
