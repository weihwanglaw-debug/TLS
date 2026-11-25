# Current scheduling reset behaviour

See [Event scheduling](TOURNAMENT_SCHEDULE_PLANNER.md). Schedule reset clears bookings from a selected date onward, inclusively, and blocks if any affected match is called, started, awaiting confirmation, completed or a walkover. It never clears results or deletes draws. Earlier bookings/results and all session availability remain. Byes retain automatic advancement and do not gain live-score rows. Duration/grid locks derive from current bookings and walkover correction references, not history. There is no persisted scheduling draft.

Undo walkover validates the original slot, preserves future reserved times and rejects silent removal of later called/started/scored matches. Unplayed dependent draw records may be rebuilt by existing result correction. Optional earlier-time changes require an explicit preview and confirmation; undo restores their compact before/after timing batch only while the affected bookings are unchanged and uncalled/unstarted. Consecutive walkovers keep independent references for undo in reverse order. Successful undo, resets and invalidating redraws clean those references; audits never block eligibility.

# Reset lifecycle (6 November 2026)

This policy supersedes older history-based reset restrictions. Current play and current results determine ordinary editing eligibility. Audit entries and correction history never lock setup, scheduling, presets or dates.

## Reset a result

Clear Result accepts a completed/walkover match with the current match version. Its confirmation explains that all later knockout rounds in the same program are removed; clearing a group result removes all knockout rounds. Those removed rounds lose their results, tablet assignments and planning references. Other programs are preserved.

The selected match retains its scheduled slot, officials and scoring preset for replay. Scores, winner, actual timestamps, result outcome/reason, scoring details, paper-resumption markers, correction records, serving state, undo/receipt buffers and court exceptions are reset. Group ranking decisions and obsolete official decisions are discarded. A concise MATCH_RESULT_CLEAR audit entry identifies the match and reason. Existing audit rows are not deleted.

## Reset Schedule

Choose From date, review the affected dates and booking counts, then Confirm reset. The transaction checks every affected match again and rejects the whole reset if any is called, started, awaiting confirmation, completed or a walkover. Blockers are named in the preview. Releasing a call or correcting results is a separate deliberate operation; reset cannot perform it implicitly.

For an eligible range, clear start/end/court, affected officials, review availability and empty live buffers, including future-only reservations. Keep all draw records/pairings, seeds, rankings/qualifiers, sessions/courts and duration settings. Earlier-day calls, bookings and results remain unchanged. A round spanning the cutoff keeps its duration locked while any earlier booking remains. Structural BYEs are untouched. Empty ranges, dates outside the event and stale revisions reject without changes. A single RESET_SCHEDULE audit entry records the event/date; no per-match reset audits are generated. Both reset API routes use `fromDate`; old round-selection payloads are rejected.

Affected future bookings and walkover timing references are cleaned. Unrelated sessions/programs and bookings remain. There is no persisted schedule draft. Date changes clear obsolete session/day settings; removing draws/rounds also cleans their booking and undo references.

## Device and concurrency behavior

Reset advances affected unstarted match versions. Called bindings in the reset range block the transaction; bindings before the cutoff remain untouched. Explicit result correction/reset still revokes its affected tablet sessions. Old tokens cannot score a replay. An umpire receiving a definitive closed/revoked-session response clears the local pending action and returns to access-code entry. Revisions, command IDs and versions protect against duplicate points and stale writes.

## Same-day operation

Called and started matches must use an open court, have valid prerequisites and remain free of actual player/court/official conflicts. On the scheduled playing date, session windows and session court allocation do not block early/late operation. Early starts still require confirmation. Future-day preparation uses the allocated courts, and actual play must occur on the scheduled day within event dates.

## Publication

Confirmed results are published as complete snapshots, including an empty snapshot after explicit result correction clears all results. Schedule reset preserves recorded results; it does not publish a fabricated empty result set. Only successful deliveries suppress subsequent identical publications. Failures retry at max(5 seconds, configured interval). Changed destination/credential invalidates delivery signatures. Options > Submit Result is available for configured programs. Selecting it without completed/walkover results displays No result to submit. without making a request. Restart sends current snapshots again using increasing persisted stream versions.

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
