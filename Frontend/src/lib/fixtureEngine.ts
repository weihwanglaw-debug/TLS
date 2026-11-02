import type {GroupEntry,GroupStanding,TiebreakCriteria,BracketState} from "@/types/config";
const pts=(v:string)=>Number(v)||0;
export function computeGroupStandings(
  group: GroupEntry,
  tiebreakOrder: TiebreakCriteria[] = ["head_to_head", "game_ratio", "point_ratio"],
  pointsConfig = { win: 2, draw: 1, loss: 0 }
): GroupStanding[] {
  void tiebreakOrder;
  const map: Record<string, GroupStanding> = {};
  for (const team of group.teams) {
    map[team.id] = {
      team, played: 0, wins: 0, losses: 0, draws: 0,
      gamesFor: 0, gamesAgainst: 0, pointsFor: 0, pointsAgainst: 0,
      points: 0, rank: 0,
    };
  }

  for (const match of group.matches) {
    if (match.status !== "C" && match.status !== "WO") continue;
    const s1 = map[match.team1.id];
    const s2 = map[match.team2.id];
    if (!s1 || !s2) continue;
    s1.played++; s2.played++;
    for (const g of match.games) {
      if (g.p1 === "" || g.p2 === "") continue;
      const a = pts(g.p1), b = pts(g.p2);
      s1.pointsFor += a; s1.pointsAgainst += b;
      s2.pointsFor += b; s2.pointsAgainst += a;
      if (a > b) { s1.gamesFor++; s2.gamesAgainst++; }
      else if (b > a) { s2.gamesFor++; s1.gamesAgainst++; }
    }
    if (match.winner === "team1")      { s1.wins++; s1.points += pointsConfig.win;  s2.losses++; s2.points += pointsConfig.loss; }
    else if (match.winner === "team2") { s2.wins++; s2.points += pointsConfig.win;  s1.losses++; s1.points += pointsConfig.loss; }
    else { s1.draws++; s1.points += pointsConfig.draw; s2.draws++; s2.points += pointsConfig.draw; }
  }

  const standings = Object.values(map);
  const h2h: Record<string, Record<string, "A" | "B" | "draw">> = {};
  for (const m of group.matches) {
    if (m.status !== "C" && m.status !== "WO") continue;
    const a = m.team1.id, b = m.team2.id;
    if (!h2h[a]) h2h[a] = {}; if (!h2h[b]) h2h[b] = {};
    if (m.winner === "team1")      { h2h[a][b] = "A"; h2h[b][a] = "B"; }
    else if (m.winner === "team2") { h2h[a][b] = "B"; h2h[b][a] = "A"; }
    else { h2h[a][b] = "draw"; h2h[b][a] = "draw"; }
  }

  const compareMetrics = (a: GroupStanding, b: GroupStanding) => {
    const gameDiff = (b.gamesFor - b.gamesAgainst) - (a.gamesFor - a.gamesAgainst);
    if (gameDiff !== 0) return gameDiff;
    const pointDiff = (b.pointsFor - b.pointsAgainst) - (a.pointsFor - a.pointsAgainst);
    if (pointDiff !== 0) return pointDiff;
    const pointsFor = b.pointsFor - a.pointsFor;
    if (pointsFor !== 0) return pointsFor;
    return (a.team.seed ?? Number.MAX_SAFE_INTEGER) - (b.team.seed ?? Number.MAX_SAFE_INTEGER)
      || a.team.id.localeCompare(b.team.id);
  };

  const primary = standings.sort((a, b) =>
    (b.points - a.points) || (b.wins - a.wins)
  );

  const ordered: GroupStanding[] = [];
  for (let i = 0; i < primary.length;) {
    const tied = [primary[i]];
    let j = i + 1;
    while (j < primary.length && primary[j].points === primary[i].points && primary[j].wins === primary[i].wins) {
      tied.push(primary[j]);
      j++;
    }

    if (tied.length === 2) {
      const [a, b] = tied;
      const r = h2h[a.team.id]?.[b.team.id];
      if (r === "A") ordered.push(a, b);
      else if (r === "B") ordered.push(b, a);
      else ordered.push(...tied.sort(compareMetrics));
    } else {
      ordered.push(...tied.sort(compareMetrics));
    }
    i = j;
  }

  ordered.forEach((s, i) => { s.rank = i + 1; });
  return ordered;
}


export function getAllMatches(state:BracketState){return [...state.groups.flatMap(g=>g.matches),...state.matches];}
