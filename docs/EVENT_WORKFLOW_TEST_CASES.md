# Event workflow test cases — revised 21 October 2026

The former Round Progression setting is removed. Every program advances on its own confirmed results. A coordinated event is planned by choosing dates for rounds, rather than blocking draw generation across programs.

## X/Y on day one, Z on day two

Use an event with four courts and at least two dates. Review seeding and generate opening-round draws for X, Y and Z without sessions or officials. In Schedule Event, add dated sessions and select X/Y opening rounds for day one and Z for day two. Preview and confirm. Finish X's required results while Y remains unfinished, generate X's next draw and select that new round for day two. Existing Z slots remain unchanged; player rest/conflicts are respected. Unplaced matches are reported, not forced outside sessions.

## Coordinated programs

Generate each program's draw independently. Schedule selected opening rounds on the chosen date. The admin may wait until both programs finish before generating/scheduling their next rounds, but TLS does not enforce cross-program waiting or matching bracket sizes. Required own-round results/standings still block invalid advancement.

## Validation and preservation

Overlapping or out-of-event sessions, invalid courts, duplicate match dates and foreign match IDs are rejected. Too-short capacity produces a visible unplaced list; preview never saves slots. Existing saved matches are not moved. Session edits cannot exclude existing scheduled slots. Unscheduled matches cannot pair or record results. Stale/replayed schedule previews are rejected. Original participant IDs/clubs, entries, draws/results and recorded schedules survive SQL restart.

Run tests/scheduling_integration.py for the three-program/two-day case, tests/event_workflow_integration.py for both legacy progression values now advancing independently, and tests/frontend_workflow_integration.py for actual SQL-backed screens plus date-selection and explicit-move component cases. All use disposable SQL only.

## Guided planning and explicit moves

Continue should open the next unfinished step and preserve event/program context while navigating Seeding → Draw → Schedule → Fixture & Results. Verify initial and projected bracket connectors, saved planned metadata in bracket/table, and secondary commands under Options. A completed final/Round Robin must not advertise a fictitious next draw.

Select All affects only eligible current generated stages. A stage allocated on another day is disabled and names that date. Moving it requires Move Round → Preview Move → Confirm; preview alone leaves SQL unchanged. Reject partial-stage moves, insufficient capacity, played/paired/corrected stages and silent date reassignment. A valid whole-stage move preserves unrelated slots, match IDs, presets and official roles. Historical split allocations remain readable without rewriting them.

For the shared four-step header, saved draw list, default Table View, preset defaults and required default presets and per-match scoring protection, see [Guided fixture planning](FIXTURE_PLANNING.md).
