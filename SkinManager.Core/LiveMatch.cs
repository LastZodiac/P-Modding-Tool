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
        public LiveState Copy() { var c = (LiveState)MemberwiseClone(); c.LegalStages = new List<int>(LegalStages); return c; }
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

        public LiveState State = new LiveState();
        bool _inSession;

        /// <summary>Reads one log line; true when it changed what's known.</summary>
        public bool Feed(string line)
        {
            Match m;
            var s = State;
            if ((m = LockIn.Match(line)).Success)
            {
                int c = Fighters.Same(Convert.ToInt32(m.Groups[2].Value, 16));
                if (int.Parse(m.Groups[1].Value) <= 1) _inSession = false;   // "game 1": searching for a new set (later games of a set say 2, 3...)
                if (!_inSession) { State = new LiveState { Phase = MatchPhase.Searching, MyChar = c, LegalStages = s.LegalStages }; return true; }
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
                s.OpponentName = m.Groups[2].Value; s.OpponentCode = m.Groups[3].Value; s.TheirPort = int.Parse(m.Groups[1].Value);
                s.Phase = MatchPhase.Found;
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
                return true;
            }
            if (!_inSession) return false;
            if ((m = Setup.Match(line)).Success)
            {
                int g = int.Parse(m.Groups[1].Value);
                if (g != s.Game) { s.Game = g; s.TheirChar = null; }
                s.Stage = Convert.ToInt32(m.Groups[2].Value, 16);
                if (m.Groups[3].Success && m.Groups[4].Success)   // the host's line names both characters too
                {
                    int p1 = Fighters.Same(Convert.ToInt32(m.Groups[3].Value, 16)), p2 = Fighters.Same(Convert.ToInt32(m.Groups[4].Value, 16));
                    s.MyChar = s.MyPort == 2 ? p2 : p1; s.TheirChar = s.MyPort == 2 ? p1 : p2;
                }
                s.Phase = MatchPhase.Loading;
                return true;
            }
            if ((m = Fighter.Match(line)).Success)
            {
                int port = int.Parse(m.Groups[2].Value) + 1, c = Fighters.Same(int.Parse(m.Groups[1].Value));
                if (port == s.MyPort) s.MyChar = c; else if (port == s.TheirPort) s.TheirChar = c; else return false;
                if (s.Phase == MatchPhase.GameOver) { s.Phase = MatchPhase.Loading; }
                if (s.MyChar != null && s.TheirChar != null && s.Phase == MatchPhase.Loading) s.Phase = MatchPhase.Playing;
                return true;
            }
            if ((m = Winner.Match(line)).Success)
            {
                int winner = int.Parse(m.Groups[2].Value) + 1;
                s.LastGameWon = winner == s.MyPort;
                if (s.LastGameWon == true) s.MyWins++; else s.TheirWins++;
                s.Game = int.Parse(m.Groups[1].Value) + 1;
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
