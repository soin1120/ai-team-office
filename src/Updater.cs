using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace AITeam
{
    // 업데이트: 제작자가 GitHub 릴리스(soin1120/ai-team-office)에 새 버전을 올리면
    // 화면의 [업데이트] 버튼이 빨간불로 바뀌고, 누르면 받아서 교체한 뒤 다시 켠다.
    static class Updater
    {
        public const string Repo = "soin1120/ai-team-office";
        static readonly object sync = new object();
        static string latest, notes, zipUrl, error;
        static long checkedAt;
        public static string Status = "idle"; // idle | checking | downloading | installing | error
        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AITeam", "update");

        public static bool Newer { get { lock (sync) return latest != null && Compare(latest, AppInfo.Version) > 0; } }

        public static Dictionary<string, object> State()
        {
            var d = new Dictionary<string, object>();
            lock (sync)
            {
                d["current"] = AppInfo.Version; d["latest"] = latest; d["newer"] = latest != null && Compare(latest, AppInfo.Version) > 0;
                d["notes"] = notes; d["checkedAt"] = checkedAt; d["error"] = error; d["status"] = Status; d["repo"] = Repo;
            }
            return d;
        }

        public static int Compare(string a, string b)
        {
            Func<string, int[]> parse = v => (v ?? "0").TrimStart('v', 'V').Split('.', '-', '+').Take(4).Select(x => { int n; return int.TryParse(x, out n) ? n : 0; }).ToArray();
            var x1 = parse(a); var x2 = parse(b);
            for (int i = 0; i < Math.Max(x1.Length, x2.Length); i++)
            {
                int p = i < x1.Length ? x1[i] : 0, q = i < x2.Length ? x2[i] : 0;
                if (p != q) return p.CompareTo(q);
            }
            return 0;
        }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            var w = new WebClient { Encoding = Encoding.UTF8 };
            w.Headers[HttpRequestHeader.UserAgent] = "AITeamOffice-Updater";
            w.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return w;
        }

        public static void Check()
        {
            lock (sync) { Status = "checking"; }
            try
            {
                using (var w = Client())
                {
                    var j = Json.Parse(w.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest"));
                    var tag = Json.Str(j, "tag_name");
                    string url = null;
                    foreach (var a in Json.Arr(j, "assets"))
                    {
                        var n = Json.Str(a, "name") ?? "";
                        if (n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { url = Json.Str(a, "browser_download_url"); break; }
                    }
                    lock (sync) { latest = tag != null ? tag.TrimStart('v', 'V') : null; notes = Json.Str(j, "body"); zipUrl = url; error = url == null && tag != null ? "새 버전에 내려받을 파일이 없어요" : null; }
                }
            }
            catch (WebException ex)
            {
                var r = ex.Response as HttpWebResponse;
                lock (sync) error = r != null && r.StatusCode == HttpStatusCode.NotFound ? "아직 올라온 버전이 없어요" : "업데이트 서버에 연결하지 못했어요";
            }
            catch (Exception ex) { lock (sync) error = "확인 실패: " + ex.Message; }
            lock (sync) { checkedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds(); if (Status == "checking") Status = "idle"; }
        }

        public static void StartChecker()
        {
            new Thread(() => { Thread.Sleep(5000); while (true) { Check(); Thread.Sleep(TimeSpan.FromHours(3)); } }) { IsBackground = true }.Start();
        }

        // 새 버전을 받아 풀고, 이 프로그램이 꺼지면 파일을 바꿔 끼운 뒤 다시 켜는 스크립트를 띄운다
        public static string Install()
        {
            string url, ver;
            lock (sync) { url = zipUrl; ver = latest; }
            if (url == null || ver == null) return "받을 새 버전이 없어요";
            if (Compare(ver, AppInfo.Version) <= 0) return "이미 최신 버전이에요";
            try
            {
                lock (sync) Status = "downloading";
                Directory.CreateDirectory(Dir);
                var zip = Path.Combine(Dir, "v" + ver + ".zip");
                var outDir = Path.Combine(Dir, "v" + ver);
                using (var w = Client()) w.DownloadFile(url, zip);
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
                System.IO.Compression.ZipFile.ExtractToDirectory(zip, outDir);
                // zip 안의 AI팀사무실\ 폴더(또는 바로 아래)에서 새 exe 찾기
                var newExe = Directory.GetFiles(outDir, "*.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (newExe == null) { lock (sync) Status = "error"; return "받은 파일에서 프로그램을 찾지 못했어요"; }
                var src = Path.GetDirectoryName(newExe);
                var exe = System.Windows.Forms.Application.ExecutablePath;
                var app = Path.GetDirectoryName(exe);
                // 팀규칙.md: 사용자가 고치지 않았을 때만 새 것으로 (고쳤으면 팀규칙.새버전.md로 옆에 둔다)
                var teamNow = Path.Combine(app, "규칙", "팀규칙.md");
                bool userEdited = false;
                try { userEdited = File.Exists(teamNow) && File.ReadAllText(teamNow, Encoding.UTF8).Trim() != Rules.Resource("rules_team.md").Trim(); } catch { }
                var ps1 = Path.Combine(Dir, "apply-v" + ver + ".ps1");
                var s = new StringBuilder();
                s.AppendLine("# AI 팀 사무실 업데이트 v" + AppInfo.Version + " -> v" + ver);
                s.AppendLine("$ErrorActionPreference = 'Stop'");
                s.AppendLine("$pidOld = " + Process.GetCurrentProcess().Id);
                s.AppendLine("for ($i = 0; $i -lt 60; $i++) { if (-not (Get-Process -Id $pidOld -ErrorAction SilentlyContinue)) { break }; Start-Sleep -Milliseconds 500 }");
                s.AppendLine("$src = '" + src.Replace("'", "''") + "'; $app = '" + app.Replace("'", "''") + "'; $exe = '" + exe.Replace("'", "''") + "'");
                s.AppendLine("try {");
                s.AppendLine("  Copy-Item $exe ($exe + '.bak') -Force");
                s.AppendLine("  Copy-Item (Join-Path $src (Split-Path $exe -Leaf)) $exe -Force -ErrorAction SilentlyContinue");
                s.AppendLine("  if (-not (Test-Path (Join-Path $src (Split-Path $exe -Leaf)))) { Copy-Item '" + newExe.Replace("'", "''") + "' $exe -Force }");
                s.AppendLine("  if (Test-Path (Join-Path $src 'README.txt')) { Copy-Item (Join-Path $src 'README.txt') (Join-Path $app 'README.txt') -Force }");
                s.AppendLine("  $rules = Join-Path $src '규칙\\팀규칙.md'");
                s.AppendLine("  if (Test-Path $rules) { New-Item -ItemType Directory -Force (Join-Path $app '규칙') | Out-Null; " +
                             (userEdited ? "Copy-Item $rules (Join-Path $app '규칙\\팀규칙.새버전.md') -Force" : "Copy-Item $rules (Join-Path $app '규칙\\팀규칙.md') -Force") + " }");
                s.AppendLine("} catch {");
                s.AppendLine("  # 프로그램 폴더에 쓸 권한이 없으면 관리자 권한으로 한 번 더");
                s.AppendLine("  if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {");
                s.AppendLine("    Start-Process powershell -Verb RunAs -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',\"`\"$PSCommandPath`\"\"); exit }");
                s.AppendLine("  Copy-Item ($exe + '.bak') $exe -Force; [void][System.Reflection.Assembly]::LoadWithPartialName('System.Windows.Forms'); [System.Windows.Forms.MessageBox]::Show('업데이트하지 못했어요: ' + $_.Exception.Message, 'AI 팀 사무실') | Out-Null");
                s.AppendLine("}");
                s.AppendLine("Start-Process $exe -WorkingDirectory $app");
                File.WriteAllText(ps1, s.ToString(), new UTF8Encoding(true));
                lock (sync) Status = "installing";
                Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ps1 + "\"") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
                return null;
            }
            catch (Exception ex) { lock (sync) { Status = "error"; error = "업데이트 실패: " + ex.Message; } return "업데이트 실패: " + ex.Message; }
        }
    }
}
