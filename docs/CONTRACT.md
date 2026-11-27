# AI 팀 사무실 — 화면 ↔ 서버 ↔ 3D 사무실 약속 (작업 분담용)

파일 담당 (서로의 파일은 고치지 않는다)
- 서버(C#): `src/*.cs` — 메인 세션
- 3D 사무실: `src/office.js` — 에이전트 A
- 화면: `src/ui.html` — 에이전트 B
빌드: `powershell -ExecutionPolicy Bypass -File D:\Projects\ai-team\build.ps1 -OutputPath <임시경로>\check.exe`
(실행 중인 ..\AI팀사무실.exe를 덮어쓰지 않게 -OutputPath로 임시 경로를 줄 것. office.js는 esbuild로 묶여 exe에 들어간다.)

## 1. GET /api/state?since=N (기존 + 추가)
```
phase: idle|planning|approval|working|review|summary|elevation|done|stopped|failed
leader: 현재 팀장 id (작업 중 바뀔 수 있음 — 팀장 교대 시 이벤트 kind "leader")
elevation: null | { taskId, agent, what, detail }   // phase=="elevation"일 때 관리자 권한 요청 모달을 띄운다
mcp: { ready: bool, missing: ["hwp", ...], report: "문장" }
agentState[id]: {
  busy, task, taskTitle, current, thought, concern, done, total, role,
  leave: null | { kind: "vacation"|"halfday"|"early"|"rest", reason: "사람 말투 사유", until: ms|0, auto: bool },
  fatigue: 0~100,          // 이번 작업에서 많이 일할수록 올라감. 80 이상 + 대기 중이면 수면실에서 잔다(rest)
  quotaOut: bool,          // 사용량 바닥
  mood: "busy"|"idle"|"away"
}
events[]: { seq, ts, agent, kind, text, detail?, task? }
  kind 추가: "leader"(팀장 교대), "leave"(휴가·복귀), "elevation"(관리자 권한 요청/결과)
tasks[]: 기존 + effort, effortWhy, prevOwner, denials[], attempts
```

## 2. GET /api/usage (형식 통일)
```
{
  claude: { windows: [ { label: "5시간", used: 0~100, resetsAt: ms }, { label: "주간", used, resetsAt } ], plan, seen: ms },
  codex:  { windows: [...같은 형식...], plan, seen },
  gemini: { windows: [ { label: "Gemini 주간", used, resetsAt }, { label: "Claude·GPT 주간", used, resetsAt } ], seen },
  tokens: { claude: { in, out }, codex: {...}, gemini: {...} }   // 이번 작업
}
```
값을 못 얻은 창은 생략한다. used는 '사용한 %'(남은 % = 100-used).

## 3. 휴가·반차·조기퇴근·휴식
- GET /api/leave → `{ claude: leave|null, codex: ..., gemini: ... }` (leave 형식은 위와 같음)
- POST /api/leave `{ agent, kind: "vacation"|"halfday"|"early"|"rest"|"back", until?: ms, reason?: string }`
  - vacation/halfday/early: 그 시각까지 팀에서 빠짐 (until 없으면 서버가 사용량 초기화 시각으로 채움)
  - rest: 수면실에서 쉬게 함 (작업 배정은 계속 가능, 일 생기면 깨어나서 자리로)
  - back: 복귀
- 서버가 자동으로 거는 경우(auto=true): 사용량 바닥 → 초기화 시각까지 휴가/반차/조기퇴근(남은 시간에 따라), 과로(fatigue≥80) → 대기 중일 때 rest

## 4-0. (변경) 세션 규칙 — 2026-11-25
- 모든 작업(run)에는 `sessionId`가 있다. GET /api/state 의 `state.sessionId` = 지금 작업의 세션.
- 새 지시(이어서가 아님) = **새 대화**: POST /api/run 에 `previous`를 보내지 않는다 → 서버가 새 sessionId를 만들고, 이전 기록을 전혀 기억하지 않는다.
- 세션을 이어서 지시 = POST /api/run `{ previous: 그 세션의 lastRun, remember: true, ... }` → 서버는 **그 세션 안의 작업들만** 기억한다(다른 세션 내용은 섞지 않음).
- GET /api/sessions 는 **오래된 것 → 최신 순(최신이 맨 아래)**. team 세션에 `lastRun`(이어서 지시할 때 previous로 보낼 runId)과 `runs[]`가 있다.
- GET /api/session?id=<sessionId>&kind=team → messages 오름차순. 진행 중인 작업이 그 세션이면(state.sessionId == id) 화면은 state.events 를 실시간으로 이어 붙인다.

## 4-1. (추가) 대체 모델 표시
- agentState[id]: `activeModel`, `activeModelName`, `modelNote`(대체 중이면 "Gemini 한도를 다 써서 Claude Sonnet 4.6로 대신 일해요"), `activeGroup`("gemini"|"thirdparty"|null)
- GET /api/usage: 각 `windows[]`에 `active: bool`(지금 쓰는 모델이 쓰는 한도면 true), 각 에이전트에 `activeModel: {model, name, note, group}`.

## 4. 업무 이력 = 세션
- GET /api/sessions?dir=작업폴더 →
  `[ { id, kind: "team"|"claude", title, startTs, lastTs, status, count } ]` 최신순
  - team: 이어서 지시로 연결된 AI 팀 작업 묶음(같은 폴더의 작업들을 시간순으로 한 세션으로)
  - claude: 이 PC의 Claude Code 대화 중 작업 폴더가 같은 것(사용자와 클로드가 나눈 대화)
- GET /api/session?dir=&id=&kind= →
  `{ id, kind, title, messages: [ { ts, role: "user"|"agent"|"system", agent?, text, kind?, runId? } ], runs: [runId...] }`
  - team 세션: 사용자 요청(user) → 팀원들의 말(agent, 자연어 이벤트만) → 최종 보고(agent, kind "report")
  - claude 세션: 사용자/클로드 메시지 텍스트만 (도구 호출 제외)
- (기존) GET /api/runs, /api/runlog 는 그대로 둔다.

## 5. MCP
- GET /api/mcp → `{ ready, missing: [...], python: bool, report }`
- POST /api/mcp-install → 설치·등록 후 `{ ok, report }` (오래 걸릴 수 있음 → 화면은 진행 표시)

## 6. 관리자 권한
- phase=="elevation"이면 화면은 모달: "관리자 권한이 필요해요: {what}" + [관리자 권한으로 이어서 하기] [이 일은 건너뛰기]
- POST /api/elevate `{ ok: true|false }` → ok면 서버가 UAC 창을 띄워 관리자 권한으로 프로그램을 다시 열고 같은 작업을 이어서 한다(화면은 새로 열린 창으로 넘어감).

## 7. 규칙 파일
- GET /api/rules → `{ team: 경로, pc: 경로 }` ; POST /api/open-file `{ path }` (메모장으로 열기)

## 8. 3D 사무실 API (office.js가 제공, ui.html이 호출)
- `window.updateOfficeScene(agentState, events, state, extra)` — 기존. agentState[id].leave / fatigue / quotaOut / busy 를 보고 행동을 정한다.
- `window.officeAPI` 기존 기능 유지(getProfile, setProfile, resetProfile, uploadModel, clips, hasModel, clearModel, room, setRoom, view, firstPerson, lounge, isLounge, xray, portrait, select, defaults, agents)
- 추가: `officeAPI.setTheme(name)` — 화면 테마(dark|light|warm|navy|forest)에 맞춰 사무실 분위기(조명·바닥·벽)를 바꾼다.
- 추가: `officeAPI.restRoom()` — 카메라를 수면실로.
- `window.openOfficeCard(id)` — ui.html이 제공 (캐릭터 클릭 시 신분증)
