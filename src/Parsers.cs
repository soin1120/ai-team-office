using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AITeam
{
    // 각 CLI의 출력 한 줄(JSON)을 화면용 기록으로 바꾸고, 마지막 답변을 모은다
    abstract class OutputParser
    {
        public string FinalText = "";
        public string Error;
        public string Usage;
        public long InTok, OutTok;                  // 이번 실행에서 쓴 토큰
        public List<string> Denials = new List<string>(); // 권한 때문에 막힌 일

        protected void CheckDenied(string what, string output)
        {
            if (string.IsNullOrEmpty(output)) return;
            var o = output.ToLowerInvariant();
            if (o.Contains("permission") && (o.Contains("denied") || o.Contains("not granted") || o.Contains("haven't granted")) ||
                o.Contains("access is denied") || o.Contains("access denied") || o.Contains("operation not permitted") ||
                o.Contains("requires approval") || o.Contains("blocked by sandbox") || o.Contains("액세스가 거부") || o.Contains("권한이 없"))
            {
                var d = Short((what ?? "") + " — " + output, 220);
                if (!Denials.Contains(d)) Denials.Add(d);
            }
        }
        public Action<string, string, string> Emit; // (kind, text, detail)
        public Action<object> RateLimit;            // 한도 정보 (클로드)
        public Action<string> Wrote;                // 파일을 만들거나 고친 전체 경로 (규칙 점검용)
        protected void WroteFile(string path) { if (Wrote != null && !string.IsNullOrEmpty(path)) Wrote(path); }
        // 보조 에이전트(서브에이전트)가 뜨고 끝날 때: (보조 id, 시작 여부, 맡은 일)
        public Action<string, bool, string> Sub;
        protected void SubAgent(string id, bool start, string what) { if (Sub != null && !string.IsNullOrEmpty(id)) Sub(id, start, what); }

        public abstract void Line(string line);

        protected static string Short(string s, int n)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Length > n ? s.Substring(0, n) + "…" : s;
        }

        protected static string FileName(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            try { return Path.GetFileName(p.TrimEnd('\\', '/')); } catch { return p; }
        }

        public static OutputParser For(string id)
        {
            switch (id)
            {
                case "claude": return new ClaudeParser();
                case "codex": return new CodexParser();
                default: return new GeminiParser();
            }
        }
    }

    class ClaudeParser : OutputParser
    {
        public override void Line(string line)
        {
            var ev = Json.Parse(line);
            if (ev == null) { if (line.Trim().Length > 0) Emit("info", Short(line, 200), null); return; }
            var type = Json.Str(ev, "type");
            if (type == "assistant")
            {
                foreach (var c in Json.Arr(ev, "message", "content"))
                {
                    var ct = Json.Str(c, "type");
                    if (ct == "text")
                    {
                        var t = Json.Str(c, "text");
                        if (!string.IsNullOrWhiteSpace(t)) { Emit("say", t.Trim(), null); FinalText = t; }
                    }
                    else if (ct == "thinking")
                    {
                        var t = Json.Str(c, "thinking");
                        if (!string.IsNullOrWhiteSpace(t)) Emit("think", Short(t, 300), null);
                    }
                    else if (ct == "tool_use")
                    {
                        var tn = Json.Str(c, "name");
                        if (tn == "Task" || tn == "Agent")
                            SubAgent(Json.Str(c, "id"), true, Json.Str(c, "input", "description") ?? Json.Str(c, "input", "subagent_type") ?? "보조 작업");
                        if (ToolKind(tn) == "file") WroteFile(Json.Str(c, "input", "file_path") ?? Json.Str(c, "input", "notebook_path"));
                        Emit(ToolKind(tn), Describe(tn, Json.Get(c, "input")), null);
                    }
                }
            }
            else if (type == "user")
            {
                foreach (var c in Json.Arr(ev, "message", "content"))
                    if (Json.Str(c, "type") == "tool_result") SubAgent(Json.Str(c, "tool_use_id"), false, null);
                foreach (var c in Json.Arr(ev, "message", "content"))
                    if (Json.Str(c, "type") == "tool_result" && Json.Str(c, "is_error") == "True")
                    {
                        CheckDenied("도구 사용", ContentText(Json.Get(c, "content")));
                        Emit("cmd-fail", "도구 오류: " + Short(ContentText(Json.Get(c, "content")), 200), ContentText(Json.Get(c, "content")));
                    }
            }
            else if (type == "rate_limit_event")
            {
                if (RateLimit != null) RateLimit(Json.Get(ev, "rate_limit_info"));
            }
            else if (type == "result")
            {
                foreach (var pd in Json.Arr(ev, "permission_denials"))
                {
                    var d = "권한 거부 — " + (Json.Str(pd, "tool_name") ?? "도구");
                    if (!Denials.Contains(d)) Denials.Add(d);
                }
                InTok = Json.Num(ev, "usage", "input_tokens") + Json.Num(ev, "usage", "cache_read_input_tokens") + Json.Num(ev, "usage", "cache_creation_input_tokens");
                OutTok = Json.Num(ev, "usage", "output_tokens");
                var r = Json.Str(ev, "result");
                if (!string.IsNullOrEmpty(r)) FinalText = r;
                if (Json.Str(ev, "is_error") == "True" || (Json.Str(ev, "subtype") ?? "success") != "success")
                    Error = string.IsNullOrEmpty(r) ? (Json.Str(ev, "subtype") ?? "오류") : r;
                Usage = string.Format("{0}턴 · 입력 {1:N0} / 출력 {2:N0} 토큰",
                    Json.Num(ev, "num_turns"),
                    Json.Num(ev, "usage", "input_tokens") + Json.Num(ev, "usage", "cache_read_input_tokens") + Json.Num(ev, "usage", "cache_creation_input_tokens"),
                    Json.Num(ev, "usage", "output_tokens"));
            }
        }

        static string ContentText(object content)
        {
            if (content is string) return (string)content;
            var sb = new StringBuilder();
            foreach (var c in Json.Arr(content)) sb.Append(Json.Str(c, "text"));
            return sb.ToString();
        }

        static string ToolKind(string name)
        {
            if (name == "Write" || name == "Edit" || name == "MultiEdit" || name == "NotebookEdit") return "file";
            if (name == "Bash" || name == "PowerShell") return "cmd";
            if (name == "WebSearch" || name == "WebFetch") return "web";
            return "tool";
        }

        static string Describe(string name, object input)
        {
            switch (name)
            {
                case "Write": return "파일 작성: " + FileName(Json.Str(input, "file_path"));
                case "Edit": case "MultiEdit": return "파일 수정: " + FileName(Json.Str(input, "file_path"));
                case "Read": return "파일 읽기: " + FileName(Json.Str(input, "file_path"));
                case "Bash": case "PowerShell": return "명령 실행: " + Short(Json.Str(input, "command"), 200);
                case "Glob": return "파일 찾기: " + Json.Str(input, "pattern");
                case "Grep": return "내용 검색: " + Json.Str(input, "pattern");
                case "WebSearch": return "웹 검색: " + Json.Str(input, "query");
                case "WebFetch": return "웹 읽기: " + Json.Str(input, "url");
                case "TodoWrite":
                    return "계획: " + string.Join(" / ", Json.Arr(input, "todos").Select(t =>
                        (Json.Str(t, "status") == "completed" ? "✔ " : "○ ") + Json.Str(t, "content")).ToArray());
            }
            return "도구: " + name;
        }
    }

    class CodexParser : OutputParser
    {
        public override void Line(string line)
        {
            var ev = Json.Parse(line);
            if (ev == null) { if (line.Trim().Length > 0) Emit("info", Short(line, 200), null); return; }
            var type = Json.Str(ev, "type");
            var item = Json.Get(ev, "item");
            var it = Json.Str(item, "type");
            // 코덱스의 보조 에이전트(협업 도구) 항목
            if (it != null && it != "agent_message" && (it.Contains("collab") || it.Contains("agent")))
            {
                if (type == "item.started") SubAgent(Json.Str(item, "id"), true, Json.Str(item, "prompt") ?? Json.Str(item, "tool") ?? "보조 작업");
                else if (type == "item.completed") SubAgent(Json.Str(item, "id"), false, null);
            }
            switch (type)
            {
                case "item.completed":
                    if (it == "agent_message") { var t = Json.Str(item, "text") ?? ""; Emit("say", t, null); FinalText = t; }
                    else if (it == "reasoning") Emit("think", Short(Json.Str(item, "text"), 300), null);
                    else if (it == "command_execution")
                    {
                        bool ok = Json.Str(item, "exit_code") == "0";
                        var output = Json.Str(item, "aggregated_output") ?? "";
                        if (!ok) CheckDenied("명령 " + Short(Json.Str(item, "command"), 80), output);
                        if (output.Length > 3000) output = output.Substring(output.Length - 3000);
                        Emit(ok ? "cmd-ok" : "cmd-fail", Json.Str(item, "command") ?? "", output);
                    }
                    else if (it == "file_change")
                    {
                        foreach (var ch in Json.Arr(item, "changes")) WroteFile(Json.Str(ch, "path"));
                    }
                    if (it == "file_change")
                        Emit("file", "파일 변경: " + string.Join(", ", Json.Arr(item, "changes").Select(c =>
                            KindKo(Json.Str(c, "kind")) + " " + FileName(Json.Str(c, "path"))).ToArray()), null);
                    else if (it == "todo_list")
                        Emit("plan", "계획: " + string.Join(" / ", Json.Arr(item, "items").Select(t =>
                            (Json.Str(t, "completed") == "True" ? "✔ " : "○ ") + Json.Str(t, "text")).ToArray()), null);
                    else if (it == "web_search") Emit("web", "웹 검색: " + Json.Str(item, "query"), null);
                    else if (it == "error") Emit("error", Short(Json.Str(item, "message"), 300), null);
                    break;
                case "turn.completed":
                    InTok = Json.Num(ev, "usage", "input_tokens"); OutTok = Json.Num(ev, "usage", "output_tokens");
                    Usage = string.Format("입력 {0:N0} / 출력 {1:N0} 토큰", Json.Num(ev, "usage", "input_tokens"), Json.Num(ev, "usage", "output_tokens"));
                    break;
                case "turn.failed":
                    Error = Json.Str(ev, "error", "message") ?? "작업 실패"; break;
                case "error":
                    Error = Json.Str(ev, "message") ?? "오류";
                    Emit("error", Short(Error, 300), null); break;
            }
        }

        static string KindKo(string k)
        {
            switch (k) { case "add": return "생성"; case "update": return "수정"; case "delete": return "삭제"; }
            return k ?? "";
        }
    }

    class GeminiParser : OutputParser
    {
        readonly Dictionary<long, StringBuilder> texts = new Dictionary<long, StringBuilder>();

        public override void Line(string line)
        {
            var ev = Json.Parse(line);
            if (ev == null) { if (line.Trim().Length > 0) Emit("info", Short(line, 200), null); return; }
            var e = Json.Str(ev, "event");
            if (e == "step_update")
            {
                var s = Json.Get(ev, "step_update");
                var st = Json.Str(s, "step_type");
                var idx = Json.Num(s, "step_index");
                bool done = Json.Str(s, "state") == "DONE";
                if (st == "agent_response")
                {
                    StringBuilder sb;
                    if (!texts.TryGetValue(idx, out sb)) texts[idx] = sb = new StringBuilder();
                    var delta = Json.Str(s, "text_delta");
                    if (delta != null) sb.Append(delta);
                    if (done && sb.Length > 0) { var t = sb.ToString().Trim(); Emit("say", t, null); FinalText = t; }
                }
                else if (st == "tool" && !done)
                {
                    var nm = (Json.Str(s, "tool_name") ?? Json.Str(s, "tool_info", "name") ?? "").ToLowerInvariant();
                    if (nm.Contains("subagent") || nm.Contains("sub_agent")) SubAgent("g" + idx, true, Json.Str(s, "tool_info", "parameters", "Task") ?? Json.Str(s, "tool_info", "parameters", "TaskName") ?? "보조 작업");
                }
                if (st == "tool" && done) SubAgent("g" + idx, false, null);
                if (st == "tool" && done)
                {
                    var name = Json.Str(s, "tool_name") ?? Json.Str(s, "tool_info", "name") ?? "도구";
                    var pars = Json.Get(s, "tool_info", "parameters");
                    var output = Json.Str(s, "tool_info", "output");
                    CheckDenied(name, output != null && output.Length > 600 ? output.Substring(0, 600) : output);
                    if (ToolKind(name) == "file")
                    {
                        var pd = pars as IDictionary<string, object>;
                        if (pd != null) foreach (var key in new[] { "TargetFile", "AbsolutePath", "Path", "File", "FilePath" })
                            { object v; if (pd.TryGetValue(key, out v) && v != null) { WroteFile(Convert.ToString(v)); break; } }
                    }
                    Emit(ToolKind(name), Describe(name, pars), output != null && output.Length > 3000 ? output.Substring(output.Length - 3000) : output);
                }
            }
            else if (e == "result")
            {
                var r = Json.Get(ev, "result");
                var resp = Json.Str(r, "response");
                if (!string.IsNullOrEmpty(resp)) FinalText = resp;
                if (Json.Str(r, "status") != "SUCCESS") Error = Json.Str(r, "error") ?? Json.Str(r, "status") ?? "오류";
                InTok = Json.Num(r, "usage", "input_tokens"); OutTok = Json.Num(r, "usage", "output_tokens");
                Usage = string.Format("{0}턴 · 입력 {1:N0} / 출력 {2:N0} 토큰", Json.Num(r, "num_turns"),
                    Json.Num(r, "usage", "input_tokens"), Json.Num(r, "usage", "output_tokens"));
            }
        }

        static string ToolKind(string name)
        {
            name = (name ?? "").ToLowerInvariant();
            if (name.Contains("write") || name.Contains("replace") || name.Contains("edit")) return "file";
            if (name.Contains("command")) return "cmd";
            if (name.Contains("search_web") || name.Contains("url") || name.Contains("browser")) return "web";
            return "tool";
        }

        static string Describe(string name, object pars)
        {
            var d = pars as IDictionary<string, object>;
            string detail = "";
            if (d != null)
            {
                foreach (var key in new[] { "CommandLine", "TargetFile", "AbsolutePath", "File", "Path", "Query", "query", "Url", "SearchPath", "DirectoryPath" })
                {
                    object v;
                    if (d.TryGetValue(key, out v) && v != null)
                    {
                        var s = Convert.ToString(v);
                        detail = key.EndsWith("File") || key.EndsWith("Path") ? FileName(s) : Short(s, 160);
                        break;
                    }
                }
            }
            return name + (detail.Length > 0 ? ": " + detail : "");
        }
    }
}
