using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace AITeam
{
    // 에이전트 한 명의 정보 (설치 위치, 로그인 상태)
    class AgentInfo
    {
        public string Id;          // claude | codex | gemini
        public string Name;        // 화면 이름
        public string Role;        // 잘하는 일 (계획 세울 때 참고)
        public string Path;        // 실행 파일 경로 (없으면 null)
        public bool LoggedIn;
        public string Plan;        // 요금제 등 부가 정보
        public string Note;        // 상태 설명
        public string Install;     // 설치 명령
        public string Login;       // 로그인 명령
        public List<object> Models = new List<object>();   // [{id, name}]
        public List<string> Efforts = new List<string>();
        public string DefaultModel;
        public List<string> McpNames = new List<string>();

        public bool Ready { get { return Path != null && LoggedIn; } }

        public void AddModel(string id, string name)
        {
            var d = new Dictionary<string, object>(); d["id"] = id; d["name"] = name; Models.Add(d);
        }

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>();
            d["id"] = Id; d["name"] = Name; d["role"] = Role;
            d["installed"] = Path != null; d["loggedIn"] = LoggedIn; d["ready"] = Ready;
            d["plan"] = Plan; d["note"] = Note; d["install"] = Install; d["login"] = Login;
            d["path"] = Path;
            d["models"] = Models; d["efforts"] = Efforts; d["defaultModel"] = DefaultModel; d["mcp"] = McpNames;
            return d;
        }
    }

    static class Agents
    {
        public static readonly string[] Order = { "claude", "codex", "gemini" };

        public static string NameOf(string id)
        {
            switch (id) { case "claude": return "클로드"; case "codex": return "코덱스"; case "gemini": return "제미나이"; case "system": return "시스템"; }
            return id;
        }

        public static List<AgentInfo> Detect()
        {
            var list = new List<AgentInfo>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // 클로드 (Claude Code)
            var c = new AgentInfo
            {
                Id = "claude", Name = "클로드", Role = "생각하는 일: 요청 해석·설계·계획·정밀한 지시서 작성·검토·최종 보고 (사고력이 가장 좋지만 토큰이 비쌈)",
                Install = "irm https://claude.ai/install.ps1 | iex", Login = "claude  (실행 후 /login, Pro 이상 구독)"
            };
            c.Path = FindExe("claude", Path.Combine(home, ".local\\bin\\claude.exe"), Path.Combine(roaming, "npm\\claude.cmd"));
            if (c.Path != null)
            {
                var r = RunQuick(c.Path, "auth status", 20000);
                var j = Json.Extract(r.Item2);
                c.LoggedIn = j != null && Json.Str(j, "loggedIn") == "True";
                var sub = Json.Str(j, "subscriptionType");
                c.Plan = sub != null ? "Claude " + Cap(sub) : (c.LoggedIn ? "로그인됨" : null);
                c.Note = c.LoggedIn ? "준비됨" : "로그인이 필요해요";
                // Claude Code는 별칭으로 최신 모델을 고른다
                // 기본은 Opus 5.5 (사용자가 바꾸지 않는 한)
                c.DefaultModel = "claude-opus-5-5";
                c.AddModel("claude-opus-5-5", "Opus 5.5 (기본·고성능)");
                c.AddModel("fable", "Fable (최고 성능)"); c.AddModel("opus", "Opus (최신 별칭)");
                c.AddModel("sonnet", "Sonnet (균형)"); c.AddModel("haiku", "Haiku (빠름·절약)");
                c.Efforts.AddRange(new[] { "low", "medium", "high", "xhigh", "max" });
            }
            else c.Note = "설치되지 않았어요";
            list.Add(c);

            // 코덱스 (OpenAI Codex CLI)
            var x = new AgentInfo
            {
                Id = "codex", Name = "코덱스", Role = "실행하는 일: 지시서대로 구현·수정·문서 작성·스크립트·테스트 실행 (지시가 구체적일수록 정확하게 해냄)",
                Install = "npm install -g @openai/codex  (Node.js 필요)", Login = "codex login  (ChatGPT Plus 이상)"
            };
            x.Path = FindExe("codex", Path.Combine(roaming, "npm\\codex.cmd"));
            if (x.Path != null)
            {
                var r = RunQuick(x.Path, "login status", 20000);
                x.LoggedIn = r.Item1 == 0 && r.Item2.IndexOf("Logged in", StringComparison.OrdinalIgnoreCase) >= 0;
                x.Plan = x.LoggedIn ? (r.Item2.IndexOf("ChatGPT", StringComparison.OrdinalIgnoreCase) >= 0 ? "ChatGPT 구독" : "API 키") : null;
                x.Note = x.LoggedIn ? "준비됨" : "로그인이 필요해요";
                CodexModels(x, home);
            }
            else x.Note = "설치되지 않았어요 (데스크톱 앱만 있으면 CLI를 따로 설치해야 해요)";
            list.Add(x);

            // 제미나이 (Google Antigravity CLI)
            var g = new AgentInfo
            {
                Id = "gemini", Name = "제미나이", Role = "확인하는 일: 자료 조사·웹 검색·브라우저로 실제 화면 접속·로그인·클릭 확인·실사용 테스트·데이터 정리 (브라우저 도구 있음)",
                Install = "irm https://antigravity.google/cli/install.ps1 | iex", Login = "agy  (처음 실행 시 브라우저로 구글 로그인)"
            };
            g.Path = FindExe("agy", Path.Combine(local, "agy\\bin\\agy.exe"));
            if (g.Path != null)
            {
                // agy에는 로그인 확인 명령이 없어서, 로그인 정보 저장 여부로 판단한다
                g.LoggedIn = AgyLoggedIn(home);
                g.Plan = "Antigravity";
                g.Note = g.LoggedIn ? "준비됨" : "로그인이 필요할 수 있어요 (터미널에서 agy 한 번 실행)";
                if (!g.LoggedIn) g.LoggedIn = true; // 확실하지 않으면 실행 시점에 확인
                // agy models 출력: "모델id<TAB>이름"
                var r = RunQuick(g.Path, "models", 30000);
                foreach (var line in r.Item2.Split('\n'))
                {
                    var parts = line.Trim().Split('\t');
                    if (parts.Length >= 2 && parts[0].Length > 0 && !parts[0].Contains(" ")) g.AddModel(parts[0].Trim(), parts[1].Trim());
                }
                g.Efforts.AddRange(new[] { "low", "medium", "high", "xhigh", "max" });
            }
            else g.Note = "설치되지 않았어요";
            list.Add(g);
            return list;
        }

        // Codex가 받아 둔 모델 목록(~/.codex/models_cache.json)과 현재 기본 모델(config.toml)
        static void CodexModels(AgentInfo x, string home)
        {
            try
            {
                var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
                if (string.IsNullOrEmpty(codexHome)) codexHome = Path.Combine(home, ".codex");
                var cache = Json.Parse(File.ReadAllText(Path.Combine(codexHome, "models_cache.json"), Encoding.UTF8));
                var efforts = new List<string>();
                foreach (var m in Json.Arr(cache, "models"))
                {
                    if (Json.Str(m, "visibility") != "list") continue;
                    var slug = Json.Str(m, "slug");
                    x.AddModel(slug, Json.Str(m, "display_name") ?? slug);
                    foreach (var lv in Json.Arr(m, "supported_reasoning_levels"))
                    {
                        var e = Json.Str(lv, "effort");
                        if (e != null && !efforts.Contains(e)) efforts.Add(e);
                    }
                }
                x.Efforts.AddRange(efforts);
                var cfg = Path.Combine(codexHome, "config.toml");
                if (File.Exists(cfg))
                    foreach (var line in File.ReadAllLines(cfg))
                    {
                        var t = line.Trim();
                        if (t.StartsWith("[")) break; // 최상위 설정만
                        if (t.StartsWith("model ") || t.StartsWith("model="))
                            x.DefaultModel = t.Substring(t.IndexOf('=') + 1).Trim().Trim('"');
                    }
            }
            catch { }
        }

        static bool AgyLoggedIn(string home)
        {
            try
            {
                var dir = Path.Combine(home, ".gemini\\antigravity-cli");
                if (!Directory.Exists(dir)) return false;
                return Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Any(f =>
                    f.IndexOf("oauth", StringComparison.OrdinalIgnoreCase) >= 0 || f.IndexOf("cred", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    f.IndexOf("account", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch { return false; }
        }

        static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1); }

        // PATH와 알려진 위치에서 실행 파일 찾기. WindowsApps(스토어 앱 내부)는 밖에서 실행할 수 없어 제외한다.
        public static string FindExe(string name, params string[] extra)
        {
            var exts = new[] { ".exe", ".cmd", ".bat" };
            var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';');
            foreach (var d in dirs)
            {
                if (string.IsNullOrWhiteSpace(d) || d.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                foreach (var e in exts)
                {
                    try { var p = Path.Combine(d.Trim(), name + e); if (File.Exists(p)) return p; } catch { }
                }
            }
            foreach (var p in extra) if (File.Exists(p)) return p;
            return null;
        }

        // 짧은 명령 실행 (상태 확인용). 반환: (종료코드, 출력)
        public static Tuple<int, string> RunQuick(string exe, string args, int timeoutMs)
        {
            try
            {
                var psi = MakePsi(exe, args, null);
                using (var p = Process.Start(psi))
                {
                    p.StandardInput.Close();
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    var errTask = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs)) { Kill(p); return Tuple.Create(-1, ""); }
                    return Tuple.Create(p.ExitCode, outTask.Result + "\n" + errTask.Result);
                }
            }
            catch (Exception ex) { return Tuple.Create(-1, ex.Message); }
        }

        public static ProcessStartInfo MakePsi(string exe, string args, string cwd)
        {
            var psi = new ProcessStartInfo();
            if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                psi.Arguments = "/d /s /c \"\"" + exe + "\" " + args + "\"";
            }
            else { psi.FileName = exe; psi.Arguments = args; }
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            psi.StandardErrorEncoding = new UTF8Encoding(false);
            if (cwd != null) psi.WorkingDirectory = cwd;
            psi.EnvironmentVariables["NO_COLOR"] = "1";
            psi.EnvironmentVariables["FORCE_COLOR"] = "0";
            return psi;
        }

        public static void Kill(Process p)
        {
            try
            {
                if (p.HasExited) return;
                var k = Process.Start(new ProcessStartInfo("taskkill", "/PID " + p.Id + " /T /F") { CreateNoWindow = true, UseShellExecute = false });
                if (k != null) k.WaitForExit(5000);
            }
            catch { }
        }

        // Windows 명령줄 인자 한 개를 안전하게 따옴표로 감싼다
        public static string Quote(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char ch in arg)
            {
                if (ch == '\\') { slashes++; continue; }
                if (ch == '"') { sb.Append('\\', slashes * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', slashes); sb.Append(ch); }
                slashes = 0;
            }
            sb.Append('\\', slashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        // 단계별 실행 인자. readOnly=계획 단계(파일 수정 금지), auto=명령 실행 자동 승인
        // 반환: (인자, 프롬프트를 stdin으로 보낼지)
        static bool Safe(string s)
        {
            return !string.IsNullOrEmpty(s) && s.All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.' || ch == '[' || ch == ']');
        }

        // 실행 인자 만들기
        //  full=true  : '모든 권한 위임' — 묻지 않고 모든 도구·폴더·MCP 사용 (읽기 전용 단계는 쓰기 도구만 막음)
        //  full=false : 제한 모드 — 작업 폴더 + 지시문에 나온 폴더(extraDirs) + MCP만 허용
        public static Tuple<string, bool> BuildArgs(string id, string prompt, string cwd, bool readOnly, bool full, string model, string effort,
                                                    IEnumerable<string> images, IEnumerable<string> extraDirs, IEnumerable<string> mcpNames)
        {
            if (!Safe(model)) model = null;
            if (!Safe(effort)) effort = null;
            var dirs = (extraDirs ?? new string[0]).Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var mcps = (mcpNames ?? new string[0]).Where(Safe).ToList();
            switch (id)
            {
                case "claude":
                    {
                        // 스킬 목록을 빼서 매 실행마다 읽는 토큰을 줄인다 (팀원으로 일할 땐 필요 없음)
                        var a = "-p --output-format stream-json --verbose --no-session-persistence --disable-slash-commands";
                        if (model != null) a += " --model " + model;
                        if (effort != null) a += " --effort " + effort;
                        if (full)
                        {
                            a += " --dangerously-skip-permissions";
                            if (readOnly) a += " --disallowedTools Write,Edit,MultiEdit,NotebookEdit";
                        }
                        else
                        {
                            var allow = new List<string> { "Read", "Glob", "Grep", "WebSearch", "WebFetch" };
                            allow.AddRange(mcps.Select(m => "mcp__" + m));
                            if (readOnly) a += " --permission-mode dontAsk";
                            else { a += " --permission-mode acceptEdits"; allow.AddRange(new[] { "Bash", "PowerShell" }); }
                            a += " --allowedTools " + string.Join(",", allow.ToArray());
                        }
                        if (dirs.Count > 0) a += " --add-dir " + string.Join(" ", dirs.Select(Quote).ToArray());
                        return Tuple.Create(a, true);
                    }
                case "codex":
                    {
                        var a = "exec --json --skip-git-repo-check -C " + Quote(cwd);
                        if (full) a += " --dangerously-bypass-approvals-and-sandbox";
                        else
                        {
                            a += " -s " + (readOnly ? "read-only" : "workspace-write");
                            foreach (var d in dirs) a += " --add-dir " + Quote(d);
                        }
                        if (model != null) a += " -m " + model;
                        if (effort != null) a += " -c model_reasoning_effort=" + effort;
                        if (images != null)
                            foreach (var image in images) a += " --image " + Quote(image);
                        a += " -";
                        return Tuple.Create(a, true);
                    }
                case "gemini":
                    {
                        var a = "-p " + Quote(prompt) + " --output-format stream-json --print-timeout 40m";
                        if (model != null) a += " --model " + model;
                        if (effort != null) a += " --effort " + effort;
                        if (full) a += " --dangerously-skip-permissions";
                        else if (readOnly) a += " --mode plan";
                        else a += " --mode accept-edits";
                        foreach (var d in dirs) a += " --add-dir " + Quote(d);
                        return Tuple.Create(a, false);
                    }
            }
            throw new ArgumentException(id);
        }

        // 노력 수준 이름을 그 AI가 지원하는 값으로 맞춘다 (없으면 가장 가까운 값)
        public static readonly string[] EffortScale = { "low", "medium", "high", "xhigh", "max" };
        public static string FitEffort(AgentInfo a, string want)
        {
            if (string.IsNullOrEmpty(want) || a == null || a.Efforts.Count == 0) return want;
            if (a.Efforts.Contains(want)) return want;
            int w = Array.IndexOf(EffortScale, want); if (w < 0) return null;
            for (int d = 1; d < EffortScale.Length; d++)
            {
                if (w - d >= 0 && a.Efforts.Contains(EffortScale[w - d])) return EffortScale[w - d];
                if (w + d < EffortScale.Length && a.Efforts.Contains(EffortScale[w + d])) return EffortScale[w + d];
            }
            return null;
        }
        public static string StepUp(string effort)
        {
            int i = Array.IndexOf(EffortScale, effort ?? "medium"); if (i < 0) i = 1;
            return EffortScale[Math.Min(EffortScale.Length - 2, i + 1)]; // max 는 자동으로 쓰지 않음
        }

        // ───────── MCP 공유: 클로드에 연결된 로컬 MCP를 코덱스·제미나이에도 등록 ─────────
        public class Mcp { public string Name, Command; public List<string> Args = new List<string>(); public Dictionary<string, string> Env = new Dictionary<string, string>(); }

        public static List<Mcp> ClaudeMcps(string claudePath)
        {
            var list = new List<Mcp>();
            var r = RunQuick(claudePath, "mcp list", 60000);
            foreach (var line in r.Item2.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("claude.ai ") || t.IndexOf(": ", StringComparison.Ordinal) <= 0) continue;
                var name = t.Substring(0, t.IndexOf(": ", StringComparison.Ordinal));
                if (!Safe(name) || t.Contains("://")) continue; // 원격(http) 서버는 계정에 묶여 있어 공유하지 않음
                var g = RunQuick(claudePath, "mcp get " + name, 30000).Item2;
                var m = new Mcp { Name = name };
                bool inEnv = false;
                foreach (var gl in g.Split('\n'))
                {
                    var s = gl.Trim();
                    if (s.StartsWith("Type:") && !s.Contains("stdio")) { m = null; break; }
                    if (s.StartsWith("Command:")) { m.Command = s.Substring(8).Trim(); inEnv = false; }
                    else if (s.StartsWith("Args:")) { m.Args = SplitPaths(s.Substring(5).Trim()); inEnv = false; }
                    else if (s.StartsWith("Environment:")) inEnv = true;
                    else if (inEnv && s.Contains("=") && !s.StartsWith("To remove")) { var i = s.IndexOf('='); m.Env[s.Substring(0, i).Trim()] = s.Substring(i + 1).Trim(); }
                    else if (s.Length == 0 || s.StartsWith("To remove")) inEnv = false;
                }
                if (m != null && !string.IsNullOrEmpty(m.Command)) list.Add(m);
            }
            return list;
        }

        // "C:\a b\x.py --flag" 처럼 공백이 섞인 경로를 실제 존재 여부로 다시 묶는다
        static List<string> SplitPaths(string s)
        {
            var tok = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var outp = new List<string>();
            for (int i = 0; i < tok.Length; i++)
            {
                int best = -1;
                for (int j = tok.Length - 1; j > i; j--)
                {
                    var cand = string.Join(" ", tok, i, j - i + 1);
                    if (File.Exists(cand) || Directory.Exists(cand)) { best = j; break; }
                }
                if (best > i) { outp.Add(string.Join(" ", tok, i, best - i + 1)); i = best; }
                else outp.Add(tok[i]);
            }
            return outp;
        }

        static HashSet<string> ListNames(string exe, string args)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in RunQuick(exe, args, 60000).Item2.Split('\n'))
            {
                var t = line.Trim(); if (t.Length == 0) continue;
                var name = t.Split(new[] { ' ', ':', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
                if (Safe(name)) set.Add(name);
            }
            return set;
        }

        // 반환: 화면에 보여줄 결과 문장
        public static string ShareMcps(List<AgentInfo> agents, List<Mcp> fallback = null)
        {
            var c = agents.FirstOrDefault(a => a.Id == "claude");
            var mcps = c != null && c.Path != null ? ClaudeMcps(c.Path) : new List<Mcp>();
            if (c != null) c.McpNames = mcps.Select(m => m.Name).ToList();
            // 클로드가 없거나 MCP가 없으면, 프로그램에 들어 있는 한/글·PowerPoint MCP를 직접 등록
            if (mcps.Count == 0 && fallback != null) mcps = fallback;
            if (mcps.Count == 0) return "공유할 로컬 MCP가 없어요";
            var report = new List<string>();
            foreach (var a in agents.Where(x => x.Id != "claude" && x.Path != null))
            {
                var have = ListNames(a.Path, "mcp list");
                var added = new List<string>();
                foreach (var m in mcps)
                {
                    if (have.Contains(m.Name)) continue;
                    var env = string.Join(" ", m.Env.Select(kv => "--env " + Quote(kv.Key + "=" + kv.Value)).ToArray());
                    var cmd = Quote(m.Command) + " " + string.Join(" ", m.Args.Select(Quote).ToArray());
                    var args = a.Id == "codex" ? "mcp add " + m.Name + " " + env + " -- " + cmd
                                               : "mcp add " + env + " " + m.Name + " " + cmd;
                    var r = RunQuick(a.Path, args, 60000);
                    if (r.Item1 == 0) added.Add(m.Name);
                }
                a.McpNames = ListNames(a.Path, "mcp list").Where(n => mcps.Any(m => m.Name.Equals(n, StringComparison.OrdinalIgnoreCase))).ToList();
                report.Add(a.Name + ": " + (a.McpNames.Count > 0 ? string.Join(", ", a.McpNames.ToArray()) : "없음") + (added.Count > 0 ? " (새로 등록 " + added.Count + ")" : ""));
            }
            return (c != null && c.Path != null ? "클로드: " + string.Join(", ", c.McpNames.ToArray()) + " · " : "") + string.Join(" · ", report.ToArray());
        }
    }
}
