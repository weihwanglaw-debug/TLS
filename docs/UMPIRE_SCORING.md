# Umpire scoring and TRS controls — 19 October 2026

## Organiser workflow

TLS reuses TRS's Switch and ActionFeedbackDialog components. On/off choices use toggles; explicit save/error feedback appears in a modal prompt. Connection state, unsaved acknowledgements and scheduling information remain persistent status indicators rather than repeated popups.

Event Details now contains event information, daily sessions, the event court pool, duration/rest, progression and officials, followed by Programs as its final section. Review-equipped courts are no longer configured at event level.

When scheduling or assigning a match, the organiser decides whether review is available for that match on its assigned court. A challenge-enabled preset requires this confirmation before tablet pairing. Moving an unplayed match to another court clears the previous availability decision unless explicitly confirmed again. Started matches keep the decision during tablet replacement. TLS records challenge outcomes; it does not operate external video equipment.

## Tablet workflow

1. Scan the organiser's QR to connect automatically, or open the venue /umpire URL and enter the access code. The invitation expires after three minutes; an established session is not limited to three minutes. The preset is already selected and frozen onto the match.
2. Choose Set up & start match. Confirm serving side, server, receiver and which team is on the umpire's left. Singles have one selectable player per side; doubles have two.
3. The court diagram shows each player once, scores inside the court, a server-to-receiver arrow, player card markers, challenge balances, shuttle count, game scores, elapsed time and venue acknowledgement. Point buttons follow the physical court sides when ends change.
4. A point changes score and serving positions on the server. Undo restores the previous score, service positions, ends, game boundary, interval deadline and rally-log length. Shuttle/card/challenge records remain separate and are not silently removed by a point undo.
5. At the next game, confirm the winning side's chosen server and the receiving player. Ends change. The deciding-game end change follows the saved preset. Interval timers are guidance; scoring is not automatically suspended by a countdown.
6. Cards, challenges, position corrections, setup and the scoresheet open in dialogs. Corrections to an already configured playing match need a reason. Cards and challenge outcomes do not automatically award points or decide a disqualification result.
7. Submit the completed match for organiser confirmation. A replacement tablet receives the same saved score, serving state, rules, metadata and recorded rallies.

