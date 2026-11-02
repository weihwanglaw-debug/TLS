import type {MatchEntry,SeedEntry} from '@/types/config';
import {getEntryDisplay} from './entryDisplay';
const safeFilename=(s:string)=>s.replace(/[<>:"/\\|?*]/g,'-');
const csv=(rows:string[][])=>'\uFEFF'+rows.map(row=>row.map(value=>'"'+(/^[=+@-]/.test(value)?"'":"")+value.replace(/"/g,'""')+'"').join(',')).join('\r\n');
function download(name:string,text:string){const url=URL.createObjectURL(new Blob([text],{type:'text/csv;charset=utf-8'}));const a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}
export function exportFixtureRoundCsv(
  eventName: string,
  programName: string,
  roundLabel: string,
  matches: MatchEntry[],
) {
  const headers = [
    "Round",
    "Team 1",
    "Team 1 Club / Team",
    "Team 1 Seed",
    "Team 2",
    "Team 2 Club / Team",
    "Team 2 Seed",
    "Result Score",
    "Winner",
    "Walkover",
    "Walkover Winner",
    "Court / Venue",
    "Date",
    "Actual Start Time",
    "Actual End Time",
    "Status",
    "Officials",
    "Remark",
  ];

  const teamName = (team: MatchEntry["team1"]) =>
    getEntryDisplay({
      teamMode: team.teamMode,
      label: team.label,
      participants: team.participants,
      participantClubs: team.participantClubs,
    }, "compact").main || team.label;
  const score = (match: MatchEntry) => match.walkover
    ? "W/O"
    : match.games
      .filter(g => g.p1 !== "" || g.p2 !== "")
      .map(g => `${g.p1}-${g.p2}`)
      .join(", ");
  const winner = (match: MatchEntry) => {
    if (match.winner === "team1") return teamName(match.team1);
    if (match.winner === "team2") return teamName(match.team2);
    if (match.walkoverWinner === "team1") return teamName(match.team1);
    if (match.walkoverWinner === "team2") return teamName(match.team2);
    return "";
  };
  const officials = (match: MatchEntry) =>
    match.officials.map(o => [o.role, o.name].filter(Boolean).join(": ")).filter(Boolean).join("; ");

  const rows = matches.map(match => [
    roundLabel,
    teamName(match.team1),
    match.team1.label,
    match.team1.seed != null ? String(match.team1.seed) : "",
    teamName(match.team2),
    match.team2.label,
    match.team2.seed != null ? String(match.team2.seed) : "",
    score(match),
    winner(match),
    match.walkover ? "Yes" : "No",
    match.walkoverWinner === "team1" ? teamName(match.team1)
      : match.walkoverWinner === "team2" ? teamName(match.team2)
      : "",
    match.courtNo,
    match.matchDate,
    match.startTime,
    match.endTime,
    match.status,
    officials(match),
    match.remark ?? "",
  ]);

  download(
    safeFilename(`${eventName} - ${programName} - ${roundLabel} - Fixture Table.csv`),
    csv([headers, ...rows]),
  );
}

export function exportParticipantsCsv(
  eventName:   string,
  programName: string,
  participants: SeedEntry[],
  isBadminton:  boolean,
) {
  const headers = [
    "No.", "Club / Team / School", "Player(s)", "Participant Club(s)",
    ...(isBadminton ? ["SBA ID"] : []),
    "Seed",
  ];

  const rows = participants.map((p, i) => [
    String(i + 1),
    p.club,
    p.participants.join(" / "),
    p.teamMode ? p.club : (p.participantClubs ?? []).join(" / "),
    ...(isBadminton ? [p.sbaIds?.join(" / ") ?? p.sbaId ?? ""] : []),
    p.seed != null ? String(p.seed) : "",
  ]);

  download(
    safeFilename(`${eventName} - ${programName} - Participants.csv`),
    csv([headers, ...rows]),
  );
}

