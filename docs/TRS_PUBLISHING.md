# Event score publishing

TRS transfers and Excel imports can publish scores. For Excel, use the numeric
TRS event ID in the workbook Event sheet. Workbook program references may be
alphanumeric. Set the compatible target URL and credential in Event Settings >
Score Publishing. TLS sends the credential in the X-TLS-Key request header.

- Live: PUT /api/integration/live-scores. The event snapshot contains active matches
  with court and current score. An empty update clears live matches.
- Results: PUT /api/integration/event-results. The full event result snapshot contains
  every program in programs, including programs with no confirmed results. Each
  result carries program ID, phase, round, date when known, and confirmed scores.
  Numeric source program IDs can link to TRS IDs. Alphanumeric workbook references
  are preserved alongside display IDs and marked ineligible for TRS links.

Both feeds include UTC generatedAt and an increasing version. Versions are persisted
per event and stream. Result retries reuse the failed payload and its stamp as long
as the source and destination remain the same. A newer correction replaces queued
work. Options > Resend Results rebuilds and publishes the whole event, including
empty results after a clear. It sends no live update. Restart republishes current
data with a newer version.

Deploy the compatible receiver before using event-wide publishing. A results URL
containing {programId} keeps the previous per-program contract for compatible
legacy targets. Use that form only when the imported programs carry numeric IDs.
There is no TLS database migration for this change.

## Historical contract

The notes below describe earlier releases. The event publishing contract above
takes precedence for the current release.

# Publishing live scores and official results to TRS

TRS and TLS use event-level live snapshots and program-level confirmed-result snapshots. Original TRS event/program IDs identify the destination; local TLS match and entry references remain descriptive snapshot content. TRS prioritizes its own recorded results when both sources exist, otherwise it displays the TLS snapshot. No TRS fixtures are created by publication.

## Configure

For direct event fetching, use Master Setting → TRS Connection to edit the TRS API URL and Integration key with the same row-level Edit/Save controls as other master settings. The key is intentionally visible in this local-admin-only section. Saved connection changes apply to the next import request without restarting TLS. Existing `Trs__ApiBaseUrl` and `Trs__ApiKey` server settings are used as a fallback until the first master connection save. A blank key save retains the existing key. The saved master connection takes precedence over configuration files. Without the API URL, the existing `Trs:EventFile` transfer-file connection still works. In TRS, mark the event Ready for Event Integration after validation; registration must be closed and the event must have no TRS fixtures. Pending registrations block readiness, while confirmed unpaid entries remain eligible. Unmarking in TRS does not delete an existing TLS copy. Export/import use setup schema 2. There is no post-transfer ownership lock.

In Event Settings → Score Publishing, set the live API URL and results API URL, the destination API credential and 2–120 second interval; then enable publishing. The same receiver URLs work for all events and programs:

- `https://trs.example/api/integration/live-scores`
- `https://trs.example/api/integration/results`

TRS must apply its `20261103_create_tls_snapshots.sql` script and configure `TlsIntegration__ApiKey` with the same random key (at least 32 printable characters). Use HTTPS for deployed connections. Redirects are disabled.

Settings use the existing event-operation JSON. Each event keeps its own publishing URLs and credential, independently of the TRS import connection. For TRS destinations, enter the same integration key configured in TRS; another compatible website can use its own credential. Publishing sends this event credential in `X-TLS-Key` and requires the same snapshot API contract. The credential is encrypted with ASP.NET Data Protection. The local-admin publishing settings API returns it as plain text for the Score Publishing field; it is never included in spectator payloads or audit snapshots. Preserve the service account/key ring across deployments; re-enter saved credentials if that key ring changes. A blank credential field preserves the saved event credential. Existing readable TLS IDs remain unchanged. Publication versions use existing persistent ID sequences and remain increasing across restarts. No TLS SQL schema upgrade is required. The master import connection is stored separately in `SystemSettings.LiveVenueJson.trsConnection`, with its key encrypted using ASP.NET Data Protection. Only local-admin `GET /api/trs/connection` returns the clear key for the settings screen; general setup JSON contains only ciphertext, public APIs never expose it, and connection audits contain the URL and credential-presence flag only. Preserve the Data Protection key ring. Before rollback to an older strict reader, restore the previous settings JSON or remove only `trsConnection`; restore the previous configuration-based connection.

## Operation

The background publisher sends current IP/RC live matches periodically. It attempts an empty live snapshot promptly when the last match stops being live. All confirmed program results (including preceding rounds) publish after changes. Exceptional outcomes preserve scores and send the outcome code. Private administrator reasons (`OfficialReason`) remain inside TLS and never enter publication snapshots. TRS derives the public outcome label from the code. Its receipt timestamp is authoritative even if the TLS clock differs.

