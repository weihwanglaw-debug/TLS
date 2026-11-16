# TLS visual design — 16 November 2026

Organiser and umpire controls use one fixed light appearance inspired by the scheduling prototype. Shared root tokens in Frontend/src/trs-theme.css define navy #233c58, page #f5f7fa, white inputs/panels, soft #eaf0f7, borders #dce3eb and burnt-orange actions #b74328. Shadcn primary/ring mappings, system typography and restrained radii follow the same design. Semantic feedback colours remain distinct. Shared organiser surfaces live in styles.css.

SetupApp clears the obsolete livescore_theme preference safely and no longer toggles it. AdminLayout has no theme switch. UmpireApp always selects the fixed appearance. Root tokens are available before mounting, including direct tablet/display URLs. System dark appearance does not change control styling.

The hall bigscreen keeps its dedicated fixed dark high-contrast presentation and configured team colours; this is not a selectable application mode. Umpire team colours and scoresheet print layout remain intact. Background-specific logo settings remain valid on navy navigation and light surfaces.

No API, backend, database, scoring or scheduling rule changed. No deployment, normal service restart or commit is included. Rollback restores only scoped pre-task frontend files and rebuilds; no data conversion is needed. Existing unrelated work must be preserved.


Verification: TypeScript and Vite production build passed. The full disposable SQL-backed frontend suite passed 269 tests across 37 files; the subsequent navigation test addition passed all 3 navigation checks (270 tests in combined coverage). Focused navigation/tablet-timer/bigscreen/scoresheet checks passed 38 tests before that addition. Browser checks on organiser offline dialog and tablet access at desktop/mobile sizes confirmed fixed light controls under system dark preference plus obsolete saved theme b, no theme switch and no JavaScript errors. Team/paper/display behaviour is covered by existing component tests; full populated-page visual review and physical printing remain unverified. Existing bundle-size advisory remains. No normal services were started.
