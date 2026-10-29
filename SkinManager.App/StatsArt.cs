// The pictures for the Stats page and the opponent card, read once from the SD card: each character's head (battle
// portrait) and select-screen portrait (default costume), and each stage's select-screen preview.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using SkinManager.Core;

namespace SkinManager.App
{
    class StatsArt
    {
        public readonly Dictionary<int, Bitmap> Heads = new Dictionary<int, Bitmap>();      // game character number -> head
        public readonly Dictionary<int, Bitmap> Portraits = new Dictionary<int, Bitmap>();  // -> select-screen portrait
        public readonly Dictionary<int, Bitmap> Stages = new Dictionary<int, Bitmap>();     // stage slot -> preview (the stage part)

        static StatsArt _shared;
        /// <summary>The pictures if they've been read already (never waits).</summary>
        public static StatsArt Cached => _shared;
        static readonly object Gate = new object();

        /// <summary>The pictures (read the first time it's asked for; null if the card can't be read).</summary>
        public static StatsArt Get(Func<Installer> installer)
        {
            lock (Gate)
            {
                if (_shared != null) return _shared;
                try { using (var card = installer()?.OpenCard()) if (card != null) _shared = Read(card); }
                catch (Exception e) { Program.Log("stats pictures", e); }
                return _shared;
            }
        }

        static StatsArt Read(Fat32Image card)
        {
            var art = new StatsArt();
            byte[] stock = null;
            foreach (var id in Enumerable.Range(0, 60).Select(Fighters.Same).Distinct())
            {
                string name = Fighters.Name(id);
                var c = Characters.All.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (c == null) continue;
                try { if (card.Exists(c.BattlePortraitPath(0))) art.Heads[id] = Cosmetics.ReadPicture(card.ReadFile(c.BattlePortraitPath(0)), c.BattlePortraitTextureName(0)); } catch { }
                try { if (card.Exists(c.CspFilePath)) art.Portraits[id] = Cosmetics.ReadPicture(card.ReadFile(c.CspFilePath), c.CspTextureName(0)); } catch { }
                if (!art.Heads.ContainsKey(id) || art.Heads[id] == null)   // no battle portrait: the stock icon
                    try { stock = stock ?? card.ReadFile(Character.StockFilePath); art.Heads[id] = Cosmetics.ReadPicture(stock, c.StockTextureName(0)); } catch { }
            }
            try
            {
                var ss = StageSelect.Read(card);
                var file = card.ReadFile(StageSelect.PicturesFile);
                var pics = Cosmetics.ReadPictures(file, ss.Pictures.Values.Distinct().Select(StageSelect.PreviewTexture));
                foreach (var kv in ss.Pictures)
                    if (pics.TryGetValue(StageSelect.PreviewTexture(kv.Value), out var p) && p != null) art.Stages[kv.Key] = StageSelect.StagePart(p);
            }
            catch (Exception e) { Program.Log("stage pictures", e); }
            return art;
        }

        public Bitmap Head(int? id) => id != null && Heads.TryGetValue(id.Value, out var b) ? b : null;
        public Bitmap Portrait(int? id) => id != null && Portraits.TryGetValue(id.Value, out var b) ? b : Head(id);
        public Bitmap Stage(int? slot) => slot != null && Stages.TryGetValue(slot.Value, out var b) ? b : null;

        /// <summary>Draws a picture to fill a box (cut to keep its shape), optionally mirrored, with rounded corners.</summary>
        public static void Cover(Graphics g, Image img, RectangleF box, float radius = 6, bool mirror = false, float alignX = 0.5f, float alignY = 0.5f)
        {
            if (img == null) return;
            float s = Math.Max(box.Width / img.Width, box.Height / img.Height);
            float w = img.Width * s, h = img.Height * s;
            var dest = new RectangleF(box.X + (box.Width - w) * alignX, box.Y + (box.Height - h) * alignY, w, h);
            var state = g.Save();
            using (var path = Round(box, radius)) g.SetClip(path, CombineMode.Intersect);
            var small = Theme.Scaled(img, (int)Math.Round(dest.Width), (int)Math.Round(dest.Height), mirror);
            Theme.Blit(g, small, (int)Math.Round(dest.X), (int)Math.Round(dest.Y));
            g.Restore(state);
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            if (radius <= 0) { p.AddRectangle(r); return p; }
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        /// <summary>A dark fade over a picture so text on it stays readable (from the left, the bottom...).</summary>
        public static void Shade(Graphics g, RectangleF box, int from, int to, LinearGradientMode mode)
        {
            using (var b = new LinearGradientBrush(box, Color.FromArgb(from, 12, 13, 18), Color.FromArgb(to, 12, 13, 18), mode)) g.FillRectangle(b, box);
        }
    }
}
