# Import-only event preparation

## Supported workflow

Events and Master Configuration remain available. Import event accepts one complete Excel workbook; Get from TRS shows eligible events from the administrator-configured external transfer file. Event Details shows Programs with links to filtered Participant and Fixture and Result pages. No manual creation, partial imports or identity-merge prompts remain.

The existing TRS theme, buttons, fields, sidebar, tables and tabs are reused. There is no new visual design system.

## Excel

For a new event, click Select Excel File, choose the completed workbook and click Import Event once. The selected filename and size remain visible; Change File lets the organiser select another workbook before importing. Download Template is a secondary action. Selecting a file does not start an import. TLS runs the existing server validation and immediately confirms the validated import if it succeeds; no preview, acknowledgement switch or second confirmation is shown. The success prompt appears only after saving succeeds and includes the event name and program, entry and participant counts. Validation/save errors use the existing error prompt, retaining server worksheet/row details where provided. Validation failures do not save event data. Import and file selection are disabled while processing, with a local in-flight guard against repeated clicks. Duplicate and stale-revision checks remain enforced on the server. If the save acknowledgement is lost, refresh to check whether it saved before retrying.

Replace Preparation retains Validate and Review, the acknowledgement and Confirm Replacement. TRS event imports retain their existing review/confirmation workflow. No API contract, backend validation, duplicate prevention, transactional persistence, audit logging or database schema changes are required; the new Excel screen sequences the existing preview and confirm requests automatically. Rollback restores EventImport.tsx and SetupApp.tsx and rebuilds the frontend.

Verification: the frontend build and seven focused import screen tests passed, covering one-click saving, row-level validation errors, duplicate rejection, failed confirmation, file-size limits, repeated-click protection and replacement acknowledgement. No organiser event data was imported during these checks.

### Navigation and import clarity — 27 October 2026

Dashboard is a standalone navigation group. Event Management contains Event, Participant and Fixture and Result. Configuration contains SBA Rankings, Game Setting Preset, Master Setting and the existing disabled User Management entry. Expanded navigation shows section headings and separators; collapsed navigation retains labelled icons, group separators and existing destinations. The dashboard remains the landing page. The Excel selection area uses a visible button instead of relying on the native file-input text, with explicit instructions to select a workbook and then import it. TRS import and replacement safeguards are unchanged.

Files: Frontend/src/AdminLayout.tsx and EventImport.tsx; Frontend/tests/admin-navigation.test.tsx and event-import.test.tsx; this guide; rebuilt served frontend assets. Ten focused screen checks passed for navigation groups, collapsed destinations, file-picker activation, filename/change-file behaviour and the existing import success/error/replacement cases. No API, database, authentication or import-validation changes. Rollback restores the matching frontend sources and rebuilds the served assets. No organiser event data was imported for these checks.

Download the workbook from Import event. The plain workbook contains three required data sheets: Event (one row), Program and Participant. Each must contain data. Headers are bold; normal Excel gridlines are visible, with no header fill or decorative borders. Use stable IDs, unique within each record type. Repeat a participant row for each program assignment, retaining the same ParticipantID and identical personal details. ProgramID and GroupID define the entry. Doubles require two participant IDs in one entry; teams use the configured roster size.

Required participant data: name, DOB, gender, email and mobile. SBA ID, club and nationality are optional. Dates accept YYYY-MM-DD text, numeric Excel dates and standard ISO date cells. IDs/mobile/SBA ID should be text. Formula cells are rejected. Workbooks are limited to 1.5 MB compressed, 20 MB expanded, 100 ZIP entries and 10,000 data rows per sheet. Only Event, Program and Participant data sheets and the reserved _Lists helper sheet are accepted. Required sheet names and column headings must be preserved. The plain template has dropdowns for SportsType, GameType, program/participant Gender and Nationality. A hidden _Lists sheet supplies TRS-style country labels; the three data sheets remain the only visible sheets. SportsType accepts Badminton or Non-badminton; GameType accepts Singles or Doubles; program Gender accepts O/M/F/X and participant Gender accepts M/F. Server validation is authoritative.

## TRS JSON (file-based test transfer)

