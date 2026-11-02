# Fixture and Result — TRS adaptation

TLS uses the existing TRS fixture configuration, bracket, standings, table, result dialog, printing and CSV presentation. Components were copied into TLS and adapted to its SQL-backed participant and match records. TRS registration, payment and database dependencies were not copied. TRS itself was not changed.

## Operator flow

1. Open Fixture and Result and Continue a saved program through Seeding → Draw → Schedule → Fixture & Results. Event and program preparation comes from the local TLS SQL database.
2. Save officials in Event Details. Configure dated sessions, timing and courts under Fixture → Schedule; courts come from Master Setting and programs progress independently. Create scoring profiles in the standalone Game Setting Preset module.
3. From the program overview, choose Generate Fixture. Review imported seeds or assign seeds manually. Choose Knockout, Round Robin or Group + Knockout. Heats remains visible and is disabled for badminton; other sports still have no TLS scoring workflow.
4. Open the separate Seeding page from the program list. SBA assistance uses local SQL rankings only when Program Name exactly matches a category; no manual category override exists. Auto-seeding follows TRS: match singles by SBA ID, doubles by both partner IDs, then order by accumulated points. Unmatched entries remain unseeded. The category and retrieved ranking snapshot are stored in SQL, without changing imported participant identities. Refreshing rankings never silently changes saved seeds or fixtures. A draw prevents later ranking replacement.
5. Select round courts, earliest start and estimated match duration on configuration. Open the separate server-validated preview, optionally swap two positions in a single program's first draw, review the bracket and court/time records, and confirm. Preview swaps recalculate the schedule and do not write fixtures to SQL until confirmation.
6. Use Bracket View or Table View. The latter retains TRS result entry and court/date editing, with TLS conflict validation and a Schedule / Officials action. Print / Save PDF, CSV export and permitted resets are grouped under Options. Saved planned date/time/court appear in both bracket and table; projected bracket connectors remain continuous.
7. Enter complete game scores or a walkover manually, or review scores submitted by the tablet. The server calculates and validates the winner against the match's saved scoring rules.
8. Generate the next round with its chosen courts/time/duration. Each program separately permits independent advancement; All programs together waits for every active program’s compatible current stage. Round Robin ends with standings.

## One match record, two input methods

Both entry methods read and write the same SQL-backed match. Tablet submission pre-fills games, exact start/finish timestamps, officials and additional details. Manual entry requires complete games or a walkover; actual times, shuttle total, cards, challenges and remarks are optional. No synthetic rally or serving history is generated from final scores.

Additional details:

- Shuttle total is blank when unknown, otherwise 0–999.
- Cards identify a match participant, game, type Y/R/B and optional remark. These represent yellow warnings, red faults and black referee disqualifications.
- Challenges identify side A/B, game and outcome S/U/C (successful/unsuccessful/cancelled). Unsuccessful allowances come from the saved match rules and reset for each game.
- Duration is calculated only when actual start and finish are both known; estimated duration is separate.
- Source codes M/T/P identify manual, tablet or paper reconciliation. Corrections retain prior scores/details and their reason; scoring audit records remain available.

The tablet can record these details when enabled in its match rules. Recording a card or challenge does not automatically award a rally point, decide a walkover or connect to a video-review service. Apply the appropriate score/result separately. Undo applies to score/game progression; metadata mistakes can be corrected through the organiser's result review.

## Safeguards

- A playing tablet match must be switched to paper through Dashboard before manual result entry. Switching revokes the tablet session and keeps its court occupied until reconciliation.
- Manual saves use both the catalog revision and the match version. A newer tablet update cannot be overwritten by an older result dialog.
- An unchanged tablet submission can be confirmed without a correction reason. Changes to its final scores, details or recorded finish require a reason. The tablet's actual start is preserved exactly.
- Results cannot be amended/cleared while a dependent round exists. A completed result can be cleared with a reason; its prior record remains in correction/audit history.
- The latest knockout round can be reset only when entirely unplayed and without waiting tablet assignments. Earlier completed rounds remain intact. The existing whole-draw reset remains limited to unplayed draws.
- Changing result presentation does not replace local fixture IDs or imported source IDs.

## TRS integration without packaged examples

No runtime demo event list, generated dated demo event, example workbook or public sample JSON is included. The Excel template is blank. Get from TRS retains the normal event picker.

Until the event API is agreed, an administrator can configure `Trs:EventFile` in private `Backend/LiveScore.Api/appsettings.Local.json` or `Trs__EventFile` in the environment. It points to an external JSON file containing an array of actual TRS event transfer packages (schema version 2; see SETUP.md). An unconfigured integration returns an empty event list; it never creates fictional events. The browser receives this through GET /api/trs/events and does not ask organisers to upload JSON. Existing eligibility and duplicate-name rules still apply.

For SBA, import the ranking workbook through TLS's standalone SBA Rankings module. Seeding no longer reads Trs:RankingFile or Trs:BaseUrl. Program Name must match the local category exactly, including case, punctuation and spaces; there is no name normalization or category override. See SEEDING.md. The legacy /api/trs/rankings and /api/trs/ranking-snapshot routes remain available but now use local SQL, and the latter enforces the same exact-name rule. Manual/imported seeding and random draws remain available without rankings.

Integration files are inputs only. Once imported/saved, operational records and ranking snapshots are held in SQL and do not depend on those files remaining accessible.