The serving model tracks service-court positions rather than player movement during a rally. Its service rotation follows the BWF Laws of Badminton, section 4.1, laws 10–12: [BWF source](https://system.bwfbadminton.com/documents/folder_1_81/Statutes/CHAPTER-4---RULES-OF-THE-GAME/SECTION%204.1-%20Laws%20of%20Badminton.pdf). Scoring thresholds and intervals come from the organiser's saved preset.

## Portrait, landscape and scoresheets

### Tablet popups and touch controls

All umpire dialogs fill the viewport: setup, match actions, cards, challenges, scoresheet, paper handover, final submission and error feedback. Their heading and bottom action bar remain visible while only the body scrolls. Umpire dialogs have a labelled 48-pixel Close control and large Cancel/Back actions; feedback has a 56-pixel OK button. Admin-console dialogs keep their existing size and appearance.

Setup, player/card and challenge selections use large buttons instead of native dropdowns. Selected choices use the theme primary colour and a check mark. A singles server or receiver is displayed automatically because there is only one valid player; doubles offers the two relevant players. Next-game serving-side and end restrictions are unchanged. All choice buttons are type=button, so selecting an option cannot submit the form. Explicit confirmation still sends the existing command, and cancelling discards unsaved choices. Correction reasons and paper reasons remain text inputs.

All dialog buttons have at least 48×48-pixel touch targets, choices are at least 72 pixels high, and confirmation buttons at least 56 pixels high. Scoresheets share their layout with the organiser console; only the console offers a print action. These dialogs resize within browser full screen without changing the browser full-screen state.

Files: `Frontend/src/UmpireApp.tsx`, `umpire.css`, `UmpireChoices.tsx` (new), `components/ui/dialog.tsx` (optional showClose flag, default unchanged), `Frontend/tests/umpire-choices.test.tsx` (new), `Frontend/tests/workflow.test.tsx` and this guide. No database, backend/API or scoring-rule changes. The umpire-specific feedback replaces only the umpire's use of the shared admin popup. Rollback restores these frontend files and rebuilds/copies the assets.

Verification on 19 October 2026: frontend build passed. All 12 focused tests passed: five React workflows against disposable SQL (including cancellation before starting, point acknowledgement, scoresheet and paper handover), four browser-fullscreen component tests and three choice-control tests. Chrome verified full-size setup at 1024×768 and 768×1024 and scrolling content at 680×488. Actual measured buttons met the 48-pixel minimum, with header/footer remaining visible. A separate disposable doubles SQL match verified card/challenge choices, scoresheet, paper and full-size error feedback. No changes were saved to the user's match during browser review, and the disposable SQL server/database were removed afterwards. Physical tablet touch, virtual keyboard and printing remain venue UAT.

### Full Screen button

The umpire header includes Full Screen on both the pairing page and the assigned match. Tap it to request the browser's Fullscreen API; Exit Full Screen leaves that mode. The whole document is used so scoring dialogs and feedback remain visible. Browser-driven exits update the button through fullscreenchange. The application never enters full screen automatically, locks orientation, or changes scoring/pairing state. Unsupported or rejected requests show the existing TRS feedback dialog. Windows users can also exit with Esc; Android exit gestures depend on the device/browser.

This is a browser feature, not an APK or kiosk lock. Windows Chrome/Edge and Android Chrome are intended targets where permitted by the browser. Test a physical tap on the venue tablet before use; browser restrictions, embedded browsers and policy can reject a request. Browser full screen is separate from F11 and is not restored automatically after refresh.

Changed files: `Frontend/src/UmpireApp.tsx`, `Frontend/src/umpire.css`, `Frontend/tests/umpire-fullscreen.test.tsx` and this guide. No API or database changes. Four focused component tests passed for entry/exit calls, external exit notification, unsupported browsers and rejected request/retry, using browser-API stubs only (no fixture/score mocks). Frontend production build passed. Chrome automation exercised the rejection popup and responsive placement, but could not enter real full screen: automated activation was rejected. Actual Windows/Android entry and OS exit therefore remain a manual UAT check. Rollback restores these frontend files and rebuilds/copies the assets; no server restart is needed.

The main scoring page uses the available viewport height. Landscape puts scores at the outer edges; portrait places scores above each team's player positions and arranges secondary controls in two rows. Main scoring controls do not require page scrolling at the verified tablet sizes: 1024×768, 768×1024, 960×600 and 600×960. Secondary dialogs may scroll when needed. Physical tablet/browser chrome and touch use still need venue testing.

The scoresheet displays the acknowledged rally log with game, server, receiver and score, plus final games/cards/challenges/shuttles. It can be printed from the organiser console scoresheet dialog. It contains only recorded information. Older matches and manually reconciled results are marked as having incomplete rally history; final scores never fabricate prior rallies.

## Connection loss and paper

Each action retains the existing operation ID and per-match version checks. The tablet retains one pending command and cannot add more points until it is acknowledged or reconciled. Heartbeats do not pretend that a pending point has been saved.

Continue on paper, while connected, explicitly revokes tablet scoring and keeps the court occupied. The organiser subsequently reconciles the complete result. While disconnected or with an unacknowledged action, the dialog shows the last confirmed score and explains that the server has not confirmed paper mode; inform the organiser and record any uncertain point on paper. There is no automatic merge of competing paper/tablet histories.

## SQL, API and compatibility

There is no table or column migration. Existing SQL/EF JSON records gain:

- MatchLiveRecords: Serving, IntervalUntil, Rallies, RallyHistoryComplete and extended undo snapshots.
- Fixture JSON: ReviewAvailable per match.
- Match player responses: side A/B, plus current server/receiver IDs and serving/rally data.

Old rows without serving data remain readable. They must explicitly confirm current positions before further tablet points; previous rallies are not invented. Old event ReviewCourts data is retained only to read legacy match assignments, and is not enforced for new assignments. No startup data rewrite or test data insertion occurs in TLS.

Deploy the matching frontend and backend together. START and NEXT tablet commands now require a serving object `{serverSide, serverId, receiverId, leftSide}`. SETUP confirms/corrects a playing match's positions; existing positions require a reason in remark. PAPER uses remark for its reason and returns the last match view while revoking its token. Pairing and schedule requests accept reviewAvailable. Score/state responses add serving, serverId, receiverId, intervalUntil, rallies, rallyHistoryComplete, reviewAvailable and player side.

An older strict JSON reader cannot be used against newly saved fields without a matching database restore or a separately reviewed downgrade. No automatic downgrade or backup was created for this update. Existing TLS/TRS data was not cleared or reseeded.

## Verification

Run `python tests/umpire_serving_integration.py`, the existing import/tournament/live/fixture/event-workflow suites and `python tests/frontend_workflow_integration.py`. They create disposable SQL databases and never populate TLS/TRS. Coverage includes singles/doubles rotation, setup validation, next-game choices, undo across deciding ends, restart/replacement, legacy recovery, paper handover, TRS dialogs/toggles and Programs last. The browser review used a separate disposable SQL match and checked actual geometry for visible controls and absence of page scrolling at all four tablet sizes.

### Completed checks

- Frontend and backend builds passed; backend had zero warnings/errors.
- 1,043 HTTP checks passed: 240 import, 150 tournament, 162 live lifecycle, 214 fixture/result, 196 event workflow and 81 serving/recovery.
- Five React screen-flow tests passed against an isolated SQL API, including TRS toggle/dialog behaviour, Programs last and tablet setup/scoring/scoresheet/paper controls.
- Chrome verified 1024×768, 768×1024, 960×600 and 600×960: page dimensions matched the viewport and main scoring controls stayed visible. The test SQL database and browser tabs were removed afterwards.
- Saved screenshots are in D:/Source/TLS-Test-Files/outputs/20261019. Actual tablets/network and physical printing remain venue UAT checks.

## Files changed

Backend files under `Backend/LiveScore.Api/`: `BadmintonServing.cs` (new), `LiveModels.cs`, `LiveRuntime.cs`, `SqlStorage.cs`, `TournamentModels.cs`, `TournamentRules.cs`, `TournamentScheduler.cs`, `TournamentEndpoints.cs`, `SetupEndpoints.cs`.

Frontend files under `Frontend/src/`: `ActionMessage.tsx` (new), `UmpireApp.tsx`, `umpire.css` (new), `live.css`, `LiveDialog.tsx`, `live-types.ts`, `tournament-types.ts`, `EventSettings.tsx`, `FoundationPages.tsx`, `SetupApp.tsx`, `EventImport.tsx`, `GameSettingPresets.tsx`, `TournamentConsole.tsx`, `LiveDashboard.tsx`, `FixtureViews.tsx`, `components/ui/ActionFeedbackDialog.tsx` (copied from TRS), `components/admin/fixtures/FixturePreview.tsx` and `components/admin/fixtures/ScoreModal.tsx`. The existing TRS Switch component was reused.

Verification files: `tests/umpire_serving_integration.py` (new), `tests/live_integration.py`, `tests/frontend_workflow_integration.py`, `Frontend/tests/workflow.test.tsx`. README and the database, foundation, tournament, dashboard and fixture/result markdown guides were updated. No TRS source files were modified; its unrelated payment changes remain outside this task.


## Pairing and console calling — 27 October 2026

See LIVE_DASHBOARD.md for the three-minute QR/manual invitation flow, expiry regeneration and match-specific session replacement. QR scanning removes its invitation fragment from the address bar immediately. A saved pending score is retained and must be reconciled before switching matches. A replacement inherits authoritative scores/serving positions; its successful pairing revokes the old token. Temporary network loss allows normal reconnection without new pairing. Console Cancel Call disconnects an unstarted tablet assignment and frees the court and umpire while preserving the saved schedule. Started matches cannot be cancelled this way. The organiser is responsible for player readiness/rest at actual call/start; court and umpire availability are still checked by TLS. Scheduling retains existing rest rules.

## Browser storage failure — 30 October 2026

Storage failures no longer prevent pairing/rendering or leave the send lock held. The current match and unacknowledged command are kept in memory before network transmission. Retry uses the same operation ID. A persistent on-screen warning tells the umpire to keep the page open until the venue acknowledges the action. Closing/reloading a page with unavailable storage cannot preserve its in-memory command; reconcile with the organiser if this occurs. Existing device replacement, server receipts, stale-command rejection and normal offline recovery remain unchanged.

## Scoreboard and early starts — 30 October 2026

The umpire header shows the umpire name, event, court, match and connection state. The redundant footer row and TLS branding are removed. Shuttle control stays on the main screen; unavailable challenge counters are hidden. Server, receiver and starting-end setup remain unchanged for singles and doubles.

Starting before the planned time prompts the umpire to confirm. The tablet command includes `confirmEarlyStart: true`; the server independently checks it and permits early play only on the scheduled playing date within event dates. Outside an active session, the assigned court must be allocated in the session containing the planned start. During an active session, its existing court restrictions still apply. Closed courts, occupied courts/officials and unresolved prerequisites remain blocked. The actual start is recorded independently; no planned match times are moved. Player readiness/rest remains the umpire/organiser's responsibility.

`earlyStartMinutes` is supplied by the server in match views. `GET /api/dashboard/scoresheet/{matchId}` is a local-console-only read endpoint for scheduled, ongoing and completed matches. Dashboard and Fixture Table View use the same ScoresheetButton viewer and PaperScoresheet renderer; the umpire uses that same renderer without a print button.

Pairing links are handled on initial load and subsequent hash/history navigation. Match Actions → Change match opens pairing, preserving the previous confirmed session until replacement succeeds. An active assignment on the server must still be released before that tablet joins another match. Pending scoring actions block switching and are never discarded. Expired/revoked sessions without pending actions return to pairing. A fresh match token replaces the cached match only after successful pairing.

## Session-specific umpire URLs — 30 October 2026

`/umpire` always displays access-code entry, even if the browser has a saved match. Successful code/QR pairing navigates to `/umpire/{sessionId}` using the existing public binding ID. The eight-character access code and bearer scoring token are not used in that path. QR invitations retain their existing one-use `#pair=` flow and the fragment is removed after processing.

Refreshing a session URL only resumes a matching locally stored assignment. The server validates its token, session ID and match before scoring is enabled. A different session URL or a copied URL on an unpaired device returns to access-code entry; it never opens whichever match was cached. Back/forward navigation follows the URL. Expired or released credentials return to `/umpire` with an explanation. Refreshing a paper-mode session also returns to access-code entry because tablet scoring has been revoked.

Entering the access page does not erase a pending scoring action. Pairing another match is blocked until it is reconciled. A Resume pending match button navigates to the original session for retry; if that session has been revoked, the pending operation is preserved for organiser recovery. Normal saved matches are not automatically resumed from the access-code page.

Implementation: `Frontend/src/main.tsx` recognises nested umpire routes; `Frontend/src/UmpireApp.tsx` manages the URL and validates the matching session. Pairing/storage/SQL workflow tests cover these paths. No backend/API contract or database schema changes are required.

Verification: frontend build and all 147 tests across 25 files passed, including the disposable SQL-backed React workflow. HTTP checks for /umpire and a nested session route returned 200. Updated frontend assets were published without restarting the backend. The existing frontend chunk-size advisory remains.

## Clear result review and player-aligned scores — 30 October 2026

The umpire court panels display earlier game scores beside each side's large current score. The score history follows the team when ends change and supports doubles and deciding games. A completed game still shown as the current large score is not duplicated in the smaller history. The combined unlabelled previous-game score is removed from the top bar.

Console court cards align player/team name, earlier game scores and current score on each row. Court/match identity, elapsed time, umpire and connection status remain visible; device name, IP and last heartbeat are grouped under expandable Device details.

Umpire Submit Match Result and console Confirm Match Result both render the existing PaperScoresheet component. The console confirmation dialog is wider and keeps its confirmation/cancel actions in a sticky footer. Umpires can return to the scoreboard before submitting; console confirmation still checks the saved revision and match version.

A match-winning score replaces the point controls with Match finished — review and submit, winner and final games. Only successful server submission changes the banner to Result submitted — awaiting organiser confirmation and offers Back to access code. Returning navigates to /umpire without confirming the result or releasing the court. Unacknowledged submissions remain pending for retry; no return button is offered as if they succeeded. The organiser still confirms and frees the court.

Files: Frontend/src/UmpireApp.tsx, umpire.css, LiveDashboard.tsx, live.css, LiveDialog.tsx; Frontend/tests/result-clarity.test.tsx; these workflow documents. No database schema or API changes.

Verification: frontend build and 153 tests across 26 files passed, including the real React workflow against disposable SQL. Focused result tests cover side changes, doubles, deciding games, submission failure/recovery, return-to-code navigation and console confirmation using the shared scoresheet. Updated frontend assets were published; HTTP health returned 200. Existing frontend chunk-size advisory remains. Backend and existing tournament records were not changed.

## Scoresheet continuation and winner placement — 30 October 2026

Wrapped rally records remain in 24-column blocks. A multi-block game labels the earlier blocks as continuing below and subsequent blocks as Game N — continued, with reduced spacing. Earlier blocks no longer display intermediate totals; only the last block displays the game score. Recorded rallies and resumption markers are preserved without inventing paper history.

Completed-game winners have bold names and final scores with a subtle highlight on every relevant block. Incomplete games do not declare a game winner. A completed match places a Winner badge and bold player names directly in the winning Side A/B summary panel, including doubles. Match winners are derived from completed game wins when the existing match-won flag is set.

Shuttles used now appears alongside match/court/umpire/start/finish/duration in the top metadata row, including a recorded zero or Not recorded as appropriate. The former bottom Shuttles/Winner row is removed. The shared renderer applies these changes to umpire review, console review and scoresheet printing; print styling retains bold winners and grayscale highlights.

Files: Frontend/src/PaperScoresheet.tsx, Frontend/src/paper-scoresheet.css, Frontend/tests/paper-scoresheet.test.tsx and these workflow documents. No API or database changes.

Verification: frontend build and 157 tests across 26 files passed, including the SQL-backed React workflow. Focused scoresheet checks cover continuation labels, final-block-only totals, 59-rally doubles games, game/match winner ownership, unfinished games and zero shuttles. Frontend assets published and HTTP health returned 200. Existing frontend chunk-size advisory remains. Physical printer output was not tested.

## Officials by position — 30 October 2026

Dashboard Assign Court / Tablet and Fixture Schedule / Officials share the OfficialsEditor. Each row selects an event official and position, with Add official and Remove controls. Exactly one umpire is required, at most one service judge is allowed, and multiple line judges can be assigned. Empty, duplicate and invalid-event officials are rejected. Existing assignments preload into the rows.

Dashboard availability disables officials reserved for another called, playing or submitted match, regardless of their position. The server validates every assigned role again at assignment and at match start. Schedule / Officials retains the existing scheduled-time conflict validation. Tablet replacement keeps all officials locked once play has started.

Challenge review available on this court is displayed only for challenge-enabled match rules (or the selected preset before rules are assigned). The dashboard's Court exception control and reservation explanation have been removed. Administrators adjust court allocation through scheduling; closed/occupied court, session and official protections remain. The existing court-exception API is retained for compatibility.

PairCourtRequest adds optional serviceJudgeId and lineJudgeIds. Omitted/null optional fields preserve saved assignments for older callers. An empty serviceJudgeId clears that role; an empty lineJudgeIds array clears line judges. Live match views now include those saved IDs. Existing match fields are reused; no schema migration or record deletion is required. Court assignment continues through the existing audited store change, including full match snapshots.

Files: Frontend/src/OfficialsEditor.tsx (new), LiveDashboard.tsx, TournamentConsole.tsx, live-types.ts; Backend/LiveScore.Api/LiveModels.cs and LiveRuntime.cs. Tests: officials-editor.test.tsx, live-dashboard-calling.test.tsx, workflow.test.tsx and tests/official_roles_integration.py.

Verification: backend build passed with zero warnings/errors. All 162 frontend tests across 27 files passed, including the real React/SQL workflow. The disposable officials API suite passed 98 checks, and the existing live lifecycle suite passed 236 HTTP assertions covering pairing, restart/replacement, heartbeat, scoring, paper reconciliation and reservation protection. No existing tournament records were modified by tests.

## Shared time picker — 2 November 2026

TimePicker remains a generic controlled time-of-day field, reused directly by EventSchedule and inside DateTimePicker by scheduling, result entry and dashboard forms. It accepts one HH:mm value and emits only that field on Set. Cancel/Escape discard changes. It has no start/end comparison, session overlap, date adjustment or current-time business checks; containing screens retain those rules at save time.

The shared dialog uses a large hour/minute/AM-PM display, 1–12 hour grid, AM/PM choices, minute shortcuts 00/15/30/45, one-minute minus/plus controls, Cancel and a Set button showing the chosen time. Minutes can also be typed in the large display. Opening focuses the selected hour rather than bringing up a tablet keyboard automatically.

One-minute steps carry across hours and AM/PM and wrap within 00:00–23:59. DateTimePicker retains its selected date even when a time step crosses midnight; the caller remains responsible for date/time validity. Changing one time field does not change another. Required/disabled fields and venue-local HH:mm storage are retained.

Files: Frontend/src/TimePicker.tsx, Frontend/src/time-picker.css (new), Frontend/tests/time-picker.test.tsx, date-picker.test.tsx and event-schedule-allocation.test.tsx. No API or database changes.

Verification: frontend build and all 169 tests across 27 files passed, including the disposable SQL-backed React workflow. Focused tests cover one-minute stepping across noon/midnight, cancellation, independent start/end inputs, date preservation, required/disabled fields and parent-form isolation. Frontend assets published to backend wwwroot. Live HTTP verification could not run because the local TLS server was not listening on port 5180; the server was not started or stopped in this task. The existing frontend chunk-size advisory remains.
