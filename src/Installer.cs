using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace AITeam
{
    // 클릭 한 번 설치: 설명이 적힌 PowerShell 창을 열어 공식 설치 → 로그인까지 차례로 진행
    static class Installer
    {
        static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AITeam");

        static string Q(string s) { return "'" + s.Replace("'", "''") + "'"; }

        static string Header(string title, string why)
        {
            return "Write-Host ''\r\nWrite-Host '==================================================' -ForegroundColor DarkCyan\r\n" +
                   "Write-Host " + Q("  " + title) + " -ForegroundColor Cyan\r\n" +
                   "Write-Host '==================================================' -ForegroundColor DarkCyan\r\n" +
                   string.Join("", why.Split('\n').Select(l => "Write-Host " + Q("  " + l) + " -ForegroundColor Gray\r\n").ToArray()) +
                   "Write-Host ''\r\n";
        }

        static string Step(string text) { return "Write-Host " + Q("▶ " + text) + " -ForegroundColor Yellow\r\n"; }

        // 새로 설치한 프로그램이 바로 보이도록 PATH 다시 읽기
        const string RefreshPath = "$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')\r\n";

        static string Part(string id, bool installed)
        {
            var sb = new StringBuilder();
            switch (id)
            {
                case "claude":
                    sb.Append(Header("클로드 (Claude Code)", "팀장 역할을 맡는 AI예요. 요청을 해석하고 일을 나누고 검토해요.\nClaude Pro 이상 구독 계정이 필요해요. 설치는 Anthropic 공식 설치 스크립트로 해요."));
                    if (!installed)
                    {
                        sb.Append(Step("1/2 설치하는 중이에요 (공식 스크립트: https://claude.ai/install.ps1)"));
                        sb.Append("irm https://claude.ai/install.ps1 | iex\r\n" + RefreshPath);
                    }
                    sb.Append(Step((installed ? "" : "2/2 ") + "로그인할게요. 잠시 뒤 브라우저가 열리면 Claude 계정으로 로그인하세요."));
                    sb.Append(Step("로그인이 끝나 대화 화면이 나오면 /exit 를 입력해 닫으면 돼요."));
                    sb.Append("$c = Join-Path $env:USERPROFILE '.local\\bin\\claude.exe'; if (-not (Test-Path $c)) { $c = 'claude' }\r\n& $c\r\n");
                    break;
                case "codex":
                    sb.Append(Header("코덱스 (OpenAI Codex CLI)", "지시서대로 구현·수정·테스트를 정확히 해내는 실행 담당 AI예요.\nChatGPT Plus 이상 구독 계정이 필요해요. Codex는 Node.js 위에서 돌아가서, 없으면 Node.js부터 깔아요."));
                    if (!installed)
                    {
                        sb.Append(Step("1/3 Node.js 확인 (없으면 Microsoft winget으로 공식 LTS 버전 설치)"));
                        sb.Append("if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { winget install -e --id OpenJS.NodeJS.LTS --accept-source-agreements --accept-package-agreements; " + RefreshPath.Trim() + " } else { Write-Host '  Node.js는 이미 있어요.' -ForegroundColor Green }\r\n");
                        sb.Append(Step("2/3 Codex CLI 설치 (npm 공식 패키지 @openai/codex)"));
                        sb.Append("npm install -g @openai/codex\r\n" + RefreshPath);
                    }
                    sb.Append(Step((installed ? "" : "3/3 ") + "로그인할게요. 브라우저가 열리면 ChatGPT 계정으로 로그인하세요."));
                    sb.Append("$x = Join-Path $env:APPDATA 'npm\\codex.cmd'; if (-not (Test-Path $x)) { $x = 'codex' }\r\n& $x login\r\n");
                    break;
                case "gemini":
                    sb.Append(Header("제미나이 (Google Antigravity CLI · agy)", "자료 모으기·개수 세기·형식 정리 같은 쉽고 양 많은 일을 맡는 AI예요.\n구글 계정이 필요해요. 설치는 구글 공식 설치 스크립트로 해요.\n※ 구글 공식 FAQ상 개인(무료) 계정을 다른 앱에서 자동 실행하는 건 지원 대상이 아니에요. 사용 여부는 직접 판단하세요."));
                    if (!installed)
                    {
                        sb.Append(Step("1/2 설치하는 중이에요 (공식 스크립트: https://antigravity.google/cli/install.ps1)"));
                        sb.Append("irm https://antigravity.google/cli/install.ps1 | iex\r\n" + RefreshPath);
                    }
                    sb.Append(Step((installed ? "" : "2/2 ") + "로그인할게요. '1. Google OAuth'를 고르고 브라우저에서 로그인하세요. 끝나면 /exit 로 닫아요."));
                    sb.Append("$g = Join-Path $env:LOCALAPPDATA 'agy\\bin\\agy.exe'; if (-not (Test-Path $g)) { $g = 'agy' }\r\n& $g\r\n");
                    break;
            }
            return sb.ToString();
        }

        // ids: 설치·로그인할 AI 목록. installed: 이미 설치된 AI (로그인만)
        public static string Launch(List<string> ids, HashSet<string> installed)
        {
            ids = ids.Where(i => Agents.Order.Contains(i)).ToList();
            if (ids.Count == 0) return "설치할 AI를 골라 주세요.";
            Directory.CreateDirectory(DataDir);
            var sb = new StringBuilder();
            sb.Append("$ErrorActionPreference = 'Continue'\r\n");
            sb.Append("Write-Host 'AI 팀 사무실 — 팀원 설치 도우미' -ForegroundColor White\r\n");
            sb.Append("Write-Host '이 창은 공식 설치 명령을 차례로 실행해요. 중간에 묻는 창이 뜨면 안내대로 진행해 주세요.' -ForegroundColor Gray\r\n");
            foreach (var id in ids) sb.Append(Part(id, installed.Contains(id)));
            sb.Append("Write-Host ''\r\nWrite-Host '모두 끝났어요! 이 창을 닫고 AI 팀 사무실의 [다시 확인]을 누르세요.' -ForegroundColor Green\r\n");
            var ps1 = Path.Combine(DataDir, "install-" + string.Join("-", ids.ToArray()) + ".ps1");
            File.WriteAllText(ps1, sb.ToString(), new UTF8Encoding(true));
            Process.Start(new ProcessStartInfo("powershell.exe", "-NoExit -ExecutionPolicy Bypass -File \"" + ps1 + "\"") { UseShellExecute = true });
            return null;
        }
    }
}
