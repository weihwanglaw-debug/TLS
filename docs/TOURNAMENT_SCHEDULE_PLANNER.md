# Event scheduling â€” 17 November 2026

This is the current scheduling contract. It replaces the saved-draft and partial-event saving workflows described in earlier notes.

## Operator workflow

1. **Program setup and Draw:** complete the opening draw for each active badminton singles/doubles program. Future rounds use winner/qualifier positions; results are not required to plan them. Missing draws are listed before schedule configuration starts.
2. **Schedule â†’ Durations:** set minutes per program/round, including warm-up and changeover; preferred rest warning (0â€“240 minutes); start-time steps (5, 10 or 15 minutes, default 15).
3. **Sessions:** configure real dates, opening/closing times and courts. Sessions cannot overlap, even on different courts. Apply session updates only to the current page configuration.
4. **Assign matches:** add matches to the selected session or Auto Assign remaining. Usable openings are displayed per court, aligned to the selected clock grid. A match must fit one continuous opening; separate court/gap balances cannot be combined into a match slot.
5. **Review â†’ Save schedule:** every playable match must be placed. Review actionable warnings once, then save all sessions, estimates, settings and current/future bookings in one transaction. Incomplete saves are rejected, including after a date-onward reset.
6. **Manage schedule:** validated moves/swaps save selected bookings atomically. Results are event-wide, grouped by program with each program's own actions. Program selection appears inside Seeding and Draw only.

There is one event heading and one primary fixture navigation bar. Schedule's compact Durations / Sessions / Assign matches / Review navigation sits below its main section. Footer navigation names the destination. Necessary guidance uses the shared `ui-help` presentation beside the affected control; routine explanatory paragraphs are omitted.

Session date/start/end reuse the existing `DatePicker` and `TimePicker`; match moves reuse `DateTimePicker`, as result-entry screens do. Picker display follows the existing AM/PM design while payloads retain venue-local values. Start-time grid and session validation remain server-side. The frontend UI consistency guard rejects native date/time picker inputs; run it with the normal Vitest suite. Any shared-component-internal exception requires explicit approval.

## Save and leave behaviour

There is no Save draft or Reload draft, persisted editing buffer, draft endpoint or legacy scheduler UI. Only a complete-event save persists schedule preparation. Applying a session or navigating schedule substeps writes no business data. Unsaved page work is lost on refresh, closure or crash; browser leave, sidebar/main-step navigation, Back and Refresh are guarded where the app can intercept them. A browser crash cannot be prevented. Local form state and short-lived review tokens are not recovery storage.

Changing timing settings after allocation may invalidate placements. Start-time steps are locked while current bookings or walkover slot references exist. Round durations stay locked while that round has any retained booking or walkover reference, including on dates before a reset cutoff. Unconfirmed placements must be cleared before changing their start-time grid or duration. History/audit counts never determine eligibility.

## Event dates

Edit Event can extend the date range after play starts or remove unused dates. Save validates inclusive event overlap, sessions (including legacy daily windows), current and future bookings, and actual play dates. It preserves the timetable and results. Remove an unused dated session or move unplayed matches before excluding their date. Legacy daily windows repeat across an extended range.

## Reset schedule from date

Reset Schedule reuses the shared date picker and dialog. Choose From date within the event, review affected dates/counts, then Confirm reset. The cutoff is inclusive and uses each booking's venue-local start date. Reset stays disabled while the planner has unsaved edits; save or clear those edits first.

Every affected actual match is rechecked transactionally. Any called, started, awaiting-confirmation, completed or walkover match blocks the entire operation; the preview identifies the match, date, court and reason. No partial clearing occurs. Earlier-day results and calls do not block clearing later dates. Structural BYEs are excluded. Audit/correction history alone does not block an unstarted match.

An eligible reset clears real and future-only time/court bookings from the selected date onward, affected officials/review availability and current empty scoring buffers. It retains earlier bookings/results/calls byte-for-byte, every draw/pairing, qualifiers, seeds, sessions/courts and duration/rest/grid settings. Invalidated walkover timing references are removed. No dependent draw rounds or results are deleted. Select the event's first date to clear the whole schedule, subject to the same protections. Empty ranges, invalid dates and stale revisions reject without changes.

After success, return to Assign matches with the selected/later session in view. Reassignment still requires a complete event timetable before Save. Auto Assign remains an event-wide allocation action; a date reset itself never reallocates any match. Result correction and draw reset remain separate explicit workflows.

## Allocation and validation

