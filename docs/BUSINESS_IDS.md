# Readable TLS business IDs — 28 October 2026

## Format and ownership

| Record | Example | Numbering scope |
| --- | --- | --- |
| Event | EV000001 | Installation |
| Program | EV000001-P001 | Event |
| Participant | EV000001-U00001 | Event; shared across that event's programs |
| Entry/team | EV000001-P001-E0001 | Program |
| Draw group | EV000001-P001-G001 | Program |
| Match | EV000001-P001-M0001 | Program |
| Official | EV000001-O001 | Event |
| Game preset | GP0001 | Installation |

These are the actual stored business IDs used by APIs and URLs. Prefixes are independent of names, event dates, current round and row order. Padding is a minimum width and grows as necessary. Committed IDs are permanent: removed records, discarded/replaced draw rounds and deleted officials do not free their numbers. Match Number remains a separate event-facing fixture number. Existing external SBA membership IDs, ranking IDs and club IDs retain their own master-system meaning.

All permanent allocation happens on the server. SetupStore serializes business writes through the existing SQL transaction, singleton settings lock and revision check. IdSequences retains the highest committed number per prefix; no maximum-over-current-rows numbering is used. Import and draw previews use detached counters without SQL writes. Preview numbers become permanent only on successful confirmation. A stale/concurrent confirmation is rejected; failed or abandoned previews do not consume numbers. The draw preview carries its counter snapshot into the same confirmation transaction as the new fixtures. Batch draw generation uses that same allocator.

## Imports and officials

ImportIdMappings relates a private source fingerprint (source kind/system/event, record kind and original source ID) to the readable ID. Entries additionally include their source program identity, so moving an entry to another program cannot keep an incorrect parent prefix. Original source IDs remain in this mapping and the saved original EventImports package. Different sources/events can reuse their source IDs. Reordered replacement imports keep the same local IDs; new source records receive new numbers. Retired mappings stay reserved through replacement, allowing a removed/reintroduced source record to regain its original ID. Duplicate names, source identity, revision and replacement-before-play validation remain enforced.

Fingerprints are private lookup keys, not event/program IDs and never appear in organiser URLs. API request/response ID fields remain strings. Internal mapping/counter dictionaries are not exposed by setup JSON. The Excel template and TRS source contract do not change; users continue using their source/template IDs.

Event Settings may use temporary browser keys while editing new officials. Saving assigns permanent event-scoped official IDs and returns the authoritative updated plan; existing official IDs are retained. Pairing codes, tablet tokens, preview tokens and command-idempotency IDs remain random. Predictable business IDs do not replace access checks or pairing credentials.

## Database upgrade and approved test cleanup

New databases include IdSequences and ImportIdMappings. For an existing TLS database, stop the server and run from the project root:

```powershell
dotnet run --no-build --project Backend/LiveScore.Api -- --upgrade-business-ids true
```

The upgrade is additive and idempotent. It does not rewrite records or delete data. Normal startup never applies upgrades or clears data. Before using new generation on an old dataset, convert its mappings/IDs through a separately approved migration or explicitly clear disposable test data. This task uses the user's approved clean reimport approach.

The separately authorized local maintenance command is:

```powershell
dotnet run --no-build --project Backend/LiveScore.Api -- --clear-test-records true
```

It clears active Events, Programs, Participants, ProgramEntries, EntryMembers, EventImports (including original packages), EventOperations, ProgramOperations, MatchLiveRecords and ImportIdMappings, plus venue tablet bindings and old test AuditLogs. It writes one TEST_RECORDS_CLEARED audit containing counts and retained preset references, without retaining deleted personal records in audit snapshots. Masters are preserved: SBA rankings, badminton clubs, game-preset names/rules/status/default, timezone, court count/availability, app name/logos and private configuration. Legacy preset IDs are changed to readable GP IDs once event references are removed. Already-readable preset IDs are retained. Persistent counters are never reset, even on repeated cleanup; a later reimport receives a new event ID. The cleanup is one SQL transaction and verifies exact master contents before committing. Failed verification rolls back the whole cleanup. Commands are restricted to the TLS database or generated TLS_Test databases.

Old event/program links and tablet sessions are invalid after this approved deletion. Reimport restores preparation, not deleted scores or schedules. No database backup or old-ID aliases are created for the explicitly disposable test dataset. Archived legacy business tables were inspected and contained zero rows; legacy Courts and StorageMigrations metadata are retained.

## Validation and rollback

Tests exercise readable parent prefixes, import preview/no-write behaviour, replacement/reordering/new records, concurrent commits, reset/redraw non-reuse, group/official IDs, restart persistence, master-preserving cleanup, legacy preset renumbering and retained counters after cleanup. Existing import, fixture, scheduling, reset and live tablet suites cover IDs flowing through business operations.

Keep the additive tables and their values when rolling application changes back. Once readable IDs are issued, any backend rollback must retain compatible mapping/allocation behaviour; simply restoring hash-based import code would break replacement identity. Deleted test data is intentionally unrecoverable by code rollback. Do not drop sequence/mapping tables or restore stale counters over subsequently issued IDs.

Main files: Backend/LiveScore.Api/{BusinessIds,ImportWorkflow,SetupModels,SqlStorage,TournamentRules,TournamentEndpoints,GamePresets,DatabaseSetup,TestRecordCleanup,Program}.cs; Backend/TLS.Data/{Models,TLSDbContext}.cs; Backend/LiveScore.Api/Sql/20261028_business_ids.sql; Backend/TLS.Data/Sql/InitialSchema.sql; tests/business_ids_integration.py and affected regression fixtures. Documentation: this guide, SETUP.md, DATABASE.md and README.md.


## Completed local verification

Both backend and frontend production builds passed; backend reported zero warnings/errors and frontend retains its existing bundle-size warning. Disposable SQL suites passed 84 ID/cleanup, 240 import, 154 tournament, 177 group-scheduling, 236 live/tablet, 226 fixture/result and 151 reset checks (1,268 total). The complete SQL-backed React workflow passed 116 tests across 21 files after updating obsolete UI test selectors and replacing fixed official/court assumptions with server-issued identities. No application behaviour was changed to accommodate those obsolete selectors.

The authorized local cleanup removed 1 event, 2 programs, 24 participants, 24 entries, 28 matches, 1 import, its schedules and 232 prior test audit entries. Exact master-data verification passed: 1,640 SBA rows, 93 clubs, court count 4, Asia/Singapore settings/logos/app details and the preset Badminton-Shutter-Misconduct remain; its ID is GP0001. Active operational tables and import mappings are empty; one cleanup audit is retained. Legacy business tables were empty and retained court/storage metadata was not deleted. No event was reimported into the local database during verification. TLS was restarted at localhost:5180; the next import will use EV000001. The original workbook was not edited.


Event Details also supports removing a fixture-free event. This removes that event's source-ID mappings but preserves all committed counters and audit history. Importing the same source file afterward receives a new event ID; deleted IDs are not reused. See [event preparation](SETUP.md) for the validation and confirmation workflow.
