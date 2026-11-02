# Tournament Livescore System (TLS)

TLS runs its organiser console and umpire scoring against the local TLS SQL Server database through EF Core. Imported preparation is read-only except TLS event dates, which can be edited after saved schedules are reset, provided no started/result history exists. Only one event is allowed on each calendar day; imports and date edits reject overlapping inclusive ranges. Its Fixture and Result module reuses TRS components and supports Knockout, Round Robin and Group + Knockout, with local scheduling, manual results and tablet result review.

## Run locally

```powershell
git clone https://github.com/weihwanglaw-debug/TLS.git
cd TLS
.\Build.ps1
dotnet run --no-build --project Backend/LiveScore.Api
```

Open http://localhost:5180. The backend serves the compiled React UI. The organiser console uses module URLs such as /dashboard, /events and /fixtures; browser Back/Forward and direct-link refresh restore saved event/program context. See [navigation](docs/SETUP.md). For frontend development, run `npm.cmd run dev` from Frontend (port 5174). Private `ConnectionStrings:TLSConnection` in Backend/LiveScore.Api/appsettings.Local.json configures SQL; credentials are not committed. Operator APIs are localhost-only; paired tablets use /umpire on the private venue network.

## Repository contents

This repository includes application source, build scripts, documentation, the blank Excel import template and automated tests. Local database credentials, runtime/imported data, databases, dependency folders, compiled assets, logs and generated test reports are excluded by `.gitignore`.

Before the first build/run, install .NET 8 and Node.js, install frontend dependencies with `npm.cmd ci` from `Frontend`, and configure your own `Backend/LiveScore.Api/appsettings.Local.json`. Follow [database setup](docs/DATABASE.md) for creating and configuring the local SQL database. Do not commit the local configuration or imported tournament records. Test sources are retained to check future scheduling, scoring and recovery changes; the SQL integration tests need a local test database connection with create/drop permissions as described below.

## SBA Rankings

The standalone sidebar module copies the TRS ranking page and stores rankings/clubs in TLS SQL. Import the same SBA .xlsx format as TRS. Participant club text remains supplied by the event import/interface, with no master-club linkage or overwrite. Existing databases need the additive `--upgrade-sba` command; see [SBA Rankings](docs/SBA_RANKINGS.md). The separate Seeding stage is available from the Fixture and Result list. Draw generation and event scheduling are now separate; see [Scheduling](docs/SCHEDULING.md). See [Seeding](docs/SEEDING.md).

## Test with your records

1. Open Event and use an existing SQL-backed event, or download the blank Excel template and import your own information.
2. Use the event's program menu to open filtered Participant or Fixture and Result details.
3. In Event Details, save Match Timing and officials, and edit dates before scheduling if needed. Master Setting defines venue courts; choose each session's courts in Fixture → Schedule. Create scoring rules in Game Setting Preset.
4. In Fixture and Result, use Continue through the persistent Seeding → Draw → Schedule → Fixture & Results steps. Save seeding, then configure/preview/save the draw without dates or courts. Apply Draw Settings to Other Programs can generate opening draws for selected eligible programs with their own reviewed seeds. Select All includes eligible programs only. Use Schedule Event to select dated sessions, court subsets and generated rounds for each day; preview and confirm proposed slots. Programs advance independently when their own required results are complete. Existing match assignments remain unchanged when adding rounds. See [Scheduling](docs/SCHEDULING.md).

5. From Dashboard, select a saved game preset before assigning an unplayed match and issuing its tablet access code. Playing matches must switch to paper before manual reconciliation.

There are no packaged demo events, runtime sample JSON files or example workbooks. Existing imported SQL records are preserved. Get from TRS uses an external administrator-configured JSON transfer file until live event integration is agreed. Unconfigured integration shows no events. No organiser JSON upload screen is exposed.

See [Fixture/result workflow and integration configuration](docs/FIXTURE_RESULTS.md), [dashboard and tablet operation](docs/LIVE_DASHBOARD.md), [import contract](docs/SETUP.md), [foundation navigation](docs/FOUNDATION_UI.md) and [database setup/rollback](docs/DATABASE.md).

## Verification

```powershell
dotnet build Backend/LiveScore.Api/LiveScore.Api.csproj
# From Frontend:
npm.cmd run build
# From project root:
python tests/sba_integration.py
python tests/import_integration.py
python tests/tournament_integration.py
python tests/live_integration.py
python tests/fixture_result_integration.py
```

Tests use disposable TLS_Test databases on ports 5193/5197/5198/5199 and temporary synthetic transfer inputs. They do not insert test records into TLS or TRS. SQL test credentials require permission to create/drop their disposable databases.

Heats is visible but disabled for badminton. Serving-court visuals and a printable recorded-rally scoresheet are available. Public hall displays, internet publishing, live TRS preparation locks and operator user management remain unfinished. Card/challenge records do not automatically award rally points or operate external review equipment.

The two-program/four-court scenarios and preset rules are documented in [Event workflow test cases](docs/EVENT_WORKFLOW_TEST_CASES.md). Run `python tests/event_workflow_integration.py` and `python tests/frontend_workflow_integration.py` for isolated SQL/API and React screen-flow checks. Those event-workflow checks require no additional schema change; the standalone SBA module requires the additive `--upgrade-sba` step described above.

See [Umpire scoring and TRS controls](docs/UMPIRE_SCORING.md) for portrait/landscape operation, saved service positions, per-match review availability, paper handover and the revised tablet command contract. Event Details uses TRS toggles/alert dialogs and shows Programs last.

Seeding uses a combined Seeds / Participants count (2/8, 0/8 or NA/8), alongside Stage, Draw and Schedule progress indicators. The separate page lets admins choose Number of Seeds for manual or eligible local SBA-assisted assignments; team rosters expand from their imported organisation/entry label. See [Seeding workflow](docs/SEEDING.md).

For the shared four-step header, saved draw list, default Table View, preset defaults and required default presets and per-match scoring protection, see [Guided fixture planning](docs/FIXTURE_PLANNING.md).

To change completed preparation, work backward: Reset Schedule → Reset Draw → Reset Seeding. Schedule reset keeps pairings and seeds. For an unplayed postponement, reset all saved schedules, change event dates (old sessions are cleared), then schedule the preserved draws again. Started/result rounds and waiting tablet bindings are protected. See [Scheduling](docs/SCHEDULING.md).

Business records now use readable server-generated IDs such as EV000001 and EV000001-P001. Existing installations need the additive business-ID schema upgrade; see [ID generation and database operations](docs/BUSINESS_IDS.md). The import template and external SBA IDs remain unchanged.

Practical result recovery, saved planning drafts and session court checks: [Phase 2 changes and device checklist](docs/PHASE2_REMEDIATION.md).