## Storage, API and verification

No table/column migration is required. Format and ranking snapshots extend the existing ProgramOperations JSON; result details, source and corrections extend MatchLiveRecords JSON. EF remains the persistence mechanism. Older program rows default to GK and missing result details default to unknown/empty. Normal startup never creates demo records.

Tournament APIs replace the former /rules editor with /draw-settings for format KO/RR/GK; scoring profiles now use the standalone /api/presets routes. Result requests add optional startedAt/details/reason and matchVersion, which is required when live state already exists. New POST routes: preview-swap, clear-result and reset-latest-round. Integration routes are GET /api/trs/events, GET /api/trs/rankings and POST /api/trs/ranking-snapshot. Tablet commands add SHUTTLE/CARD/CHALLENGE using the same version and idempotent operation ID contract. Operator/integration APIs remain loopback-only.

Run the normal backend/frontend builds and tests/import_integration.py, tests/tournament_integration.py, tests/live_integration.py and tests/fixture_result_integration.py. Tests generate synthetic inputs only inside disposable TLS_Test databases and temporary transfer files; none are shipped as runtime samples or inserted into TLS/TRS. Coverage includes all three formats, swaps, byes, resets, independent rounds, result validation/corrections, manual consumption of tablet data, timestamp preservation, challenge allowances, identity protection, SQL restart persistence and ranking snapshots.

Before this change, source was backed up to D:\Source\TLS-Backups\fixtures-20261014-103317 and TLS received a COPY_ONLY/CHECKSUM SQL backup. Removed runtime samples and historical SQLite-era source backups were archived there. Rollback requires the matching source and SQL backup; preserve any subsequent operator work before restoring. Existing TLS imported records were preserved, including any events previously imported for testing.

The tablet now provides saved serving positions and a printable log of recorded rallies; see UMPIRE_SCORING.md. Public hall displays, internet publishing, live TRS preparation locking and operator user management remain separate unfinished modules.

## Changed files and final checks

- Frontend orchestration: src/TournamentConsole.tsx, FixtureViews.tsx, fixture-adapter.ts, ResultDetailsForm.tsx, UmpireApp.tsx, live-types.ts, EventImport.tsx and trs-events.ts.
- TRS-derived components: src/components/admin/fixtures/BracketView.tsx, ResultsTab.tsx, ScoreModal.tsx, GroupStandingsTable.tsx and FixtureConfiguration.tsx; src/components/ui/dialog.tsx, switch.tsx and NoticeDialog.tsx; src/types/config.ts and src/lib/entryDisplay.ts, fixtureEngine.ts, exportCsv.ts, fixturePrint.ts and utils.ts.
- Frontend dependencies/aliases: package.json, package-lock.json, tsconfig.json and vite.config.ts. Compiled wwwroot assets were refreshed by the normal Build.ps1.
- Backend: LiveScore.Api/TournamentModels.cs, TournamentRules.cs, TournamentEndpoints.cs, LiveModels.cs, LiveRuntime.cs, SqlStorage.cs, SetupEndpoints.cs and Program.cs; new ResultDetails.cs and TrsIntegration.cs.
- Tests: import_integration.py, tournament_integration.py and live_integration.py; new fixture_result_integration.py and generated-input helper test_inputs.py. Old test dependencies on public sample files were removed.
- Documentation: README.md and docs/FIXTURE_RESULTS.md, TOURNAMENT.md, LIVE_DASHBOARD.md, SETUP.md, FOUNDATION_UI.md and DATABASE.md.
- Removed runtime assets: the public/backend/dist samples directories and their example workbook/JSON files. Historical Data/backups source copies were moved outside the project. No TRS files were modified.

Final verification: normal frontend/backend builds passed with no backend warnings/errors. All four suites passed (240 import + 148 tournament + 153 live + 208 fixture/result = 749 HTTP assertions). SQL backup checksum verification passed. The restarted local application returned HTTP 200 for console/tablet pages and read the existing one event, eight participants and four configured courts from TLS. No TRS integration events were configured, so its picker correctly returned an empty list. Browser automation failed to initialise; visual browser/physical-tablet checks remain manual.

## Preset selection and revised preparation

Event Details holds officials, with date editing allowed only before saved fixtures. Court selection belongs to Schedule and uses the master venue courts. Dated sessions, duration/rest and generated-round allocations belong to Schedule Event under Fixture and Result; draw preview/save does not allocate slots. Round progression is always specific to the program; see SCHEDULING.md. Fixture configuration and preview are separate TRS-derived screens; entry-side swaps are supported for initial knockout/group draws and Back preserves selections. Confirm is the only operation that saves the draft seeds/configuration/fixtures. Create a game preset independently, then select it before tablet code issuance or first manual result. Editing/deactivating the master preset cannot alter frozen match rules. See TOURNAMENT.md and EVENT_WORKFLOW_TEST_CASES.md for progression, API details and rollback.

Review availability is selected on an individual match assignment rather than in Event Details. Event Programs are the final section; on/off choices and action feedback reuse TRS toggles and modal dialogs. Manual/paper changes mark the rally history incomplete when it no longer describes the final games.

For the shared four-step header, saved draw list, default Table View, preset defaults and required default presets and per-match scoring protection, see [Guided fixture planning](FIXTURE_PLANNING.md).