No login, HTTP connector or TRS modification is performed. JSON schema version 2 requires:

```json
{
  "schemaVersion": 2,
  "source": "TRS",
  "sourceSystemId": "stable-trs-installation-id",
  "sourceTimeZone": "Asia/Singapore",
  "exportedAt": "2026-10-12T03:00:00Z",
  "registrationClosed": true,
  "hasFixtures": false,
  "events": [],
  "programs": [],
  "participants": [],
  "entries": []
}
```

Fill the arrays using the transfer contract described here. Exactly one event is required. `sourceSystemId` is a stable identity, not a URL. IDs and references are strings. `registrationClosed` and `hasFixtures` are mandatory boolean declarations: any fixtures in any program means `hasFixtures: true` and blocks the entire import. The organiser clears fixtures/results in TRS; TLS never deletes them. An export containing undeclared fields, including fixture payloads, is rejected instead of silently ignored.

Registration must be closed. Past, completed and archived TRS events are blocked. Past means end date is earlier than today's date in the supplied source timezone; an ongoing multi-day event remains eligible. Export timestamps must be present and not in the future beyond a small clock-skew allowance.

This file cannot prove current TRS state or take a lock. Organisers must finalise eligible participants, resolve outstanding transactions and stop amending the source event after export. Live TRS checking/locking remains deferred. Schema v2 is a new test contract; the earlier TRS v1 API export is not compatible and was not changed in this task.

## Validation and identity

Excel and JSON use the same core validation: required fields, unique IDs, parent references, age, gender, duplicate people/program entries, maximum program count, roster sizes, mixed-pair composition, seeds and maximum capacity. Complete invalid files save nothing. Errors identify the relevant record; parsing errors include worksheet/row where available. There is no automatic participant merge. TRS JSON must use one record per person; Excel repeats that person for each program assignment and TLS consolidates matching ParticipantIDs.

Age is actual age as of today's date in the TLS master timezone, retaining the current TRS-style calculation. Date-only fields are not shifted between zones. Source timezone and export timestamp are retained as metadata. Existing imports are not revalidated merely because the date or master timezone changes; later competition checks must use explicit rules.

Local business IDs are readable sequences such as EV000001 and EV000001-P001. Persistent source mappings preserve identity across replacement imports, independent of row order; private source fingerprints are lookup keys only. See BUSINESS_IDS.md. The original IDs and relationships remain unchanged in the stored source package. Different source systems can use the same IDs without colliding. A replacement must use the same source and event identity. The original package is private server storage and is not exposed as a public file.

Minimum capacity is a readiness condition rather than an import error. Readiness also requires an active badminton singles/doubles program with at least two active entries. Other sports/team formats can be reviewed but are not marked ready. Fixture generation and optional SBA ranking retrieval are available through the tournament console; see FIXTURE_RESULTS.md.

## Confirmation and replacement

Preview is server-held for ten minutes and tied to the catalog revision. Confirm revalidates eligibility and saves all data plus audit within one SQL transaction. Concurrent/stale confirmations return HTTP 409. An interrupted confirmation requires Refresh to check saved state before retrying. Duplicate initial import is blocked, regardless of export timestamp or file contents.

Replacement is an explicit full-event operation available only for import stage `P` (preparation). There is no property-edit endpoint. Invalid files, wrong identities or stale revisions leave the prior event intact. Records absent from the corrected file are removed within that same transaction. Original source snapshots remain in audit history through the normal before/after data records; EventImports retains the latest raw package. Existing events without import provenance remain readable but cannot be replaced through this workflow.

Generating a draw transitions the event to F under the catalog transaction/revision guard. Resetting every entirely unplayed program returns it to P. Any entered result prevents reset. Complete replacement clears event/program operational preparation so obsolete seeds or sessions cannot survive a source replacement.

## Current API

- `GET /api/setup/`: read-only catalog, import metadata and program readiness.
- `GET /api/setup/zones`, `PUT /api/setup/settings`: master timezone and application branding.
- `GET /api/setup/template`: Excel workbook.
- `POST /api/setup/preview?kind=excel|trs&revision=N&replaceEventId=...`: raw file upload; replacement ID optional.
- `POST /api/setup/confirm`: `{revision,token}` from a successful preview.

