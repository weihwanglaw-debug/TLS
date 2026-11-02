# TLS database operations

TLS uses EF Core 8.0.23 and SQL Server. Active models are in `Backend/TLS.Data`; setup validation, imports and persistence are in `Backend/LiveScore.Api`. Each store operation creates its own DbContext. Consistent reads use serializable transactions; writes first lock the singleton SystemSettings row and verify its revision, then save records and audit together.

## Configuration and provisioning

The workstation uses `localhost\SQLEXPRESS`, database `TLS`, with the previously configured SQL login. `Backend/LiveScore.Api/appsettings.Local.json` stores the private connection under `ConnectionStrings:TLSConnection` and is ignored by Git. Environment variable `ConnectionStrings__TLSConnection` overrides it. TLS does not access TRS configuration or databases at runtime.

For a new empty database, run from Backend/LiveScore.Api:

```powershell
dotnet run -- --initialize-sql true
```

For an existing EF-based TLS database, apply the additive import schema:

```powershell
dotnet run -- --upgrade-imports true
```

`Sql/20261012_import_only.sql` creates EventImports if absent. It adds no destructive changes. Initial schema is also available at `Backend/TLS.Data/Sql/InitialSchema.sql`. Initial creation is not an upgrade mechanism. Neither operation runs during ordinary app startup. Administrative setup is restricted to TLS and generated test database names.

## Active storage

- Events, Programs, Participants, ProgramEntries and EntryMembers hold relational preparation data.
- SystemSettings holds master timezone and concurrency revision.
- EventImports holds source identity, latest original JSON package, source/export timestamps and preparation stage (`P`).
- AuditLogs holds timestamped before/after data snapshots for confirmed imports, replacement and settings changes.

Existing ImportReceipts, ParticipantAliases, WorkspaceStates, Courts, MatchOperations, QueueEntries and StorageMigrations tables, and the old TrsUrl column, are retained untouched for recovery/history. They are no longer mapped or used by the application. New installations do not create them. No compatibility code or legacy UI is retained. Removing archived tables requires a separate, explicitly approved data-cleanup task.

The one-time SQLite migration is retired. Its original files and pre-EF backups are historical only; SQLite no longer receives writes. The Microsoft.Data.Sqlite dependency has been removed.

## Backup and rollback

Before this upgrade, source/configuration was copied to `D:\Source\TLS-Backups\import-only-20261012-104658`. A SQL Server COPY_ONLY backup with CHECKSUM was written to the instance backup directory as `TLS-before-import-only-20261012-104658.bak`. Keep private configuration backups access-restricted.

Continue taking regular SQL Server backups of TLS. To roll back, stop TLS and restore both the matching pre-upgrade application and its database backup. If new imports have occurred, first back up the current database and reconcile those records; restoring an older database loses subsequent work. Do not drop TRS/TLS or delete local data as a shortcut. This upgrade does not modify TRS.

## Tests

`python tests/import_integration.py` creates a uniquely named `TLS_Test_<guid>` database and runs an isolated API on port 5193. Only that generated database is deleted afterward. The configured login must have database-creation/deletion permissions. Production TLS and TRS records are never used for test writes.

The plain-template update adds AppName, LogoLightUrl and LogoDarkUrl to SystemSettings through the reviewed additive upgrade script. The default application name is TLS; logos default blank. Existing records and import identities are retained. No tables or columns are dropped. Source/application and SQL COPY_ONLY backups were taken before this update under TLS-Backups/plain-template-* and the SQL instance backup directory.

## Tournament update
The additive Sql/20261012_tournament.sql script adds CourtCount to SystemSettings and EventOperations/ProgramOperations tables keyed to the imported event/program. Run --upgrade-tournament true on an existing database; the new initializer creates all current tables directly. The JSON operational aggregates follow TRS bracket storage and are saved through EF Core in the existing revision-checked/audited transaction. No source records are changed by seeding or results. The original import stage is F while any generated draw exists; reset is permitted only without entered results.
Before this update, source and SQL COPY_ONLY backups were taken under TLS-Backups/tournament-* and the SQL instance backup directory. See TOURNAMENT.md for operation and rollback details.

