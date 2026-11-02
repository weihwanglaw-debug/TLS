# Live dashboard and umpire tablet



The dashboard is the organiser's venue control screen for one selected event. It reads actual local-server data, never simulated court/device activity. It displays the venue courts from Master Setting for the selected event. Legacy event court pools no longer restrict the list. Another event's reservation/occupation is clearly marked on shared courts and cannot appear available. Cards and Table use TRS headings, filters, tables, action menus, buttons and theme tokens. Cards support two or four columns and adapt on smaller screens. With 16 or 32 event courts, four columns provide four or eight rows on a sufficiently wide screen.



## Test on this computer

1. Open http://localhost:5180. Dashboard is the landing page. Select an event; there is no All Events choice. The browser remembers the last valid selection; a sole event is selected automatically. With multiple events and no remembered choice, no court/queue/program operations are shown until selection. Empty courts are normal before matches are called.

2. Use an existing SQL-backed event or import your own completed Excel template. No dashboard demo event is generated or offered.

3. Set court count in Master Setting and officials in Event Details. Select dated sessions and courts in Fixture → Schedule. Programs advance independently.

4. Create scoring rules in Game Setting Preset. Fixture and Result → Generate Fixture → configuration → preview → Confirm & Save.

5. Dashboard → available court → Call Next Match, or queue → Assign to Court. Choose an available court and umpire; confirm the saved round preset (choose one only if the round has none); confirm review availability for this match when its preset enables challenges. Assign & Generate Code displays a one-use eight-character code valid for three minutes.

6. Scan the QR on a venue tablet to open /umpire and connect automatically. If scanning fails, open the displayed venue URL and enter the access code and tablet name. localhost /umpire remains available for a same-computer test. Confirm server/receiver/ends to start, award a point, undo an incorrect action, confirm next-game setup if needed, then submit the result. Scoring uses the saved fixture rules, not a hardcoded 21-point assumption.

7. Watch the dashboard: current score, games won, elapsed time, umpire, observed device IP and last heartbeat update. Disconnecting or closing the scoring tab triggers Disconnected after the configured threshold; the match/court remains occupied.

8. Dashboard court → Replace Tablet. Issue a new code and pair another browser/device. Generating a new invitation invalidates earlier unused codes. The previous tablet keeps access until the replacement pairs successfully, then its token is revoked and the new tablet resumes the authoritative score. An unacknowledged rally may need reconciliation with the umpire before continuing; never assume it was saved.

9. A match-winning score appears as Awaiting umpire submission. Submitted results appear as Result confirmation. Confirm Result & Free Court records the winner and releases court/officials. A program whose own current round is confirmed displays Ready for next round. Generate Next Round opens that program's existing generator independently of unfinished programs. Schedule the new matches into free slots without changing saved schedules. Court/official availability and event/session windows remain protected; actual player readiness/rest is the organiser's responsibility.



## Tablets in the hall

The default Kestrel listener is now http://0.0.0.0:5180 so tablets can reach the local machine. The organiser uses http://localhost:5180 on that server. The pairing dialog uses a detected venue IPv4 address ending in /umpire and lets the organiser choose when several are available; another tablet must use one of those addresses, not localhost. Both devices must be on a network that allows them to reach each other.



Organiser/setup/tournament APIs remain restricted to a loopback peer and localhost/loopback host. Tablet endpoints allow loopback and private/link-local venue addresses and require the code/session token. The server records the actual remote IP; client-provided IP fields are rejected. IP is informational, not the tablet identity. A browser-generated device identifier and a per-match opaque token identify the session. Session tokens expire after 24 hours or earlier when replaced, released, switched to paper or confirmed. Codes/tokens are stored hashed on the server; raw codes are only returned to the organiser on issuance.



Windows Firewall may need an inbound TCP 5180 rule. Enable-TabletLan.ps1 creates/updates a rule limited to the TLS executable, the configured port and LocalSubnet. Run it in an elevated PowerShell if the ordinary process lacks permission. Do not change the computer's network category merely to make TLS reachable. This build uses Kestrel directly; hosting behind a reverse proxy requires separately configured trusted forwarding and access checks. HTTP is suitable for testing on a controlled LAN; use properly configured HTTPS for deployment on a shared network. No internet publishing or public hall display is added by this task.



## Operational rules

