using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AITeam
{
    // 업무 이력 = 세션
    //  - team  : 같은 폴더의 AI 팀 작업들 중 '이전 대화 기억'으로 이어진 것들을 한 세션으로 묶음
    //  - claude: 이 PC의 Claude Code 대화 중 작업 폴더가 같은(또는 하위인) 것
    static class Sessions
    {
        static readonly HashSet<string> Natural = new HashSet<string> { "start", "say", "plan", "assign", "handoff", "done", "review", "approval", "memory", "concern", "leave", "leader", "elevation", "violation" };

        static string Norm(string p) { try { return Path.GetFullPath(p).TrimEnd('\\').ToLowerInvariant(); } catch { return (p ?? "").ToLowerInvariant(); } }

        class Run { public string Id; public object Board; public long Start, End; public int Remembered; }

        static List<Run> Runs(string dir)
        {
            var list = new List<Run>();
            var root = Path.Combine(dir, ".aiteam");
            if (!Directory.Exists(root)) return list;
            foreach (var d in Directory.GetDirectories(root).OrderBy(x => x, StringComparer.Ordinal))
            {
                var b = Path.Combine(d, "board.json");
                if (!File.Exists(b)) continue;
                try
                {
                    var o = Json.Parse(File.ReadAllText(b, Encoding.UTF8));
                    list.Add(new Run { Id = Path.GetFileName(d), Board = o, Start = Json.Num(o, "startTs"), End = Math.Max(Json.Num(o, "endTs"), Json.Num(o, "startTs")), Remembered = (int)Json.Num(o, "remembered") });
                }
                catch { }
            }
            return list;
        }

        // 세션 = 같은 sessionId 를 가진 작업들. sessionId 가 없는 예전 기록은 작업 하나가 세션 하나
        static string Sid(Run r) { return Json.Str(r.Board, "sessionId") ?? r.Id; }

        static List<List<Run>> Chains(string dir)
        {
            return Runs(dir).GroupBy(Sid).Select(g => g.OrderBy(r => r.Id, StringComparer.Ordinal).ToList())
                            .OrderBy(c => c[0].Id, StringComparer.Ordinal).ToList();
        }

        public static string SessionIdOf(string dir, string runId)
        {
            var r = Runs(dir).FirstOrDefault(x => x.Id == runId);
            return r == null ? null : Sid(r);
        }

        public static List<string> RunsOfSession(string dir, string sessionId)
        {
            return Runs(dir).Where(r => Sid(r) == sessionId).Select(r => r.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        static string First(string s, int n) { s = (s ?? "").Trim().Split('\n')[0]; return s.Length > n ? s.Substring(0, n) + "…" : s; }

        public static List<object> List(string dir)
        {
            var outp = new List<object>();
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return outp;
            foreach (var c in Chains(dir))
            {
                var d = new Dictionary<string, object>();
                d["id"] = Sid(c[0]); d["kind"] = "team"; d["title"] = First(Json.Str(c[0].Board, "request"), 70); d["lastRun"] = c.Last().Id;
                d["startTs"] = c[0].Start; d["lastTs"] = c.Last().End; d["status"] = Json.Str(c.Last().Board, "phase"); d["count"] = c.Count;
                d["runs"] = c.Select(r => (object)r.Id).ToList();
                d["members"] = c.SelectMany(r => Json.Arr(r.Board, "members").Select(Convert.ToString)).Distinct().Cast<object>().ToList();
                outp.Add(d);
            }
            foreach (var f in ClaudeFiles(dir))
            {
                var info = ClaudeInfo(f);
                if (info == null) continue;
                outp.Add(info);
            }
            return outp.OrderBy(x => Json.Num(x, "lastTs")).ToList(); // 오래된 것 → 최신 (최신이 맨 아래)
        }

        public static Dictionary<string, object> Get(string dir, string id, string kind)
        {
            var res = new Dictionary<string, object>(); res["id"] = id; res["kind"] = kind;
            var msgs = new List<object>();
            if (kind == "claude")
            {
                var f = ClaudeFiles(dir).FirstOrDefault(x => Path.GetFileNameWithoutExtension(x) == id);
                if (f != null) { var info = ClaudeInfo(f); res["title"] = info != null ? info["title"] : id; msgs = ClaudeMessages(f); }
            }
            else
            {
                var chain = Chains(dir).FirstOrDefault(c => Sid(c[0]) == id || c.Any(r => r.Id == id));
                if (chain != null)
                {
                    res["title"] = First(Json.Str(chain[0].Board, "request"), 70);
                    res["runs"] = chain.Select(r => (object)r.Id).ToList();
                    foreach (var r in chain)
                    {
                        msgs.Add(Msg(r.Start, "user", null, Json.Str(r.Board, "request"), "request", r.Id));
                        var ef = Path.Combine(dir, ".aiteam", r.Id, "events.jsonl");
                        if (File.Exists(ef))
                            using (var s = new FileStream(ef, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (var rd = new StreamReader(s, Encoding.UTF8))
                            {
                                string line;
                                while ((line = rd.ReadLine()) != null && msgs.Count < 3000)
                                {
                                    var e = Json.Parse(line); if (e == null) continue;
                                    var k = Json.Str(e, "kind");
                                    if (!Natural.Contains(k)) continue;
                                    var ag = Json.Str(e, "agent");
                                    if (ag == "system" && k == "start") continue; // 사용자 요청과 같은 내용
                                    msgs.Add(Msg(Json.Num(e, "ts"), ag == "system" ? "system" : "agent", ag, Json.Str(e, "text"), k, r.Id));
                                }
                            }
                        var rep = Json.Str(r.Board, "finalSummary");
                        if (!string.IsNullOrEmpty(rep))
                        {
                            var m = Msg(r.End, "agent", Json.Str(r.Board, "leader"), rep, "report", r.Id);
                            m["brief"] = Json.Get(r.Board, "finalBrief");
                            var vio = Json.Get(r.Board, "violations") as IDictionary<string, object>;
                            if (vio != null) m["violations"] = vio.SelectMany(kv => Json.Arr(kv.Value).Select(v => (object)(Agents.NameOf(kv.Key) + ": " + Convert.ToString(v)))).ToList();
                            msgs.Add(m);
                        }
                    }
                }
            }
            res["messages"] = msgs;
            return res;
        }

        static Dictionary<string, object> Msg(long ts, string role, string agent, string text, string kind, string runId)
        {
            var d = new Dictionary<string, object>(); d["ts"] = ts; d["role"] = role; d["agent"] = agent; d["text"] = text ?? ""; d["kind"] = kind; d["runId"] = runId; return d;
        }

        // ───── Claude Code 대화 (~/.claude/projects/*/*.jsonl) ─────
        static readonly Dictionary<string, string> cwdCache = new Dictionary<string, string>();

        static IEnumerable<string> ClaudeFiles(string dir)
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
            if (!Directory.Exists(root)) yield break;
            var want = Norm(dir);
            foreach (var pd in Directory.GetDirectories(root))
            {
                string[] files;
                try { files = Directory.GetFiles(pd, "*.jsonl"); } catch { continue; }
                foreach (var f in files)
                {
                    string cwd;
                    lock (cwdCache) cwdCache.TryGetValue(f, out cwd);
                    if (cwd == null) { cwd = Norm(ReadCwd(f) ?? ""); lock (cwdCache) cwdCache[f] = cwd; }
                    if (cwd.Length > 0 && (cwd == want || cwd.StartsWith(want + "\\"))) yield return f;
                }
            }
        }

        static string ReadCwd(string f)
        {
            try
            {
                using (var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var r = new StreamReader(s, Encoding.UTF8))
                    for (int i = 0; i < 40; i++)
                    {
                        var line = r.ReadLine(); if (line == null) break;
                        if (!line.Contains("\"cwd\"")) continue;
                        var c = Json.Str(Json.Parse(line), "cwd"); if (!string.IsNullOrEmpty(c)) return c;
                    }
            }
            catch { }
            return null;
        }

        static Dictionary<string, object> ClaudeInfo(string f)
        {
            try
            {
                var fi = new FileInfo(f);
                string title = null, firstUser = null; long start = 0; int count = 0;
                using (var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var r = new StreamReader(s, Encoding.UTF8))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        if (line.Contains("\"type\":\"summary\"")) { var t = Json.Str(Json.Parse(line), "summary"); if (!string.IsNullOrEmpty(t)) title = t; continue; }
                        if (!line.Contains("\"type\":\"user\"") && !line.Contains("\"type\":\"assistant\"")) continue;
                        var e = Json.Parse(line); if (e == null) continue;
                        var text = TextOf(e); if (string.IsNullOrEmpty(text)) continue;
                        count++;
                        if (start == 0) start = TsOf(e);
                        if (firstUser == null && Json.Str(e, "type") == "user") firstUser = text;
                    }
                }
                if (count == 0) return null;
                var d = new Dictionary<string, object>();
                d["id"] = Path.GetFileNameWithoutExtension(f); d["kind"] = "claude";
                d["title"] = First(title ?? firstUser ?? "Claude 대화", 70);
                d["startTs"] = start; d["lastTs"] = (long)(fi.LastWriteTimeUtc - new DateTime(1970, 1, 1)).TotalMilliseconds;
                d["status"] = "claude"; d["count"] = count;
                return d;
            }
            catch { return null; }
        }

        static List<object> ClaudeMessages(string f)
        {
            var list = new List<object>();
            try
            {
                using (var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var r = new StreamReader(s, Encoding.UTF8))
                {
                    string line;
                    while ((line = r.ReadLine()) != null && list.Count < 3000)
                    {
                        if (!line.Contains("\"type\":\"user\"") && !line.Contains("\"type\":\"assistant\"")) continue;
                        var e = Json.Parse(line); if (e == null) continue;
                        var text = TextOf(e); if (string.IsNullOrEmpty(text)) continue;
                        if (text.Length > 6000) text = text.Substring(0, 6000) + "…";
                        var role = Json.Str(e, "type") == "user" ? "user" : "agent";
                        list.Add(Msg(TsOf(e), role, role == "agent" ? "claude" : null, text, role == "user" ? "request" : "say", null));
                    }
                }
            }
            catch { }
            return list;
        }

        // 사용자·클로드가 실제로 쓴 글만 (도구 호출·결과, 시스템 알림은 뺀다)
        static string TextOf(object e)
        {
            if (Json.Str(e, "isMeta") == "True") return null;
            var content = Json.Get(e, "message", "content");
            string t;
            if (content is string) t = (string)content;
            else
            {
                var sb = new StringBuilder();
                foreach (var c in Json.Arr(content)) if (Json.Str(c, "type") == "text") sb.AppendLine(Json.Str(c, "text"));
                t = sb.ToString();
            }
            t = (t ?? "").Trim();
            // 프로젝트 채널에서 온 메시지(<wake>/<relay> 안의 <message>)는 실제 글만 꺼낸다
            if (t.StartsWith("<wake") || t.StartsWith("<relay") || t.StartsWith("<project"))
            {
                var parts = System.Text.RegularExpressions.Regex.Matches(t, @"<message\b[^>]*>([\s\S]*?)</message>")
                    .Cast<System.Text.RegularExpressions.Match>().Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value.Trim())).Where(x => x.Length > 0).ToList();
                t = parts.Count > 0 ? string.Join("\n", parts) : System.Text.RegularExpressions.Regex.Replace(t, @"<[^>]+>", " ").Trim();
            }
            if (t.Length == 0 || t.StartsWith("<system-reminder") || t.StartsWith("<command-") || t.StartsWith("<local-command") || t.StartsWith("[Request interrupted")) return null;
            // 안쪽의 시스템 알림 블록은 지운다
            t = System.Text.RegularExpressions.Regex.Replace(t, @"<system-reminder>[\s\S]*?</system-reminder>", "").Trim();
            return t.Length == 0 ? null : t;
        }

        static long TsOf(object e)
        {
            DateTime ts;
            if (DateTime.TryParse(Json.Str(e, "timestamp"), null, System.Globalization.DateTimeStyles.RoundtripKind, out ts))
                return (long)(ts.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;
            return 0;
        }
    }
}