All setup routes remain loopback-only. Retired manual editing, live TRS connector, fixture-console and partial-merge routes return 404. The legacy `?console=1` switch no longer opens a separate application. LAN authentication and venue deployment remain outside this build.

## Plain workbook columns
- Event: EventID, EventName, SportsType, StartDate, EndDate. Exactly one data row. EventID is user-supplied and must remain unchanged for replacement.
- Program: ProgramID, ProgramName, GameType, Gender, MinAge, MaxAge.
- Participant: ParticipantID, ProgramID, GroupID, Name, Dob, Gender, Email, Mobile, Club/School/Company, Nationality, SbaId, Seed.
There are no status, venue, capacity or entry-sheet fields. Venue is optional internally; Excel supplies a blank value. Status defaults are internal. Excel has no user-configured capacity maximum; the technical file limits still apply. At least two entries are needed for fixture readiness.
Repeated ParticipantIDs must agree on every personal field. GroupID is scoped to ProgramID; Singles groups require one player, Doubles groups require two. Seed is optional and belongs to the whole group: fill it once or repeat the same value, never conflicting values.
The sidebar uses TRS AdminLayout's collapsed-by-default, hover-expand and pin behaviour with a fixed 64px main-content offset. Master Configuration uses TRS's group tabs, setting/value/action tables, mobile cards and inline Edit/Save/Cancel controls. Logo URLs and application name are under Branding; the timezone is under System.

See [Tournament console, scheduling and advancement](TOURNAMENT.md) for the implemented operational workflow.

## Foundation UI
The organiser uses Event → Import Excel or Get from TRS. The latter is a selectable list from the configured external TRS transfer file, with JSON retained only as the internal transfer contract. See FOUNDATION_UI.md for the current screen workflow. No live TRS API or locking is implemented.

## Import and navigation corrections
Get from TRS only displays eligible complete events; an active program below minimum participation hides the whole event. For badminton singles/doubles, at least two active entries are required. Withdrawn programs do not block this participation check. These rules apply to TRS transfer, including replacement; Excel preparation remains permitted below draw readiness.
Duplicate event names across all sources are rejected on preview and again under the confirmation transaction. Names compare case-insensitively after trimming and collapsing whitespace. Replacements exclude only their own event from this comparison. Existing identity and revision safeguards remain in force. No existing records are renamed or deleted.
Program actions navigate to the existing Participant page with initial filters or the selected program’s Fixture and Result detail. Event-date badges are derived from dates and the master-timezone server today; they do not replace persisted imported status.

Runtime sample files and the example download have been removed. Get from TRS has no embedded events; configure an external actual-event JSON array as documented in FIXTURE_RESULTS.md. All saved TLS records remain SQL-backed.

## Event operation after import

Imported source records remain read-only. Event Details separately configures daily sessions that repeat on all event dates, selected master courts, officials, estimated duration/rest and progression. These are local operational settings, not edits to imported identities. Scoring presets are managed separately and frozen onto individual matches before tablet pairing or first manual result. See TOURNAMENT.md.

## Event date reservations — 19 October 2026

TLS reserves full inclusive calendar dates for one event, independent of playing-session gaps, court choice, import source or saved event status. Excel/TRS preview and confirmation use the same overlap check: candidate start <= another event end AND candidate end >= another event start. A new event beginning on the previous event's end date is blocked; beginning on the following day is allowed. Errors name the conflicting event/range. Confirmation rechecks under the existing revision/SQL write lock, so competing previews cannot commit overlapping reservations. Replacement excludes its own event ID but still checks all other events. Existing overlapping data is retained; it is not automatically removed or rewritten, and unrelated operational saves are not blocked by a global legacy-data scan.

Imported IDs, participants and program details stay read-only. The Event Details date editor is an explicit TLS-only exception before fixtures; it does not update the TRS export/package or upstream system. See FOUNDATION_UI.md for the date endpoint, eligibility and session safeguards. No runtime sample data or actual TRS API was added.


### URL navigation — 28 October 2026

