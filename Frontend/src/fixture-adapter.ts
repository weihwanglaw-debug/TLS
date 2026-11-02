import type {BracketState,MatchEntry,SeedEntry,TeamEntry} from './types/config';
import type {View,Match,Plan} from './TournamentConsole';

export const formatName:Record<string,string>={GK:'group_knockout',KO:'knockout',RR:'round_robin'};
export function seedEntries(view:View,plan:Plan):SeedEntry[]{
 return view.entries.filter(e=>e.programId===plan.programId&&e.status==='A').map(e=>{
  const people=e.participantIds.map(id=>view.participants.find(p=>p.id===id)!);
  return {id:e.id,club:e.name,participants:people.map(p=>p.name),participantClubs:people.map(p=>p.club||''),seed:plan.seeds[e.id]??null,sbaIds:people.map(p=>p.sbaId||'').filter(Boolean)};
 });
}
export function asMatch(view:View,m:Match):MatchEntry{
 const plan=view.plans.find(p=>p.programId===m.programId)!;
 const team=(id:string|null):TeamEntry=>{if(!id)return {id:'bye-'+m.id,label:'BYE',participants:[]};if(id===m.entryA&&m.pendingA||id===m.entryB&&m.pendingB)return {id,label:'Awaiting earlier result',participants:['Awaiting earlier result']};const e=view.entries.find(e=>e.id===id)!;return {id,label:e.name,participants:e.participantIds.map((personId,i)=>(i===0&&m.phase==='K'&&m.round===1&&plan.qualificationOrigins?.[id]?plan.qualificationOrigins[id]+' · ':'')+(view.participants.find(p=>p.id===personId)?.name||personId)),participantClubs:e.participantIds.map(id=>view.participants.find(p=>p.id===id)?.club||''),seed:plan.seeds[id]??undefined};};
 const locked=m.phase==='G'?plan.matches.some(x=>x.phase==='K'):plan.matches.some(x=>x.phase==='K'&&x.round>m.round);
 const officials=[{id:m.umpireId,role:'Umpire'},{id:m.serviceJudgeId,role:'Service Judge'},...m.lineJudgeIds.map(id=>({id,role:'Line Judge'}))].filter(o=>o.id).map(o=>({id:o.id!,role:o.role,name:view.eventPlan.officials.find(x=>x.id===o.id)?.name||o.id!}));
 return {id:m.id,resultKind:m.resultKind,phase:m.phase==='G'?'group':'knockout',round:m.round,roundLabel:m.roundLabel,groupId:m.groupId??undefined,team1:team(m.entryA),team2:team(m.entryB),games:m.games.map(g=>({p1:String(g.a),p2:String(g.b)})),winner:m.winnerId===m.entryA?'team1':m.winnerId===m.entryB&&m.entryB?'team2':null,walkover:m.status==='WO',walkoverWinner:m.status==='WO'?(m.winnerId===m.entryA?'team1':'team2'):'',remark:m.resultDetails?.remark||'',matchDate:m.start?.slice(0,10)||'',startTime:m.start?.slice(11,16)||'',endTime:m.end?.slice(11,16)||'',courtNo:m.court?String(m.court):'',officials,status:m.status as MatchEntry['status'],expanded:false,resultDisabled:!m.start||!m.court||m.status==='BY'||m.pendingA||m.pendingB,shuttlesUsed:m.resultDetails?.shuttlesUsed,cardCount:m.resultDetails?.cards.length,challengeCount:m.resultDetails?.challenges.length,duration:m.actualDurationMinutes};
}
export function asBracket(view:View,plan:Plan,matches=plan.matches):BracketState{
 const seeds=seedEntries(view,plan);
 const teams=seeds.map(e=>({id:e.id,label:e.club,participants:e.participants,participantClubs:e.participantClubs,seed:e.seed??undefined}));
 return {format:(formatName[plan.format||'GK']||'group_knockout') as BracketState['format'],config:{format:(formatName[plan.format||'GK']||'group_knockout') as BracketState['format'],numSeeds:seeds.filter(s=>s.seed!==null).length,numGroups:plan.groupCount,advancePerGroup:plan.advancePerGroup},locked:matches.some(m=>['C','WO','RC','IP'].includes(m.status)),phase:plan.phase==='G'?'group':'knockout',seeds,groups:plan.groups.filter(g=>matches.some(m=>m.groupId===g.id)).map(g=>({id:g.id,name:g.name,pendingRanking:view.standings.find(s=>s.groupId===g.id)?.pending?.flat(),officialStandings:view.standings.find(s=>s.groupId===g.id)?.rows.map((r,i)=>({...r,team:teams.find(t=>t.id===r.entryId)!,rank:i+1,losses:r.played-r.wins,draws:0})),teams:teams.filter(t=>g.entryIds.includes(t.id)),matches:matches.filter(m=>m.groupId===g.id).map(m=>asMatch(view,m))})),matches:matches.filter(m=>m.phase==='K').map(m=>asMatch(view,m))};
}
