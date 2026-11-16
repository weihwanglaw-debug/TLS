# Whole-tournament schedule planner — 16 November 2026

This is the current scheduling workflow. It supersedes the event-wide duration and current-round-only UI described in older scheduling notes. Draw/seeding, live scoring and normal result-confirmation progression remain separate.

## Operator steps

1. Generate opening draws for the active badminton singles/doubles programs. The planner derives playable group and knockout rounds, including future rounds. Byes consume no court time. Missing draws are named; full Auto Assign/confirmation waits for them, while manual draft work remains available.
2. Open Schedule. **Match durations:** set minutes per program/round. Include warm-up and changeover. Configure a preferred rest gap; this is advisory, not a compulsory break. Existing booked rounds are locked; reset the relevant schedule before changing estimates. Historical logs do not control this lock.
3. **Sessions:** set actual dates, start/end and specific open courts. Sessions on one date never overlap, even on different courts. Save availability as a draft. No match bookings are made by saving a session.
4. **Assign matches:** choose a session and use round −/+ controls, or Auto Assign remaining. A later round follows the complete preceding round's planned completion. Group qualification slots and future winners remain planning references, not invented entries or results.
5. **Review & confirm:** timetable grouped by date with a program filter. Totals and validation always cover the whole plan. Check the complete timetable, review actionable warnings, then confirm. The page becomes Manage schedule. Existing fixture/results screens show materialized match bookings; future planning references remain in the planner until normal progression generates those matches.

Save draft and step navigation retain the current planning configuration. Routine saving uses inline feedback. Clear draft allocation removes only unconfirmed placements, keeping confirmed bookings, sessions and estimates. Reload saved draft explicitly replaces local unsaved changes. Browser refresh returns saved state; use Save draft before leaving via browser navigation. The in-page Back action saves a valid configured draft before leaving.

## Placement rules

- Every newly planned start uses a **15-minute grid**. Earliest legal start is rounded up; durations remain exact. A 40-minute match finishing at 14:40 plus 30 minutes preferred rest gives a 15:15 start. Manual moves/swaps use the same grid.
- Session availability can start/end at any minute; the 15-minute grid applies to match starts, not the venue's opening hours. Unchanged legacy bookings keep their saved times; a changed/new placement follows the grid.
- Auto Assign preserves current placements. It tries preferred rest and separation for possible shared players, then alternative program order, then relaxes speculative separation, and finally preferred rest. Finals target the last playing date, with an earlier date reported as a warning when needed.
- Initial automatic allocation is all-or-nothing: if any remaining match cannot be placed, no new automatic placements are applied. Sessions and the current draft remain. Manual allocation may be incomplete while editing. Initial confirmation requires every playable slot in included active programs.
- The search is bounded: six deterministic preference/order attempts, at most2,000,000 candidate court checks per attempt and an 8-second overall placement budget. Failure means no complete solution was found within that search, not mathematical proof of impossibility. Add availability or adjust manual choices.
- Court-minute totals are an availability budget, not a match-count capacity guarantee. Separate short gaps cannot fit a longer match.
- Same court, known-player or known-official overlaps and unmet dependencies are errors. Rest below preference and guaranteed knockout-winner rest are warnings the admin may accept using **one shared acknowledgement**. Possible qualifier clashes/rest are collapsed information with a count and **never require acknowledgement or block confirmation**. Group qualification rest is speculative until the actual qualifier is known.

## Changes after confirmation

Choose Change on an unstarted match:

- Move: choose venue-local date/time and court inside a saved session.
- Swap: exchange starts/courts with another eligible match.

Both retain each match's own duration. Unequal-duration swaps are valid only if both resulting intervals fit. Check shows the proposed changed rows and warnings. Apply changes to the draft, then check and confirm the full timetable to save current bookings atomically. No neighbouring matches move silently.

Called matches must be released first. Playing/completed matches are fixed. Reset is a separate explicit action and uses existing result-reset semantics: selected and dependent later schedules/results are cleared, while draw structure, session availability and duration settings remain. Future-only round bookings can also be reset. No audit-history condition keeps a cleared round locked.

**Planned times are estimates during play.** TLS calls matches through the live queue. Delays do not automatically rewrite the timetable. No shift-everything operation is introduced.

## Future rounds and live checks

Slot identity is program/phase/round/position, with a source-draw fingerprint. Slots contain source-match/qualifier labels and potential player sets. They do not create TournamentMatch records before normal draw progression. When confirmed results and any group tie decisions allow the existing progression to generate a round, the existing slot binds to the real match exactly once and transfers its confirmed start/court/duration.

For the common two-groups/two-qualifiers format, labels identify group winner vs opposite runner-up. Other qualifying formats use neutral qualifier-slot labels because seed-based placement is not yet resolved; candidate membership is conservative. Planned byes are excluded from court booking count.

On actual qualification, the planner recalculates known player sets. Dashboard queue messages name known overlaps; calling/starting a conflicting match is rejected until adjusted. Other matches remain usable. Dashboard rest notices are advisory; they do not force a rest gap or shift bookings. An active player's match must finish/release before another match for that player can be called.

## Persistence and API compatibility

EventPlan adds optional `schedulePlanner` JSON containing one draft, confirmed future-booking references, round minutes, rest-warning preference and source fingerprints. Existing EventOperation JSON storage is reused. Actual match Start/End/Court/DurationMinutes remain authoritative once a slot materializes. No table, column or migration is added.

New local-admin endpoints under `/api/scheduling/planner`:

| Route | Purpose |
|---|---|
| GET `/{eventId}` | Slots, current draft/bookings, preparation gaps and findings |
| POST `/draft` | Save incomplete configuration without booking matches |
| POST `/preview` | `check`, `add` or complete `auto`; no persistent mutation |
| POST `/confirm` | Validate latest state and atomically save reviewed placements |
| POST `/reset` | Explicit round reset, including future-only slots |

Preview tokens expire after 10 minutes and are single-use after confirmation. Definitive errors leave the draft/bookings intact. Match state is rechecked at confirmation, so calling/starting a match since preview cannot move it inadvertently. Existing request revision checks remain; scoring changes use existing per-match versions. History counts never determine eligibility.

Legacy scheduling endpoints remain available with their old contracts. The former frontend implementation remains a named legacy export for regression coverage; the application entry uses the new planner. Legacy saved daily sessions receive explicit court defaults on planner read. The event-wide estimate remains readable as a fallback for old/unconfigured round data, but is no longer an editable Event Settings control. Existing bookings are not silently regenerated.

The dashboard queue adds optional `scheduleWarnings`; existing queue fields and tablet contracts remain intact. When using the new planner, legacy manual override validation also enforces the 15-minute start grid.

## Verification and rollback

Focused rule tests cover full trees/byes, rest preferences, exact durations/grid, atomic no-fit, manual preservation, RR/group qualification and 120-match scheduling. SQL integration covers persistence/restart, real qualifier materialization, stale tokens, duration locks and reset cleanup. Frontend tests cover the four-step UI, filtering, warnings, failed auto assignment and move checks. Real-browser checks use disposable SQL/API and built frontend assets; no operational tournament records are test fixtures.

Deploy matching frontend/backend only after review. This task builds but does not publish web assets or restart normal services. There is no database migration.

**Rollback requires care:** shared JSON deserialization rejects unknown fields. An older API binary cannot safely read JSON containing `schedulePlanner`. Before any future deployment, retain a pre-change operational backup. Restoring the preceding application requires either that matching backup or a separately reviewed conversion removing only the new planner property after preserving materialized match times. Never blindly install older binaries on newly written planner data. No rollback data conversion is run by this task.

