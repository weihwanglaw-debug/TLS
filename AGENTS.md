# TLS development instructions

## Graphify — Mandatory Graph-First Development Workflow

Follow the applicable global change approval policy. For debugging, architecture reviews, feature development, refactoring and impact analysis in TLS, use this sequence.

1. **Check freshness.** From the TLS root run `python tools/graphify_tls.py check`. Exit 0 means current, 2 means missing or stale, and 1 indicates failure. Skip graph work for trivial edits. If a refresh needs approval under the applicable policy, include the generated local artifacts in scope. Until refreshed, use source inspection.
2. **Consult the graph.** Resolve the current graph with `python tools/graphify_tls.py path`. Use Graphify query, path, explain or affected with that explicit graph. Start with the relevant module or symbol and look at callers and downstream dependencies before broad searches.
3. **Verify in source.** Read the implementation on both ends of important edges. Trace routes, JSON storage, dynamic calls and missing edges directly. A current graph can still have incomplete coverage.
4. **Assess impact before changes.** For scoring, fixtures, scheduling, results and administrative overrides, inspect affected UI, API, storage and downstream workflows. Preserve sport-specific rules, administrator flexibility and existing result-correction safeguards.
5. **Synchronize after changes.** After testing a graph-relevant change, run `python tools/graphify_tls.py sync`, check freshness again and verify a relevant graph finding. Report any failed refresh. The same sync command refreshes stale data at the start of a task.
6. **Keep work proportional.** Reuse a current graph and previous targeted query results. Prefer incremental extraction. If the tool fails, continue urgent work using source and report the gap.
7. **Report.** For relevant tasks identify the graph areas consulted, source verified, changes, tests and final sync status.

See [the local Graphify runbook](docs/GRAPHIFY.md) for examples, coverage limits and recovery. These instructions run within a Codex session. External edits are detected on the next check.
