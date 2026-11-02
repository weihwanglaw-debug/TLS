import type {Match,Plan} from './tournament-types';
export type FixtureStep='seeding'|'draw'|'schedule'|'results';
type StagePlan=Partial<Omit<Plan,'matches'>> & {matches:Partial<Match>[]};
const finished=(m:Partial<Match>)=>['C','WO','BY'].includes(m.status||'');
function knockoutLabel(entries:number){let size=2;while(size<entries)size*=2;return size===2?'Final':size===4?'Semifinals':size===8?'Quarterfinals':`Round of ${size}`;}
export function fixtureProgress(plan?:StagePlan){
 const matches=plan?.matches||[];const knockout=matches.filter(m=>m.phase==='K');const lastRound=plan?.lastRound||Math.max(0,...knockout.map(m=>m.round||1));const group=plan?.phase==='G'||!knockout.length&&matches.some(m=>m.phase==='G');
 const current=group?matches.filter(m=>m.phase==='G'):knockout.filter(m=>(m.round||1)===lastRound);
 const allDone=current.length>0&&current.every(finished);const completed=allDone&&(plan?.format==='RR'||!group&&current.length===1);
 const nextDraw=allDone&&!completed;const pendingDraw=!matches.length||nextDraw;
 const stage=!matches.length?'Opening round':nextDraw?knockoutLabel(group?(plan?.groupCount||1)*(plan?.advancePerGroup||2):current.length):group?(plan?.format==='RR'?'Round Robin':'Group stage'):current[0]?.roundLabel||'Opening round';
 const playable=current.filter(m=>m.status!=='BY');const assigned=playable.filter(m=>m.start&&m.court).length;
 const schedule=completed?'Completed':pendingDraw?'Pending':assigned===playable.length&&playable.length?'Scheduled':assigned?'Partly scheduled':'Pending';
 const resultsAvailable=matches.some(m=>m.status!=='BY'&&m.start&&m.court);
 const scheduled=matches.filter(m=>m.status!=='BY'&&m.start&&m.court).length;const unscheduled=matches.filter(m=>m.status!=='BY'&&(!m.start||!m.court)).length;
 const nextStep:FixtureStep=!plan?.seedsReviewed?'seeding':pendingDraw?'draw':schedule!=='Scheduled'&&!completed?'schedule':'results';
 return {resultsAvailable,scheduled,unscheduled,stage,completed,nextDraw,pendingDraw,nextStep,seeding:plan?.seedsReviewed?'Completed':'Pending',draw:completed?'Completed':pendingDraw?'Pending draw':'Draw generated',schedule};
}
export function roundKey(m:Pick<Match,'programId'|'phase'|'round'>){return `${m.programId}:${m.phase}:${m.round}`;}
