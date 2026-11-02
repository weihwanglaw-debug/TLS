//  Fixture types

export type FixtureFormat =
  | "knockout"       // single elimination bracket
  | "group_knockout" // round-robin groups -> top N advance to KO
  | "round_robin"    // everyone vs everyone, standings only
  | "heats";         // individual/non-vs: rounds of individual results -> advancement -> final

export type FixtureMode = "internal" | "external" | "not_required";

export type TiebreakCriteria =
  | "head_to_head"
  | "game_ratio"
  | "point_ratio"
  | "goal_difference"
  | "goals_scored";

//  Round Robin standing points

export interface StandingPoints {
  win:  number;   // default 2
  draw: number;   // default 1
  loss: number;   // default 0
}

//  Heats format config

export interface HeatsConfig {
  numRounds:      number;   // total rounds including final (min 2)
  advancePerRound: number;  // how many advance from each round (except final)
  resultLabel:    string;   // display label for result column e.g. "Time", "Score", "Distance"
  placesAwarded:  number;   // how many places to assign in final (1=winner only, 3=podium)
}

// Wizard-produced config - stored inside BracketState
export interface WizardConfig {
  format:          FixtureFormat;
  numSeeds:        number;
  // group_knockout
  numGroups?:      number;
  advancePerGroup?: number;
  // round_robin standing points
  standingPoints?: StandingPoints;
  // heats
  heatsConfig?:    HeatsConfig;
}

export type FixtureFormatConfig = WizardConfig;

export interface SbaRanking {
  id: number;
  rankingType: string;
  player1: { sbaId: string; name: string; club: string; dob: string };
  player2: { sbaId: string; name: string; club: string; dob: string } | null;
  accumulatedScore: number;
  ranking: number;
  tournaments: number;
  yearOfBirth?: number | null;
  updatedAt?: string;
}

//  Match / bracket types

export interface GameScore {
  p1: string;
  p2: string;
}

export interface Official {
  id: string;
  role: string;
  name: string;
}

export interface TeamEntry {
  id: string;
  label: string;
  participants: string[];
  participantClubs?: string[];
  teamMode?: boolean;
  seed?: number;
}

export type MatchStatus = "SC" | "IP" | "RC" | "C" | "WO" | "BY";
export type MatchPhase  = "group" | "knockout";

export interface MatchEntry {
  resultKind?: string;
  id: string;
  phase: MatchPhase;
  round: number;
  roundLabel: string;
  groupId?: string;
  team1: TeamEntry;
  team2: TeamEntry;
  games: GameScore[];
  winner: "team1" | "team2" | null;
  walkover: boolean;
  walkoverWinner: "team1" | "team2" | "";
  remark?: string;
  matchDate: string;
  startTime: string;
  endTime: string;
  courtNo: string;
  officials: Official[];
  status: MatchStatus;
  expanded: boolean;
  resultDisabled?: boolean;
  duration?: number | null;
  shuttlesUsed?: number | null;
  cardCount?: number;
  challengeCount?: number;
}

export interface SeedEntry {
  id: string;
  club: string;
  participants: string[];
  participantClubs?: string[];
  teamMode?: boolean;
  seed: number | null;
  sbaId?: string;
  sbaIds?: string[];
  registrationId?: string;
  groupId?: string;
}

export interface GroupStanding {
  team: TeamEntry;
  played: number;
  wins: number;
  losses: number;
  draws: number;
  gamesFor: number;
  gamesAgainst: number;
  pointsFor: number;
  pointsAgainst: number;
  points: number;
  rank: number;
}

export interface GroupEntry {
  officialStandings?: GroupStanding[];
  pendingRanking?: string[];
  id: string;
  name: string;
  teams: TeamEntry[];
  matches: MatchEntry[];
}

//  Heats bracket types

export interface HeatParticipantResult {
  teamId:   string;
  result:   string;   // free text: "12.4s", "847", "222,223,225"
  advanced: boolean;  // did they advance to next round?
  place?:   number;   // final round: 1st, 2nd, 3rd...
}

export interface HeatRound {
  id:           string;
  roundNumber:  number;
  label:        string;      // "Heat 1", "Semi-Final", "Final"
  isFinal:      boolean;
  results:      HeatParticipantResult[];
  isComplete:   boolean;
}

//  BracketState

export interface BracketState {
  format: FixtureFormat;
  config: FixtureFormatConfig;
  locked: boolean;
  phase:  "group" | "knockout";
  groups: GroupEntry[];
  matches: MatchEntry[];
  seeds:  SeedEntry[];
  // heats format only
  heatRounds?: HeatRound[];
}


export interface SbaRankingType { value:string;label:string;players:number;gender:string;minAge:number;maxAge:number; }
