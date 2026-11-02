import {useState,type ReactNode} from "react";
import {ArrowLeftRight,ChevronLeft,CheckCircle,Loader2} from "lucide-react";
import type {BracketState,SeedEntry,TeamEntry} from "@/types/config";
import {getAllMatches,computeGroupStandings} from "@/lib/fixtureEngine";
import {getEntryDisplay} from "@/lib/entryDisplay";
export function FixturePreview({ bracket, seeds, onSwap, onConfirm, onBack, saving, allowSwap=true, saved=false, children }: {
  bracket:   BracketState;
  seeds:     SeedEntry[];
  onSwap:    (idA: string, idB: string) => Promise<void>;
  onConfirm: () => void;
  onBack:    () => void;
  saving:    boolean;
  allowSwap?: boolean;
  saved?: boolean;
  children?: ReactNode;
}) {
  const [selectedTeamId, setSelectedTeamId] = useState("");
  const handleTeamPick = async (team: TeamEntry) => {
    if (team.id.startsWith("bye-") || team.label === "BYE") return;
    if (!selectedTeamId) {
      setSelectedTeamId(team.id);
      return;
    }
    if (selectedTeamId === team.id) {
      setSelectedTeamId("");
      return;
    }

    try { await onSwap(selectedTeamId, team.id); } catch { return; }
    setSelectedTeamId("");
  };

  const previewEntryDisplay = (team: TeamEntry) => {
    return getEntryDisplay({
      teamMode: team.teamMode,
      label: team.label,
      participants: team.participants,
      participantClubs: team.participantClubs,
    }, "compact");
  };

  const SwapPickButton = ({ team }: { team: TeamEntry }) => {
    const isBye = team.id.startsWith("bye-") || team.label === "BYE";
    if (isBye||!allowSwap) return null;
    const selected = selectedTeamId === team.id;
    return (
      <button
        type="button"
        className="p-1.5 flex-shrink-0 hover:opacity-80"
        title={selected ? "Cancel swap selection" : "Select for swap"}
        disabled={saving} onClick={() => void handleTeamPick(team)}
        style={{
          color: selected ? "var(--color-hero-text)" : "var(--color-primary)",
          backgroundColor: selected ? "var(--color-primary)" : "transparent",
          border: "1px solid var(--color-primary)",
        }}
      >
        <ArrowLeftRight className="h-3.5 w-3.5" />
      </button>
    );
  };

  // Heats preview
  const HeatsPreview = () => (
    <div style={{ border: "1px solid var(--color-table-border)" }}>
      <div className="px-4 py-2 font-bold text-xs uppercase tracking-wide"
        style={{ backgroundColor: "var(--color-primary)", color: "var(--color-hero-text)" }}>
        Participants - {bracket.heatRounds?.[0]?.results.length ?? 0} in Round 1
      </div>
      <table className="trs-table">
        <thead><tr><th>#</th><th>Club / School</th><th>Players</th><th>Seed</th></tr></thead>
        <tbody>
          {seeds.map((s, i) => (
            <tr key={s.id} style={selectedTeamId === s.id ? { backgroundColor: "var(--badge-open-bg)" } : undefined}>
              <td className="opacity-30 font-mono text-xs">{i + 1}</td>
              <td className="font-medium text-sm">{getEntryDisplay({
                teamMode: s.teamMode,
                club: s.club,
                participants: s.participants,
                participantClubs: s.participantClubs,
              }, "compact").main}</td>
              <td className="text-xs opacity-60">
                {s.teamMode ? "" : s.participants.join(" / ")}
              </td>
              <td>{s.seed != null
                ? <span className="text-xs font-bold px-1.5 py-0.5" style={{ backgroundColor: "var(--color-primary)", color: "var(--color-hero-text)" }}>#{s.seed}</span>
                : <span className="opacity-25 text-xs">-</span>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );

  // Group preview
  const GroupPreview = () => (
    <div className="grid grid-cols-1 md:grid-cols-2 2xl:grid-cols-4 gap-4">
      {bracket.groups.map(grp => {
        const points = bracket.config.standingPoints;
        const standings = computeGroupStandings(grp, undefined, points
          ? { win: points.win, draw: points.draw, loss: points.loss }
          : undefined);
        return (
          <div key={grp.id} className="min-w-0" style={{ border: "1px solid var(--color-table-border)" }}>
            <div className="px-4 py-2 font-bold text-xs uppercase tracking-wide"
              style={{ backgroundColor: "var(--color-primary)", color: "var(--color-hero-text)" }}>
              {grp.name}
            </div>
            <table className="trs-table">
              <thead><tr><th style={{ width: 44 }}>#</th><th>Entry</th><th style={{ width: 64 }}>Seed</th></tr></thead>
              <tbody>
                {standings.map(s => {
                  const entry = previewEntryDisplay(s.team);
                  return (
                  <tr key={s.team.id} style={selectedTeamId === s.team.id ? { backgroundColor: "var(--badge-open-bg)" } : undefined}>
                    <td className="font-bold text-sm">{s.rank}</td>
                    <td>
                      <div className="flex items-center gap-2"><SwapPickButton team={s.team}/><div className="font-medium text-sm leading-tight">{entry.main}</div></div>
                      {entry.sub && <div className="text-xs opacity-50 mt-0.5">{entry.sub}</div>}
                    </td>
                    <td>{s.team.seed != null
                      ? <span className="text-xs font-bold px-1.5 py-0.5" style={{ backgroundColor: "var(--color-primary)", color: "var(--color-hero-text)" }}>#{s.team.seed}</span>
                      : <span className="opacity-25 text-xs">-</span>}
                    </td>
                  </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        );
      })}
    </div>
  );

  const TeamPreviewCell = ({ team }: { team: TeamEntry }) => {
    const entry = previewEntryDisplay(team);
    const selected = selectedTeamId === team.id;
    return (
      <div
        className="min-w-0 flex items-center gap-2 p-1"
        style={selected ? { backgroundColor: "var(--badge-open-bg)" } : undefined}
      >
        <SwapPickButton team={team} />
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-1.5 min-w-0">
            {team.seed != null && (
              <span
                className="text-xs font-bold px-1.5 py-0.5 flex-shrink-0"
                style={{ backgroundColor: "var(--color-primary)", color: "var(--color-hero-text)" }}
              >
                #{team.seed}
              </span>
            )}
            <span className="font-medium text-sm truncate">
              {entry.main}
            </span>
          </div>
          {entry.sub && (
            <p className="text-xs opacity-50 truncate mt-0.5">{entry.sub}</p>
          )}
        </div>
      </div>
    );
  };

  // KO round 1 preview
  const KoPreview = () => {
    const firstRound=Math.min(...bracket.matches.map(m=>m.round));
    const r1 = saved?getAllMatches(bracket):bracket.matches.filter(m => m.round === firstRound);
    if (!r1.length) return null;
    return (
      <div style={{ border: "1px solid var(--color-table-border)" }}>
        <table className="trs-table" style={{ tableLayout: "fixed", width: "100%" }}>
          <thead>
            <tr>
              <th style={{ width: 72 }}>Match</th>
              <th style={{ width: "calc((100% - 120px) / 2)" }}>Team 1</th>
              <th className="text-center" style={{ width: 48 }}>vs</th>
              <th style={{ width: "calc((100% - 120px) / 2)" }}>Team 2</th>
            </tr>
          </thead>
          <tbody>
            {r1.map((m, i) => (
              <tr key={m.id}>
                <td className="opacity-30 font-mono text-xs text-center">{i + 1}</td>
                <td><TeamPreviewCell team={m.team1} /></td>
                <td className="text-center opacity-25 font-bold text-xs">vs</td>
                <td><TeamPreviewCell team={m.team2} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    );
  };

  return (
    <div className="space-y-5">
      <div className="flex items-center justify-between">
        <div>
          <p className="font-bold text-sm">
            {saved?'Saved Draw':'Preview Draw'}
            {bracket.matches.length > 0 && (
              <span className="ml-2 opacity-50 font-semibold">/ {bracket.matches[0]?.roundLabel||"Group Matchups"}</span>
            )}
          </p>
          {!saved&&<p className="text-xs opacity-50">Review the draw, then confirm to save.</p>}
        </div>
      </div>


  {/* Bracket preview */}
      <div className="space-y-3">
        {bracket.format === "heats"         && <HeatsPreview />}
        {!saved&&bracket.groups.length > 0 && <GroupPreview />}
        {(bracket.matches.length > 0||saved&&bracket.groups.length>0) && <KoPreview />}
      </div>

      {children}

  {/* Actions */}
      {!saved&&<div className="flex justify-between items-center pt-2"
        style={{ borderTop: "1px solid var(--color-table-border)" }}>
        <button disabled={saving} onClick={onBack} className="btn-outline flex items-center gap-2 px-5 py-2.5 text-sm">
          <ChevronLeft className="h-4 w-4" /> Back
        </button>
        <button onClick={onConfirm} disabled={saving}
          className="btn-primary flex items-center gap-2 px-6 py-2.5 text-sm font-semibold disabled:opacity-40">
          {saving
            ? <><Loader2 className="h-4 w-4 animate-spin" /> Saving...</>
            : <><CheckCircle className="h-4 w-4" /> Confirm &amp; Save</>}
        </button>
      </div>}
    </div>
  );
}

