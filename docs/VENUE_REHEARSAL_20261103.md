# Venue load rehearsal — 3 November 2026

## Scope and environment

This is a simulated HTTP/SQL rehearsal on the development computer, not a physical eight-tablet venue test. Windows, Intel Core Ultra 9 285H, normal Debug build of the .NET 8 API, configured SQL Server backend. API and load clients run on the same host. The script creates and deletes its own TLS_Test database; existing TLS/TRS records are untouched.

Fixture: 8 programs, 256 participants, 128 scheduled opening-round matches, 8 paired/playing matches and 8 reserved umpires. Synthetic 50-point rules keep games active through all three phases. Each tablet sends approximately one point per second, polls state every 2 seconds and heartbeats every 5 seconds. The dashboard and each display poll every 2 seconds. Three phases target 20 seconds each; worker shutdown finishes around 20.8–20.9 seconds. Fixtures are prepared before timing starts.

Command: `python tests/venue_load_rehearsal.py --seconds 20 --entries 32`

## Repeat run without other test suites alongside it

| Display clients | Scoring commands | Median scoring ms | p95 scoring ms | Maximum scoring ms | p95 tablet-state ms | p95 dashboard ms | p95 display ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | 158 | 61.8 | 157.6 | 195.4 | 161.1 | 161.8 | — |
| 2 | 159 | 56.7 | 78.5 | 91.5 | 67.6 | 94.2 | 79.2 |
| 4 | 160 | 63.1 | 102.1 | 242.7 | 92.2 | 107.3 | 110.5 |

All 903 measured requests succeeded. All 477 confirmed points and their rally records matched the commands sent; no lost or duplicate points. Display reads numbered 20 with two clients and 40 with four clients. There were 80 tablet-state polls and 32 heartbeats per phase. Percentiles use nearest-rank selection.

An earlier run of the same larger fixture alongside other verification work also completed without errors; its highest scoring p95 was 433.9 ms. Timing is sensitive to other workloads, first-phase warm-up and SQL/OS caches; the lower two-display result is not evidence that adding displays improves performance.

## Decision and limits

No caching, database architecture or storage changes were made. The measured local workload does not currently justify those changes. This short run is not a capacity guarantee: database size, historical results, growing audit records, venue hardware and Wi-Fi can change response times. Latency covers the HTTP response, not browser rendering or touch input.

Before a real event, repeat with 8 physical tablets, one dashboard and the intended 2–4 screens on venue Wi-Fi. Check acknowledgement time, reconnection, background/sleep behaviour, visible display updates and Undo. Run a longer full-event rehearsal with representative historical data. Session overrun policy remains outside this task and is unchanged.

## Functional verification

- 111 focused rules checks, including two/three/four groups, one/two qualifiers, ID permutation invariance, seeded BYE precedence and unchanged group results.
- 66 qualifier preview API checks: initial draw, BYE swaps, origin/result/live-metadata preservation, same-group rejection, scheduled-preview rejection, confirmation and later-round restrictions.
- 162 existing batch draw API checks.
- 236 existing live scoring/lifecycle API checks.
- 179 frontend/workflow tests, including fast-ack double-tap protection, pending retry, usable Undo during the 500 ms lockout and qualifier preview wiring.
- Frontend and backend builds passed; backend had no warnings or errors. Frontend retains its existing bundle-size advisory.

These are reproducible automated checks, not a claim that physical venue testing has been completed.