## Live dashboard update
Sql/20261013_live.sql adds SystemSettings.LiveVenueJson; apply --upgrade-live true before running this build against an existing database. MatchLiveRecords is an additive table keyed by match ID with a Programs foreign key; its live rows keep high-frequency score writes separate from the ProgramOperations fixture document. See LIVE_DASHBOARD.md for backup, migration, bounded histories, compact auditing and matching application/database rollback.

## TRS fixture/result adaptation (14 October 2026)

No new table/column migration. Existing ProgramOperations JSON adds format/ranking snapshot fields; MatchLiveRecords JSON adds optional result details/source/corrections. EF remains the storage layer. Existing missing fields receive compatible defaults. No demo data is initialized and existing imported records are retained. See FIXTURE_RESULTS.md for versioned result APIs, verification and the matching source/SQL backup.

## 2026-10-14 event workflow and presets

No SQL table/column migration is required. EventOperations JSON now stores DailySessions, Courts, CourtsConfigured and Progression (I/A). SystemSettings.LiveVenueJson stores presets (A/X); programme fixture JSON holds PresetId and RulesAssigned with frozen match rules. SQL/EF persists all changes and audits. Legacy dated sessions and rule snapshots remain readable; no fixture/time/participant data is automatically replaced. No runtime samples or test rows are seeded into TLS.

Source backup: D:/Source/TLS-Backups/event-workflow-20261014-155709. SQL COPY_ONLY/CHECKSUM backup: TLS-event-workflow-7C5097B7-181D-4FAD-986D-85FE5AE50F22.bak in the SQL instance Backup directory. Restore matching application/database versions for rollback, preserving later records first. Test runners create and remove their own disposable TLS_Test databases.

## 19 October 2026 umpire serving state

No schema migration: serving positions, interval deadline, recorded rallies and undo extensions are stored in MatchLiveRecords JSON; ReviewAvailable is stored per fixture. Legacy fields remain readable without a startup rewrite. Old matches must explicitly establish current serving positions before tablet points; manual final scores never generate missing rally history. Old EventPlan.ReviewCourts remains only for reading legacy assignments. Deploy frontend/backend together and consult UMPIRE_SCORING.md before any downgrade.


## 21 October 2026 SBA storage

`Backend/LiveScore.Api/Sql/20261021_sba_rankings.sql` adds SbaRankings and BadmintonClub only. Run the API's `--upgrade-sba` command from its content directory against an existing TLS database. Fresh `--initialize-sql` creates the same tables through EF. The script is transactional and repeatable; it never deletes or updates event, participant or fixture data. Participant Club remains an independent imported string, without a foreign key or master-name replacement. See SBA_RANKINGS.md for API, verification and rollback details.


## Readable business IDs — 28 October 2026

IdSequences and ImportIdMappings store permanent counters and original-source-to-local-ID mapping. Apply the additive --upgrade-business-ids command before running the new backend against an existing database. No automatic destructive startup is introduced. The separately approved test-record cleanup preserves masters and counters, renumbers legacy preset IDs, clears business records and old test audits, and records one cleanup audit. See [business ID operations](BUSINESS_IDS.md) for commands, transaction guarantees, affected tables, testing and rollback restrictions.

## Phase 2 additive operation JSON — 30 October 2026

No schema migration or operational-data cleanup is required. EventOperation JSON adds PlanningDraft. ProgramOperation JSON adds ranking decisions, qualifier origins, official result decisions, result-kind/reason, dependent-source/pending markers and per-match court exceptions. Older JSON remains readable through defaults. Current partial score is derived from live state for the API and excluded from fixture JSON so rally writes keep their existing persistence scope. MatchLiveRecord and existing audit storage remain in use.

Rollback requires coordinated application rollback. Earlier binaries may ignore/drop new JSON fields on save and cannot safely represent exceptional results or pending replay dependencies. Do not downgrade an event using these features mid-tournament; retain/export its operation/history data and complete or reconcile recovery first.