- Dashboard refreshes every two seconds. Tablet heartbeat defaults to five seconds; disconnected threshold is fifteen seconds. Last seen is derived from server receipt time. A dashboard/server outage is marked stale and disables controls; it does not falsely claim all tablets disconnected.

- Heartbeats stay in memory and do not write scoring data or increment catalog revision. On server restart every device is unknown/disconnected until it sends a fresh heartbeat. Device grants, confirmed scores, game history and match start survive restart in SQL.

- Actual elapsed time starts on Start Match and stops on result submission. It includes intervals. Planned time is not treated as actual start. Existing fixture times stay as planning records when the organiser calls/moves a queued match.

- A called match reserves its court while awaiting pairing/start. Queue assignments offer available courts only. Calling reserves the court immediately; a waiting reservation blocks other assignments even if a legacy override flag is supplied. To change an occupied waiting court, release its waiting assignment first. Moving a queued match to an available court changes only that match and revokes its previous code/token, without swapping or rearranging other matches or altering planned times. Calling/moving needs no reason; replacing a tablet for a started match retains its reason requirement. Playing or submitted matches cannot be moved. Start checks physical court and official occupancy, event dates and playing sessions. Player availability/rest is an organiser decision at calling/start time; scheduling still uses participant IDs and configured rest.

- Queue estimates account for existing court use, queued work, officials and session gaps. They are estimates, not guaranteed start times. An overrun uses a provisional five-minute remaining estimate until updated. Awaiting result confirmation uses a provisional one-minute release estimate. Delayed is based on the saved planned start, so recalculating an estimate cannot conceal lateness. Closed/no-session queues have no usable estimate.

- Open/close courts through the court action menu. Occupied/assigned courts must be cleared first. Closed courts cannot receive/start matches or be selected for fixture generation.

- Each score command carries an operation ID and a match version. Duplicate retries apply once; changed data with the same ID or competing stale versions are rejected. Recent 128 receipts are kept. Undo retains the last 50 score/next-game snapshots. A tablet keeps only its current match snapshot, token and one pending command for retry; it does not become the authoritative database or accept additional unconfirmed points offline.

- Use Switch to Paper if the tablet/link fails. The court and umpire stay occupied and tablet input is revoked. When service returns, use Resume on Tablet to reconcile completed-game scores, the unfinished current-game score, server, receiver and court ends; save and generate a fresh access code, then reconnect the tablet. If the match has finished, use Enter Paper Result with complete game scores (or walkover) and actual venue finish time. Direct fixture-result entry remains blocked for a playing tablet match until paper mode is chosen. If the entire server is down, continue on paper and reconcile/resume after recovery.

- Multiple events share physical court occupancy. Court and official reservations are checked across events. Organisers manage actual player availability and rest; planning checks continue to use imported participant identities, not name matching.

- The tablet page implements the input needed for this dashboard: pairing, start, points, undo, game progression and result submission. The court diagram now includes saved service positions and misconduct/challenge/shuttle controls; user management remains unfinished. Game Setting Preset is implemented separately.



## Storage and rollback

The additive Sql/20261013_live.sql upgrade adds SystemSettings.LiveVenueJson. It stores closed courts and current hashed device grants. The additive MatchLiveRecords table stores each match’s status and live JSON separately: per-match version, acknowledged scores, start/submit timestamps, bounded undo history and operation receipts. Its ProgramId references the existing Programs table. ProgramOperations retains fixture structure and confirmed results; rally updates do not rewrite the entire program draw. Finished matches clear transient undo/receipt data while compact scoring audits remain. No imported event/program/player IDs are changed. No tables or columns are dropped. CLI --upgrade-live true is idempotent; new databases are initialized with this field automatically. InitialSchema.sql reflects the updated EF model.



Source/configuration backup: D:/Source/TLS-Backups/live-20261013-163433. A SQL COPY_ONLY/CHECKSUM backup was taken in the SQL instance backup directory before the upgrade. Rollback requires matching application AND database snapshots because the older strict JSON reader cannot read new live fields. Preserve later scoring work before any restoration; do not simply overwrite live records.



