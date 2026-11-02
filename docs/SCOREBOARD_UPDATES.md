# Scoreboard updates — 30 October 2026

## Changes
- Umpire/event information moved into the header; TLS branding and the extra footer row removed. Shuttle controls remain visible; unavailable challenges are hidden. Service setup is unchanged.
- Early starts require umpire confirmation and server validation. Actual start is recorded separately; no planned match times move.
- Scheduled date, event dates, court allocation, closed/occupied courts, officials and prerequisite results remain protected. Outside session hours the court must be allocated in the session containing the planned start; during an active session its court restrictions apply.
- Dashboard and Fixture Table View offer View Scoresheet / Print Scoresheet through the existing PaperScoresheet renderer. Umpire scoresheets have no application print action.
- Pairing links work on an already-open page. Change match preserves the previous session until pairing succeeds; pending scores prevent switching. Revoked sessions without pending work return to pairing. Existing server assignments must still be released before the same tablet joins another match.

## Files
Frontend: `src/UmpireApp.tsx`, `src/umpire.css`, `src/live-types.ts`, `src/ScoresheetButton.tsx` (new shared console viewer), `src/LiveDashboard.tsx`, `src/components/admin/fixtures/ResultsTab.tsx`, `src/paper-scoresheet.css`.

Backend: `LiveScore.Api/LiveRuntime.cs`, `LiveModels.cs`, `LiveEndpoints.cs`.

Tests: `Frontend/tests/umpire-pairing.test.tsx`, `scoresheet-console.test.tsx`, `workflow.test.tsx`; `tests/early_start_integration.py`, `tests/RulesChecks/Program.cs`.

Documentation: this report, `UMPIRE_SCORING.md`, `LIVE_DASHBOARD.md`. Production frontend assets were rebuilt under backend wwwroot.

## Database and API impact
No schema changes, migrations, record deletion or edits to existing tournament data. Integration checks used disposable databases.

Tablet commands accept optional `confirmEarlyStart` (false by default); early requests require true. Match views include additive `earlyStartMinutes`. New local-console-only GET `/api/dashboard/scoresheet/{matchId}` returns the existing match view without pairing tokens.

## Verification
- Both builds passed; backend had no warnings/errors. Frontend retains its existing chunk-size advisory.
- 139 frontend tests across 25 files passed, including the real React/SQL workflow.
- 48 focused backend checks passed, including confirmation, normal starts, date restrictions, court allocation, occupied court/umpire and prerequisite results.
- 81 disposable API checks passed, including unchanged timetable, early start, retry idempotency and scoresheet data.
- TLS restarted; HTTP health check returned 200.

Physical tablet touch/layout and actual printer output remain device checks. Automated printing checks cover the shared renderer and print action, not a physical printer.
