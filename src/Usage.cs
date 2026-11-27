using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AITeam
{
    // 남은 사용량(구독 한도) 모으기
    //  - 클로드: `claude -p /usage` (5시간·주간 사용률과 초기화 시각, 토큰 사용 없음). 못 읽으면 rate_limit_event로 대신
    //  - 코덱스: ~/.codex/sessions 기록의 rate_limits (5시간/주간 사용률·초기화 시각)
    //  - 제미나이: `agy -p /usage` (Gemini 모델 그룹·Claude·GPT 모델 그룹의 주간 남은 비율과 초기화 시각)
    static class Usage
    {
        static readonly object sync = new object();
        static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AITeam", "usage.json");
        static Dictionary<string, object> claude = Load();
        static readonly Dictionary<string, long[]> tokens = new Dictionary<string, long[]>();

        static Dictionary<string, object> Load()
        {
            try
            {
                var o = Json.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) as Dictionary<string, object>;
                if (o != null) return o;
            }
            catch { }
            return new Dictionary<string, object>();
        }

        public static void ResetTokens() { lock (sync) tokens.Clear(); }

        public static long[] TokensOf(string agent)
        {
            lock (sync) { long[] t; return tokens.TryGetValue(agent, out t) ? new[] { t[0], t[1] } : new long[2]; }
        }

        public static void AddTokens(string agent, long inTok, long outTok)
        {
            lock (sync)
            {
                long[] t;
                if (!tokens.TryGetValue(agent, out t)) tokens[agent] = t = new long[2];
                t[0] += inTok; t[1] += outTok;
            }
        }

        public static void ClaudeRate(object info)
        {
            var type = Json.Str(info, "rateLimitType") ?? "five_hour";
            var d = new Dictionary<string, object>();
            d["type"] = type;
            d["status"] = Json.Str(info, "status");
            d["resetsAt"] = Json.Num(info, "resetsAt") * 1000;
            var u = Json.Get(info, "utilization");
            if (u != null)
            {
                double v; double.TryParse(Convert.ToString(u, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
                d["used"] = v <= 1.0 ? v * 100 : v;
            }
            d["seen"] = Team.Now();
            lock (sync)
            {
                claude[type] = d;
                try { File.WriteAllText(FilePath, Json.Stringify(claude), new UTF8Encoding(false)); } catch { }
            }
        }

        static string Label(string type)
        {
            switch (type)
            {
                case "five_hour": return "5시간";
                case "seven_day": return "주간";
                case "seven_day_opus": return "주간 (Opus)";
                case "seven_day_sonnet": return "주간 (Sonnet)";
            }
            return type;
        }

        // ───── 정확한 사용량: 각 CLI의 공식 /usage 명령 (토큰을 쓰지 않음) ─────
        static Dictionary<string, object> claudeCli, geminiCli, codexCache;
        static long lastRefresh;
        static string claudePath, agyPath;

        public static void SetPaths(string claude, string agy) { lock (sync) { claudePath = claude; agyPath = agy; } }

        // 백그라운드에서 1분마다 갱신
        public static void StartRefresher()
        {
            new System.Threading.Thread(() =>
            {
                while (true)
                {
                    try { Refresh(); } catch { }
                    System.Threading.Thread.Sleep(60000);
                }
            }) { IsBackground = true }.Start();
        }

        public static void Refresh()
        {
            string cp, ap; lock (sync) { cp = claudePath; ap = agyPath; }
            Dictionary<string, object> c = null, g = null;
            if (cp != null) c = ClaudeUsage(cp);
            if (ap != null) g = GeminiUsage(ap);
            var x = Codex();
            lock (sync) { if (c != null) claudeCli = c; if (g != null) geminiCli = g; codexCache = x; lastRefresh = Team.Now(); }
        }

        static Dictionary<string, object> Window(string label, double used, long resetsAt)
        {
            var d = new Dictionary<string, object>(); d["label"] = label; d["used"] = Math.Round(Math.Max(0, Math.Min(100, used)), 1); d["resetsAt"] = resetsAt; return d;
        }

        // "Current session: 0% used · resets Nov 25, 7pm (Asia/Seoul)"
        static Dictionary<string, object> ClaudeUsage(string exe)
        {
            var r = Agents.RunQuick(exe, "-p /usage --output-format json --no-session-persistence", 40000);
            var j = Json.Extract(r.Item2);
            var text = Json.Str(j, "result");
            if (string.IsNullOrEmpty(text)) return null;
            var wins = new List<object>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text,
                @"Current (session|week)([^:\n]*):\s*(\d+(?:\.\d+)?)% used[^\n]*?resets\s+([A-Za-z]{3}\s+\d{1,2},\s*\d{1,2}(?::\d{2})?\s*[ap]m)"))
            {
                var label = m.Groups[1].Value == "session" ? "5시간" : "주간" + (m.Groups[2].Value.Contains("all models") || m.Groups[2].Value.Trim().Length == 0 ? "" : " " + m.Groups[2].Value.Trim().Trim('(', ')'));
                double used; double.TryParse(m.Groups[3].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out used);
                wins.Add(Window(label, used, ParseReset(m.Groups[4].Value)));
            }
            if (wins.Count == 0) return null;
            var d = new Dictionary<string, object>(); d["windows"] = wins; d["seen"] = Team.Now(); d["source"] = "claude /usage";
            return d;
        }

        static long ParseReset(string s)
        {
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", "");
            DateTime dt;
            if (!DateTime.TryParseExact(s, new[] { "MMMd,htt", "MMMd,h:mmtt", "MMMdd,htt", "MMMdd,h:mmtt" }, new System.Globalization.CultureInfo("en-US"),
                System.Globalization.DateTimeStyles.AssumeLocal, out dt)) return 0;
            var now = DateTime.Now;
            dt = new DateTime(now.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, DateTimeKind.Local);
            if (dt < now.AddDays(-2)) dt = dt.AddYears(1);
            return (long)(dt.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;
        }

        // agy -p /usage --output-format json → command.data.groups[].buckets[] (remaining_fraction, reset_time)
        static Dictionary<string, object> GeminiUsage(string exe)
        {
            var r = Agents.RunQuick(exe, "-p /usage --output-format json --print-timeout 40s", 50000);
            object j = null;
            foreach (var line in r.Item2.Split('\n')) { var t = line.Trim(); if (t.StartsWith("{") && t.Contains("\"command\"")) { j = Json.Parse(t); break; } }
            if (j == null) return null;
            var wins = new List<object>();
            foreach (var grp in Json.Arr(j, "command", "data", "groups"))
            {
                var gname = Json.Str(grp, "name") ?? "";
                foreach (var b in Json.Arr(grp, "buckets"))
                {
                    double rem; double.TryParse(Json.Str(b, "remaining_fraction"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rem);
                    DateTime ts; long reset = 0;
                    if (DateTime.TryParse(Json.Str(b, "reset_time"), null, System.Globalization.DateTimeStyles.RoundtripKind, out ts))
                        reset = (long)(ts.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;
                    var label = (gname.StartsWith("Gemini") ? "Gemini" : gname.Contains("Claude") ? "Claude·GPT" : gname) + (Json.Str(b, "window") == "weekly" ? " 주간" : " " + (Json.Str(b, "window") ?? ""));
                    var w = Window(label, 100 - rem * 100, reset);
                    w["group"] = gname.StartsWith("Gemini") ? "gemini" : "thirdparty";
                    wins.Add(w);
                }
            }
            if (wins.Count == 0) return null;
            var d = new Dictionary<string, object>(); d["windows"] = wins; d["seen"] = Team.Now(); d["source"] = "agy /usage";
            return d;
        }

        // 바닥 여부: (바닥났는지, 다시 쓸 수 있는 시각 ms). 제미나이는 Gemini 그룹만 바닥이면 'gemini-group' 로 따로 알린다
        public static Tuple<bool, long> Exhausted(string agent)
        {
            Dictionary<string, object> u;
            lock (sync) u = agent == "claude" ? claudeCli : agent == "gemini" ? geminiCli : codexCache;
            if (u == null) return Tuple.Create(false, 0L);
            var full = Json.Arr(u, "windows").Where(w => Json.Num(w, "resetsAt") > Team.Now() && UsedOf(w) >= 99.5).ToList();
            if (agent == "gemini")
            {
                var all = Json.Arr(u, "windows");
                bool anyLeft = all.Any(w => UsedOf(w) < 99.5);
                return Tuple.Create(!anyLeft, full.Count > 0 ? full.Min(w => Json.Num(w, "resetsAt")) : 0L);
            }
            return Tuple.Create(full.Count > 0, full.Count > 0 ? full.Max(w => Json.Num(w, "resetsAt")) : 0L);
        }

        // 제미나이: Gemini 모델 그룹이 바닥났지만 Claude·GPT 그룹이 남았는지
        public static bool GeminiNeedsThirdParty()
        {
            Dictionary<string, object> u; lock (sync) u = geminiCli;
            if (u == null) return false;
            var w = Json.Arr(u, "windows");
            return w.Any(x => Json.Str(x, "group") == "gemini" && UsedOf(x) >= 99.5) && w.Any(x => Json.Str(x, "group") == "thirdparty" && UsedOf(x) < 99.5);
        }

        static double UsedOf(object w)
        {
            double v; double.TryParse(Json.Str(w, "used"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v); return v;
        }

        public static Dictionary<string, object> Snapshot()
        {
            var res = new Dictionary<string, object>();
            lock (sync)
            {
                if (claudeCli != null) res["claude"] = claudeCli;
                else
                {
                    // /usage를 아직 못 읽었으면 Claude Code 출력의 한도 알림으로 대신
                    var cw = new List<object>();
                    foreach (var kv in claude)
                    {
                        var d = new Dictionary<string, object>((Dictionary<string, object>)kv.Value);
                        d["label"] = Label(kv.Key);
                        if (Json.Num(d, "resetsAt") < Team.Now()) { d["stale"] = true; d.Remove("used"); }
                        cw.Add(d);
                    }
                    res["claude"] = new Dictionary<string, object> { { "windows", cw } };
                }
                if (geminiCli != null) res["gemini"] = geminiCli;
                var tk = new Dictionary<string, object>();
                foreach (var kv in tokens) tk[kv.Key] = new Dictionary<string, object> { { "in", kv.Value[0] }, { "out", kv.Value[1] } };
                res["tokens"] = tk;
                res["refreshed"] = lastRefresh;
            }
            res["codex"] = codexCache ?? Codex();
            return res;
        }

        static Dictionary<string, object> Codex()
        {
            var res = new Dictionary<string, object>();
            try
            {
                var home = Environment.GetEnvironmentVariable("CODEX_HOME");
                if (string.IsNullOrEmpty(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
                var dir = Path.Combine(home, "sessions");
                if (!Directory.Exists(dir)) return res;
                // 날짜 폴더(년/월/일) 최신순으로 몇 개만 본다
                var files = new List<FileInfo>();
                foreach (var y in Directory.GetDirectories(dir).OrderByDescending(x => x).Take(2))
                    foreach (var m in Directory.GetDirectories(y).OrderByDescending(x => x).Take(2))
                        foreach (var d in Directory.GetDirectories(m).OrderByDescending(x => x).Take(3))
                            files.AddRange(new DirectoryInfo(d).GetFiles("*.jsonl"));
                foreach (var f in files.OrderByDescending(x => x.LastWriteTimeUtc).Take(6))
                {
                    string[] lines;
                    using (var s = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var r = new StreamReader(s, Encoding.UTF8)) lines = r.ReadToEnd().Split('\n');
                    for (int i = lines.Length - 1; i >= 0; i--)
                    {
                        if (lines[i].IndexOf("\"rate_limits\"", StringComparison.Ordinal) < 0) continue;
                        var ev = Json.Parse(lines[i]);
                        var rl = Json.Get(ev, "payload", "rate_limits");
                        if (rl == null || Json.Get(rl, "primary") == null) continue;
                        var wins = new List<object>();
                        foreach (var key in new[] { "primary", "secondary" })
                        {
                            var w = Json.Get(rl, key);
                            if (w == null) continue;
                            var mins = Json.Num(w, "window_minutes");
                            double used; double.TryParse(Json.Str(w, "used_percent"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out used);
                            var reset = Json.Num(w, "resets_at") * 1000;
                            var d = new Dictionary<string, object>();
                            d["label"] = mins >= 10000 ? "주간" : mins >= 60 ? (mins / 60) + "시간" : mins + "분";
                            d["used"] = reset < Team.Now() ? 0 : used;   // 초기화가 지났으면 0%
                            d["resetsAt"] = reset;
                            wins.Add(d);
                        }
                        res["windows"] = wins;
                        res["plan"] = Json.Str(rl, "plan_type");
                        res["limited"] = Json.Str(rl, "rate_limit_reached_type");
                        DateTime ts;
                        if (DateTime.TryParse(Json.Str(ev, "timestamp"), null, System.Globalization.DateTimeStyles.RoundtripKind, out ts))
                            res["seen"] = (long)(ts.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;
                        return res;
                    }
                }
            }
            catch { }
            return res;
        }
    }
}