TLS organiser modules now use browser history URLs: /dashboard, /events, /participants, /fixtures, /sba-rankings, /game-presets and /settings. Opening / replaces that address with /dashboard without adding an extra history entry. Event Details uses /events/{encodedEventId}; import uses /events/import/excel or /events/import/trs, with an optional replace query parameter. Fixture planning uses /fixtures/{encodedEventId}?program={programId}&step={seeding|draw|schedule|results}. Fixture list filters use /fixtures?event={eventId}&program={programId}. Participant filters use /participants?event={eventId}&program={programId}; person selects a participant detail. IDs are URL-encoded, not names or credentials.

Module changes, event/program selection and fixture step changes update the address. Browser Back/Forward restores navigation context; refresh and copied links reopen the selected screen using current server data. The initial automatic fixture program/step selection replaces its incomplete address instead of adding an artificial Back step. Results links still respect saved-fixture availability. Invalid addresses and missing event/program/participant records display a recovery action to Dashboard. /umpire, tablet codes/fragments and paired sessions are unchanged. Existing static-file SPA fallback already supports deep-link refresh; no backend or deployment configuration change is required. These links do not grant access or change local-admin API restrictions. Unsaved forms, schedule previews and local drafts are not persisted by URLs; reopening reloads saved data. Search text and dashboard event selection remain local screen state.

Implementation uses the existing browser History API without a new dependency. Changed source: Frontend/src/{admin-route.ts,SetupApp.tsx,FoundationPages.tsx,FixtureWorkspace.tsx}. Routing tests cover encoded IDs, invalid links, event refresh, Back/Forward, fixture context and filters, import addresses, missing records and real fixture workspace step/program restoration. Rollback restores matching sources and rebuilds the served frontend; no SQL conversion is needed.

Navigation/session verification: frontend production build passed with the existing bundle-size warning. Forty-four React checks passed across admin routing, real fixture workspace history, scheduling, sidebar, umpire pairing, Excel import and fixture progress. Direct HTTP requests for all module routes, an event deep link and /umpire served the matching build. Chrome verification confirmed fixture Schedule URL/context, refresh, Event module URL, browser Back, and the unchanged umpire screen. No local event, schedule or result was saved, reset or deleted. Existing server fallback and APIs required no changes. Screenshots: C:/Users/weihw/AppData/Local/Temp/tls-consecutive-sessions.png and tls-session-time-validation.png.


### Remove an imported event

Open Event Details and choose **Remove Event**. The button stays available regardless of fixture state. Each click asks the server to validate the current event. If any program has saved generated fixtures (including unscheduled matches), TLS shows a message and does not offer confirmation. An unconfirmed draw preview does not count as a generated fixture.

For an eligible event, confirmation shows its name and program, participant and entry counts. Cancel makes no changes. Confirm removes only that event, its programs, participants, entries, event/fixture preparation settings, source import package and source-ID mappings. Master data, other events, audit history and permanent ID counters remain intact. Removal is permanent; import the workbook again to restore preparation data. Reimporting allocates a new event ID.

Both checks are restricted to the existing local administrator access. `GET /api/setup/events/{eventId}/removal` returns the current revision and confirmation counts without writing data. `DELETE /api/setup/events/{eventId}?revision={revision}` rechecks the revision and fixtures in the serializable store transaction, records `EVENT_REMOVED`, and returns the refreshed setup view. A missing event returns 404; fixtures or stale data return 409. No schema change is required.

Verified with `tests/event_removal_integration.py` (disposable SQL database) and `Frontend/tests/event-removal.test.tsx`: eligible removal, cancellation, fixture blocking, stale confirmation, other-event/master preservation, audit retention, reimport IDs and restart persistence.


Implementation files: `Frontend/src/EventRemovalDialog.tsx`, `Frontend/src/SetupApp.tsx`, `Backend/LiveScore.Api/SetupEndpoints.cs`, and the dependent-import deletion ordering in `Backend/LiveScore.Api/SqlStorage.cs`. Validation completed: both production builds, 8 focused screen/navigation tests, 61 removal checks, 240 existing import checks, and 84 ID/master-preservation checks passed. API tests use disposable databases and leave the local TLS import area empty.
