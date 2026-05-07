# DevSprint

> A Windows desktop companion that brings together your team's work across Jira,
> GitHub, and Confluence — designed to help the right people connect on the right
> work item, at the right time.

DevSprint is an open lab project exploring how a single, focused desktop app can
unify the **work** (Jira backlog and sprints), the **code** (GitHub branches and
pull requests), and the **knowledge** (Confluence pages) that already exist
across your organisation — and surface the **people** behind each of those
artefacts so collaboration becomes a one-click affair instead of a half-hour
context-switching expedition.

It is built as a Windows 11 native app on WPF, talks to its data sources through
proper OAuth 2.0 flows (no long-lived API tokens), and stores every secret on
disk DPAPI-encrypted under your Windows account. Every piece of personal or
organisational configuration lives in a setup wizard the user can revisit at any
time — there are no hard-coded URLs, accounts, or repository names anywhere in
the codebase.

---

## Vision

The long-term goal is a **collaborative AI assistant** that helps teams pick the
right people to involve in the right work, grounded in the trail of evidence
they leave across their tooling: who's authored which Confluence pages, who's
reviewed which pull requests, who's been assigned which tickets, who's been
quoted in which discussions. We're not there yet — what exists today is the
careful data-aggregation foundation that such an assistant would need.

Three concrete directions this project is heading:

1. **People-centred recommendation.** Given a backlog item, suggest the people
   on the team most likely to have context — measured first by raw signal (touch
   counts on related Confluence pages, prior work on adjacent tickets), and
   later by semantic similarity on AI-generated expertise profiles.
2. **Cross-platform coverage.** Confluence + Jira + GitHub today; **Azure
   DevOps** integration is on the roadmap, alongside other code hosting and
   knowledge-management platforms.
3. **Local-first, privacy-respecting.** No cloud backend, no shared service.
   Tokens and configuration encrypted on disk, tied to the Windows user account.
   When AI features land, the design will keep sensitive content out of remote
   inference calls by default, with optional opt-in to managed LLM APIs.

---

## What it does today

DevSprint is a working desktop app with these features end-to-end. None of it
requires a backend service to be deployed.

### Backlog management (Jira)

- **Backlog tab** — full Atlassian backlog with infinite-scroll paging,
  per-tab in-memory search (kicks in at 3+ characters, matches issue key or
  title), and clickable state badges that filter the list (Total / Refined /
  In Analysis).
- **Sprints tab** — quarter-aware sprint dropdown listing active and recently
  closed sprints, story-point totals, status breakdown, and the same in-memory
  search as the backlog.
- **Assigned to me / Contributing to** — focused views of the work the current
  user owns or is involved in.
- **Issue detail sidebar** — click any work item to open a side panel with
  three tabs (People / Knowledge / Code).

### Code repository (GitHub)

- **Branch and PR discovery per issue** — for the selected work item, scans
  configured repositories for branches and pull requests whose title or branch
  name references the issue key.
- **Contributor surfacing** — extracts PR authors, assignees, and requested
  reviewers; resolves their GitHub profile info on demand and caches it for
  the session.

### Knowledge (Confluence)

