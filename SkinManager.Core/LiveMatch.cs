// Follows Brawl Online's game log while you play (read only) to know, before a game starts, who you're playing, on what
// stage and with which characters, and how the set is going. Every line used here was checked against real logs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SkinManager.Core
{
    public enum MatchPhase { None, Searching, Found, Loading, Playing, GameOver, SetOver }

    public sealed class LiveState
    {
        public MatchPhase Phase;
        public string Mode;                    // "ranked", "unranked", "direct"...
        public string OpponentName, OpponentCode;
        public int MyPort, TheirPort;          // 1-based
        public int? MyChar, TheirChar, Stage;
        public int Game = 1;
        public int MyWins, TheirWins;
        public bool? LastGameWon;
        public List<int> LegalStages = new List<int>();
        public int StocksTaken, StocksLost;    // this game, as they happen
        public DateTime LastKo; public bool LastKoMine;   // the latest stock lost or taken (LastKoMine: you took it)
        public List<(int Frame, bool Mine)> Kos = new List<(int, bool)>();   // this game's stocks, in order (Mine: you took it)
        public int Frame;                      // the game's frame, as last reported (60 a second)
        public double Ping = -1, Jitter, Rollbacks = -1;   // the connection (ms; % of frames re-run), every 10 seconds
        public int? EndStocksMine, EndStocksTheirs; public double? EndDamageMine, EndDamageTheirs;   // how the last game ended
        public LiveState Copy() { var c = (LiveState)MemberwiseClone(); c.LegalStages = new List<int>(LegalStages); c.Kos = new List<(int, bool)>(Kos); return c; }
    }

    public sealed class LiveMatch
    {
        static readonly Regex MatchId = new Regex(@"\[Matchmaking\] Match ID: mode\.([a-z]+)-");
        static readonly Regex Peer = new Regex(@"\[Matchmaking\] P(\d) (.+) \(([^()]+#\d+)\): internet");
        static readonly Regex Session = new Regex(@"gprb: session as (host|joiner) on P(\d)");
        static readonly Regex Setup = new Regex(@"gprb lobby: game (\d+) setup(?: from the host)?: stage 0x([0-9a-fA-F]+)(?:.*?P1 0x([0-9a-fA-F]+)/\d+, P2 0x([0-9a-fA-F]+)/)?");
        static readonly Regex Ended = new Regex(@"gprb: session ended at game frame");
        static readonly Regex Fighter = new Regex(@"#ftManager entry : EntryId\[[0-9a-fA-F]+\] ChrKind\[(\d+):[^\]]*\] Color\[\d+\] SlotNo\[(\d+)\]");
        static readonly Regex Winner = new Regex(@"gprb lobby: game (\d+) winner (\d+)");
        static readonly Regex LockIn = new Regex(@"gprb lobby: local lock-in ready for game (\d+) \(char 0x([0-9a-fA-F]+)/");
        static readonly Regex Stages = new Regex(@"Stages from the server: ([\d,]+)");
        // a stock lost: the game reports it with the player's place (0 = P1) and the frame (rollbacks can repeat it)
        static readonly Regex Dead = new Regex(@"notifyEventDead _nSleepFrame\[(\d+)\] = (\d+)");
        static readonly Regex Net = new Regex(@"gprb net: frames \d+-(\d+):.*?rollbacks \d+ \(([\d.]+)%\).*?ping (\d+) ms \(avg [\d.]+, jitter ([\d.]+)\)");
        static readonly Regex End = new Regex(@"stocks/damage: P1 (\d+)/([\d.]+) P2 (\d+)/([\d.]+)");

        /// <summary>What happened, in words, newest last (for the Live Match page).</summary>
        public readonly List<(DateTime When, string Text, int Kind)> Events = new List<(DateTime, string, int)>();   // Kind: 0 info, 1 good, 2 bad
        void Say(string text, int kind = 0) { Events.Add((DateTime.Now, text, kind)); if (Events.Count > 300) Events.RemoveAt(0); }
        static string Clock(int frame) { var t = TimeSpan.FromSeconds(frame / 60.0); return $"{(int)t.TotalMinutes}:{t.Seconds:00}"; }
        readonly HashSet<string> _deaths = new HashSet<string>();

        public LiveState State = new LiveState();
        bool _inSession;
        // the last game that ended (in a room every game is its own match: the score carries over while it's the same person)
        string _lastOpp; int _lastWins, _lastTheirs, _lastGame; DateTime _lastAt;
        static string Mode(string m) => m == "ranked" ? "Ranked" : m == "unranked" ? "Unranked" : m == "direct" ? "Direct" : m;

        /// <summary>Reads one log line; true when it changed what's known.</summary>
        public bool Feed(string line)
        {
            Match m;
            var s = State;
            if ((m = LockIn.Match(line)).Success)
            {
                int c = Fighters.Same(Convert.ToInt32(m.Groups[2].Value, 16));
                // "game 1" outside a set: searching for a new one (later games of a set say 2, 3...; inside a set the lobby
                // repeats its own game number while it loads)
                bool inSet = _inSession && (s.Phase == MatchPhase.Loading || s.Phase == MatchPhase.Playing || s.Phase == MatchPhase.GameOver);
                if (int.Parse(m.Groups[1].Value) <= 1 && !inSet) _inSession = false;
                if (!_inSession) { if (s.Phase != MatchPhase.Searching && s.Mode != "room") Say("Looking for a match as " + Fighters.Name(c)); State = new LiveState { Phase = MatchPhase.Searching, MyChar = c, LegalStages = s.LegalStages }; return true; }
                if (s.MyChar != c) { s.MyChar = c; return true; }
                return false;
            }
            if ((m = MatchId.Match(line)).Success)
            {
                State = new LiveState { Phase = MatchPhase.Found, Mode = m.Groups[1].Value, MyChar = _inSession ? null : s.MyChar, LegalStages = s.LegalStages };
                _inSession = false;
                return true;
            }
            if ((m = Peer.Match(line)).Success && (s.Phase == MatchPhase.Found || s.Phase == MatchPhase.Searching))
            {
                bool again = s.OpponentCode == m.Groups[3].Value;
                s.OpponentName = m.Groups[2].Value; s.OpponentCode = m.Groups[3].Value; s.TheirPort = int.Parse(m.Groups[1].Value);
                if (s.Mode == "room" && s.MyWins + s.TheirWins == 0 && _lastOpp == s.OpponentCode && (DateTime.Now - _lastAt).TotalMinutes < 30)
                { s.MyWins = _lastWins; s.TheirWins = _lastTheirs; s.Game = _lastGame; }
                s.Phase = MatchPhase.Found;
                if (!again && !(s.Mode == "room" && _lastOpp == s.OpponentCode && (DateTime.Now - _lastAt).TotalMinutes < 30)) Say($"Match found: {s.OpponentName} ({s.OpponentCode})" + (s.Mode != null ? " · " + Mode(s.Mode) : ""), 1);
                return true;
            }
            if ((m = Stages.Match(line)).Success)
            {
                s.LegalStages = m.Groups[1].Value.Split(',').Where(x => x.Length > 0).Select(int.Parse).ToList();
                return false;
            }
            if ((m = Session.Match(line)).Success)
            {
                _inSession = true;
                s.MyPort = int.Parse(m.Groups[2].Value);
                if (s.TheirPort == 0) s.TheirPort = s.MyPort == 1 ? 2 : 1;
                s.Phase = MatchPhase.Loading;
                Say($"Connected. You're player {s.MyPort}");
                return true;
            }
            if (!_inSession) return false;
            if ((m = Net.Match(line)).Success)
            {
                s.Frame = int.Parse(m.Groups[1].Value);
                s.Rollbacks = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                s.Ping = int.Parse(m.Groups[3].Value);
                s.Jitter = double.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            if ((m = Setup.Match(line)).Success)
            {
                int g = int.Parse(m.Groups[1].Value);
                if (s.Mode == "room") { if (s.Phase != MatchPhase.Loading) s.TheirChar = null; }   // a room counts its games itself: the score carries over (see Peer)
                else if (g != s.Game) { s.Game = g; s.TheirChar = null; }
                s.Stage = Convert.ToInt32(m.Groups[2].Value, 16);
                if (m.Groups[3].Success && m.Groups[4].Success)   // the host's line names both characters too
                {
                    int p1 = Fighters.Same(Convert.ToInt32(m.Groups[3].Value, 16)), p2 = Fighters.Same(Convert.ToInt32(m.Groups[4].Value, 16));
                    s.MyChar = s.MyPort == 2 ? p2 : p1; s.TheirChar = s.MyPort == 2 ? p1 : p2;
                }
                s.Phase = MatchPhase.Loading;
                return true;
            }
            if ((m = Dead.Match(line)).Success)
            {
                if (s.Phase != MatchPhase.Playing || !_deaths.Add(s.Game + ":" + m.Value)) return false;
                int port = int.Parse(m.Groups[1].Value) + 1;
                int frame = int.Parse(m.Groups[2].Value);
                if (port == s.MyPort) { s.StocksLost++; s.LastKoMine = false; }
                else if (port == s.TheirPort) { s.StocksTaken++; s.LastKoMine = true; }
                else return false;
                s.LastKo = DateTime.Now; s.Kos.Add((frame, s.LastKoMine)); s.Frame = Math.Max(s.Frame, frame);
                Say(s.LastKoMine ? $"Stock taken at {Clock(frame)} (KOs {s.StocksTaken}–{s.StocksLost})" : $"Stock lost at {Clock(frame)} (KOs {s.StocksTaken}–{s.StocksLost})", s.LastKoMine ? 1 : 2);
                return true;
            }
            if ((m = Fighter.Match(line)).Success)
            {
                int port = int.Parse(m.Groups[2].Value) + 1, c = Fighters.Same(int.Parse(m.Groups[1].Value));
                if (port == s.MyPort) s.MyChar = c; else if (port == s.TheirPort) s.TheirChar = c; else return false;
                if (s.Phase == MatchPhase.GameOver) { s.Phase = MatchPhase.Loading; }
                if (s.MyChar != null && s.TheirChar != null && s.Phase == MatchPhase.Loading)
                {
                    s.Phase = MatchPhase.Playing; s.StocksTaken = s.StocksLost = 0; s.LastKo = default; s.Kos.Clear(); s.Frame = 0;
                    Say($"Game {s.Game} started: {Fighters.Name(s.MyChar.Value)} vs {Fighters.Name(s.TheirChar.Value)}", 1);
                }
                return true;
            }
            if ((m = Winner.Match(line)).Success)
            {
                int winner = int.Parse(m.Groups[2].Value) + 1, gameNo = int.Parse(m.Groups[1].Value);
                s.LastGameWon = winner == s.MyPort;
                if ((m = End.Match(line)).Success)
                {
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    int p1s = int.Parse(m.Groups[1].Value), p2s = int.Parse(m.Groups[3].Value);
                    double p1d = double.Parse(m.Groups[2].Value, inv), p2d = double.Parse(m.Groups[4].Value, inv);
                    bool p2 = s.MyPort == 2;
                    s.EndStocksMine = p2 ? p2s : p1s; s.EndStocksTheirs = p2 ? p1s : p2s; s.EndDamageMine = p2 ? p2d : p1d; s.EndDamageTheirs = p2 ? p1d : p2d;
                }
                Say($"Game {(s.Mode == "room" ? s.Game : gameNo)}: you {(s.LastGameWon == true ? "won" : "lost")}" + (s.EndStocksMine != null ? $" ({(s.LastGameWon == true ? s.EndStocksMine : s.EndStocksTheirs)} stock{((s.LastGameWon == true ? s.EndStocksMine : s.EndStocksTheirs) == 1 ? "" : "s")} left)" : ""), s.LastGameWon == true ? 1 : 2);
                if (s.LastGameWon == true) s.MyWins++; else s.TheirWins++;
                s.Game = Math.Max(gameNo, s.Game) + 1;
                _lastOpp = s.OpponentCode; _lastWins = s.MyWins; _lastTheirs = s.TheirWins; _lastGame = s.Game; _lastAt = DateTime.Now;
                s.Stage = null;
                s.Phase = MatchPhase.GameOver;
                return true;
            }
            return false;
        }
    }

    /// <summary>Reads what's added to Brawl Online's current game log (opened for reading only; starts at its end).</summary>
    public sealed class LogFollower
    {
        readonly string _path;
        long _at = -1;
        DateTime _created;
        readonly StringBuilder _partial = new StringBuilder();

        public LogFollower(string path) { _path = path; }

        public static string LogPath(BrawlOnlineInstall install) => Path.Combine(install.NetplayFolder, "User", "Logs", "dolphin.log");

        /// <summary>The whole lines written since the last call.</summary>
        public List<string> NewLines()
        {
            var lines = new List<string>();
            try
            {
                if (!File.Exists(_path)) return lines;
                var info = new FileInfo(_path);
                if (_at < 0) { _at = info.Length; _created = info.CreationTimeUtc; return lines; }   // only what happens from now on
                if (info.Length < _at || info.CreationTimeUtc != _created) { _at = 0; _created = info.CreationTimeUtc; _partial.Clear(); }   // a new log (the game restarted)
                if (info.Length == _at) return lines;
                using (var s = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    s.Seek(_at, SeekOrigin.Begin);
                    var buf = new byte[Math.Min(4 << 20, s.Length - _at)];
                    int n = s.Read(buf, 0, buf.Length);
                    _at += n;
                    _partial.Append(Encoding.UTF8.GetString(buf, 0, n));
                }
                var text = _partial.ToString();
                int last = text.LastIndexOf('\n');
                if (last < 0) return lines;
                lines.AddRange(text.Substring(0, last).Split('\n').Select(l => l.TrimEnd('\r')));
                _partial.Clear(); _partial.Append(text.Substring(last + 1));
            }
            catch { }
            return lines;
        }
    }
}
