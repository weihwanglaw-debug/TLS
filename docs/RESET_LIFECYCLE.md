# Planner booking cleanup — 16 November 2026

The [whole-tournament planner](TOURNAMENT_SCHEDULE_PLANNER.md) removes selected and dependent future booking references when schedules reset. Future-only rounds can be reset explicitly. Round-duration locks derive from current bookings, never history. In-page clear-draft retains confirmed bookings; it does not reset results. Event-date changes clear obsolete planner draft availability after existing schedule-reset eligibility checks.

# Reset lifecycle (6 November 2026)

This policy supersedes older history-based reset restrictions. Current play and current results determine ordinary editing eligibility. Audit entries and correction history never lock setup, scheduling, presets or dates.

## Reset a result

Clear Result accepts a completed/walkover match with the current match version. Its confirmation explains that all later knockout rounds in the same program are removed; clearing a group result removes all knockout rounds. Those removed rounds lose their results, tablet assignments and planning references. Other programs are preserved.

The selected match retains its scheduled slot, officials and scoring preset for replay. Scores, winner, actual timestamps, result outcome/reason, scoring details, paper-resumption markers, correction records, serving state, undo/receipt buffers and court exceptions are reset. Group ranking decisions and obsolete official decisions are discarded. A concise MATCH_RESULT_CLEAR audit entry identifies the match and reason. Existing audit rows are not deleted.

## Reset Schedule

Select one or more rounds. The action clears their results as well as start/end/court, officials, review availability, live scoring state and tablet assignments. It removes all later rounds in each selected program; selecting a group stage removes its knockout rounds. Initial pairings, seeds, participants, event settings and session hours remain. BYEs retain their structural automatic advancement, without played scores.

Selecting all rounds resets the event schedule. There is no prerequisite to individually clear completed results or release called tablets. Repeating a reset is valid. Unknown rounds and stale revisions reject the entire transaction. A single RESET_SCHEDULE audit entry describes the event and selected rounds; no per-match reset audit entries are generated.

Affected draft match IDs, move IDs and quantities are cleaned. Unrelated sessions/programs and their draft quantities remain. Changed draft cards require saving again. If no draft match selections remain, the draft is discarded. Date changes clear the event planning draft and obsolete session/day settings. Removing draws/rounds also cleans their draft references.

## Device and concurrency behavior

Reset advances the match version and revokes affected tablet bindings. Old tokens cannot score a replay. An umpire receiving a definitive closed/revoked-session response clears the local pending action and returns to access-code entry. Network failures retain pending actions for retry. Revisions, command IDs and versions remain functional protections against duplicate points and stale writes.

## Same-day operation

Called and started matches must use an open court, have valid prerequisites and remain free of actual player/court/official conflicts. On the scheduled playing date, session windows and session court allocation do not block early/late operation. Early starts still require confirmation. Future-day preparation uses the allocated courts, and actual play must occur on the scheduled day within event dates.

## Publication

Confirmed results are published as complete snapshots, including an empty snapshot after clearing/resetting all results. Only successful deliveries suppress subsequent identical publications. Failures retry at max(5 seconds, configured interval). Changed destination/credential invalidates delivery signatures. Options > Submit Result is available for configured programs. Selecting it without completed/walkover results displays No result to submit. without making a request; automatic reset synchronization still sends empty snapshots. Restart sends current snapshots again using increasing persisted stream versions.

Remote clearing is asynchronous and requires publishing enabled and a reachable receiver. Existing TRS manual results remain separately owned and retain their precedence. Reset never changes TRS registration/payment data.

## Logging

Default admin audit entries use compact counts/revision rather than complete system snapshots. Explicit scoped audit views remain where provided. Existing audit entries are retained as reference and never used for eligibility. Operational histories for reset matches are removed, with only a fresh version-bearing scoring state retained to reject old commands.

## Validation

Regression coverage includes complete-result reset, selective cleanup, dependent-round removal, draft isolation, repeated reset, preset edits after clear, preserved conflict protections, closed-session tablet recovery, empty publication retries, destination changes, and SQL restart persistence.

### Executed verification

- Backend build: 0 warnings, 0 errors.
- Frontend production build: passed; existing bundle-size advisory remains.
- Full React suite with disposable SQL workflow: 219 tests passed.
- RulesChecks: 140 checks passed.
- Disposable SQL suites: reset 213, presets 92, publication 98, session confirmation 140, early-start 131, session scheduling 127, group scheduling 230 checks passed.
- Served frontend asset and local API were verified after restart. Existing event revision remained 128; match 9 is SC, unscheduled and no longer preset-locked. No real event reset was performed.

### Implementation files

Backend/LiveScore.Api: FixtureResets.cs, EventDates.cs, EventOperations.cs, GamePresets.cs, LiveRuntime.cs, ScheduleEndpoints.cs, ScheduleSlotSwaps.cs, SessionAllocation.cs, SessionCourts.cs, SetupStore.cs, TournamentEndpoints.cs, TrsPublication.cs.

Frontend/src: EventSchedule.tsx, ResetScheduleDialog.tsx, TournamentConsole.tsx, UmpireApp.tsx, ResendResults.tsx.

Tests: tests/RulesChecks/Program.cs, tests/fixture_reset_integration.py, tests/round_preset_integration.py, tests/trs_publishing_integration.py, tests/early_start_integration.py, Frontend/tests/umpire-pairing.test.tsx, Frontend/tests/resend-results.test.tsx.

Documentation: this file, SCHEDULING.md, FIXTURE_RESULTS.md, LIVE_DASHBOARD.md, TRS_PUBLISHING.md. Existing unrelated working-tree changes are preserved.
