# Tournament operation

1. Set venue court count in Master Setting.
2. Import your event from Excel or select an eligible event from Get from TRS. Imported event/program/player identities stay read-only.
3. Open Event Details. Add officials' names. Master Setting defines venue courts; select the courts to use for each date in Schedule. Playing sessions and timing are configured separately under Fixture and Result → Schedule Event.
4. Create rules in the standalone Game Setting Preset module. No scoring editor appears in Fixture and Result.
5. Review seeding from the clickable Seeds / Participants column. Use Continue to open the next unfinished step in the persistent Seeding → Draw → Schedule → Fixture & Results workspace. Save seeding and Continue to Draw; earlier steps remain accessible. Choose format, preview/swap, then Confirm & Save. No dates, courts or officials are allocated during drawing. Heats stays disabled for badminton.
6. Preview first-draw entries can be swapped using the button beside each entry. Back returns to the same selections. Draft configuration, seeds and fixtures are not saved until confirmation. Preview expiry/catalog changes require a fresh preview.
7. Open Schedule Event from the event/program list or saved draw. Choose dated sessions, court subsets and generated program rounds per date; use the event-date dropdown and Select All eligible current stages as needed. A stage allocated elsewhere shows its date; Move Round explicitly previews a whole unplayed/unpaired stage relocation. Set duration/rest, preview proposed slots and unplaced matches, then confirm. Existing assignments remain unchanged. Select a saved preset and official before tablet code issuance; manual results use the same frozen rules.
8. Enter manual complete-game scores or a walkover, or confirm tablet results. Optional actual times, shuttles, cards and challenges use the same record. Never invent point-by-point history from final scores. Switch a playing tablet match to paper before manual reconciliation.
9. Generate the selected program's next round after its own results/standings permit advancement. Other programs never block it. Add the generated round to the desired date through Schedule Event; existing slots remain unchanged. Round Robin ends with standings.

## Progression and scheduling

Round Progression is removed. Legacy I/A values remain readable for compatibility, but both operate independently per program. Required own-round results, standings/tie resolution, BYEs and dependent-round correction protections remain enforced. Scheduling does not generate draws or reserve unknown future-round fixtures.

Dated sessions use the venue timezone and event dates. Match estimates must fit within one session, with court capacity, player clashes/rest and own-round prerequisites checked. No-fit matches remain visibly unscheduled in preview and may be scheduled later; confirmation saves the proposed valid slots and planning settings only. Unscheduled matches cannot pair tablets or accept results. Officials are selected when calling matches or through individual match settings, so initial planning does not require an officials list. Existing session/court references are protected. See [Scheduling](SCHEDULING.md).

Only active badminton singles/doubles generate/scoring-pair fixtures. Other sports support manual/no-seeding review. Match overrides remain validated and playing/completed matches cannot be silently moved.

## Persistence and API

SQL/EF remains authoritative. EventOperations JSON now holds dated Sessions and ScheduleDays; existing daily defaults carry into the date editor without changing stored data until confirmation. Program matches can be SC with null court/start/end pending scheduling. No migration, table/column deletion or imported-data rewrite is needed.

POST /api/tournament/preview accepts additive drawOnly=true for independent draw previews; current UI omits timing. Legacy scheduled-generation requests remain compatible. POST /api/scheduling/preview and /confirm manage revision-checked, expiring event schedule drafts. PUT /api/tournament/schedule retains validated manual adjustment of unplayed matches. Existing draw/result/preset endpoints remain. All operator endpoints are loopback-only.

Rollback: restore matching frontend/backend and rebuild; retain all business data. Before returning to older code, schedule newly created unscheduled draws through the current scheduler. Do not clear operation tables. See SCHEDULING.md for affected files and verification.

Tests use disposable TLS_Test databases, never TLS/TRS. Run import_integration.py, tournament_integration.py, live_integration.py, fixture_result_integration.py, event_workflow_integration.py and frontend_workflow_integration.py. The last runner tests actual React screens against its isolated SQL API. Physical tablet/network and visual layout checks remain separate.

Current tablet serving setup, adaptive layout, per-match review availability and TRS control changes: see UMPIRE_SCORING.md. Event Details ends with Programs; it no longer configures review-equipped courts.

## Date editing before fixtures

Event Details always shows Edit Dates. Clicking fetches current fixture availability: any saved match in any program, including BYE, unplayed, playing, review, completed or walkover, produces the alert Event dates cannot be changed because fixtures already exist. Unsaved previews do not lock dates. The server enforces the same event-wide lock. Existing fixture/reset protections are unchanged: clear only permitted unplayed draws through Fixture and Result, never delete completed records just to enable dates. The editor does not move any fixture, change source IDs or update TRS.

Date-only ranges reserve both endpoints and must not overlap any other saved event. Saved daily hours repeat across the revised range. Legacy explicitly dated sessions must still lie within it, and timezone/DST checks remain in force; an invalid range/session change is rejected atomically. Existing participant validation is rerun using the current master-timezone date, preserving TLS's existing age calculation rather than silently changing it to event-day age. Date updates are revision protected and audited as EVENT_DATES with old/new event snapshots. A failure leaves event dates, operations and imported associations unchanged.

For the shared four-step header, saved draw list, default Table View, preset defaults and required default presets and per-match scoring protection, see [Guided fixture planning](FIXTURE_PLANNING.md).

## Practical result and qualification recovery — 30 October 2026

Badminton group order uses wins, head-to-head when two competitors remain tied, then game and point differences for larger ties. Each narrowed two-way tie is rechecked head-to-head. Unresolved ties are shown as ties; IDs/names never decide qualification. Resolve the tied positions with a reason before generating affected qualifiers. Decisions are tied to the actual group results and become stale when results change. A lower tie that cannot affect qualification does not block advancement.

Group qualifiers keep origin labels (A1, A2, etc.). The initial knockout placement separates same-group opponents where a valid crossover is available, preserving each qualifier exactly once. Review the generated draw before confirming. Existing initial-draw manual adjustments remain available.

Official results support short codes N (normal), W (walkover), R (retirement), D (disqualification), M (admin decision). Retirement retains actual completed and partial game scores; an unfinished game does not count as a completed game in standings. Group results are retained after retirement/disqualification; TLS does not automatically purge a withdrawing competitor's previous results.

Use Exceptional / Override Official Result from the fixture result editor. Choose winner, actual scores and reason, review the listed affected matches, then confirm. Safe unplayed dependants update. Started/submitted/completed dependants require an explicit choice: retain existing participants/outcomes or reset the listed matches for replay. Replay revokes affected tablet assignments, preserves before/after history, and blocks later matches until corrected predecessors finish. Replay retains planned slots; the organiser must review their suitability. Group-result corrections conservatively list the entire existing knockout draw. An unresolved corrected group or an older draw lacking qualifier origins cannot be automatically requalified: explicitly retain the existing draw, then record the referee's ranking decision, or reset an unplayed draw using existing reset controls.

Result impact review includes a fingerprint of match/dependant state and bindings; changed state requires another review. Existing revision and match-version guards still apply. Official history includes timestamp, reason, before/after snapshots and downstream decision. No new identity system was introduced: these actions retain local-console authorization. Normal Call → Pair → Score → Submit → Admin Confirm is unchanged.
