# Current appearance — restored light/dark themes, 17 November 2026

TLS again uses the original two selectable palettes from commit `69dc815`: light Slate–Coral (`a`) and dark Obsidian–Rose (`b`). The sidebar's Toggle theme button switches modes and saves `livescore_theme` in browser storage. Refreshing restores that preference; invalid/unavailable storage falls back to light. The umpire screen reads the same saved preference on opening.

Shared tokens, native input colour schemes and light/dark live-status colours are restored. The new scheduler inherits the selected palette instead of forcing light colours. White-only panel/card/table overrides are removed. The hall display keeps its dedicated presentation, team colours remain unchanged, and printing retains existing styles.

Only presentation, its preference and appearance tests were changed. Scheduling, scores, result submission, API contracts, database records and server settings are untouched. Existing scheduling and unrelated working-tree changes are preserved. The running development frontend picks up the changes; deployed assets are not replaced.

Validation and rollback: run the frontend build and existing UI tests, check switching/reload, populated participant/schedule pages, dialogs and direct umpire/display URLs. Scoped pre-restoration backups are outside the repository. Restore only those scoped presentation files to undo this restoration.

Verification completed: frontend production build passed (existing bundle-size advisory); all 286 frontend tests across 38 files passed. Real Chrome checks verified both participant palettes, switching, reload persistence, scheduler inputs, dark preset dialog, direct umpire preference and tablet navigation with no page errors. Browser checks used read-only operational APIs and an isolated browser profile; no business records were edited. Existing patches outside this presentation scope were compared against the pre-task snapshot and stayed identical.

Files changed: `SetupApp.tsx`, `AdminLayout.tsx`, `UmpireApp.tsx`, `trs-theme.css`, `styles.css`, `live.css`, `schedule-planner.css`, `FoundationUi.tsx`, `Frontend/tests/admin-navigation.test.tsx` and this guide. No backend, database or API change.

## Fixture and schedule hierarchy

One event heading anchors the page. Main Seeding / Draw / Schedule / Fixtures & Results navigation defines task scope. Program selection belongs inside Seeding and Draw. Schedule and results show the whole event, with per-program actions attached to the corresponding results section.

The schedule substeps are a smaller nested navigation row, not another set of equally prominent cards. Destination-labelled footer actions replace ambiguous floating Back buttons. Settings are prepared locally and saved together; no extra Save draft controls or repeated event headings exist.

Necessary guidance uses `ui-help` with a consistent icon, border, spacing and short text beside the relevant control. Do not append explanatory paragraphs to otherwise complete screens. Placement errors appear beside the attempted round assignment; substantive warnings remain in the review section. Both light and dark palettes use shared tokens.
