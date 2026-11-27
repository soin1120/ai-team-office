using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AITeam
{
    // 클로드 데스크톱 앱에서 쓰는 작업 폴더(프로젝트) 읽기
    //  - 직접 이름 붙여 만든 작업 대상(+ 로 만든 것)
    //  - 고정해 둔 폴더
    //  - 최근 Code 세션들이 일한 폴더 (세션 제목·마지막 활동 시각)
    // 프로젝트(베타)의 이름·지침은 claude.ai 서버에 있어 PC에서는 읽을 수 없으므로, PC에 남은 정보만 쓴다.
    static class ClaudeApp
    {
        static IEnumerable<string> DataDirs()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roam = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var list = new List<string>();
            try { var cd = Path.Combine(local, "Claude-Data"); if (Directory.Exists(cd)) list.AddRange(Directory.GetDirectories(cd, "claude*")); } catch { }
            foreach (var d in new[] { Path.Combine(roam, "Claude"), Path.Combine(local, "Claude") })
                if (File.Exists(Path.Combine(d, "claude_desktop_config.json")) || Directory.Exists(Path.Combine(d, "claude-code-sessions"))) list.Add(d);
            return list.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public class Proj { public string Name, Dir, Kind; public long Last; public int Sessions; }

        public static List<Proj> Projects()
        {
            var map = new Dictionary<string, Proj>(StringComparer.OrdinalIgnoreCase);
            Func<string, Proj> get = dir =>
            {
                dir = (dir ?? "").TrimEnd('\\', '/');
                if (dir.Length < 3) return null;
                Proj p; if (!map.TryGetValue(dir, out p)) map[dir] = p = new Proj { Dir = dir, Name = Path.GetFileName(dir), Kind = "folder" };
                return p;
            };
            foreach (var root in DataDirs())
            {
                // 1) 이름 붙여 만든 작업 대상
                try
                {
                    var f = Path.Combine(root, "ccd-session-presets.json");
                    if (File.Exists(f))
                    {
                        var j = Json.Parse(File.ReadAllText(f, Encoding.UTF8)) as IDictionary<string, object>;
                        var by = j != null && j.ContainsKey("byAccount") ? j["byAccount"] as IDictionary<string, object> : null;
                        if (by != null) foreach (var acc in by.Values)
                            foreach (var pr in Json.Arr(acc, "presets"))
                            {
                                if (Json.Str(pr, "target", "kind") != "local") continue;
                                var p = get(Json.Str(pr, "target", "cwd")); if (p == null) continue;
                                p.Name = Json.Str(pr, "name") ?? p.Name; p.Kind = "preset";
                                p.Last = Math.Max(p.Last, (long)Json.Num(pr, "lastUsedAt"));
                            }
                    }
                }
                catch { }
                // 2) 고정한 폴더
                try
                {
                    var f = Path.Combine(root, "claude_desktop_config.json");
                    if (File.Exists(f))
                    {
                        var j = Json.Parse(File.ReadAllText(f, Encoding.UTF8));
                        foreach (var d in Json.Arr(j, "preferences", "remoteControlPinnedFolders"))
                        { var p = get(Convert.ToString(d)); if (p != null && p.Kind == "folder") p.Kind = "pinned"; }
                    }
                }
                catch { }
                // 3) 최근 Code 세션의 폴더
                try
                {
                    var sd = Path.Combine(root, "claude-code-sessions");
                    if (Directory.Exists(sd))
                        foreach (var f in Directory.EnumerateFiles(sd, "local_*.json", SearchOption.AllDirectories))
                        {
                            try
                            {
                                var fi = new FileInfo(f); if (fi.Length > 2000000) continue;
                                var j = Json.Parse(File.ReadAllText(f, Encoding.UTF8));
                                if (Json.Str(j, "isArchived") == "True") continue;
                                var p = get(Json.Str(j, "cwd")); if (p == null) continue;
                                p.Sessions++;
                                p.Last = Math.Max(p.Last, (long)Json.Num(j, "lastActivityAt"));
                            }
                            catch { }
                        }
                }
                catch { }
            }
            return map.Values.Where(p => Directory.Exists(p.Dir))
                .OrderByDescending(p => p.Kind == "preset").ThenByDescending(p => p.Kind == "pinned").ThenByDescending(p => p.Last)
                .Take(30).ToList();
        }
    }
}
