using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AITeam
{
    // 규칙 파일 두 종류
    //  - 팀규칙.md : 어느 PC에서나 같은 규칙 (프로그램에 들어 있다가 처음 실행할 때 꺼내짐)
    //  - 이PC메모.md : 이 PC에만 해당하는 메모 (빈 양식으로 만들어짐, 다른 PC로 가지 않음)
    static class Rules
    {
        // 팀규칙.md  : 프로그램 폴더의 '규칙' 폴더 (프로그램과 함께 다니는 규칙). 쓸 수 없는 위치면 문서 폴더로
        // 이PC메모.md: 내 문서\AI팀사무실 (이 PC에만 해당, 프로그램을 업데이트·이동해도 그대로)
        static readonly string ExeDir = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
        public static readonly string DocDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AI팀사무실");
        static readonly string OldDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AITeam", "rules");
        static string teamDir;
        public static string Dir { get { return TeamDir; } }
        static string TeamDir
        {
            get
            {
                if (teamDir != null) return teamDir;
                var d = Path.Combine(ExeDir, "규칙");
                try { Directory.CreateDirectory(d); var probe = Path.Combine(d, ".w"); File.WriteAllText(probe, ""); File.Delete(probe); teamDir = d; }
                catch { teamDir = DocDir; }
                return teamDir;
            }
        }
        public static string TeamPath { get { return Path.Combine(TeamDir, "팀규칙.md"); } }
        public static string PcPath { get { return Path.Combine(DocDir, "이PC메모.md"); } }
        public static bool IsRulesFile(string full)
        {
            return full.Equals(Path.GetFullPath(TeamPath), StringComparison.OrdinalIgnoreCase) || full.Equals(Path.GetFullPath(PcPath), StringComparison.OrdinalIgnoreCase);
        }

        public static void Ensure()
        {
            try
            {
                Directory.CreateDirectory(DocDir);
                // 예전 위치(%LOCALAPPDATA%\AITeam\rules)의 이PC메모는 문서 폴더로 옮긴다
                var oldPc = Path.Combine(OldDir, "이PC메모.md");
                if (!File.Exists(PcPath) && File.Exists(oldPc)) File.Copy(oldPc, PcPath);
                if (!File.Exists(TeamPath)) File.WriteAllText(TeamPath, Resource("rules_team.md"), new UTF8Encoding(true));
                if (!File.Exists(PcPath)) File.WriteAllText(PcPath, Resource("rules_pc.md"), new UTF8Encoding(true));
            }
            catch { }
        }

        public static string Resource(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream(name))
            {
                if (s == null) return "";
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        static string Read(string path, int max)
        {
            try
            {
                if (!File.Exists(path)) return "";
                var t = File.ReadAllText(path, Encoding.UTF8).Trim();
                return t.Length > max ? t.Substring(0, max) + "\n…(이하 생략)" : t;
            }
            catch { return ""; }
        }

        public static string TeamText() { Ensure(); return Read(TeamPath, 8000); }

        // 양식만 있고 내용이 없으면 보내지 않는다 (토큰 절약)
        public static string PcText()
        {
            Ensure();
            var t = Read(PcPath, 4000);
            var meaningful = t.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("- ") && l.Length > 3);
            return meaningful.Any() ? t : "";
        }
    }
}
