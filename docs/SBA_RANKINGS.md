# Standalone SBA Rankings — 21 October 2026

## Delivered scope

SBA Rankings is a standalone sidebar module, separate from fixture configuration. Frontend/src/SbaRankings.tsx is copied byte-for-byte from TRS Frontend/src/pages/admin/SbaRankings.tsx. The table, mobile cards, ranking-type filter, search, column sorting, import control, replacement summary and ActionFeedbackDialog are unchanged. The shell suppresses its duplicate header on this page. The API adapter at Frontend/src/lib/api.ts supplies the original page imports from TLS endpoints. No runtime ranking samples are packaged.

TLS is an independent product. Its ranking list and club master live in the local SQL Server database, with no synchronization or dependency on TRS. The original TRS page and source remain untouched. The separate Seeding stage now consumes this local list and enforces exact Program Name/category matching; see SEEDING.md. Draw/scheduling separation remains subsequent work. No seeding screen is embedded in this ranking-management page.

## Import and club boundary

Use the same SBA workbook accepted by TRS: recognized category sheets, the same positional columns for singles/doubles, shared-string Excel data and the same date conversion/parser. Unknown sheets are reported as skipped. A valid import replaces the entire ranking list, including categories omitted from the workbook, and adds missing club names case-insensitively. Existing clubs remain. This matches the TRS workflow and summary.

BadmintonClub is independent storage for future use. Event participant names, clubs, SBA IDs and identities remain exactly as supplied by the event Excel import or TRS interface. Ranking imports neither update participant records nor create participant-to-club foreign keys. Ranking players may show their ranking workbook club while the event participant retains a different imported club.

The local import uses a SQL transaction and the existing system-row serialization lock, so ranking replacement, new clubs and audit records commit together. Invalid data, duplicates or field-length failures leave existing rankings/clubs intact. Successful imports append SBA_RANKING_IMPORT and new-club audit entries. They do not mutate event revisions, existing seeding or saved program ranking snapshots. Malformed workbook containers return an import error without changing SQL.

## API and access

Copied controller/parser: Backend/LiveScore.Api/SbaController.cs. Routes: GET /api/sba/types, GET /api/sba/rankings?type=, GET /api/sba/members/{sbaId}, GET /api/sba/members?name=&type=, GET /api/sba/admin/members?query= and POST /api/sba/import (multipart field file). Response shapes follow TRS. The upload endpoint permits 20 MB, overriding the normal TLS 2 MB request limit. Access is integrated with TLS's existing localhost-only admin boundary, instead of introducing TRS JWT/login dependencies. Cross-origin mutations remain blocked by existing TLS middleware. No authentication/user-management module was added.

## Database and files

Backend/TLS.Data/SbaModels.cs copies the ranking and club entities with the TLS namespace. TLSDbContext.cs maps SbaRankings, its singles/pair unique indexes and BadmintonClub using TRS lengths/defaults. Program.cs adds MVC controller/filter registration and maps controllers. DatabaseSetup.cs adds --upgrade-sba. Sql/20261021_sba_rankings.sql is an additive, transactional, idempotent script; it does not drop or alter old tables.

Other files: Frontend/src/AdminLayout.tsx, SetupApp.tsx, types/config.ts, lib/api.ts and SbaRankings.tsx; tests/sba_integration.py and Frontend/tests/sba-rankings.test.tsx; README.md, DATABASE.md, FIXTURE_RESULTS.md and this guide. No package dependencies were added.

For an existing database, stop the TLS API before the normal backend build when its output is locked, then run from Backend/LiveScore.Api:

```powershell
dotnet bin/Debug/net8.0/LiveScore.Api.dll --upgrade-sba
```

Use the existing private ConnectionStrings:TLSConnection configuration. Do not place credentials in source or command arguments. The command validates that the target is TLS or a disposable TLS_Test database. The local TLS database was upgraded in this delivery. Before/after verification confirmed existing events, programs, participants, participant club text, entries, seeds, import metadata, operating plans, fixtures and device records were unchanged. No sample rankings or clubs were imported into the real database.

Rollback: restore the prior matching application files and rebuild. Leave the new tables unused to preserve imported rankings/clubs. No destructive schema rollback is required. Replacing rankings is an intentional admin import action; rolling the application back does not restore a previous ranking workbook.

## Verification and manual checks

Backend build: zero warnings/errors. Frontend TypeScript/Vite production build passed. Disposable SQL integration covers fresh/existing-schema upgrade, repeated upgrade, singles/doubles import, replacement across categories, skipped sheets, malformed/empty/wrong-extension workbooks, duplicate rollback (including new-club rollback), filters, member lookup/search, concurrent imports, localhost access, uploads above the default 2 MB limit and participant/event/seed isolation. All 25 React screen/component checks passed, including three standalone ranking screen tests for filters/search/sorting, import summary and failed-import list retention. Browser checks on the actual running TLS instance covered the new sidebar item, single page header, empty SQL-backed list, category filtering, light/dark themes and the 390x844 mobile layout without horizontal overflow. The page source was verified byte-identical to TRS; a populated live ranking list requires the user's real workbook.

Manual happy path: Sidebar → SBA Rankings → Import XLSX → check imported rows/categories/added clubs → filter/search/sort. Validation: invalid workbook must show the existing TRS error popup and retain prior data. Edge cases: doubles partner search, skipped sheets, replacement missing categories, case-insensitive club reuse and a participant whose imported club differs from their ranking club. The latter must retain their event club in Participant Details.