- **Knowledge tab in the issue sidebar** — runs a CQL search combining the
  issue key and significant keywords from the issue title, filtered to
  recent pages and blog posts. Each match shows the page title, space,
  last-modified date, original author, last editor, and a small italic
  caption explaining *why* the page surfaced ("Mentions ENG-456" vs "Title
  matches: oauth, login").

### People view (cross-source unification)

- **People tab in the issue sidebar** — every individual involved with the
  selected work item, de-duplicated across Jira / GitHub / Confluence sources.
- Each row carries one or more **role tags**: a blue *"Team member"* tag for
  people surfaced by Jira or GitHub, a grey *"Knowledge contributor"* tag for
  Confluence authors and editors. People who appear in multiple sources show
  both tags.
- Identity matching uses Atlassian account ID first (which is shared between
  Jira and Confluence on the same site), then GitHub username, then email,
  then display name.
- **Selectable group chat** — check the people you want, click *"Start group
  chat with selected"*, and DevSprint launches Microsoft Teams with the
  selected emails pre-populated and a pre-filled topic + initial message
  containing the issue key, summary, and a link back to Jira.
- **Manual identity linking** — when a GitHub-only contributor doesn't auto-
  match an Atlassian person (different name, no email visible in the GitHub
  profile), a one-click flow lets you link them to a known Jira teammate so
  future work items match them correctly.

### First-run experience

- **Onboarding wizard** with two areas — *Backlog Management* (Atlassian site,
  email, board ID, project key, OAuth client id + secret + callback port) and
  *Code Repository* (GitHub username, organisation, repos, OAuth client id).
  All values are stored DPAPI-encrypted at `%AppData%\TeamHub\config.dat`,
  tied to the Windows user account.
- **Reconfigure command** in the app header lets the user revisit the wizard
  any time without dropping their cached OAuth tokens.

---

## Architecture overview

A single .NET 10 WPF project (`DevSprint.UI`) following strict MVVM with
[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) source
generators (`[ObservableProperty]`, `[RelayCommand]`).

```
DevSprint.UI/
├── Auth/                 OAuth flows + DPAPI-encrypted token store
│   ├── GitHub/           Device Flow against github.com
│   ├── Jira/             OAuth 2.0 (3LO) with PKCE against auth.atlassian.com
│   └── Confluence/       Reuses the Atlassian token, routes to /ex/confluence/
├── Onboarding/           First-run wizard + DPAPI-encrypted config store
├── Services/             API clients (JiraService, GitHubService, ConfluenceService)
├── Models/               Plain data classes (no UI dependencies)
├── ViewModels/           ObservableObject-derived VMs with RelayCommands
├── Views/                Window-level dialogs (DeviceCodeDialog, JiraSignInDialog, OnboardingWizardDialog)
├── Behaviors/            Attached behaviours (PasswordBoxHelper, ScrollEndBehavior)
├── Converters/           IValueConverter implementations (Bool↔Visibility, etc.)
├── Themes/               Fluent-style resource dictionary (Win11 look)
├── MainWindow.xaml       Application shell with header, tabs, issue sidebar
└── App.xaml.cs           DI graph + eager OAuth sign-in before main window
```

Some design notes worth knowing:

- **DI via `Microsoft.Extensions.DependencyInjection`.** Every service is
  registered in `App.xaml.cs::ConfigureAuth` and resolved via constructor
  injection. There is no service locator pattern.
- **Typed HttpClients with delegating handlers.** `JiraService`,
  `GitHubService`, and `ConfluenceService` each use
  `IHttpClientFactory`-managed clients with two stacked handlers:
  - A `BearerTokenHandler` subclass that calls the relevant auth service for a
    fresh access token on every request and stamps the `Authorization` header.
  - A `BaseUriHandler` (Jira and Confluence) that rewrites the request path to
    the Atlassian gateway form `/ex/{product}/{cloudId}/...` so the API
    services don't have to know about cloud-id routing.
- **Atlassian auth is multi-product.** A single OAuth flow issues one access
  token; both `JiraService` and `ConfluenceService` consume it. Confluence
  was added without any change to the sign-in path.
- **Single project, no shared library yet.** Everything is in
  `DevSprint.UI` because the codebase is small and the abstractions are still
  settling. Moving the Auth / Services / Models layers into separate
  assemblies is a future refactor, not a current one.

---

## Security model

Three properties the project actively maintains:

1. **No secrets in source.** No tokens, client secrets, account IDs, or URLs
   pointing at any specific organisation. The OAuth client IDs themselves
   (which aren't secret) live in user-supplied configuration; the client
   secrets and refresh tokens live encrypted on disk.
2. **DPAPI encryption tied to the Windows user.** Two files at
   `%AppData%\TeamHub\`:
   - `tokens.dat` — encrypted GitHub and Atlassian access + refresh tokens.
   - `config.dat` — encrypted user-entered configuration (site URLs, OAuth
     client ids, the Jira OAuth client secret).

   Both files use `ProtectedData.Protect` with `DataProtectionScope.CurrentUser`.
   Copying either to another user account or another machine renders them
   unreadable.
3. **No long-lived static API tokens.** GitHub uses OAuth Device Flow (no
   client secret needed). Atlassian uses OAuth 2.0 (3LO) with PKCE — the
   client secret is required by Atlassian's flow but lives only in the
   encrypted local store, never on disk in plain text. Refresh tokens rotate
   on every refresh and are persisted as they're issued.

---

## Quick start

### Prerequisites

- **Windows 11** (or Windows 10, but DPAPI behaviour and the Fluent look are
  tuned for Windows 11).
- **.NET 10 SDK** (the project targets `net10.0-windows`).
- **Visual Studio 2022/2023** or **JetBrains Rider** for the WPF designer; the
  command-line `dotnet build` works too.
- An **Atlassian Cloud** site you can sign into (Server / Data Center is not
  supported because OAuth 2.0 (3LO) is Cloud-only).
- A **GitHub** account with access to the repositories you want to surface.

### Register the OAuth apps

You'll need two app registrations — both free, both per-user.

**GitHub OAuth App** — at `https://github.com/settings/developers`:

1. *New OAuth App*. Name it anything (e.g. *"DevSprint local"*).
2. **Homepage URL**: any value (e.g. `http://localhost`).
3. **Authorization callback URL**: any value (Device Flow ignores it).
4. **Enable Device Flow**: tick the checkbox lower on the page.
5. Save. Copy the **Client ID** (looks like `Iv1.…`).

**Atlassian OAuth 2.0 (3LO) integration** — at
`https://developer.atlassian.com/console/myapps/`:

1. *Create* → *OAuth 2.0 integration*.
2. **Permissions** → add the following granular scopes:
   - **Jira API**: `read:jira-work`, `read:jira-user`, plus
     `read:sprint:jira-software` and `read:board-scope:jira-software` under
     the *Jira Software* product entry.
   - **Confluence API**: `read:confluence-content.summary`,
     `read:confluence-user`, `read:confluence-space.summary`, plus
     `search:confluence` (granular — required for CQL search).
3. **Authorization** tab → set the **Callback URL** to
   `http://127.0.0.1:7890/callback` (port can be any free local port; remember
   it for the wizard).
4. Save. Copy the **Client ID** and **Client Secret** from the *Settings* tab.

### First run

1. `dotnet build` and run the app from Visual Studio or the command line.
2. The **onboarding wizard** opens automatically on first launch:
   - Step 1 (*Backlog Management*) — paste your Atlassian site URL, email,
     board ID, project key, the Jira OAuth client id, **client secret**, and
     callback port.
   - Step 2 (*Code Repository*) — paste your GitHub username, organisation,
     comma-separated list of repositories, and GitHub OAuth client id.
3. After **Save and continue**, the **GitHub Device Flow** dialog appears —
   click *"Copy code & open browser"*, paste the code at
   `github.com/login/device`, approve.
4. Then the **Atlassian sign-in** dialog opens — your browser navigates to the
   consent screen, you approve, the local listener catches the redirect, and
   the cloud-id gets cached.
5. Main window appears. Pick a sprint, click an issue, and the People /
   Knowledge / Code tabs populate from your real data.

---

## Project structure

```
DevSprint/
├── DevSprint.UI/                 The single WPF project (see Architecture above)
├── DevSprint.UI/GUIDELINES.md    Coding conventions for contributors
├── DevSprint.UI/Auth/README.md   Detailed auth flow documentation
├── DevSprint.slnx                Solution
├── README.md                     This file
└── .gitignore                    Excludes bin/, obj/, .vs/, and config.dat-style files
```

The repo is intentionally small. There is no separate `tests/` project yet —
that's a follow-up; see *Roadmap*.

---

## Roadmap

The project moves in three loose phases. Phase 1 is what you can clone and run
today.

### Phase 1 — Aggregation foundation (current)

- Backlog and sprint browsing.
- Per-issue surfacing of GitHub PRs and Confluence pages.
- Cross-source People view with manual identity linking.
- Teams group-chat launcher with selectable participants.
- DPAPI-encrypted local config and tokens.
- Onboarding wizard with revisitable settings.

### Phase 2 — People intelligence (planned)

- **Per-team-member detail view** — drill into a single person's recent Jira
  workload, GitHub PR activity, and Confluence footprint, with date-range
  filters.
- **Suggested experts ranking** — for each backlog item, surface 1–3 team
  members ranked by raw activity counts on related Confluence pages and
  adjacent issues. Initially count-based; later replaced by similarity-based
  ranking in Phase 3.
- **Comment authors and reviewer expansion** — track Confluence commenters and
  GitHub PR reviewers per issue, not just creators.
- **Surface API errors visibly** — replace the existing silent-catch error
  handling with proper UI feedback so failures are diagnosable without
  attaching a debugger.

### Phase 3 — AI-assisted collaboration (vision)

- **Per-person profile generation** — summarise each team member's recent
  activity (Confluence pages, Jira tickets, GitHub PRs) into a structured
  expertise profile, refreshed weekly.
- **Semantic expert matching** — for each backlog item, embed both the issue
  description and the cached profiles, and rank by similarity. Three options
  on the table:
  1. Local embedding model (e.g., `all-MiniLM-L6-v2` via ONNX Runtime —
     no API calls, fully on-device).
  2. Managed LLM API (Anthropic, OpenAI) for richer rationale generation.
  3. Hybrid — embeddings for ranking, LLM for the *"why"* explanation only.
- **Privacy-first by default** — content stays local; remote inference is
  always opt-in, with clear scope on what data leaves the machine.

### Beyond Phase 3 — broader source coverage

- **Azure DevOps** integration — Boards (replacing or complementing Jira),
  Repos (replacing or complementing GitHub), and Wiki (replacing or
  complementing Confluence). The architecture is already plurally-sourced —
  adding Azure DevOps means new `IAzureDevOpsService`, a parallel auth flow,
  and routing logic in the unified People view.
- **Microsoft Planner** for lightweight task tracking.
- **GitLab** as a GitHub alternative.

The shape of the abstractions in `Services/` and `Auth/` was chosen with this
expansion in mind — adding a new platform should mean a new service, a new
auth folder, and a new field group in the onboarding wizard, without
disrupting existing code.

---

## Tech stack

- **.NET 10**, C# 13.
- **WPF** (Windows Presentation Foundation) for the UI.
- **CommunityToolkit.Mvvm** for source-generator-driven MVVM.
- **Microsoft.Extensions.{Configuration,DependencyInjection,Http,Options}**
  for the DI graph and HTTP client factory.
- **System.Security.Cryptography.ProtectedData** for DPAPI encryption.
- No third-party UI libraries — everything is built on the WPF primitives plus
  a custom Fluent-styled `ResourceDictionary`.

---

## Contributing

DevSprint is a lab project — issues, discussions, and pull requests are very
welcome, with the understanding that:

- The codebase prioritises **clarity over cleverness**. New code should follow
  the existing patterns: MVVM, no business logic in code-behind, services
  injected via constructor, secrets DPAPI-encrypted.
- The conventions in `DevSprint.UI/GUIDELINES.md` are enforced for any UI work.
- Anything that adds a new external dependency should be discussed in an issue
  first — the dependency footprint is intentionally small.
- New connectors (Azure DevOps, GitLab, etc.) are encouraged but should follow
  the `Services/` + `Auth/` pattern already established for Jira / GitHub /
  Confluence.

A `tests/` project doesn't exist yet — adding one (with `HttpMessageHandler`
fakes for the API services) would be a great first PR.

---

## License

DevSprint is released under the **GNU Affero General Public License v3.0**
(**AGPL-3.0**) — see [`LICENSE`](LICENSE) for the full text.

### Why AGPL-3.0?

The short version: anyone is free to use, fork, modify, and contribute to
DevSprint, but **any modified version that's run as a service or distributed
as a product must also be released under AGPL-3.0** with full source. This is
deliberate. The licence is meant to keep DevSprint open in spirit as well as
in name — proprietary forks and closed-source SaaS products built on top of
this code are effectively impractical, while individuals, teams, and
contributors can freely use and improve it.

If you're using DevSprint personally or inside your team, you don't need to
do anything beyond following the licence terms. If you're building a product
or commercial service on top of it, the AGPL's source-disclosure requirement
applies.

### Copyright

Each contributor retains the copyright to their own contributions. By
submitting a pull request, you agree to license your contribution under the
same AGPL-3.0 terms as the rest of the project.

---

## Acknowledgements

- The **Atlassian Developer Platform** for the OAuth 2.0 (3LO) flow and the
  CQL search engine.
- **GitHub** for the OAuth Device Flow, which makes secret-less desktop OAuth
  practical.
- **CommunityToolkit.Mvvm** maintainers for source-generator MVVM that
  eliminates the typical WPF boilerplate.
- The countless engineering teams whose collective frustration with
  context-switching across Jira, GitHub, Confluence, and Teams sparked the
  idea for a tool like this in the first place.
