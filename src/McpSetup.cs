using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AITeam
{
    // MCP 준비: 다른 PC에서 처음 켰을 때 한/글·PowerPoint MCP가 없으면 가장 먼저 설치·연결한다
    static class McpSetup
    {
        public static readonly string[] Required = { "hwp", "powerpoint" };
        static readonly string[] Files = { "hwp_server.py", "ppt_server.py", "com_worker.py", "hwp.py" };
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AITeam", "mcp", "office");
        static Dictionary<string, object> status = new Dictionary<string, object> { { "ready", true }, { "missing", new List<object>() }, { "report", "확인 중" } };
        static readonly object sync = new object();

        public static Dictionary<string, object> Status() { lock (sync) return new Dictionary<string, object>(status); }

        // 에이전트 감지 후 호출: 누가 어떤 MCP를 갖고 있는지로 준비 상태 계산
        public static void Update(List<AgentInfo> agents, string shareReport)
        {
            var ready = agents.Where(a => a.Ready).ToList();
            var missing = new List<object>();
            foreach (var m in Required)
                foreach (var a in ready)
                    if (!a.McpNames.Any(n => n.Equals(m, StringComparison.OrdinalIgnoreCase))) { missing.Add(a.Name + ":" + m); }
            var d = new Dictionary<string, object>();
            d["ready"] = missing.Count == 0; d["missing"] = missing; d["python"] = FindPython() != null;
            d["report"] = missing.Count == 0 ? (shareReport ?? "MCP 준비 완료") : "MCP가 빠진 팀원이 있어요: " + string.Join(", ", missing.Select(Convert.ToString).ToArray());
            lock (sync) status = d;
        }

        public static string FindPython()
        {
            var p = Agents.FindExe("python");
            if (p != null && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return p;
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python");
            try
            {
                if (Directory.Exists(root))
                    foreach (var d in Directory.GetDirectories(root, "Python3*").OrderByDescending(x => x))
                        if (File.Exists(Path.Combine(d, "python.exe"))) return Path.Combine(d, "python.exe");
            }
            catch { }
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            try { foreach (var d in Directory.GetDirectories(pf, "Python3*").OrderByDescending(x => x)) if (File.Exists(Path.Combine(d, "python.exe"))) return Path.Combine(d, "python.exe"); } catch { }
            return null;
        }

        // 설치·연결: 파이썬 패키지 → 스크립트 꺼내기 → 클로드 등록 → 코덱스·제미나이에 공유
        public static string Install(List<AgentInfo> agents)
        {
            var log = new StringBuilder();
            var py = FindPython();
            if (py == null) return "Python이 없어서 MCP를 설치할 수 없어요. PowerShell에서 'winget install Python.Python.3.11' 로 Python을 설치한 뒤 다시 눌러 주세요.";
            log.AppendLine("Python: " + py);
            var pip = Agents.RunQuick(py, "-m pip install --user --disable-pip-version-check mcp==2.3.0 pyhwpx pywin32 python-pptx olefile", 15 * 60000);
            log.AppendLine(pip.Item1 == 0 ? "필요한 파이썬 패키지 설치 완료" : "패키지 설치 중 문제: " + Tail(pip.Item2, 400));
            Directory.CreateDirectory(Dir);
            foreach (var f in Files) File.WriteAllText(Path.Combine(Dir, f), Rules.Resource("mcp_" + f), new UTF8Encoding(false));
            log.AppendLine("MCP 스크립트를 " + Dir + " 에 넣었어요");

            var own = new List<Agents.Mcp>();
            foreach (var name in Required)
            {
                var m = new Agents.Mcp { Name = name, Command = py };
                m.Args.Add(Path.Combine(Dir, name == "hwp" ? "hwp_server.py" : "ppt_server.py"));
                m.Env["PYTHONIOENCODING"] = "utf-8";
                own.Add(m);
            }
            var c = agents.FirstOrDefault(a => a.Id == "claude" && a.Path != null);
            if (c != null)
            {
                var have = Agents.ClaudeMcps(c.Path).Select(x => x.Name).ToList();
                foreach (var m in own.Where(m => !have.Contains(m.Name)))
                {
                    var r = Agents.RunQuick(c.Path, "mcp add -s user -e PYTHONIOENCODING=utf-8 " + m.Name + " -- " + Agents.Quote(m.Command) + " " + Agents.Quote(m.Args[0]), 60000);
                    log.AppendLine("클로드에 " + m.Name + " 등록: " + (r.Item1 == 0 ? "완료" : "실패 " + Tail(r.Item2, 200)));
                }
            }
            log.AppendLine(Agents.ShareMcps(agents, own));
            return log.ToString().Trim();
        }

        static string Tail(string s, int n) { s = (s ?? "").Trim(); return s.Length > n ? "…" + s.Substring(s.Length - n) : s; }
    }
}
