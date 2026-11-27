using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("AI 팀 사무실")]
[assembly: AssemblyProduct("AI Team Office")]
// 버전은 VERSION 파일에서 build.ps1이 obj\Version.cs로 만들어 넣는다

namespace AITeam
{
    static class Program
    {
        public static int Port;
        public static string Url { get { return "http://localhost:" + Port + "/"; } }
        static Form ui;          // 폴더 선택 창 등 UI 스레드 작업용 (숨김)
        static NotifyIcon tray;
        static long lastPing;
        static Process browser;
        static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AITeam");

        [STAThread]
        static void Main(string[] args)
        {
            Directory.CreateDirectory(DataDir);
            Rules.Ensure();
            Updater.StartChecker();
            // --resume "<작업 폴더>" <runId> : 관리자 권한으로 다시 열렸을 때 멈췄던 작업 이어서
            string resumeDir = null, resumeId = null;
            if (args.Length >= 3 && args[0] == "--resume") { resumeDir = args[1]; resumeId = args[2]; }
            bool first;
            var mutex = new Mutex(true, "AITeamOffice_SingleInstance", out first);
            if (!first && resumeDir != null)
            {
                // 이전 창이 닫힐 때까지 잠깐 기다린다
                try { first = mutex.WaitOne(30000); } catch (AbandonedMutexException) { first = true; }
            }
            if (!first)
            {
                // 이미 실행 중이면 창만 다시 연다
                try { Port = int.Parse(File.ReadAllText(Path.Combine(DataDir, "port.txt"))); OpenWindow(); } catch { }
                return;
            }
            if (resumeDir != null)
                new Thread(() =>
                {
                    Team.I.GetAgents(true);
                    var err = Team.I.Resume(resumeDir, resumeId);
                    if (err != null) MessageBox.Show(err, "AI 팀 사무실");
                }) { IsBackground = true }.Start();

            Application.EnableVisualStyles();
            Port = FreePort(47770);
            File.WriteAllText(Path.Combine(DataDir, "port.txt"), Port.ToString());

            var listener = new HttpListener();
            listener.Prefixes.Add(Url);
            try { listener.Start(); }
            catch (Exception ex) { MessageBox.Show("내부 서버를 시작하지 못했어요: " + ex.Message, "AI 팀 사무실"); return; }
            new Thread(() => Serve(listener)) { IsBackground = true }.Start();
            if (resumeDir == null) new Thread(() => Team.I.GetAgents(true)) { IsBackground = true }.Start();

            ui = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized, Opacity = 0 };
            ui.Load += (s, e) => ui.Hide();
            Icon icon = null;
            try { icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            tray = new NotifyIcon { Icon = icon ?? SystemIcons.Application, Text = "AI 팀 사무실", Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("창 열기", null, (s, e) => OpenWindow());
            menu.Items.Add("종료", null, (s, e) => Quit());
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => OpenWindow();

            // 창이 닫히고 작업도 없으면 프로그램 종료
            lastPing = Team.Now();
            var timer = new System.Windows.Forms.Timer { Interval = 5000 };
            bool notified = false;
            timer.Tick += (s, e) =>
            {
                bool idle = Team.Now() - Interlocked.Read(ref lastPing) > 25000;
                if (!idle) { notified = false; return; }
                if (!Team.I.Busy) Quit();
                else if (!notified)
                {
                    notified = true;
                    tray.ShowBalloonTip(5000, "AI 팀 사무실", "창을 닫았지만 작업은 계속 진행 중이에요. 트레이 아이콘을 두 번 누르면 다시 열려요.", ToolTipIcon.Info);
                }
            };
            timer.Start();

            OpenWindow();
            Application.Run(ui);
            listener.Stop();
            GC.KeepAlive(mutex);
        }

        // 관리자 권한으로 다시 열기 (UAC 창은 사용자가 승인). 열렸으면 이 창은 닫는다
        public static bool RestartElevated(string dir, string runId)
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, "--resume " + Agents.Quote(dir) + " " + runId) { UseShellExecute = true, Verb = "runas" };
                Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception) { return false; } // 사용자가 UAC에서 '아니요'
            catch { return false; }
            new Thread(() =>
            {
                Thread.Sleep(800);
                Team.I.Stop(true);
                if (ui != null) ui.BeginInvoke((Action)(() => { tray.Visible = false; Application.Exit(); }));
            }) { IsBackground = true }.Start();
            return true;
        }

        // 이 프로그램이 띄운 창(크롬/엣지 앱 창)을 닫는다
        static void CloseAppWindows()
        {
            try
            {
                var dirs = new[] { Path.Combine(DataDir, "window-chrome"), Path.Combine(DataDir, "window") };
                using (var q = new System.Management.ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'chrome.exe' OR Name = 'msedge.exe'"))
                    foreach (System.Management.ManagementObject o in q.Get())
                    {
                        var cl = Convert.ToString(o["CommandLine"]) ?? "";
                        if (cl.IndexOf("--type=", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (dirs.Any(d => cl.IndexOf(d, StringComparison.OrdinalIgnoreCase) >= 0))
                            try { Process.GetProcessById(Convert.ToInt32(o["ProcessId"])).Kill(); } catch { }
                    }
            }
            catch { }
        }

        static void Quit()
        {
            if (Team.I.Busy) Team.I.Stop();
            tray.Visible = false;
            Application.Exit();
        }

        static int FreePort(int start)
        {
            for (int p = start; p < start + 50; p++)
            {
                try { var l = new TcpListener(IPAddress.Loopback, p); l.Start(); l.Stop(); return p; } catch { }
            }
            return start;
        }

        // 크롬(없으면 엣지) 경로
        public static string ChromePath()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] c = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google\\Chrome\\Application\\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google\\Chrome\\Application\\chrome.exe"),
                Path.Combine(local, "Google\\Chrome\\Application\\chrome.exe")
            };
            return c.FirstOrDefault(File.Exists);
        }
        public static string EdgePath()
        {
            string[] e = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft\\Edge\\Application\\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft\\Edge\\Application\\msedge.exe")
            };
            return e.FirstOrDefault(File.Exists);
        }
        // 웹페이지·html 결과물은 크롬으로 연다 (없으면 기본 브라우저)
        public static void OpenInBrowser(string target)
        {
            try
            {
                var chrome = ChromePath();
                if (chrome != null) Process.Start(chrome, "\"" + target + "\"");
                else Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch { }
        }

        // 크롬 앱 모드(주소창 없는 창)로 연다. 크롬이 없으면 엣지, 둘 다 없으면 기본 브라우저.
        static void OpenWindow()
        {
            var chrome = ChromePath(); var edge = EdgePath();
            CloseAppWindows(); // 이전에 띄운 창(연결이 끊긴 창)이 남아 있으면 닫고 새로 연다
            try
            {
                if (chrome != null)
                {
                    MigrateEdgePrefs(edge);
                    browser = Process.Start(chrome, "--app=" + Url + "?w=main --user-data-dir=\"" + Path.Combine(DataDir, "window-chrome") + "\" --window-size=1560,980 --no-first-run --no-default-browser-check");
                }
                else if (edge != null)
                    browser = Process.Start(edge, "--app=" + Url + "?w=main --user-data-dir=\"" + Path.Combine(DataDir, "window") + "\" --window-size=1560,980 --no-first-run --no-default-browser-check");
                else Process.Start(Url);
            }
            catch { try { Process.Start(Url); } catch { } }
        }

        // 예전(엣지 창)의 화면 설정(테마·모델·캐릭터 꾸미기 등)을 크롬 창으로 옮긴다: 엣지를 화면 없이 잠깐 띄워 /migrate가 설정을 보내게 함
        static string PrefsFile { get { return Path.Combine(DataDir, "uiprefs.json"); } }
        static void MigrateEdgePrefs(string edge)
        {
            try
            {
                var old = Path.Combine(DataDir, "window");
                if (edge == null || File.Exists(PrefsFile) || !Directory.Exists(old)) return;
                // 예전 엣지 창이 아직 열려 있으면 닫는다 (같은 설정 폴더를 쓰는 창 = 이 프로그램 창)
                try
                {
                    using (var q = new System.Management.ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'msedge.exe'"))
                        foreach (System.Management.ManagementObject o in q.Get())
                        {
                            var cl = Convert.ToString(o["CommandLine"]) ?? "";
                            if (cl.IndexOf(old, StringComparison.OrdinalIgnoreCase) >= 0 && cl.IndexOf("--type=", StringComparison.OrdinalIgnoreCase) < 0)
                                try { Process.GetProcessById(Convert.ToInt32(o["ProcessId"])).Kill(); } catch { }
                        }
                    Thread.Sleep(1200);
                }
                catch { }
                var p = Process.Start(new ProcessStartInfo(edge, "--headless=new --disable-gpu --no-first-run --user-data-dir=\"" + old + "\" " + Url + "migrate") { UseShellExecute = false, CreateNoWindow = true });
                for (int i = 0; i < 60 && !File.Exists(PrefsFile); i++) Thread.Sleep(250);
                try { if (!p.HasExited) p.Kill(); } catch { }
            }
            catch { }
        }

        static string PickFolder(string start)
        {
            string result = null;
            ui.Invoke((Action)(() =>
            {
                using (var d = new FolderBrowserDialog { Description = "AI 팀이 작업할 폴더를 고르세요", ShowNewFolderButton = true })
                {
                    if (!string.IsNullOrEmpty(start) && Directory.Exists(start)) d.SelectedPath = start;
                    var owner = new Form { TopMost = true, ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.CenterScreen };
                    owner.Show();
                    if (d.ShowDialog(owner) == DialogResult.OK) result = d.SelectedPath;
                    owner.Close();
                }
            }));
            return result;
        }

        // ───────────────────────── HTTP ─────────────────────────
        static void Serve(HttpListener l)
        {
            while (l.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = l.GetContext(); } catch { break; }
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        // 주소의 ?a=b 값을 UTF-8로 읽는다 (HttpListener 기본 해석은 한글 경로를 깨뜨림)
        static string Q(HttpListenerRequest req, string name)
        {
            var q = req.Url.Query.TrimStart('?');
            foreach (var part in q.Split('&'))
            {
                var i = part.IndexOf('=');
                var k = i < 0 ? part : part.Substring(0, i);
                if (k != name) continue;
                var v = i < 0 ? "" : part.Substring(i + 1);
                try { return Uri.UnescapeDataString(v.Replace('+', ' ')); } catch { return v; }
            }
            return null;
        }

        static void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request; var res = ctx.Response;
            try
            {
                var path = req.Url.AbsolutePath;
                // 다른 사이트가 이 서버를 건드리지 못하게: API는 같은 출처에서 온 요청만
                var origin = req.Headers["Origin"];
                if ((origin != null && origin != "http://localhost:" + Port) || req.Headers["Host"] != "localhost:" + Port) { res.StatusCode = 403; res.Close(); return; }

                if (path == "/" || path == "/index.html") { SendResource(res, "ui.html", "text/html; charset=utf-8"); return; }
                if (path == "/office.js") { SendResource(res, "office.js", "application/javascript; charset=utf-8"); return; }
                if (path == "/api/ping") { Interlocked.Exchange(ref lastPing, Team.Now()); SendJson(res, true); return; }
                if (path == "/api/agents")
                {
                    var list = Team.I.GetAgents(Q(req, "refresh") == "1");
                    var d = new Dictionary<string, object>();
                    d["agents"] = list.Select(a => a.ToJson()).ToList();
                    d["mcpShare"] = Team.I.McpReport;
                    d["defaultDir"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AI팀작업", DateTime.Now.ToString("yyyyMMdd-HHmm"));
                    SendJson(res, d); return;
                }
                if (path == "/api/usage")
                {
                    // 지금 쓰는 모델이 어느 한도를 쓰는지 표시 (예: 제미나이가 Claude 모델로 대체 중이면 Claude·GPT 그룹이 active)
                    var snap = Usage.Snapshot();
                    foreach (var id in Agents.Order)
                    {
                        var u = snap.ContainsKey(id) ? snap[id] as Dictionary<string, object> : null;
                        if (u == null) continue;
                        var am = Team.I.ActiveModel(id);
                        u = new Dictionary<string, object>(u); u["activeModel"] = am; snap[id] = u;
                        var wins = new List<object>();
                        foreach (var w in Json.Arr(u, "windows"))
                        {
                            var wd = new Dictionary<string, object>((Dictionary<string, object>)w);
                            var grp = Json.Str(wd, "group");
                            wd["active"] = grp == null || am["group"] == null || grp == (string)am["group"];
                            wins.Add(wd);
                        }
                        u["windows"] = wins;
                    }
                    SendJson(res, snap); return;
                }
                if (path == "/api/history")
                {
                    var dir = Q(req, "dir");
                    var runs = !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) ? Path.Combine(Path.GetFullPath(dir), ".aiteam") : null;
                    string previous = null;
                    if (runs != null && Directory.Exists(runs))
                    {
                        foreach (var run in Directory.EnumerateDirectories(runs).OrderByDescending(x => x, StringComparer.Ordinal))
                        {
                            var board = Path.Combine(run, "board.json");
                            if (!File.Exists(board)) continue;
                            try
                            {
                                if (Json.Str(Json.Parse(File.ReadAllText(board, Encoding.UTF8)), "phase") != "done") continue;
                                previous = Path.GetFileName(run);
                                break;
                            }
                            catch (IOException) { }
                        }
                    }
                    SendJson(res, new { previous = previous }); return;
                }
                if (path == "/api/avatars")
                {
                    var d = new Dictionary<string, object>();
                    foreach (var a in Agents.Order) { var f = AvatarFiles(a).FirstOrDefault(); d[a] = f == null ? null : Path.GetFileName(f); }
                    SendJson(res, d); return;
                }
                if (path == "/api/avatar" && req.HttpMethod == "GET")
                {
                    var agent = SafeAgent(Q(req, "agent"));
                    var f = agent == null ? null : AvatarFiles(agent).FirstOrDefault();
                    if (f == null) { res.StatusCode = 404; res.Close(); return; }
                    SendBytes(res, File.ReadAllBytes(f), "application/octet-stream"); return;
                }
                if (path == "/api/runs")
                {
                    // 업무 이력: 작업 폴더의 .aiteam 아래 실행 기록 목록
                    var dir = Q(req, "dir");
                    var list = new List<object>();
                    var root = !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) ? Path.Combine(Path.GetFullPath(dir), ".aiteam") : null;
                    if (root != null && Directory.Exists(root))
                        foreach (var run in Directory.GetDirectories(root).OrderByDescending(x => x, StringComparer.Ordinal).Take(300))
                        {
                            try
                            {
                                var board = Path.Combine(run, "board.json");
                                if (!File.Exists(board)) continue;
                                var b = Json.Parse(File.ReadAllText(board, Encoding.UTF8));
                                var d = new Dictionary<string, object>();
                                d["id"] = Path.GetFileName(run);
                                var rq = Json.Str(b, "request");
                                if (string.IsNullOrEmpty(rq) && File.Exists(Path.Combine(run, "prompt.txt"))) rq = File.ReadAllText(Path.Combine(run, "prompt.txt"), Encoding.UTF8);
                                d["request"] = rq; d["phase"] = Json.Str(b, "phase"); d["leader"] = Json.Str(b, "leader");
                                d["members"] = Json.Arr(b, "members"); d["startTs"] = Json.Num(b, "startTs"); d["endTs"] = Json.Num(b, "endTs");
                                d["summary"] = Json.Str(b, "finalSummary"); d["tasks"] = Json.Arr(b, "tasks"); d["issues"] = Json.Arr(b, "issues");
                                d["planSummary"] = Json.Str(b, "planSummary");
                                list.Add(d);
                            }
                            catch { }
                        }
                    SendJson(res, list); return;
                }
                if (path == "/api/runlog")
                {
                    // 지난 작업의 대화(자연어 기록)
                    var dir = Q(req, "dir"); var id = Q(req, "id") ?? "";
                    var list = new List<object>();
                    if (System.Text.RegularExpressions.Regex.IsMatch(id, @"\A[0-9]{8}-[0-9]{6}(?:-[0-9]{3})?\z") && !string.IsNullOrWhiteSpace(dir))
                    {
                        var f = Path.Combine(Path.GetFullPath(dir), ".aiteam", id, "events.jsonl");
                        if (File.Exists(f))
                            using (var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (var r = new StreamReader(s, Encoding.UTF8))
                            {
                                string line;
                                while ((line = r.ReadLine()) != null && list.Count < 2000) { var e = Json.Parse(line); if (e != null) list.Add(e); }
                            }
                    }
                    SendJson(res, list); return;
                }
                if (path == "/api/guide")
                {
                    var gdir = Q(req, "dir");
                    var d = new Dictionary<string, object>(); d["guide"] = Team.FindGuide(gdir);
                    d["secret"] = !string.IsNullOrEmpty(gdir) && File.Exists(Path.Combine(gdir, "AGENTS.local.md"));
                    SendJson(res, d); return;
                }
                if (path == "/api/projects" && req.HttpMethod == "GET")
                {
                    // 저장한 프로젝트 목록 (이름, 폴더)
                    var f = Path.Combine(DataDir, "projects.json");
                    object list = File.Exists(f) ? Json.Parse(File.ReadAllText(f, Encoding.UTF8)) : null;
                    var items = Json.Arr(list).Select(p =>
                    {
                        var d = new Dictionary<string, object>();
                        d["name"] = Json.Str(p, "name"); d["dir"] = Json.Str(p, "dir");
                        d["exists"] = Directory.Exists(Json.Str(p, "dir") ?? "");
                        d["guide"] = Team.FindGuide(Json.Str(p, "dir"));
                        d["source"] = "mine";
                        return (object)d;
                    }).ToList();
                    // 클로드 앱에서 쓰던 작업 폴더(프로젝트)도 같이 보여 준다 (이미 저장한 폴더는 빼고)
                    try
                    {
                        var mine = new HashSet<string>(items.Select(x => ((string)((Dictionary<string, object>)x)["dir"] ?? "").TrimEnd('\\', '/')), StringComparer.OrdinalIgnoreCase);
                        foreach (var cp in ClaudeApp.Projects())
                        {
                            if (mine.Contains(cp.Dir)) continue;
                            var d = new Dictionary<string, object>();
                            d["name"] = cp.Name; d["dir"] = cp.Dir; d["exists"] = true; d["guide"] = Team.FindGuide(cp.Dir);
                            d["source"] = "claude"; d["kind"] = cp.Kind; d["sessions"] = cp.Sessions; d["last"] = cp.Last;
                            items.Add(d);
                        }
                    }
                    catch { }
                    SendJson(res, items); return;
                }
                if (path == "/api/sessions") { SendJson(res, Sessions.List(Q(req, "dir"))); return; }
                if (path == "/api/session") { SendJson(res, Sessions.Get(Q(req, "dir"), Q(req, "id"), Q(req, "kind") ?? "team")); return; }
                if (path == "/api/leave" && req.HttpMethod == "GET") { SendJson(res, Team.I.LeavesJson()); return; }
                if (path == "/api/mcp" && req.HttpMethod == "GET") { SendJson(res, McpSetup.Status()); return; }
                if (path == "/migrate")
                {
                    // 엣지 창의 localStorage를 서버로 보내는 한 번용 페이지
                    var html = "<!DOCTYPE html><meta charset=utf-8><script>var d={};for(var i=0;i<localStorage.length;i++){var k=localStorage.key(i);d[k]=localStorage.getItem(k);}"
                             + "fetch('/api/uiprefs',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({all:d,migrated:true})}).then(function(){document.title='ok'});</script>";
                    var b = Encoding.UTF8.GetBytes(html); res.ContentType = "text/html; charset=utf-8"; res.OutputStream.Write(b, 0, b.Length); res.Close(); return;
                }
                if (path == "/api/uiprefs" && req.HttpMethod == "GET")
                {
                    var txt = File.Exists(PrefsFile) ? File.ReadAllText(PrefsFile, Encoding.UTF8) : "{}";
                    var b = Encoding.UTF8.GetBytes(txt); res.ContentType = "application/json; charset=utf-8"; res.OutputStream.Write(b, 0, b.Length); res.Close(); return;
                }
                if (path == "/api/update" && req.HttpMethod == "GET") { SendJson(res, Updater.State()); return; }
                if (path == "/api/version")
                {
                    var d = new Dictionary<string, object>(); d["version"] = AppInfo.Version; d["commit"] = AppInfo.Commit; d["changelog"] = Rules.Resource("CHANGELOG.md");
                    SendJson(res, d); return;
                }
                if (path == "/api/rules")
                {
                    var d = new Dictionary<string, object>(); d["team"] = Rules.TeamPath; d["pc"] = Rules.PcPath; d["dir"] = Rules.Dir; SendJson(res, d); return;
                }
                if (path == "/api/state")
                {
                    Interlocked.Exchange(ref lastPing, Team.Now());
                    long since; long.TryParse(Q(req, "since"), out since);
                    SendJson(res, Team.I.State(since)); return;
                }
                if (req.HttpMethod == "POST")
                {
                    if (path == "/api/clipboard-files")
                    {
                        if (origin != "http://localhost:" + Port || req.ContentType != "application/json")
                        { res.StatusCode = 403; res.Close(); return; }
                        string[] paths = new string[0];
                        if (ui != null) ui.Invoke((Action)(() =>
                        {
                            try { paths = System.Windows.Forms.Clipboard.GetFileDropList().Cast<string>().Take(10).ToArray(); }
                            catch (System.Runtime.InteropServices.ExternalException) { }
                        }));
                        var files = new List<object>();
                        foreach (var file in paths)
                        {
                            try
                            {
                                using (var source = File.OpenRead(file))
                                {
                                    if (source.Length > 20 * 1024 * 1024) continue;
                                    var id = StoreAttachment(Path.GetFileName(file), source);
                                    files.Add(new { id = id, name = Path.GetFileName(file) });
                                }
                            }
                            catch (IOException) { }
                            catch (UnauthorizedAccessException) { }
                        }
                        SendJson(res, new { files = files }); return;
                    }
                    if (path == "/api/avatar")
                    {
                        // 캐릭터 모델 저장 (다음 실행에도 유지)
                        var agent = SafeAgent(Q(req, "agent"));
                        var ext = Path.GetExtension(Q(req, "name") ?? "").ToLowerInvariant();
                        if (agent == null || !new[] { ".glb", ".gltf", ".fbx", ".vrm" }.Contains(ext) || req.ContentLength64 <= 0 || req.ContentLength64 > 60 * 1024 * 1024)
                        { res.StatusCode = 400; SendJson(res, new { error = "GLB·GLTF·FBX·VRM 파일을 60MB까지 넣을 수 있습니다." }); return; }
                        foreach (var f in AvatarFiles(agent)) try { File.Delete(f); } catch { }
                        var dir = Path.Combine(DataDir, "avatars"); Directory.CreateDirectory(dir);
                        using (var dest = File.Create(Path.Combine(dir, agent + ext))) req.InputStream.CopyTo(dest);
                        SendJson(res, new { ok = true }); return;
                    }
                    if (path == "/api/attachment")
                    {
                        if (req.ContentLength64 < 0 || req.ContentLength64 > 20 * 1024 * 1024
                            || req.ContentType != "application/octet-stream")
                        { res.StatusCode = 400; SendJson(res, new { error = "파일은 최대 20MB까지 첨부할 수 있습니다." }); return; }
                        var name = Path.GetFileName(Q(req, "name") ?? "");
                        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name == "." || name == "..")
                        { res.StatusCode = 400; SendJson(res, new { error = "파일 이름이 올바르지 않습니다." }); return; }
                        var id = StoreAttachment(name, req.InputStream);
                        SendJson(res, new { id = id }); return;
                    }
                    string body;
                    using (var r = new StreamReader(req.InputStream, Encoding.UTF8)) body = r.ReadToEnd();
                    var j = Json.Parse(body);
                    if (path == "/api/run")
                    {
                        var wanted = Json.Arr(j, "agents").Select(x => Convert.ToString(x)).ToList();
                        var autoFlag = Json.Str(j, "auto") == "True";
                        var attachmentPaths = new List<string>();
                        foreach (var item in Json.Arr(j, "attachments").Take(10))
                        {
                            Guid id;
                            if (!Guid.TryParseExact(Convert.ToString(item), "N", out id)) continue;
                            var folder = Path.Combine(DataDir, "attachments", id.ToString("N"));
                            if (Directory.Exists(folder)) attachmentPaths.AddRange(Directory.GetFiles(folder));
                        }
                        Team.I.OpenResults = Json.Str(j, "openResults") != "False";
                        var err = Team.I.Start(Json.Str(j, "prompt") ?? "", Json.Str(j, "workdir") ?? "", wanted, Json.Str(j, "leader") ?? "claude", autoFlag, Json.Get(j, "settings"), Json.Str(j, "previous"), Json.Str(j, "mode"), attachmentPaths,
                            Json.Str(j, "remember") != "False", Json.Str(j, "approve") == "True", Json.Str(j, "review"),
                            Json.Str(j, "thinkOnly") != "False", Json.Str(j, "autoEffort") != "False");
                        var d = new Dictionary<string, object>(); d["error"] = err; SendJson(res, d); return;
                    }
                    if (path == "/api/stop") { Team.I.Stop(); SendJson(res, true); return; }
                    if (path == "/api/approve")
                    {
                        var d = new Dictionary<string, object>();
                        var taskId = Json.Str(j, "taskId");
                        d["error"] = taskId != null ? Team.I.DecidePermission(taskId, Json.Str(j, "ok") == "True") : Team.I.Decide(Json.Str(j, "ok") == "True", Json.Str(j, "note"));
                        SendJson(res, d); return;
                    }
                    if (path == "/api/install")
                    {
                        // { agents: ["claude","codex"] } 또는 { agents: ["missing"] } (빠진 것 전부)
                        var list = Team.I.GetAgents(false);
                        var want = Json.Arr(j, "agents").Select(Convert.ToString).ToList();
                        if (want.Contains("missing")) want = list.Where(a => !a.Ready).Select(a => a.Id).ToList();
                        var installed = new HashSet<string>(list.Where(a => a.Path != null).Select(a => a.Id));
                        var d = new Dictionary<string, object>(); d["error"] = want.Count == 0 ? "빠진 AI가 없어요. 모두 준비돼 있어요!" : Installer.Launch(want, installed);
                        SendJson(res, d); return;
                    }
                    if (path == "/api/leave")
                    {
                        var d = new Dictionary<string, object>();
                        d["error"] = Team.I.SetLeave(Json.Str(j, "agent"), Json.Str(j, "kind"), Json.Num(j, "until"), Json.Str(j, "reason"), false);
                        SendJson(res, d); return;
                    }
                    if (path == "/api/elevate")
                    {
                        var d = new Dictionary<string, object>(); d["error"] = Team.I.DecideElevation(Json.Str(j, "ok") == "True"); SendJson(res, d); return;
                    }
                    if (path == "/api/mcp-install")
                    {
                        var d = new Dictionary<string, object>(); d["report"] = Team.I.InstallMcp(); var st = McpSetup.Status(); d["ok"] = st["ready"]; d["status"] = st;
                        SendJson(res, d); return;
                    }
                    if (path == "/api/open-file")
                    {
                        // 규칙 파일과 작업 폴더의 안내서만 메모장으로 연다
                        var p = Json.Str(j, "path") ?? "";
                        var full = Path.GetFullPath(p);
                        var okPath = Rules.IsRulesFile(full) ||
                                     Team.GuideFiles.Concat(new[] { "AGENTS.local.md" }).Contains(Path.GetFileName(full), StringComparer.OrdinalIgnoreCase);
                        if (okPath && File.Exists(full)) Process.Start("notepad.exe", "\"" + full + "\"");
                        SendJson(res, new { ok = okPath }); return;
                    }
                    if (path == "/api/avatar-clear")
                    {
                        var agent = SafeAgent(Q(req, "agent"));
                        if (agent != null) foreach (var f in AvatarFiles(agent)) try { File.Delete(f); } catch { }
                        SendJson(res, true); return;
                    }
                    if (path == "/api/update-check") { Updater.Check(); SendJson(res, Updater.State()); return; }
                    if (path == "/api/update")
                    {
                        // 일하는 중에는 바꾸지 않는다 (하던 일이 끊기지 않게)
                        if (Team.I.Busy) { SendJson(res, new { ok = false, error = "팀이 일하는 중이에요. 끝난 뒤에 업데이트해 주세요." }); return; }
                        var uerr = Updater.Install();
                        SendJson(res, new { ok = uerr == null, error = uerr });
                        if (uerr == null)
                            new Thread(() => { Thread.Sleep(1200); CloseAppWindows(); if (ui != null) ui.BeginInvoke((Action)(() => { tray.Visible = false; Application.Exit(); })); }) { IsBackground = true }.Start();
                        return;
                    }
                    if (path == "/api/uiprefs")
                    {
                        // 화면 설정(localStorage) 백업: 창 프로그램(크롬/엣지)을 바꾸거나 다시 설치해도 그대로
                        var all = Json.Get(j, "all");
                        if (all != null) File.WriteAllText(PrefsFile, Json.Stringify(all), new UTF8Encoding(false));
                        SendJson(res, true); return;
                    }
                    if (path == "/api/projects")
                    {
                        var clean = Json.Arr(j, "projects").Select(p => (object)new Dictionary<string, object> { { "name", Json.Str(p, "name") }, { "dir", Json.Str(p, "dir") } }).ToList();
                        File.WriteAllText(Path.Combine(DataDir, "projects.json"), Json.Stringify(clean), new UTF8Encoding(false));
                        SendJson(res, true); return;
                    }
                    if (path == "/api/pickfolder")
                    {
                        var d = new Dictionary<string, object>(); d["path"] = PickFolder(Json.Str(j, "start")); SendJson(res, d); return;
                    }
                    if (path == "/api/open")
                    {
                        var p = Json.Str(j, "path");
                        if (p != null && Directory.Exists(p)) Process.Start("explorer.exe", "\"" + p + "\"");
                        SendJson(res, true); return;
                    }
                    if (path == "/api/terminal")
                    {
                        // 설치/로그인 명령을 사용자가 직접 실행할 수 있게 PowerShell 창을 연다 (명령은 입력만 해 두고 실행은 사용자가)
                        var cmd = Json.Str(j, "cmd") ?? "";
                        Clipboard(cmd);
                        var ps1 = Path.Combine(DataDir, "help.ps1");
                        File.WriteAllText(ps1, "Write-Host '아래 명령이 클립보드에 복사됐어요. 이 창에 붙여넣고(마우스 오른쪽 클릭) Enter를 누르세요.' -ForegroundColor Cyan\r\nWrite-Host ''\r\nWrite-Host '" + cmd.Replace("'", "''") + "' -ForegroundColor Yellow\r\nWrite-Host ''\r\n", new UTF8Encoding(true));
                        Process.Start(new ProcessStartInfo("powershell.exe", "-NoExit -ExecutionPolicy Bypass -File \"" + ps1 + "\"") { UseShellExecute = true });
                        SendJson(res, true); return;
                    }
                }
                if (path.StartsWith("/ws/"))
                {
                    // 작업 폴더의 결과물 미리보기
                    var st = Team.I.State(long.MaxValue);
                    var wd = st["workdir"] as string;
                    if (wd != null)
                    {
                        var rel = Uri.UnescapeDataString(path.Substring(4)).Replace('/', '\\');
                        var full = Path.GetFullPath(Path.Combine(wd, rel));
                        if (full.StartsWith(Path.GetFullPath(wd), StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                        {
                            SendBytes(res, File.ReadAllBytes(full), Mime(full)); return;
                        }
                    }
                }
                res.StatusCode = 404; res.Close();
            }
            catch (Exception ex)
            {
                try { res.StatusCode = 500; SendBytes(res, Encoding.UTF8.GetBytes(ex.Message), "text/plain; charset=utf-8"); } catch { }
            }
        }

        static string SafeAgent(string a) { return Agents.Order.Contains(a) ? a : null; }

        static IEnumerable<string> AvatarFiles(string agent)
        {
            var dir = Path.Combine(DataDir, "avatars");
            if (!Directory.Exists(dir)) return new string[0];
            return Directory.GetFiles(dir, agent + ".*");
        }

        static string StoreAttachment(string name, Stream source)
        {
            var id = Guid.NewGuid().ToString("N");
            var folder = Path.Combine(DataDir, "attachments", id);
            Directory.CreateDirectory(folder);
            using (var dest = File.Open(Path.Combine(folder, name), FileMode.CreateNew)) source.CopyTo(dest);
            return id;
        }

        static void Clipboard(string text)
        {
            ui.Invoke((Action)(() => { try { System.Windows.Forms.Clipboard.SetText(text); } catch { } }));
        }

        static string Mime(string f)
        {
            switch (Path.GetExtension(f).ToLowerInvariant())
            {
                case ".html": case ".htm": return "text/html; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".js": case ".mjs": return "text/javascript; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".mp3": return "audio/mpeg";
                case ".wav": return "audio/wav";
                case ".txt": case ".md": return "text/plain; charset=utf-8";
            }
            return "application/octet-stream";
        }

        static void SendJson(HttpListenerResponse res, object o)
        {
            SendBytes(res, Encoding.UTF8.GetBytes(Json.Stringify(o)), "application/json; charset=utf-8");
        }

        static void SendResource(HttpListenerResponse res, string name, string type)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream(name))
            using (var m = new MemoryStream()) { s.CopyTo(m); SendBytes(res, m.ToArray(), type); }
        }

        static void SendBytes(HttpListenerResponse res, byte[] data, string type)
        {
            res.ContentType = type;
            res.Headers["Cache-Control"] = "no-store";
            res.ContentLength64 = data.Length;
            res.OutputStream.Write(data, 0, data.Length);
            res.Close();
        }
    }
}
