# TLS architecture index

## Local workflow

From the TLS Git root on Windows, run:

```powershell
python tools/graphify_tls.py check
python tools/graphify_tls.py sync
$graph = python tools/graphify_tls.py path
if ($LASTEXITCODE -ne 0) { throw "TLS graph is unavailable" }
$cli = "$env:USERPROFILE/.local/bin/graphify.exe"
& $cli query "schedule planner" --graph $graph
& $cli path "LiveRuntime.Command()" "BadmintonServing.Point()" --graph $graph
& $cli path "DrawBatch.Apply()" "TournamentRules.CreateRound()" --graph $graph
& $cli path "TournamentEndpoints.MapTournament()" "ResultDependencies.Apply()" --graph $graph
```

The three path queries returned one extracted call each on 20 November 2026. The corresponding calls appear in LiveRuntime.cs (POINT command), DrawBatch.cs (applying a saved draw to another program) and TournamentEndpoints.cs (applying a result decision). Method-level edges for endpoint registration can represent calls **inside a request handler**. Inspect the route body to establish when a call executes.

The helper resolves Graphify on PATH or in the default user uv bin directory. Tested with graphifyy 0.9.113. The generated HTML and GRAPH_REPORT.md are beside the resolved graph.json. Open the HTML locally in a browser. The report includes extraction confidence and token counts. No external model is required.

## Workflow and source coverage

| Workflow | Source to verify after querying |
|---|---|
| Entry points, program setup and participants | Frontend/src/main.tsx selects the admin application, umpire or venue display. SetupApp, FoundationPages, tournament-types and the API clients provide UI context. ImportWorkflow and SetupStore handle imported preparation. |
| Draw, seeding and advancement | FixtureWorkspace, SeedingPage and FixtureViews lead into tournament endpoints. TournamentRules.CreateRound builds knockout, round-robin and group-to-knockout fixtures. BadmintonStandings and QualificationPlacement are entry points for standings and qualifier placement. |
| Scheduling and court assignment | SchedulePlanner.tsx, SchedulePlannerEndpoints and SchedulePlanner cover proposed bookings. LiveDashboard, LiveEndpoints and LiveRuntime connect court assignment and tablet operation. Inspect LiveAccess and endpoint filters for network and device access. |
| Scoring and submission | UmpireApp sends tablet commands. POST /api/tablet/command calls LiveRuntime.Command. A point updates badminton serving state. Submission records the winner and moves the match to review. Dashboard confirmation completes it and releases the binding. |
| Result correction and advancement | MatchResultEditor, ScoreModal, OfficialResults and ResultDependencies cover the winner, impact review and application to downstream matches. TournamentEndpoints records an official decision with reason and snapshots. |
| Audience display, printing and publishing | LiveDisplay polls /api/display two seconds after each request finishes. LiveEndpoints applies venue-network access. PaperScoresheet and lib/fixturePrint cover printing. Program.cs registers TrsPublication as a hosted service. |
| Persistence | TLSDbContext links programs and participants to events, entries to programs, and entry members to entries and participants. EntryMembers uses entry ID and position as its composite key. EventOperations and ProgramOperations use the corresponding IDs as keys and store JSON. MatchLiveRecords uses MatchId as key, ProgramId as foreign key, and stores status and JSON. Inspect SqlStorage and the SQL source for exact persistence semantics. |

## Freshness and integrity

The helper hashes existing tracked and nonignored untracked implementation files in Frontend/src and Backend. Eligible extensions are TS, TSX, JS, JSX, C#, csproj and SQL. Generated directories, dependencies, runtime data, configuration secrets, build outputs and symlinked inputs are outside the corpus. Tests, documentation and styling are outside this implementation graph. The baseline includes paths, SHA-256 content hashes, helper digest, CLI version and hashes of four published artifacts. A renamed file changes the set of paths even when its contents match.

Sync creates an allowlisted temporary source copy under graphify-out. It runs Graphify extract with --code-only, then cluster-only with --no-label. A compatible prior graph and Graphify manifest enable its incremental scan. An unchanged snapshot exits before extraction. New or incompatible baselines and damaged artifacts trigger a clean extraction. A Windows file lock serializes writers. Sync verifies input coverage, extraction stamps, node and edge integrity, nonempty exports and a second source snapshot. It writes the inactive a/b generation, then atomically replaces current.json. A failed build leaves the last published generation available. Always resolve the current generation through the helper.

If check returns 2, run sync and retry check. If sync fails, read the error and inspect source directly. If the pointer itself is unreadable, retain a copy of the generated folder for diagnosis, then regenerate into an empty graphify-out. To roll back the integration remove its generated folder, helper, runbook and TLS instructions added for Graphify, and remove the graphify-out ignore entry.

## Known limits

- SQL changes participate in freshness checks. The installed environment lacks tree_sitter_sql, so the seven SQL scripts are source-inspection inputs. Use TLSDbContext, Models and the scripts to verify keys, constraints and migrations.
- Graphify reported a partial TSX parse for LiveDashboard.tsx and zero extracted symbols for Program.cs, main.tsx and EventSchedule.tsx. Inspect these directly for registration and routing. SetupEndpoints.cs required Graphify's cp1252 fallback.
- HTTP route strings, serialized JSON fields, database relationships, runtime authorization, callbacks and rule transitions require source verification. A missing edge cannot establish absence of a dependency. Use a full source search when evaluating a rename or a new cross-module call.
- The initial graph had 1,581 nodes and 4,276 edges. The post-build diagnostic found zero dangling or missing endpoints and one self-call (TournamentRules.SeedLine). Post-build diagnostics cannot measure relationships omitted by the extractor.
- This is an on-demand local tool. No watcher, Git hook or background refresh is installed.