A small TLS dashboard badge appears only on failure. Expand it for the last failure. Manual Options > Submit Result is available only under Fixture and Result, scoped to the selected program and enabled when publishing is configured. Selecting it without completed/walkover results displays No result to submit. and makes no request. Live scores cannot be manually resent. Successful sends clear the relevant failure. Live sends try again at the next interval; result failures retry at max(5 seconds, the configured interval), and can also be retried manually when results exist. There is no persistent delivery queue. Restart republishes current state, while failure badges themselves reset.

Disabling publishing stops sends; it does not clear TRS data. With the agreed no-expiry public design, TRS can retain old live scores if the empty publication cannot arrive. Restore connectivity; the next automatic live update sends the current state. Empty live snapshots do not remove official results. Result clearing/correction publishes a newer complete program snapshot.

Local organiser endpoints: GET/PUT `/api/trs/publishing/{eventId}`, POST `/{eventId}/resend?programId={localProgramId}` (results only), GET `/status/all?eventId=...`. Existing event operating-settings saves preserve these separate publishing settings. Admin APIs remain local-only.

## Validation

`python tests/trs_publishing_integration.py` creates a disposable SQL database and local HTTP receiver, verifies master connection editing, invalid URL/key rejection, blank-key preservation, encrypted storage, separate import and publishing credentials, direct setup fetching, original-ID projection, confirmed results, empty live snapshots, failed sends/recovery and increasing versions after restart. It does not write to the real TLS/TRS databases. Frontend tests cover settings, saved event credentials and the failure badge; run them with the existing frontend workflow harness.

For the complete receiving contract and TRS public-page behavior, see the TRS repository's `TRS_TLS_INTEGRATION.md`. Rollback: disable publication, restore the prior TLS binary and remove only the new `publication` property from saved event-operation JSON before using an older strict deserializer. Keep a backup; changing JSON in an existing database requires the usual explicit approval. Do not revert unrelated event settings or results.

### Event retrieval feedback

Get from TRS is disabled until a TRS API URL is configured in Master Setting. Loading failures show the error returned by TRS (or the connection/timeout error if no response arrives). A successful empty response shows “No events found in TRS.” Import review uses a single Confirm button without an acknowledgement checkbox. Existing import validation remains in force.

### Unified event editing

The event page uses one Edit Event / Save Event control for dates, match timing, officials and Score Publishing. Enable Score Publishing is a toggle; its API URLs, credential and interval appear only when enabled. Changes commit together through `PUT /api/tournament/event-settings` with revision checking and a single audit transaction. A validation failure saves nothing. Date changes retain existing schedule/result locks and clear session allocations only when the dates actually change. Publishing can target any compatible receiver; the existing snapshot contract, source identity requirements and credential protection remain unchanged. Older individual save endpoints remain compatible.

Generic receivers select the event/program using snapshot `eventId` and `programId`. No ID or placeholder is required in a publishing URL. Legacy event URLs and optional `{programId}` replacement remain supported; saved settings are not changed automatically. The payload, authentication header, per-stream versioning and separate live/result semantics are unchanged. No schema migration is required.

The event publishing credential is visible as plain text in both viewing and editing modes and after reload. A blank save still keeps the stored credential. No schema change is needed; rollback removes the `apiKey` settings response field and restores the hidden input.

Manual result resend uses the same confirmed-result snapshot builder as automatic publishing, skips programs without confirmed results and never invokes the live-score sender. The backend revalidates publishing configuration and program ownership. Automatic live snapshots, including the empty update when play ends, are unchanged. No database migration is required.


## Reset lifecycle update - 6 November 2026

[Reset lifecycle](RESET_LIFECYCLE.md) is authoritative for result/schedule clearing, history-independent eligibility, dependent-round and draft cleanup, device recovery, same-day operation and result-delivery retries. It supersedes conflicting historical descriptions above.


## Manual result action - 6 November 2026

Fixture and Result exposes **Options > Submit Result** for the selected program. There is no separate resend button. The action requires configured publishing. If the program has no completed or walkover results, selecting it displays **No result to submit.** and makes no publishing request; automatic BYEs alone do not count. Successful manual submission displays **Result submitted.** Automatic reset synchronization continues sending empty snapshots independently of this manual check.

## Live game phase

The live feed adds optional gamePhase, phaseUntil and gameNumber fields. Warm-up is sent as SC with phase WU. Other live phases use the existing IP or RC match status. The publisher sends phase transitions on its background cycle even if scores did not change. Update the TRS receiver before enabling a TLS build that publishes warm-up.