Live scoring uses the catalog SQL write lock but a separate per-match version. It saves compact audits without bumping the preparation revision for every rally, so scoring in one program does not continually invalidate another program's fixture preview. Court assignment, result confirmation, paper mode and availability changes still use the catalog revision, with focused before/after audits of the affected match/bindings and the override reason. They do not copy the whole participant catalog for every court action. Device heartbeat is not a scoring audit event.



## API and verification

GET /api/dashboard/ returns physical courts, filtered queue/program progress, server clocks, heartbeat timings, officials and tablet URLs. POST /pair issues/replaces a grant; /confirm confirms a submitted result; /paper revokes tablet access while retaining occupancy; /court changes availability; /cancel releases an unstarted call. Existing tournament generator/result endpoints are reused.



Tablet: POST /api/tablet/pair with code/device name/identifier, GET /state, POST /heartbeat, POST /command. State/heartbeat/command require Authorization: Bearer. Actions are START, POINT (side A/B), UNDO, NEXT, SUBMIT, SHUTTLE, CARD, CHALLENGE, SETUP and PAPER. START/NEXT require explicit serving setup. Result-confirmation status is RC; playing is IP; established SC/C/WO/BY remain unchanged. Court/device/phase APIs return short codes; labels are derived in the frontend.



Run tests/import_integration.py, tests/tournament_integration.py and tests/live_integration.py. They use independent disposable TLS_Test databases and ports 5193, 5197, 5198 and 5199, never TLS/TRS. Verified this update: both builds; 240 import, 150 tournament, 162 live, 214 fixture/result and 196 event-workflow HTTP checks (962 total), plus three React screen-flow checks against an isolated SQL API. Live cases include observed IP, protected APIs, code reuse, heartbeat loss, restart/reconnect, replacement revocation, idempotency, concurrent score versions, pending result release, paper reconciliation, waiting override, closed courts and independent program advancement. Browser visual and physical-tablet/network testing remain distinct from API verification.



## Shared result enhancements



See FIXTURE_RESULTS.md. Manual result review loads the same SQL record and tablet details. Actual tablet times are preserved; optional shuttle/card/challenge fields are available. Correction reasons and match versions protect tablet results. The shared console scoresheet prints recorded rallies and metadata; older/manual history is not reconstructed.



## Match rule snapshots



A scheduled match requires confirmed round rules before code issuance. The default is inherited at draw generation when configured. Saved round presets are read-only in assignment; round-wide editing/locking is deferred. A confirmed per-match preset change revokes only that target's pairing. Started/result matches retain their saved rules; unstarted siblings are independent. Tablet replacement retains saved rules. Programs progress independently. See FIXTURE_PLANNING.md.



The current UI/assignment and serving contract is described in UMPIRE_SCORING.md. Review availability is a match decision, with no event-level review-court restriction.



## Event selection, dates and session states — 19 October 2026



Changing the selection is read-only: it does not revoke a tablet token, release/move a match, generate a round or change the database revision. Previous-event rows and dialogs are hidden immediately while the new request loads. A deleted selection returns to the event picker. The selected event has the same Upcoming/Ongoing/Event Over badge used elsewhere; dates and session time are calculated by the server in the TLS master timezone.



| Situation | Dashboard behaviour |

| --- | --- |

| No event | Event picker/instructions; no court/queue/program operations |

| Upcoming | Preparation message; configure event/presets, prepare fixtures and assignments; START remains date/session guarded |

| Before session | Next session's venue-local date/time; assignments can be prepared |

| Between sessions | Break message and next session; playing matches can finish |

| Session active | Event day and live courts/queue/program status |

| No sessions or all courts closed | Specific preparation warning and link to Event Details |

| New event day, missing first draw | Fixture required per program, with existing records retained |

| New event day, next round ready | Next round required per program, respecting independent/coordinated progression |

| Event dates ended, incomplete | Warning plus outstanding matches, paper/review work and programme progress; new assignments/codes disabled in this screen; existing waiting assignments may be released |

| Event dates ended, complete | Final-results summary; fixture/result links remain |

| Server disconnected | Last data retained with stale warning; operation controls disabled |



A date change never generates or advances a round. Unfinished rounds continue across days; readiness follows confirmed results and the existing I/A progression rule. Matches already playing may finish, submit and be confirmed after a session/event window ends. New starts remain blocked by the existing backend checks. Paper reconciliation and result review remain accessible after dates end. Event Over is a calendar label, separate from programme completion.



