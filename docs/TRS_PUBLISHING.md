# Publishing live scores and official results to TRS

TRS and TLS use event-level live snapshots and program-level confirmed-result snapshots. Original TRS event/program IDs identify the destination; local TLS match and entry references remain descriptive snapshot content. TRS prioritizes its own recorded results when both sources exist, otherwise it displays the TLS snapshot. No TRS fixtures are created by publication.

## Configure

For direct event fetching, configure TLS server environment variables `Trs__ApiBaseUrl` (TRS origin) and `Trs__AccessToken` (existing TRS administrator JWT). Without the API URL, the existing `Trs:EventFile` transfer-file connection still works. Registration must be closed and the event must have no TRS fixtures. Export/import use setup schema 2. There is no post-transfer ownership lock.

In Event Settings → TRS Publishing, set the live API URL, results API URL containing `{programId}`, API key and 2–120 second interval; then enable publishing. Example URLs for original event 123:

- `https://trs.example/api/tls/events/123/live`
- `https://trs.example/api/tls/events/123/programs/{programId}/results`

TRS must apply its `20261103_create_tls_snapshots.sql` script and configure `TlsIntegration__Events__123__ApiKey` with the same random key (at least 32 printable characters). Use HTTPS for deployed connections. Redirects are disabled.

Settings use the existing event-operation JSON. The credential is encrypted with ASP.NET Data Protection, is not returned to the settings form, and is omitted from spectator payloads. Preserve the service account/key ring across deployment; re-enter the credential if the key ring changes. Existing readable TLS IDs remain unchanged. Publication versions use existing persistent ID sequences and remain increasing across restarts. No TLS SQL schema upgrade is required.

## Operation

The background publisher sends current IP/RC live matches periodically. It attempts an empty live snapshot promptly when the last match stops being live. All confirmed program results (including preceding rounds) publish after changes. Exceptional outcomes preserve their scores and reason; TRS adds a readable remark when the outcome does not fit its normal display fields.

A small TLS dashboard badge appears only on failure. Expand it for the last failure and Retry Publishing. Event Settings also provides Resend Results / Live Scores. Successful sends clear the relevant failure. Live sends try again at the next interval; result failures require manual resend or another result change. There is no persistent delivery queue. Restart republishes current state, while failure badges themselves reset.

Disabling publishing stops sends; it does not clear TRS data. With the agreed no-expiry public design, TRS can retain old live scores if the empty publication cannot arrive. Restore connectivity and use resend. Empty live snapshots do not remove official results. Result clearing/correction publishes a newer complete program snapshot.

Local organiser endpoints: GET/PUT `/api/trs/publishing/{eventId}`, POST `/{eventId}/resend`, GET `/status/all?eventId=...`. Existing event operating-settings saves preserve these separate publishing settings. Admin APIs remain local-only.

## Validation

`python tests/trs_publishing_integration.py` creates a disposable SQL database and local HTTP receiver, verifies direct setup fetching, original-ID projection, confirmed results, empty live snapshots, failed sends/recovery and increasing versions after restart. It does not write to the real TLS/TRS databases. Frontend tests cover settings, saved credentials and the failure badge; run them with the existing frontend workflow harness.

For the complete receiving contract and TRS public-page behavior, see the TRS repository's `TRS_TLS_INTEGRATION.md`. Rollback: disable publication, restore the prior TLS binary and remove only the new `publication` property from saved event-operation JSON before using an older strict deserializer. Keep a backup; changing JSON in an existing database requires the usual explicit approval. Do not revert unrelated event settings or results.
