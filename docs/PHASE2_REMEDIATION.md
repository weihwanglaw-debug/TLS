# TLS practical remediation — 30 October 2026

Normal operation remains Call → Pair → Score → Submit → Admin Confirm. No rules engine, identity system, database schema migration or operational-data cleanup was introduced.

| Problem | Implemented fix | Main source files | Verification |
|---|---|---|---|
| Narrowed group ties could produce the wrong qualifier | Recheck head-to-head for two remaining competitors; show unresolved ties and require a reasoned ranking decision where qualification is affected | BadmintonStandings.cs, TournamentRules.cs, RankingDecision.tsx, GroupStandingsTable.tsx, fixture-adapter.ts | Focused ranking cases; tournament regression |
| Immediate same-group knockout rematches | Preserve qualifier origins and separate opening opponents where possible; show A1/A2 labels in preview | QualificationPlacement.cs, TournamentRules.cs, fixture-adapter.ts | 2/3/4/5/8-group separation and qualifier uniqueness |
| Retirement/DQ/manual decisions lacked a clean result | Actual partial scores, official winner, result code and reason | OfficialResults.cs, TournamentModels.cs, TournamentEndpoints.cs, ExceptionalResultEditor.tsx, MatchResultEditor.tsx, ResultsTab.tsx | Partial retirement, DQ, normal validation, manual winner, UI confirmation |
| Winner corrections could corrupt later matches | Impact review; safe update; explicit retain/replay choice for progressed dependants; before/after history; stale review protection | ResultDependencies.cs, LiveRuntime.cs, TournamentEndpoints.cs, ExceptionalResultEditor.tsx | Safe final update, started-final rejection, retain/replay, tablet revocation, stale revision |
| Live court checks ignored session selection | Shared session court check; match-specific reasoned exception until active session end | SessionCourts.cs, LiveRuntime.cs, LiveDashboard.cs, LiveModels.cs, LiveDashboard.tsx | Court unavailable rejection, explicit exception, expiry/moved-court checks |
| Save Session did not save planning work | Persistent event draft; isolated validation simulation; publish only on Confirm Schedule | ScheduleEndpoints.cs, TournamentModels.cs, EventSchedule.tsx, tournament-types.ts | Save/reload/server restart, unchanged slots, stale save, draft cleared on confirmation |
| Storage failure could lock umpire input | Memory-first pending command, guarded storage, send-lock cleanup and same-ID retry | UmpireApp.tsx | Denied reads, quota failure, failed network action and successful retry |
| Five obsolete integration fixtures | Use returned official IDs and current preset/reset prerequisites | umpire_serving, round_preset, dashboard_event, event_workflow, seeding integration tests | Full integration regression |
| Shared-lock performance was unmeasured | Four concurrent scoring courts with tablet polling, dashboard requests, submit and confirmation | practical_remediation_integration.py | Measured local SQL/API run; architecture retained |

Backend file paths are under `Backend/LiveScore.Api`; frontend files are under `Frontend/src` (standings/results presentation also uses `components/admin/fixtures`). Additional focused tests are in `tests/RulesChecks` and `Frontend/tests/official-result.test.tsx`, `umpire-storage.test.tsx`. Existing scheduling React tests were updated for persistent-save revision/recalculation. `SqlStorage.cs` excludes derived partial scores from fixture documents, preserving rally-write scope. Compiled frontend assets are refreshed through Build.ps1.

## Result override

Open a fixture's result editor → Exceptional / Override Official Result → choose result type, official winner, actual scores and reason → Review Result → Confirm. The history shows previous/new results, reason, timestamp and handling choice. Existing local-console authorization is retained; there is no new named-referee identity.

Unplayed affected matches update and waiting tablets disconnect. Started/submitted/completed affected matches require an explicit choice to keep existing participants/outcomes or reset only the listed matches for replay. Replay waits for earlier corrected results. Planned slots remain, so review their timing. Group corrections conservatively review the whole knockout draw. If corrected qualification is unresolved or older qualifier origins are unavailable, explicitly retain the draw and resolve the referee decision, or reset an unplayed draw through existing reset controls.

## Device checklist

1. Save a session with court/program quantities; reload, preview and confirm. Check the restored counts and actual slots.
2. Pair 2–3 phones/tablets by QR and manual code. Score simultaneously; check undo, submit and admin confirmation.
3. Disconnect Wi-Fi, retry a pending point, and replace a tablet. Check that no point is duplicated or lost.
4. Enter a partial retirement; correct an earlier winner; review both an unstarted and a started dependent match.
5. Try a court outside the active session, then record a court exception and check the tablet can start.
6. Preview group qualifiers and resolve an unresolved tie before advancing.

## Operational limits

Real Wi-Fi, device sleep/background behaviour, printing and physical tablet usability still require venue testing. When browser storage is unavailable, keep the page open until pending commands are acknowledged; a reload cannot preserve memory-only data. Group results are retained after withdrawal/retirement rather than automatically purged. Replayed match slots require an organiser review. Rollback after exceptional results requires preserving/reconciling new operation/history data; older binaries cannot safely represent every new recovery state.

## Final verification and readiness

- Backend build: PASS, zero warnings/errors. Focused rules/dependency/court checks: **36 passed, 0 failed**.
- Frontend production build: PASS; existing bundle-size advisory remains. React: **24 files / 133 tests passed, 0 failed**, including the real SQL-backed screen workflow.
- API integration: **22 suites passed, 0 failed** (21-suite final run plus the dedicated 21-point scoring suite). Practical recovery simulation: **491 checks**. Dedicated 21-point scoring: **137 API assertions**.
- Four-court performance: **276 measured requests in 2.70 seconds**, median **58.4 ms**, p95 **86.1 ms**, maximum **114.0 ms**, no failed requests. An earlier run measured p95 142.9 ms. These are local laptop/API/SQL measurements, not venue Wi-Fi latency. No architecture change justified.
- Database: additive JSON only; no migration, imported-event deletion or master-data change. All test writes used disposable SQL databases.
- API: additive result-impact/ranking-decision/draft endpoints and optional official-result/court-exception fields; existing normal scoring endpoints retained.
- **READY FOR DEVICE TESTING.** Run the laptop/phone checklist above before using these changes in a live tournament.

Unrelated modified TRS payment/checkout files and TRS business-rule/runbook documents were observed in D:/Source/TRS and left untouched. TLS changes are confined to D:/Source/TLS; that directory has no Git metadata, so a repository diff baseline was not available.

Restart smoke check: localhost:5180 returned HTTP 200 with the updated frontend asset. Existing operational data remained revision 67: 1 event, 2 programs, 24 participants and 4 courts. TLS is running again; no operational records were changed during testing.