Event court lists are scoped without ignoring shared physical occupancy. A shared court occupied/reserved by another event shows Occupied by another event and its event name, with foreign scores/device details/actions suppressed. Its assignment is disabled in the court picker. The backend also rejects overriding a different event's waiting assignment, even with an override reason; switch to that event and release its unstarted assignment first. Existing playing/review occupancy checks remain global. Same-event waiting overrides are also rejected; release the waiting assignment or select another available court.



### API and storage



GET /api/dashboard/?eventId=... adds selectedEventId and eventContext. The context returns calendar U/O/C; sessionState N (not configured), I (in session), D (no session today), B (before first session), G (break), F (today's sessions finished); day, nextSession, sessionEnd, completion/unfinished/paper/confirmation counts and setup flags. These are derived read-only fields, never stored statuses. Court rows add outsideEvent and otherEventName; event-scoped match/last/next/program/queue rows do not mix event details. The event catalog remains available for selection. The no-event aggregate API is retained for existing internal clients/tests; the organiser UI never renders or operates that aggregate view.



No schema, migration or persisted event/player/source-ID changes. Cross-event waiting override now returns validation failure rather than revoking another event's binding. Existing endpoint paths and command payloads are preserved. Rollback restores the matching frontend/backend sources and rebuilds; no data restoration is required for these derived fields. No backup, data cleanup or reseeding was performed.



### Changed files and verification



Backend: LiveDashboard.cs and LiveRuntime.cs. Frontend: LiveDashboard.tsx, live-types.ts, live.css and SetupApp.tsx. Tests: tests/dashboard_event_integration.py (new) and Frontend/tests/workflow.test.tsx. This guide documents the operational and API changes.



Both builds passed (backend zero warnings/errors). Verification passed 139 event-dashboard checks, 162 existing live-lifecycle checks and 196 existing event-workflow checks (497 backend checks), plus 13 React screen/component tests. New coverage includes before/between/after sessions, second-day missing fixtures/next-round readiness, shared-court masking, cross-event override rejection, selection without mutation/token revocation, expired unfinished/completed events, new-start rejection after expiry and continuation/submission/confirmation of an existing match after expiry. Calendar rollover is simulated only in disposable TLS_Test SQL; TRS past-event import restrictions are unchanged. Screen coverage checks required selection, automatic sole-event selection, remembered selection, no All Events option and separated queues. Browser visual checks use the existing event without changing scores/assignments. Physical multi-device venue UAT remains separate.



## Available-court calling update



Available court cards and table rows offer Call Next Match, showing the next assigned players. Confirmation keeps the match/court preselected and lets the operator choose the umpire. Queue Assign to Court lists only available courts; closed, foreign, called, playing and result-review courts cannot receive another match. A called match can move to another available court, freeing its previous reservation. Existing code renewal/tablet replacement can reuse that same match's own court. Global revision and server occupancy checks protect against stale dashboard selections. Estimates recalculate on the regular refresh; saved match times and all other assignments stay unchanged. Endpoint paths, request shape, audit logging and database schema are unchanged; the legacy replaceWaiting flag no longer bypasses court availability. Rollback restores LiveDashboard.tsx, LiveRuntime.cs and their matching builds.



Verification: frontend and backend builds passed; four focused React tests cover card/table calling, available-only selection, no override controls and disabled assignment with no free courts. The isolated live lifecycle suite passed 176 HTTP checks, including moving without rearrangement, immediate reservation, stale-revision rejection and legacy override rejection; 147 event-dashboard checks passed. These tests use disposable SQL databases and do not alter the organiser's event data.



## Fixed court and umpire availability



Call Next Match shows the clicked court as fixed text; queue Assign to Court retains the available-court selector. Court access-code renewal and tablet replacement also retain their existing court. The umpire list disables officials reserved by another called, playing or result-review match, with a short Busy · Court N label. A future scheduled match alone does not reserve an official. The current match's own reservation does not block its renewal or tablet replacement. Releasing a waiting assignment or confirming its result frees that reservation; expired codes and disconnected tablets do not release it automatically. Existing start-time official checks remain in place.



The dashboard officials response adds a reservations array of matchId/court pairs. The same reservation rule is checked inside the existing serialized assignment transaction before any code is issued, protecting against stale screens and competing callers. No request payload, schema, migration or imported-data change. Rollback restores LiveDashboard.tsx, live-types.ts, LiveDashboard.cs and LiveRuntime.cs and rebuilds both applications.



Verification: both builds passed, with eight React calling tests, 186 live lifecycle HTTP checks and 147 event-dashboard checks. Coverage includes fixed court text, available-only queue selection, busy umpire labels and disabled selection, waiting/playing/review reservation rejection, own-match renewal and release after result confirmation. Integration checks run only against disposable SQL databases.



## Paper-style scoresheet and tablet recovery



The umpire Scoresheet replaces the vertical rally-record table with a printable paper-style badminton sheet: event/match/court/umpire/timing details, player/pair panels, game results and horizontal scoring bands. Each recorded rally places the winning side's cumulative score in its next column; the other side stays blank. Doubles show both players within each side's band without inventing an individual point scorer. Grids continue after 24 columns, scroll horizontally on tablets, and use a landscape print layout. Existing cards, challenges and shuttle totals remain visible. Future games and paper rallies are not fabricated.



Dashboard paper-mode courts offer Resume on Tablet alongside Enter Paper Result. Court, umpire, preset and original start time are retained. The form reconciles completed games (recorded games cannot be removed), an unfinished current-game score, server/receiver and left court side. Completed games must satisfy saved scoring rules without already deciding the match. A completed current game belongs in Completed Games; a match already won must use Enter Paper Result. A new game at 0–0 must start with the previous game's winning side serving. The existing badminton serving helper validates singles/doubles positions and derives court placement from score parity.



POST /api/dashboard/resume is a local-admin endpoint accepting revision, matchId, version, games, scoreA, scoreB and serving {serverSide,serverId,receiverId,leftSide}. One serialized transaction verifies paper mode, per-match version, reserved court/official availability and score/serving rules, then saves the correction, increments match version, clears prior undo/retry receipts, revokes old match bindings and issues a fresh three-minute one-use code. Failure rolls back all changes. This continues an already-playing match and does not run a new-match start or reset its clock. After pairing, the tablet resumes scoring directly from the corrected state. Existing access-code renewal/replacement does not bypass paper reconciliation.



LiveMatchState JSON adds resumptions [{game,a,b,rallyIndex,at}]; the view exposes these and the venue timeZone. Existing live JSON defaults to an empty list. Recorded rallies, cards, challenges and shuttle counts are retained; the paper interval sets rallyHistoryComplete=false and the sheet explicitly marks Resumed at A–B. No missing rally sequence is reconstructed. Undo can only reach scoring recorded after the latest resumption. Resumption is audited under LIVE_PAPER_RESUME with before/after snapshots. No SQL schema/migration or source event/player ID change is needed.



Rollback restores the matched frontend/backend sources and builds. If resumed records already exist, preserve their live JSON/audit history; do not restore an old database over subsequent match scores. The earlier UI will not display resumption markers, although retained scores remain valid.



Changed files: Backend/LiveScore.Api/LiveModels.cs, LiveEndpoints.cs and LiveRuntime.cs; Frontend/src/LiveDashboard.tsx, UmpireApp.tsx, live-types.ts, ResumeTabletForm.tsx, PaperScoresheet.tsx and paper-scoresheet.css; Frontend/tests/paper-scoresheet.test.tsx; tests/live_integration.py and umpire_serving_integration.py; this guide. Local served frontend assets were rebuilt.



Verification: both builds passed without backend warnings; 211 live HTTP checks, 91 serving/recovery HTTP checks and 20 focused React tests passed. Tests cover score rejection without audit/data mutation, per-match version checks, revocation, no undo across resumption, original court/umpire/start retention, recovery across server restart, doubles positions, between-game recovery and deciding ends. Screen tests cover horizontal bands without a record table, four doubles player names, long-game continuation, resumption markers, completed-game boundaries and console recovery entry. Desktop browser inspection confirmed the real 5–4 match uses the new paper-style grid and the paper-mode court opens recovery without saving changes. Print CSS supplies a landscape layout; physical tablet/printer UAT has not been performed. Temporary viewport overrides were reset. Existing organiser match scores were not changed by verification.





## QR pairing and reversible calling — 27 October 2026



Call Next Match chooses the earliest saved planned start on that court, then match number. Estimates are informative and do not reorder calls; null estimates cannot move a later match ahead of an earlier scheduled match. Unscheduled matches remain visible with Scheduling required and cannot be called. The organiser may call early, checks player attendance/conflicts/rest, and chooses an available umpire. The server rechecks court and umpire reservations in its serialized transaction. A called, playing or awaiting-confirmation match reserves its umpire. Calling/START and paper resumption no longer block on player identity/rest; schedule planning and manual slot validation keep their existing rest/conflict rules.



An unstarted called match exposes Cancel Call / Return to Queue on its court card/table. Confirmation revokes all access for that call, removes its umpire assignment and releases the court/umpire. The fixture's saved date, time and court remain intact and the match stays queued. Playing/review matches cannot use this cancellation, including when START races cancellation; scores are never cleared by this action.



The Connect Tablet panel shows a locally generated QR and the manual venue URL/eight-character code with a three-minute countdown. Scanning opens /umpire#pair=CODE and connects automatically with the existing browser device ID/default tablet name. The fragment is removed immediately and is not sent as a URL query. Both methods consume the same one-use invitation. Invalid/expired links show an error and retain manual entry. A pending unacknowledged scoring command is not discarded by scanning. QR rendering uses qrcode.react 4.2.0 locally, without a remote QR service. localhost/loopback/unspecified addresses are excluded from QR choices; absence of a venue address shows an instruction instead of an unusable QR.



Expiry hides QR/code and offers Generate New Code. Regeneration invalidates earlier unused invitations. Dashboard polling dismisses the panel after the exact displayed invitation is consumed. The three-minute limit applies only to pairing; established session lifetime remains 24 hours. Temporary Wi-Fi loss retains the court, umpire, score and token, allowing the same tablet to reconnect normally. Disconnected courts expose Generate New Code or Reconnect / Replace Tablet. Successful replacement, rather than merely displaying a fresh invitation on the same court, revokes the previous token. Moving a waiting match to a different court still revokes access immediately. Paper mode still requires reconciliation through Resume on Tablet.



POST /api/dashboard/pairing is a new local-admin endpoint accepting revision, matchId and version, returning the existing invitation shape. It renews only an existing assignment and checks status, version and court/official availability. Court device views add pairingPending and pairingExpiresAt. Existing tablet /pair and scoring contracts are retained. Invitation renewal is audited as TABLET_INVITATION_RENEW. Existing hashed binding JSON is reused; no SQL schema/migration or administrative access is granted to the tablet.



Program Progress already marks a program's completed confirmed round ready for Generate Next Round. Scheduling already appends newly selected unscheduled matches, keeps saved/unfinished slots and reports matches that cannot fit; these behaviours were verified, not rewritten. Visual timetable, hall display, utilisation/capacity enhancements, queue priorities and visual umpire setup remain later work.



Rollback: restore the matching backend/frontend sources and dependency lockfile, rebuild and serve together. Do not restore an older database over subsequent scores. Unused invitations issued by this version naturally expire after three minutes.



Changed files: Backend/LiveScore.Api/{LiveModels.cs,LiveEndpoints.cs,LiveRuntime.cs,LiveDashboard.cs}; Frontend/src/{LiveDashboard.tsx,UmpireApp.tsx,live-types.ts,TabletPairing.tsx}; Frontend/package.json and package-lock.json; focused React/isolated SQL checks; this guide and UMPIRE_SCORING.md. Served frontend assets rebuilt. No TRS source/configuration or user event data changed by the checks.


Verification for 27 October: backend build passed with zero warnings/errors; 236 live lifecycle HTTP checks, 92 serving/recovery HTTP checks and 128 session scheduling checks passed against disposable SQL databases. They verify expiry, renewal, one-use/revoked codes, retained old access until successful replacement, authoritative scores across reconnection/restart, wrong-call cancellation, available-umpire checks and independent next rounds with saved slots preserved.

Browser verification: the QR panel was visually checked; opening its venue-network link paired directly, removed the URL fragment, displayed the assigned scoreboard and dismissed the console pairing panel. Cancel Call then released the court and umpire and the old tablet reported Session closed. All browser actions used a disposable event/database. Optical scanning on a physical tablet remains venue UAT. The 27 focused React checks passed, covering countdown/manual fallback, automatic pairing, pending-score protection, cancellation and panel dismissal.

## Session court availability — 30 October 2026

Dashboard calling, court choices and tablet starts share the session-court eligibility check. During an active session, queued matches use its allocated open courts. Before a session or during a break, an upcoming match can be prepared on courts allocated to its planned start; future-day preparation uses that day's planned session. START still requires the scheduled day for early play, confirmation and event/session safeguards. Late matches with no active session cannot be newly assigned. Playing/submitted matches retain tablet replacement on their existing court after the session ends. The queue API adds eligibleCourts; occupancy and official reservations are checked separately. Administrators adjust session times/courts in Schedule. Existing explicit court-exception API requests remain supported, but the dashboard has no exception control. Replayed dependants awaiting an earlier corrected result cannot be called or started.

## Scoreboard and early starts — 30 October 2026

The umpire header shows the umpire name, event, court, match and connection state. The redundant footer row and TLS branding are removed. Shuttle control stays on the main screen; unavailable challenge counters are hidden. Server, receiver and starting-end setup remain unchanged for singles and doubles.

Starting before the planned time prompts the umpire to confirm. The tablet command includes `confirmEarlyStart: true`; the server independently checks it and permits early play only on the scheduled playing date within event dates. Outside an active session, the assigned court must be allocated in the session containing the planned start. During an active session, its existing court restrictions still apply. Closed courts, occupied courts/officials and unresolved prerequisites remain blocked. The actual start is recorded independently; no planned match times are moved. Player readiness/rest remains the umpire/organiser's responsibility.

`earlyStartMinutes` is supplied by the server in match views. `GET /api/dashboard/scoresheet/{matchId}` is a local-console-only read endpoint for scheduled, ongoing and completed matches. Dashboard and Fixture Table View use the same ScoresheetButton viewer and PaperScoresheet renderer; the umpire uses that same renderer without a print button.

Pairing links are handled on initial load and subsequent hash/history navigation. Match Actions → Change match opens pairing, preserving the previous confirmed session until replacement succeeds. An active assignment on the server must still be released before that tablet joins another match. Pending scoring actions block switching and are never discarded. Expired/revoked sessions without pending actions return to pairing. A fresh match token replaces the cached match only after successful pairing.

## Clear result review and player-aligned scores — 30 October 2026

The umpire court panels display earlier game scores beside each side's large current score. The score history follows the team when ends change and supports doubles and deciding games. A completed game still shown as the current large score is not duplicated in the smaller history. The combined unlabelled previous-game score is removed from the top bar.

Console court cards align player/team name, earlier game scores and current score on each row. Court/match identity, elapsed time, umpire and connection status remain visible; device name, IP and last heartbeat are grouped under expandable Device details.

Umpire Submit Match Result and console Confirm Match Result both render the existing PaperScoresheet component. The console confirmation dialog is wider and keeps its confirmation/cancel actions in a sticky footer. Umpires can return to the scoreboard before submitting; console confirmation still checks the saved revision and match version.

A match-winning score replaces the point controls with Match finished — review and submit, winner and final games. Only successful server submission changes the banner to Result submitted — awaiting organiser confirmation and offers Back to access code. Returning navigates to /umpire without confirming the result or releasing the court. Unacknowledged submissions remain pending for retry; no return button is offered as if they succeeded. The organiser still confirms and frees the court.

Files: Frontend/src/UmpireApp.tsx, umpire.css, LiveDashboard.tsx, live.css, LiveDialog.tsx; Frontend/tests/result-clarity.test.tsx; these workflow documents. No database schema or API changes.

## Scoresheet continuation and winner placement — 30 October 2026

Wrapped rally records remain in 24-column blocks. A multi-block game labels the earlier blocks as continuing below and subsequent blocks as Game N — continued, with reduced spacing. Earlier blocks no longer display intermediate totals; only the last block displays the game score. Recorded rallies and resumption markers are preserved without inventing paper history.

Completed-game winners have bold names and final scores with a subtle highlight on every relevant block. Incomplete games do not declare a game winner. A completed match places a Winner badge and bold player names directly in the winning Side A/B summary panel, including doubles. Match winners are derived from completed game wins when the existing match-won flag is set.

Shuttles used now appears alongside match/court/umpire/start/finish/duration in the top metadata row, including a recorded zero or Not recorded as appropriate. The former bottom Shuttles/Winner row is removed. The shared renderer applies these changes to umpire review, console review and scoresheet printing; print styling retains bold winners and grayscale highlights.

Files: Frontend/src/PaperScoresheet.tsx, Frontend/src/paper-scoresheet.css, Frontend/tests/paper-scoresheet.test.tsx and these workflow documents. No API or database changes.

## Officials by position — 30 October 2026

Dashboard Assign Court / Tablet and Fixture Schedule / Officials share the OfficialsEditor. Each row selects an event official and position, with Add official and Remove controls. Exactly one umpire is required, at most one service judge is allowed, and multiple line judges can be assigned. Empty, duplicate and invalid-event officials are rejected. Existing assignments preload into the rows.

Dashboard availability disables officials reserved for another called, playing or submitted match, regardless of their position. The server validates every assigned role again at assignment and at match start. Schedule / Officials retains the existing scheduled-time conflict validation. Tablet replacement keeps all officials locked once play has started.

Challenge review available on this court is displayed only for challenge-enabled match rules (or the selected preset before rules are assigned). The dashboard's Court exception control and reservation explanation have been removed. Administrators adjust court allocation through scheduling; closed/occupied court, session and official protections remain. The existing court-exception API is retained for compatibility.

PairCourtRequest adds optional serviceJudgeId and lineJudgeIds. Omitted/null optional fields preserve saved assignments for older callers. An empty serviceJudgeId clears that role; an empty lineJudgeIds array clears line judges. Live match views now include those saved IDs. Existing match fields are reused; no schema migration or record deletion is required. Court assignment continues through the existing audited store change, including full match snapshots.

Files: Frontend/src/OfficialsEditor.tsx (new), LiveDashboard.tsx, TournamentConsole.tsx, live-types.ts; Backend/LiveScore.Api/LiveModels.cs and LiveRuntime.cs. Tests: officials-editor.test.tsx, live-dashboard-calling.test.tsx, workflow.test.tsx and tests/official_roles_integration.py.

Verification: backend build passed with zero warnings/errors. All 162 frontend tests across 27 files passed, including the real React/SQL workflow. The disposable officials API suite passed 98 checks, and the existing live lifecycle suite passed 236 HTTP assertions covering pairing, restart/replacement, heartbeat, scoring, paper reconciliation and reservation protection. No existing tournament records were modified by tests.


## Venue live display — 2 November 2026

Master Setting → Live Display controls all venue display screens. Choose 1–6 columns and 1–6 rows independently (default 3 columns × 2 rows), automatic page rotation, 3–120 seconds per page (default 10), an event or all events, and optional court filters. No selected courts means all courts. A layout preview shows the density before saving; high-density layouts need a sufficiently large screen. Settings are saved together using the existing revision-protected settings API and venue settings JSON; no SQL schema upgrade is needed.

Open `/display` on a browser connected to the venue network. Use Full screen or the browser's normal casting/mirroring feature. This is a read-only screen without organiser controls. Playing and submitted matches appear in court order; called matches awaiting start are omitted. Cards show event, program, round, court, player-aligned completed-game scores, current points and a serving indicator. Submitted matches remain visible as Awaiting result confirmation until the organiser confirms and releases the court. The page does not hold completed results after court release.

Scores and saved configuration refresh every two seconds. Pages rotate only when needed and enabled; Previous/Next also work manually. A connection failure retains the last received scores, prominently marks them as outdated and pauses rotation until reconnection. This is browser display/casting support, not a native Chromecast receiver integration. Verify readability and connectivity on the actual venue screens.

`GET /api/display` is restricted to the venue network and loopback, following tablet network eligibility. It exposes an explicit spectator projection with match names, event/program/round, court, scores, serving side and match state. No tablet credentials, device identity/IP, official contact data or organiser commands are exposed. Existing organiser access restrictions remain unchanged. `PUT /api/setup/settings` accepts optional `liveDisplay`; all values are validated on the server. Older clients may omit it.

Display settings are stored as `display` in `SystemSettings.LiveVenueJson`. Before rolling back to a binary predating this feature, back up that JSON and remove only the new `display` property, retaining presets, court closures and bindings; older binaries reject unknown JSON fields. Removing frontend display/graph components does not alter scores or rally history.