- Starts align to venue-local clock steps, rounded upward. Durations remain exact. With 15-minute steps, two 40-minute games at 14:00 and 14:45 leave the next usable start at 15:30. With 5-minute steps they can start at 14:00 and 14:40.
- Per-court openings replace the old estimated match-count capacity display. Adding a match that cannot fit returns a concise error without changing any placements. The admin decides whether to extend a session or add availability.
- Auto Assign keeps existing placements and either fits every remaining match or applies nothing. It prioritises eligible known players entered in multiple programs. Round dependencies always take precedence.
- It tries preferred rest and possible-player separation, then relaxes speculative separation and preferred rest. Known player/official overlaps are avoided by Auto Assign. Finals prefer the last configured playing date.
- The bounded search has six preference/order attempts with a 1.3-second allowance and 2,000,000 candidate checks per attempt. Failure means no solution found within the search, not proof of impossibility.
- Court overlaps, invalid dates/courts, session boundaries, duration/grid changes and unmet round dependencies are errors. Known planned player/official overlaps and short rest are actionable warnings the admin can accept. Possible future-player conflicts are collapsed information and do not require acknowledgement.
- Future references are keyed by program/phase/round/position. They create no fake match records. Normal result progression binds reserved times to generated real matches. Redraw/reset removes affected bookings and preserves unrelated sessions/estimates/bookings.
- Newly placed matches use open courts. Existing unchanged closed-court bookings warn, so repairs elsewhere remain possible. Other events' actual and future-only reservations are checked; normal setup also prohibits overlapping event dates.

## Saved edits and walkovers

Move changes one match's time/court using the shared date/time picker. Swap follows the draw interaction: select the swap icon beside the first match, then the second. The first row highlights; clicking it again or Cancel selection cancels. The selection remains visible when filtering programs. There is no swap dropdown in the move dialog. Selecting the second match always opens a persistent confirmation dialog showing both current and proposed times/courts. Nothing is exchanged until **Confirm swap**, including for unsaved placements. Cancel closes the dialog without changing the timetable. Saved swaps are validated by the API on confirmation; if warnings are returned, the dialog stays open for acknowledgement and confirmation. Validation failures also keep it open without changing either booking.

Swaps require equal estimated durations, including across programs. Same-program swaps also require the same stage and round. Incompatible choices are disabled with a concise reason, and the API independently rejects them. Valid swaps exchange times and courts without changing teams or draw positions; sessions, dependencies and court availability are still validated. Called matches must be released first; playing/completed/walkover matches cannot swap. Warnings relevant to a saved swap require acknowledgement in the existing shared dialog. A valid saved swap applies atomically; unsaved swaps remain local until the complete-event save. Invalid changes save nothing.

**Record walkover** reuses the existing team-selection panels in a compact winner-only dialog. It omits scoring presets, timestamps, officials and optional score details. The admin selects the winner and confirms. Normal score/result correction screens retain their existing controls. Standings and advancement rules still apply; a group win does not itself guarantee qualification. Existing `WO`/`W` codes release court capacity and any tablet reservation. The original timetable row stays visible, marked Walkover with its winner highlighted, released time and inline Undo action; there is no separate walkover list.

**Move remaining matches earlier?** appears after recording, with Keep times and Review earlier times. It is also available beside an existing walkover until earlier times have been applied. The preview moves only uncalled/unstarted bookings on the same court within the same session, never crossing a called/playing/completed match. It respects current venue time, the selected start grid, exact durations, preceding rounds on all courts, court availability and session limits. Matches that cannot move retain their times with an expandable reason. Known player/rest warnings need acknowledgement; possible conflicts remain information. Nothing moves before Confirm earlier times. A stale or changed preview applies nothing and can be reviewed again. Recording the walkover is independent: declining or failing retiming keeps the result.

**Undo walkover** uses versioned result correction. It validates the original slot and restores any times changed by that walkover in one transaction. Only compact before/after bookings are retained in `schedulePlanner.walkoverMoves`, keyed by the existing walkover match ID. Undo checks that affected bookings still match the saved after-values and remain uncalled/unstarted; it never overwrites subsequent edits or results. Successful undo removes its timing reference. Consecutive walkovers retain independent references and can be undone in reverse order. Explicit resets/redraws clean invalidated references; historical audit records never determine eligibility. Unplayed dependent draw records may be rebuilt while unaffected reserved times remain. Later started/scored/called dependent matches require explicit result correction.

## Live calls and starts

Planned times are estimates. The live queue does not shift the timetable when matches run late. Planned overlaps/rest appear as alerts and may be retained.

Calling checks current players and every assigned official role against called, playing and result-review court reservations. Messages identify the person and court. Starting rechecks availability. An overlapping planned time alone does not block a call when the resources are actually free. Tablet replacement for the same match retains existing live protections.

## API and storage

`GET /api/scheduling/planner/{eventId}` returns nodes, `plan`, confirmed bookings, findings, preparation gaps and walkover result references. `plan` contains sessions, round minutes, rest warning, placements and `startStepMinutes`.

- `POST /preview`: validate, add one match or allocate all remaining matches; no persistence.
- `POST /confirm`: single-use reviewed token, revision and warning acknowledgement; complete-event transaction.
- `POST /change`: atomic saved move/swap; returns `saved:false` with warnings until acknowledged.
- `POST /reset-preview`: revision, event ID and `fromDate`; read-only date/count/blocker preview.
- `POST /reset`: revision, event ID and `fromDate`; atomic protected date-onward booking reset, including future-only references.
- `POST /earlier-preview`: revision, event ID and walkover match ID; returns a ten-minute token, before/after bookings, unchanged reasons and findings without persistence.
- `POST /earlier-confirm`: token, revision and warning acknowledgement; rechecks live state/current time and applies the reviewed batch atomically.

