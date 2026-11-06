// Notes in Data\errors.log when the app's window stops responding for a while, and what it was doing then, so a freeze
// someone saw can be found afterwards (nothing is shown to them). A background thread asks the window's thread to answer
// twice a second; an answer that comes late is written down with how late it was, what the app had said it was doing
// (Program.Doing), and how much memory it was using.
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace SkinManager.App
{
    /// <summary>Watches the window's thread for freezes and notes each one in Data\errors.log.</summary>
    static class Watchdog
    {
        const int SlowMs = 1500;                          // a freeze worth noting
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static Control _ui;
        static long _sentAt = -1;                         // when the pending question was asked (-1: none pending)
        static string _doing;                             // what the app was doing when the answer was first late

        /// <summary>Starts watching the window's thread (call it on that thread, once the window is shown).</summary>
        public static void Start(Control ui)
        {
            if (_ui != null) return;
            _ui = ui;
            new Thread(Loop) { IsBackground = true, Name = "window watchdog", Priority = ThreadPriority.BelowNormal }.Start();
        }

        /// <summary>Asks the window's thread to answer, and notes what the app is doing when an answer is late.</summary>
        static void Loop()
        {
            while (true)
            {
                Thread.Sleep(500);
                var ui = _ui;
                if (ui == null || ui.IsDisposed || !ui.IsHandleCreated) return;
                long sent = Interlocked.Read(ref _sentAt);
                if (sent < 0)
                {
                    long now = Clock.ElapsedMilliseconds;
                    Interlocked.Exchange(ref _sentAt, now);
                    _doing = null;
                    try { ui.BeginInvoke((Action)(() => Answered(now))); }
                    catch { return; }   // the window is closing
                }
                else if (_doing == null && Clock.ElapsedMilliseconds - sent > SlowMs)
                    _doing = Program.Doing ?? "something not marked";
            }
        }

        /// <summary>The window's thread answered: a late answer is written down.</summary>
        static void Answered(long sent)
        {
            long late = Clock.ElapsedMilliseconds - sent;
            Interlocked.Exchange(ref _sentAt, -1);
            if (late < SlowMs) return;
            long managed = GC.GetTotalMemory(false) / (1024 * 1024), working = 0;
            try { using (var me = Process.GetCurrentProcess()) working = me.WorkingSet64 / (1024 * 1024); } catch { }
            Program.Note($"The app's window didn't respond for {late / 1000.0:0.0} s, while {_doing ?? Program.Doing ?? "something not marked"} " +
                         $"(memory: {managed} MB managed, {working} MB in use; game running: {GameWindow.Running}).");
        }
    }
}
