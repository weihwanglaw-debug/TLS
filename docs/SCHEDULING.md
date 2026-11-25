# Scheduling

The current source of truth is [Event scheduling](TOURNAMENT_SCHEDULE_PLANNER.md).

Use Program setup → Draw → Schedule → Fixtures & Results. Complete every opening draw before scheduling; future rounds can be planned before results. Set per-round duration, rest preference and 5/10/15-minute start steps. Configure non-overlapping sessions/courts, assign every match, then save the complete event once.

There is no persisted draft workflow. Session edits and allocation previews remain local until the full save. Leave guards reduce accidental loss; browser crashes can still lose unsaved work. Usable remaining openings are shown per court instead of an estimated match capacity.

Saved moves/swaps preserve duration and validate the result. Same-program swaps stay within the same stage and round. Walkovers release slots without shifting later games; undo uses result-correction protections. Planned player overlaps warn, while actual called/playing players and officials block another call/start.

Existing session-calculation API helpers remain for preceding non-planner clients, but draft writers/types are removed and the retired UI is deleted. Legacy writers cannot overwrite planner-owned events. Strict JSON cleanup, backup and rollback instructions are in the current guide.