Review tokens expire after ten minutes. The UI obtains a fresh preview before saving and requires another review if findings changed. Global revisions and live match state are rechecked. Removed `/draft` routes are unavailable. Remaining legacy scheduling writers reject planner-owned events; the active application uses one scheduler.

The legacy `GET /api/tournament/{eventId}/reset-schedule` now returns the same date preview (optional `fromDate` query). Its POST reset route enforces the same date rules. Old `roundKeys` payloads are rejected; neither route can silently clear results. Schedule reset uses the current revision and rechecks live state at commit; it does not use a persistence draft or alter the database schema.

Existing SQL `EventOperations` JSON stores settings/future bookings and optional compact walkover timing references; materialized match times remain authoritative in `ProgramOperations`. Walkover responses add the original booking, winner side and whether earlier times were applied. No table/column migration is introduced. Optional `startStepMinutes` defaults to 15 and `walkoverMoves` defaults empty for preceding data. Roll back matching code and JSON together because older strict readers do not accept the new member.

## Upgrade, backup and rollback

The strict JSON reader requires removal of obsolete `planningDraft` and `schedulePlanner.draft` properties before the new API starts. Stop the API first. Run the reviewed utility with the configured TLS connection:

```powershell
Set-Location D:/Source/TLS/Backend/LiveScore.Api
dotnet bin/Debug/net8.0/LiveScore.Api.dll --upgrade-schedule true --backup-file C:/path/to/new-event-operations-backup.json
```

The backup file must not exist. The utility locks scheduling storage, writes all prior EventOperations rows to that file, removes only the obsolete properties, advances the revision when needed, and commits atomically. It is idempotent with a new backup path and changes no tables, actual match times, results, participants or payments. Do not promote unfinished old editing data to confirmed bookings.

Retain matching API/frontend sources and the pre-upgrade JSON backup. Older strict binaries may reject new members. Rollback requires restoring matching event JSON in a controlled transaction and invalidating client revisions, or restoring a matching database backup. Do not blindly replace binaries, rewind revisions or overwrite later scoring work. Test databases and temporary browser files must remain outside operational data.

## Verification

Focused checks cover whole trees/byes, grids, openings, multi-program priority, full-save atomicity, soft planning alerts, active player/official reservations, same-round swaps, walkover release/undo, reset, qualification binding, restart and backed-up JSON cleanup. UI verification covers hierarchy, scope, concise guidance, both themes, guarded navigation, confirmation and direct saved edits. Test SQL databases are disposable.

17 November verification: backend build passed with zero warnings/errors; frontend production build passed with the existing bundle-size advisory. All 256 frontend tests, 158 existing rule checks, 67 planner rule checks, 240 planner HTTP/data checks and 256 live HTTP assertions passed. Reset and session suites passed 213 and 117 checks respectively. Browser verification exercised a 26-match mixed group/knockout event, complete save, walkover/undo, eligible swaps, both themes and widths 1280/1024/768/390 without page errors or outer overflow.

The subsequent shared-picker and two-selection swap correction passed 273 frontend tests, 158 existing plus 71 planner rule checks, 251 planner HTTP/data checks and 228 legacy group-scheduling checks. Both builds passed. Browser checks verified selection/cancellation, filtering, disabled incompatible choices, saved two-booking swaps and warning acknowledgement in both themes at 1280/768/390; operational writes were intercepted. No database schema change was required.

The explicit swap-confirmation correction passed all 275 frontend tests and the production build. Browser checks confirmed that the dialog remains open before confirmation, cancelling makes no save request, and warning acknowledgement still works in both themes at 1280/768/390.

The inline walkover/earlier-time workflow passed 283 frontend tests, 158 existing plus 86 planner rule checks, 264 planner HTTP/data checks, 147 dedicated walkover scheduling checks and 213 reset checks. Both builds passed. Real-browser checks against disposable SQL exercised winner-only entry, inline status/undo, Keep times, preview without writes, warning acknowledgement, earlier save and full undo in both themes at 1280/768/390. Group qualification slots, consecutive walkovers, restart persistence, stale confirmations and called/moved-booking protections are covered.

20 November date-onward reset verification: backend and frontend builds passed; 291 frontend tests, 159 existing plus 96 planner rule checks, 150 date-reset, 317 planner, 154 walkover, 269 live, 162 event-date, 226 fixture/result, 125 seeding and 154 tournament API/data assertions passed. Browser verification used real disposable SQL in both themes at 1280/768/390: month-boundary date selection, named blockers, earlier completed result retained, preview/cancel without writes, confirmation, preserved draws/sessions and return to assignment. Old round-based reset payloads remain only in negative contract tests.
