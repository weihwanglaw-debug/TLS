# Event workflow implementation — 14 October 2026

Approved scope: TRS fixture screen sequence, Event Details operational setup, independent/coordinated progression and standalone match-rule presets selected before pairing.

Database: existing SQL/EF tables and JSON fields; no table/column migration, no runtime samples or production test records. Presets freeze per match. API: /draw-settings replaces /rules; /api/presets adds master/assignment operations; first previews support unsaved settings/seeds; pairing requires a saved preset for new matches.

Verification: frontend/backend builds; 962 HTTP checks plus three React screen-flow checks against disposable SQL. Chrome reviewed Event Details and fixture overview/configuration/preview. Physical tablets/network remain an operator check.

Rollback: matching source and SQL backup listed in DATABASE.md. Preserve later records first. Unrelated TRS payment/export changes were left untouched.

## Files affected

- `Backend/LiveScore.Api/EventOperations.cs` (added)
- `Backend/LiveScore.Api/GamePresets.cs` (added)
- `Backend/LiveScore.Api/LiveDashboard.cs` (updated)
- `Backend/LiveScore.Api/LiveModels.cs` (updated)
- `Backend/LiveScore.Api/LiveRuntime.cs` (updated)
- `Backend/LiveScore.Api/Program.cs` (updated)
- `Backend/LiveScore.Api/SetupEndpoints.cs` (updated)
- `Backend/LiveScore.Api/SqlStorage.cs` (updated)
- `Backend/LiveScore.Api/TournamentEndpoints.cs` (updated)
- `Backend/LiveScore.Api/TournamentModels.cs` (updated)
- `Backend/LiveScore.Api/TournamentRules.cs` (updated)
- `Backend/LiveScore.Api/TournamentScheduler.cs` (updated)
- `Frontend/package-lock.json` (updated)
- `Frontend/package.json` (updated)
- `Frontend/src/AdminLayout.tsx` (updated)
- `Frontend/src/EventSettings.tsx` (added)
- `Frontend/src/FixtureViews.tsx` (updated)
- `Frontend/src/FoundationPages.tsx` (updated)
- `Frontend/src/GameSettingPresets.tsx` (added)
- `Frontend/src/LiveDashboard.tsx` (updated)
- `Frontend/src/MatchResultEditor.tsx` (added)
- `Frontend/src/SetupApp.tsx` (updated)
- `Frontend/src/TournamentConsole.tsx` (updated)
- `Frontend/src/components/admin/fixtures/FixtureConfiguration.tsx` (updated)
- `Frontend/src/components/admin/fixtures/FixturePreview.tsx` (added)
- `Frontend/src/components/admin/fixtures/ResultsTab.tsx` (updated)
- `Frontend/src/lib/exportCsv.ts` (updated)
- `Frontend/src/live-types.ts` (updated)
- `Frontend/src/tournament-types.ts` (added)
- `Frontend/tests/workflow.test.tsx` (added)
- `Frontend/vitest.config.ts` (added)
- `README.md` (updated)
- `docs/DATABASE.md` (updated)
- `docs/EVENT_WORKFLOW_TEST_CASES.md` (added)
- `docs/FIXTURE_RESULTS.md` (updated)
- `docs/FOUNDATION_UI.md` (updated)
- `docs/LIVE_DASHBOARD.md` (updated)
- `docs/SETUP.md` (updated)
- `docs/TOURNAMENT.md` (updated)
- `tests/event_workflow_integration.py` (added)
- `tests/fixture_result_integration.py` (updated)
- `tests/frontend_workflow_integration.py` (added)
- `tests/live_integration.py` (updated)
- `tests/tournament_integration.py` (updated)
