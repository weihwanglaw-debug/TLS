import {useEffect,useState} from 'react';
import {ArrowLeftRight,Plus,Trash2} from 'lucide-react';
import {api} from './api';
import ActionMessage from './ActionMessage';
import {ActionFeedbackDialog} from './components/ui/ActionFeedbackDialog';
import FixtureOptions from './FixtureOptions';
import ResetScheduleDialog from './ResetScheduleDialog';
import {EventPanel,Table} from './FoundationUi';
import {Switch} from './components/ui/switch';
import {Dialog,DialogContent,DialogHeader,DialogTitle,DialogDescription,DialogFooter} from './components/ui/dialog';
import {roundKey} from './fixture-progress';
import TimePicker from './TimePicker';
import {getEntryDisplay} from './lib/entryDisplay';
import type {DailySession,Match,View} from './tournament-types';

type Session=DailySession&{courts:number[];matchIds:string[];programLimits?:Record<string,number>;dirty?:boolean};
type Day={date:string;courts:number[];matchIds:string[];sessions:Session[]};
const dayIds=(day:Day)=>day.sessions.length?[...new Set(day.sessions.flatMap(w=>w.matchIds))]:day.matchIds;
type CapacityResult={revision:number;matches:Match[];sessions:{index:number;capacity:number;programs:{programId:string;requested:number;scheduled:number;maximum:number}[]}[]};
type Preview={token:string;revision:number;matches:Match[];unplaced:{id:string;number:number;programId:string;reason:string}[];kept:number;sessions?:{index:number;date:string;start:string;end:string;capacity:number;programs:{programId:string;requested:number;scheduled:number}[]}[]};
export default function EventSchedule({eventId,onChanged,onBack,onDone,programId='',guided=false}:{eventId:string;onChanged:()=>Promise<void>;onBack:()=>void;onDone?:()=>void;programId?:string;guided?:boolean}){
 const [notice,setNotice]=useState(''),[draftMessage,setDraftMessage]=useState('');
 const [assessment,setAssessment]=useState<{key:string;result:CapacityResult}>(),[assessmentError,setAssessmentError]=useState<{key:string;message:string}>();
 const [view,setView]=useState<View>(),[days,setDays]=useState<Day[]>([]),[date,setDate]=useState(''),[resetting,setResetting]=useState(false),[preview,setPreview]=useState<Preview>(),[busy,setBusy]=useState(false),[error,setError]=useState(''),[message,setMessage]=useState(''),[moves,setMoves]=useState<string[]>([]),[moveKey,setMoveKey]=useState(''),[done,setDone]=useState(false),[swapSelection,setSwapSelection]=useState('');
 async function load(){
  const v=await api<View>('tournament/'+eventId);setView(v);
  const first=new Date(v.eventInfo.startDate+'T00:00:00Z'),last=new Date(v.eventInfo.endDate+'T00:00:00Z');const next:Day[]=[];
  for(let t=first.getTime();t<=last.getTime()&&next.length<10000;t+=86400000){
   const d=new Date(t).toISOString().slice(0,10),saved=v.eventPlan.scheduleDays?.find(x=>x.date===d),matches=v.plans.flatMap(p=>p.matches),known=new Set(matches.map(m=>m.id));
   const courts=(saved?.courts||v.eventCourts).filter(c=>v.eventCourts.includes(c)&&!v.closedCourts.includes(c));
   const matchIds=[...new Set([...(saved?.matchIds||[]).filter(id=>known.has(id)),...matches.filter(m=>m.start?.slice(0,10)===d&&m.status!=='BY').map(m=>m.id)])];
   const sessions:Session[]=v.eventPlan.dailySessions.length?v.eventPlan.dailySessions.map(w=>({...w,courts:[...courts],matchIds:[...matchIds]})):v.eventPlan.sessions.filter(w=>w.date===d).map(w=>({start:w.start,end:w.end,courts:(w.courts??courts).filter(c=>v.eventCourts.includes(c)&&!v.closedCourts.includes(c)),programLimits:w.programLimits||undefined,matchIds:(w.matchIds??matchIds).filter(id=>known.has(id)&&(!matches.find(m=>m.id===id)?.start||matches.find(m=>m.id===id)?.start?.slice(0,10)===d)&&(!!matches.find(m=>m.id===id)?.start||w.programLimits?.[matches.find(m=>m.id===id)!.programId]!==0))}));
   next.push({date:d,courts,matchIds,sessions});
  }
  const draft=v.eventPlan.planningDraft;
  if(draft){const known=new Set(v.plans.flatMap(p=>p.matches).map(m=>m.id));for(const d of next){d.sessions=draft.sessions.flatMap((w,index)=>w.date===d.date?[{start:w.start,end:w.end,courts:w.courts,matchIds:w.matchIds.filter(id=>known.has(id)),programLimits:w.programLimits,dirty:draft.dirtySessions.includes(index)||w.matchIds.some(id=>!known.has(id))}]:[]);d.matchIds=dayIds(d);}setDraftMessage('Saved planning draft restored. Preview and confirm to schedule matches.');}
  const focused=v.plans.find(p=>p.programId===programId);const stage=focused?.matches.filter(m=>m.phase===(focused.phase==='G'?'G':'K')&&(focused.phase==='G'||m.round===focused.lastRound)&&m.status!=='BY')||[];
  const allocation=next.find(d=>dayIds(d).some(id=>stage.some(m=>m.id===id)));const focusDate=allocation?.date||next[0]?.date||'';
  if(programId&&!allocation&&!draft){const pending=stage.filter(m=>m.status==='SC'&&!m.start&&!m.court).map(m=>m.id);const target=next.find(d=>d.date===focusDate);if(target){target.matchIds=[...new Set([...target.matchIds,...pending])];let left=pending.length;target.sessions=target.sessions.map(w=>{const limit=left;left=0;return {...w,dirty:limit>0||w.dirty,matchIds:limit?[...new Set([...w.matchIds,...pending])]:w.matchIds,programLimits:{...w.programLimits,[programId]:limit}};});}}
  setDays(next);setMoves(draft?.moveMatchIds||[]);setDate(previous=>next.some(d=>d.date===previous)?previous:focusDate);return v;
 }
 useEffect(()=>{void load().catch(e=>setError(e.message));},[eventId]);
 useEffect(()=>setSwapSelection(''),[eventId,done,preview?.token]);
 async function work(action:()=>Promise<void>){setBusy(true);setError('');setMessage('');try{await action();}catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 useEffect(()=>{
  if(!view||!days.some(d=>d.sessions.length))return;
  const body=request(),key=JSON.stringify(body);if(assessment?.key===key)return;
  if(days.some(d=>d.sessions.some(w=>w.start>=w.end||!w.courts.length))){setAssessmentError({key,message:'Set valid times and select courts to calculate capacity.'});return;}
  let active=true;
  const timer=window.setTimeout(()=>{void api<CapacityResult>('scheduling/capacity','POST',body).then(result=>{
   if(!active)return;
   if(result.sessions.length!==body.sessions.length)throw new Error('Capacity could not be calculated. Refresh and try again.');
   setAssessment({key,result});setAssessmentError(undefined);
   let changed=false,index=0;
   const next=days.map(d=>({...d,sessions:d.sessions.map(w=>{const fit=result.sessions[index++];let altered=false;const limits={...w.programLimits};let ids=[...w.matchIds];
    for(const p of fit.programs){if(p.requested<=p.scheduled)continue;limits[p.programId]=p.scheduled;altered=true;
     if(!p.scheduled)ids=ids.filter(id=>!all.some(m=>m.id===id&&m.programId===p.programId&&m.status==='SC'&&!m.start&&!m.court));
    }
    if(!altered)return w;changed=true;return {...w,programLimits:limits,matchIds:ids,dirty:true};
   })}));
   if(changed)setDays(next);
  }).catch(e=>{if(active)setAssessmentError({key,message:(e as Error).message});});},250);
  return()=>{active=false;window.clearTimeout(timer);};
 },[view,days,moves]);
 if(!view)return <><ActionMessage message={error}/><p>Loading event schedule...</p></>;
 const day=days.find(d=>d.date===date);const all=view.plans.flatMap(p=>p.matches);
 const rounds=view.plans.filter(p=>view.programs.some(program=>program.id===p.programId&&program.status==='A')).flatMap(p=>{const latest=p.matches.filter(m=>m.phase===(p.phase==='G'?'G':'K')&&(p.phase==='G'||m.round===p.lastRound)&&m.status!=='BY');return latest.length?[{key:roundKey(latest[0]),name:view.programs.find(x=>x.id===p.programId)?.name||'',round:p.phase==='G'?(p.format==='RR'?'Round Robin':'Group stage'):latest[0].roundLabel,matches:latest}]:[];});
 function pending(r:typeof rounds[number]){return r.matches.filter(m=>m.status==='SC'&&(!m.start&&!m.court||moves.includes(m.id)));}
 function allocated(r:typeof rounds[number]){return [...new Set([...days.filter(d=>dayIds(d).some(id=>r.matches.some(m=>m.id===id))).map(d=>d.date),...r.matches.filter(m=>m.start&&!moves.includes(m.id)).map(m=>m.start!.slice(0,10))])];}
 function elsewhere(r:typeof rounds[number]){return allocated(r).some(d=>d!==date);}
 function quantities(r:typeof rounds[number],draft=days){
  const ids=pending(r),pid=r.matches[0].programId,result=new Map<Session,number>();let left=ids.length;
  const ordered=draft.flatMap(d=>d.sessions.map((w,index)=>({date:d.date,w,index}))).sort((a,b)=>a.date.localeCompare(b.date)||a.w.start.localeCompare(b.w.start)||a.index-b.index);
  for(const {w} of ordered){const n=ids.length&&ids.every(m=>w.matchIds.includes(m.id))?Math.min(left,w.programLimits?.[pid]??left):0;result.set(w,n);left-=n;}
  return result;
 }
 function quantity(r:typeof rounds[number],w:Session,draft=days){return quantities(r,draft).get(w)??0;}
 function remaining(r:typeof rounds[number]){return Math.max(0,pending(r).length-[...quantities(r).values()].reduce((sum,n)=>sum+n,0));}
 function available(r:typeof rounds[number],w:Session){return Math.min(remaining(r)+quantity(r,w),sessionAssessment(w)?.programs.find(p=>p.programId===r.matches[0].programId)?.maximum??0);}
 function sessionTotal(w:Session){return rounds.reduce((sum,r)=>sum+quantity(r,w),0);}
 const capacityKey=JSON.stringify(request());
 const currentAssessment=assessment?.key===capacityKey?assessment.result:undefined;
 const capacityError=assessmentError?.key===capacityKey?assessmentError.message:'';
 const calculating=!currentAssessment&&!capacityError;
 const overCapacity=!!currentAssessment&&days.some(d=>d.sessions.some(w=>sessionTotal(w)>capacity(w)));
 function sessionAssessment(w:Session){const index=days.flatMap(d=>d.sessions).indexOf(w);return currentAssessment?.sessions[index];}
 function capacity(w:Session){return sessionAssessment(w)?.capacity??0;}
 function limitPrompt(w:Session){setNotice('This session can hold up to '+capacity(w)+' matches.');}
 function sessionIssue(w:Session,index:number,sessions:Session[]){
  if(w.start>=w.end)return 'End time must be later than start time.';
  const other=sessions.findIndex((other,i)=>i!==index&&w.start<other.end&&other.start<w.end);
  return other>=0?'This time overlaps Session '+(other+1)+'. Choose a different time.':'';
 }
 function invalidSessionMessage(){for(const d of days)for(let i=0;i<d.sessions.length;i++){const issue=sessionIssue(d.sessions[i],i,d.sessions);if(issue)return displayDate(d.date)+' · Session '+(i+1)+': '+issue;}return '';}
 function changeSession(index:number,patch:Partial<Session>){if(!day)return;
  change({sessions:day.sessions.map((w,i)=>i===index?{...w,...patch,dirty:true}:w)});
 }
 function addSession(){if(!day)return;
  const lastEnd=day.sessions.map(w=>w.end).sort().at(-1),start=lastEnd?Number(lastEnd.slice(0,2))*60+Number(lastEnd.slice(3,5)):600;
  if(1439-start<view!.eventPlan.matchMinutes){setNotice('No time remains for another session on this date. Choose another playing date.');return;}
  const time=(minutes:number)=>String(Math.floor(minutes/60)).padStart(2,'0')+':'+String(minutes%60).padStart(2,'0')+':00';
  const w:Session={start:time(start),end:time(Math.min(start+120,1439)),courts:[...day.courts],matchIds:[],programLimits:{},dirty:true};
  const inherited=day.sessions.length?[]:day.matchIds;
  for(const r of rounds){const ids=pending(r).map(m=>m.id);if(ids.some(id=>inherited.includes(id))&&remaining(r)){w.matchIds.push(...ids);w.programLimits![r.matches[0].programId]=remaining(r);}}
  change({sessions:[...day.sessions,w]});
 }
 function hasSavedMatches(w:Session){return all.some(m=>m.start&&m.end&&w.matchIds.includes(m.id)&&m.start>=date+'T'+w.start&&m.end<=date+'T'+w.end&&w.courts.includes(m.court||0));}
 async function saveSession(index:number){
  if(!day)return;const w=day.sessions[index];
  const issue=sessionIssue(w,index,day.sessions);if(issue){setNotice(issue);return;}
  if(!w.courts.length){setNotice('Select at least one court for this session.');return;}
  if(!currentAssessment){setNotice(capacityError||'Wait for capacity to finish calculating.');return;}
  if(sessionTotal(w)>capacity(w)){setNotice('Reduce match quantities to fit this session capacity.');return;}
  if(!sessionTotal(w)&&!hasSavedMatches(w)){setNotice('Select at least one program and match quantity for this session.');return;}
  await work(async()=>{const body=request();const sessionIndex=days.filter(d=>d.date<date).reduce((n,d)=>n+d.sessions.length,0)+index;
   const saved=await api<{revision:number}>('scheduling/draft','POST',{revision:body.revision,eventId,sessions:body.sessions,sessionIndex,dirtySessions:days.flatMap(d=>d.sessions).flatMap((w,i)=>w.dirty&&i!==sessionIndex?[i]:[]),moveMatchIds:moves});
   setView({...view!,revision:saved.revision});setPreview(undefined);setDraftMessage('Planning draft saved. Preview and confirm to schedule matches.');
   setDays(days.map(d=>d.date===date?{...d,sessions:d.sessions.map((old,i)=>i===index?{...old,dirty:false}:old)}:d));
  });
 }
 function removeSession(index:number){if(!day)return;
  const sessions=day.sessions.filter((_,i)=>i!==index);
  const excluded=all.some(m=>m.start?.slice(0,10)===date&&m.end&&!sessions.some(w=>w.matchIds.includes(m.id)&&m.start!>=date+'T'+w.start&&m.end!<=date+'T'+w.end&&w.courts.includes(m.court||0)));
  if(excluded){setNotice('This session contains saved matches. Adjust their schedule before removing it.');return;}
  change({sessions,matchIds:[]});
 }
 function unfinished(){if(!days.some(d=>d.sessions.some(w=>w.dirty)))return false;setNotice('Save Session or remove each unfinished session before previewing.');return true;}
 function changeQuantity(index:number,r:typeof rounds[number],w:Session,value:number){const maximum=available(r,w);if(value>maximum)limitPrompt(w);changeSession(index,{programLimits:{...w.programLimits,[r.matches[0].programId]:Math.max(0,Math.min(maximum,value||0))}});}
 function selectRound(index:number,ids:string[],checked:boolean){const w=day?.sessions[index];if(!w)return;
  const limits={...w.programLimits};let selected=[...w.matchIds];
  for(const r of rounds.filter(r=>r.matches.some(m=>ids.includes(m.id)))){
   const current=quantity(r,w),n=checked?(current||Math.min(remaining(r),available(r,w))):0;
   limits[r.matches[0].programId]=n;
   const candidates=pending(r).map(m=>m.id);selected=n?[...new Set([...selected,...candidates])]:selected.filter(id=>!candidates.includes(id));
  }
  changeSession(index,{matchIds:selected,programLimits:limits});
 }
 function request(draft=days,moveIds=moves){return {revision:view!.revision,eventId,matchMinutes:view!.eventPlan.matchMinutes,restMinutes:view!.eventPlan.restMinutes,sessions:draft.flatMap(d=>d.sessions.map(w=>({date:d.date,start:w.start,end:w.end,courts:w.courts,matchIds:w.matchIds,programLimits:Object.fromEntries([...new Set(w.matchIds.filter(id=>all.some(m=>m.id===id&&m.status==='SC'&&(!m.start||moveIds.includes(id)))).map(id=>all.find(m=>m.id===id)!.programId))].map(pid=>{const n=w.matchIds.filter(id=>all.some(m=>m.id===id&&m.programId===pid&&m.status==='SC'&&(!m.start||moveIds.includes(id)))).length;const r=rounds.find(r=>r.matches[0].programId===pid);return [pid,Math.min(n,moveIds.some(id=>all.some(m=>m.id===id&&m.programId===pid))?n:r?quantity(r,w,draft):w.programLimits?.[pid]??n)];}))}))),days:draft.filter(d=>d.sessions.length||dayIds(d).length).map(d=>({date:d.date,courts:[...new Set(d.sessions.length?d.sessions.flatMap(w=>w.courts):d.courts)],matchIds:dayIds(d)})),...(moveIds.length?{moveMatchIds:moveIds}:{})};}
 const moving=rounds.find(r=>r.key===moveKey);
 const displayDate=(value:string)=>value.split('-').reverse().join('/');
 function change(patch:Partial<Day>){setDays(days.map(d=>d.date===date?{...d,...patch}:d));}
 const name=(m:Match)=>view.programs.find(p=>p.id===m.programId)?.name||'';
 const swappable=(preview?preview.matches:all).filter(m=>m.status==='SC'&&m.start&&m.court&&!m.actualStartedAt&&!m.games.length&&!view.calledMatchIds?.includes(m.id));
 const selectedMatch=swappable.find(m=>m.id===swapSelection);
 function entryName(id:string|null){
  if(!id)return 'To be decided';const entry=view!.entries?.find(e=>e.id===id);if(!entry)return 'Unknown entry';
  const participants=entry.participantIds.map(id=>view!.participants?.find(p=>p.id===id)?.name||'').filter(Boolean);
  return getEntryDisplay({label:entry.name,participants},'compact').main||'Unknown entry';
 }
 const opponents=(m:Match)=>entryName(m.entryA)+' vs '+entryName(m.entryB);
 const matchup=(m:Match)=><div className="min-w-48 text-sm"><span className="font-medium">{entryName(m.entryA)}</span><span className="block text-xs opacity-60 my-1">vs</span><span className="font-medium">{entryName(m.entryB)}</span></div>;
 async function swap(a:string,b:string){await work(async()=>{const result=await api<Preview>('scheduling/swap','POST',{revision:preview?.revision??view!.revision,eventId,matchA:a,matchB:b,...(preview?{token:preview.token}:{})});setSwapSelection('');if(preview)setPreview(result);else{await load();await onChanged();}setMessage(preview?'Preview slots exchanged. Confirm Schedule to save.':'Slots exchanged. Every other match is unchanged.');});}
 function pickSwap(m:Match){
  if(busy||!swappable.some(x=>x.id===m.id))return;
  if(!selectedMatch){setSwapSelection(m.id);setError('');setMessage('');return;}
  if(selectedMatch.id===m.id){setSwapSelection('');return;}
  void swap(selectedMatch.id,m.id);
 }
 function swapButton(m:Match){
  const eligible=swappable.some(x=>x.id===m.id),selected=selectedMatch?.id===m.id;
  const reason=!m.start||!m.court?'Not scheduled':view!.calledMatchIds?.includes(m.id)?'Called':m.actualStartedAt||m.status==='IP'||m.games.length?'Started':m.status!=='SC'?'Unavailable':'No other eligible match';
  const label=selected?'Clear swap selection for match #'+m.number:selectedMatch?'Swap match #'+selectedMatch.number+' with match #'+m.number:'Select match #'+m.number+' for swap';
  return eligible?<button type="button" className="btn-outline px-3 py-1.5 text-xs inline-flex items-center gap-2" aria-label={label} aria-pressed={selected} title={opponents(m)} disabled={busy||swappable.length<2} onClick={()=>pickSwap(m)} style={selected?{backgroundColor:'var(--color-primary)',color:'var(--color-hero-text)'}:undefined}><ArrowLeftRight size={14}/>{selected?'Selected':selectedMatch?'Swap':'Select'}</button>:<span className="text-xs opacity-60">{reason}</span>;
 }
 const swapInstructions=<div className="text-sm mb-4" aria-live="polite"><p>{selectedMatch?<>Selected <strong>#{selectedMatch.number} · {opponents(selectedMatch)}</strong>. Select another match to exchange their time and court, or select this match again to clear.</>:'Select two matches below to exchange their time and court.'}</p><p className="text-xs opacity-70 mt-1">{preview?'Changes stay in the preview until Confirm Schedule.':'Swaps are saved immediately after validation.'} Called or started matches cannot swap.</p></div>;
 const swapRowStyle=(m:Match)=>selectedMatch?.id===m.id?{backgroundColor:'color-mix(in srgb, var(--color-primary) 12%, transparent)',boxShadow:'inset 4px 0 var(--color-primary)'}:undefined;
 const remainingGroups=preview?Object.values(preview.unplaced.reduce<Record<string,{label:string;numbers:number[]}>>((groups,m)=>{const match=all.find(x=>x.id===m.id),key=m.programId+':'+match?.phase+':'+match?.round;groups[key]??={label:(view.programs.find(p=>p.id===m.programId)?.name||'')+' · '+(match?.phase==='G'?'Group stage':match?.roundLabel||''),numbers:[]};groups[key].numbers.push(m.number);return groups;},{})):[];
 return <>{!guided&&<button type="button" className="btn-outline action mb-6" disabled={busy} onClick={onBack}>Back to programs</button>}<ActionMessage message={error}/><ActionMessage message={message} variant="success"/><ActionFeedbackDialog open={!!notice} title={notice.includes('capacity')||notice.includes('matches')||notice.includes('quantities')||notice.includes('courts')?'Session capacity':'Session time'} variant="warning" description={notice} onOpenChange={open=>{if(!open)setNotice('');}}/>
 <div className="flex flex-wrap justify-end gap-3 mb-5">{!preview&&!done&&all.some(m=>m.start)&&<button className="btn-outline action" disabled={busy} onClick={()=>setDone(true)}>View Saved Timetable</button>}<FixtureOptions items={[{label:'Reset Schedule',disabled:busy,onClick:()=>setResetting(true)}]}/></div>
 {resetting&&<ResetScheduleDialog eventId={eventId} onClose={()=>setResetting(false)} onDone={async()=>{setResetting(false);setPreview(undefined);setDone(false);try{await load();await onChanged();setMessage('Schedule reset. Draws and seeds are unchanged.');}catch{setError('Schedule reset, but the screen could not refresh. Refresh before continuing.');}}}/>}
 {done?<><EventPanel title="Saved Timetable">{swapInstructions}<Table><thead><tr><th>Program / Stage</th><th>Match</th><th>Opponents</th><th>Date / Time</th><th>Court</th><th>Swap Slots</th></tr></thead><tbody>{all.filter(m=>m.status!=='BY').map(m=><tr key={m.id} style={swapRowStyle(m)}><td>{name(m)} · {m.roundLabel}</td><td>#{m.number}</td><td>{matchup(m)}</td><td>{m.start?displayDate(m.start.slice(0,10))+' · '+m.start.slice(11,16)+'–'+m.end?.slice(11,16):'Pending scheduling'}</td><td>{m.court?'Court '+m.court:'Unassigned'}</td><td>{swapButton(m)}</td></tr>)}</tbody></Table></EventPanel><div className="fixture-actions"><button type="button" className="btn-outline action" onClick={()=>setDone(false)}>Back to Planning</button><button type="button" className="btn-primary action" disabled={guided&&!all.some(m=>m.status!=='BY'&&m.start&&m.court)} onClick={onDone||onBack}>{guided?'View Fixture & Results':'Program Fixture List'}</button></div></>:preview?<><EventPanel title="Review Schedule"><p className="text-sm mb-4">{preview.matches.length} matches slotted · {preview.kept} existing matches kept · {preview.unplaced.length} remaining unscheduled</p>{swapInstructions}<Table><thead><tr><th>Match</th><th>Opponents</th><th>Program / Round</th><th>Court</th><th>Start</th><th>End</th><th>Swap Slots</th></tr></thead><tbody>{preview.matches.map(m=><tr key={m.id} style={swapRowStyle(m)}><td>#{m.number}</td><td>{matchup(m)}</td><td>{name(m)}<p className="text-xs opacity-70">{m.roundLabel}</p></td><td>{m.court}</td><td>{m.start?.replace('T',' ')}</td><td>{m.end?.slice(11)}</td><td>{swapButton(m)}</td></tr>)}{!preview.matches.length&&<tr><td colSpan={7}>No new match slots. You can still confirm the planning settings.</td></tr>}</tbody></Table>{preview.sessions&&<div className="my-5 space-y-2">{preview.sessions.flatMap(w=>w.programs.map(p=><p className="text-sm" key={w.index+':'+p.programId}>{displayDate(w.date)} {w.start.slice(0,5)}–{w.end.slice(0,5)} · {view.programs.find(x=>x.id===p.programId)?.name}: {p.scheduled>p.requested?<strong>{p.scheduled} scheduled (manual swap; automatic request {p.requested})</strong>:<><strong>{p.scheduled}/{p.requested}</strong> requested matches scheduled</>}{p.scheduled<p.requested?' · '+(p.requested-p.scheduled)+' could not fit':''}</p>))}</div>}{remainingGroups.length>0&&<div className="mt-5"><h3 className="font-semibold text-sm mb-3">Remaining Unscheduled</h3>{remainingGroups.map(g=><details key={g.label} className="text-sm mb-3"><summary className="cursor-pointer">{g.label} — {g.numbers.length} matches awaiting scheduling</summary><p className="text-xs mt-2">Matches: {g.numbers.map(n=>'#'+n).join(', ')}. Add another session/date or review quantities and player rest.</p></details>)}</div>}<p className="text-xs mt-4 opacity-70">Existing assignments stay unchanged. Assign officials when calling matches, or adjust individual unplayed matches in Table View.</p></EventPanel><div className="flex justify-between gap-3"><button className="btn-outline action" disabled={busy} onClick={()=>setPreview(undefined)}>Back to Planning</button><button className="btn-primary action" disabled={busy} onClick={()=>void work(async()=>{await api('scheduling/confirm','POST',{revision:preview.revision,token:preview.token});setPreview(undefined);await load();await onChanged();setDone(true);setMessage('Schedule saved. Matches without a slot remain unscheduled.');})}>Confirm Schedule</button></div></>:<fieldset disabled={busy} className="schedule-fields"><div className="fixture-playing-date"><label className="form-label">Playing Date<select aria-label="Schedule date" className="field-input" value={date} onChange={e=>setDate(e.target.value)}>{days.map(d=><option key={d.date} value={d.date}>{displayDate(d.date)}</option>)}</select></label></div>
 <EventPanel title="Daily Sessions and Courts">{draftMessage&&<p role="status" className="text-sm mb-4">{draftMessage}</p>}{day&&<div className="space-y-5">{day.sessions.map((w,i)=>{const eligible=rounds.filter(r=>quantity(r,w)>0||available(r,w)>0);return <section key={i} aria-label={'Session '+(i+1)} className="border p-4 space-y-4" style={{borderColor:'var(--color-table-border)'}}><div className="flex flex-wrap items-end justify-between gap-4"><div className="flex flex-wrap items-end gap-5"><h3 className="font-semibold text-sm self-center">Session {i+1}</h3>{(['start','end'] as const).map(key=><div key={key} style={{width:180}}><p className="form-label mb-1">{key==='start'?'Start':'End'}</p><TimePicker label={'Session '+(i+1)+' '+key+' time'} value={w[key].slice(0,5)} onChange={value=>changeSession(i,{[key]:value+':00'})}/></div>)}</div><div className="flex gap-2"><button type="button" className="btn-primary action" disabled={busy||!w.dirty||calculating} onClick={()=>void saveSession(i)}>Save Session</button><button type="button" className="btn-outline action" aria-label={'Remove session '+(i+1)} onClick={()=>removeSession(i)}><Trash2 size={14}/></button></div></div>
 <div className="flex flex-wrap gap-5">{view.eventCourts.map(c=><label key={c} className="flex gap-2 items-center text-sm"><Switch aria-label={'Session '+(i+1)+' Court '+c} disabled={view.closedCourts.includes(c)} checked={w.courts.includes(c)} onCheckedChange={checked=>changeSession(i,{courts:checked?[...w.courts,c]:w.courts.filter(x=>x!==c)})}/>Court {c}</label>)}</div>{!view.eventCourts.length&&<p className="text-sm">Configure courts in Master Setting first.</p>}
 <p className="text-sm">Match capacity for this session: <strong>{calculating?'Calculating capacity…':capacityError?'Cannot calculate yet':capacity(w)+' matches'}</strong>{capacityError&&<span className="block text-xs mt-1">{capacityError}</span>}</p>
 <div className="flex flex-wrap justify-between items-center gap-3 border-t pt-4" style={{borderColor:'var(--color-table-border)'}}><h4 className="font-semibold text-sm">Programs and Stages</h4>{rounds.length>0&&<label className="flex gap-3 items-center text-sm font-semibold"><Switch aria-label={'Session '+(i+1)+' Select All Programs'} disabled={!currentAssessment||!eligible.length} checked={!!eligible.length&&eligible.every(r=>pending(r).every(m=>w.matchIds.includes(m.id)))} onCheckedChange={checked=>selectRound(i,eligible.flatMap(r=>pending(r).map(m=>m.id)),checked)}/>Select All</label>}</div>
 <div className="space-y-4">{rounds.map(r=>{const unassigned=pending(r),left=remaining(r),checked=unassigned.length?unassigned.every(m=>w.matchIds.includes(m.id)):r.matches.some(m=>w.matchIds.includes(m.id));const other=elsewhere(r),canMove=other&&r.matches.every(m=>m.status==='SC'&&!m.actualStartedAt&&!view.calledMatchIds?.includes(m.id));return <div key={r.key} className="flex flex-wrap justify-between items-center gap-3"><label className="flex gap-3 items-center text-sm"><Switch aria-label={r.name+' · '+r.round} checked={checked} disabled={!currentAssessment||!checked&&(!left||!available(r,w))} onCheckedChange={value=>selectRound(i,unassigned.map(m=>m.id),value)}/><span>{r.name} · {r.round}<small className="block opacity-70">{unassigned.length?left+' matches not allocated to sessions':r.matches.every(m=>['C','WO'].includes(m.status))?'Completed':'All matches scheduled'}</small></span></label><div className="flex flex-wrap items-center gap-3">{checked&&unassigned.length>0&&<label className="form-label">Matches to schedule<input aria-label={'Session '+(i+1)+' '+r.name+' matches'} type="number" disabled={!currentAssessment} min={0} max={available(r,w)} className="field-input w-24 ml-3" value={quantity(r,w)} onChange={e=>changeQuantity(i,r,w,Number(e.target.value))} onKeyDown={e=>{if(e.key==='ArrowUp'&&quantity(r,w)>=available(r,w)){e.preventDefault();limitPrompt(w);}}}/></label>}{canMove&&<button type="button" className="btn-outline action" onClick={()=>{if(!unfinished())setMoveKey(r.key);}}>Move Entire Round</button>}</div></div>;})}{!rounds.length&&<p className="text-sm">Generate a program draw first. Later stages appear after their draw is generated.</p>}</div>
 </section>;})}<button type="button" className="btn-outline action" onClick={addSession}><Plus size={14}/>Add Session</button></div>}</EventPanel>
 <EventPanel title="Plan Overview"><Table><thead><tr><th>Date</th><th>Sessions</th><th>Courts</th><th>Requested Matches (maximum)</th></tr></thead><tbody>{days.map(d=><tr key={d.date}><td><button type="button" className="hover:underline" style={{color:'var(--color-primary)'}} onClick={()=>setDate(d.date)}>{d.date}</button></td><td>{d.sessions.map(w=>w.start.slice(0,5)+'–'+w.end.slice(0,5)).join(', ')||'Not set'}</td><td>{[...new Set(d.sessions.flatMap(w=>w.courts))].join(', ')||'Not set'}</td><td>{d.sessions.reduce((sum,w)=>sum+rounds.reduce((n,r)=>n+quantity(r,w),0),0)}</td></tr>)}</tbody></Table></EventPanel>
 <div className="fixture-actions"><button type="button" className="btn-outline action" disabled={busy} onClick={onBack}>{guided?'Back to Draw':'Back'}</button><button type="button" className="btn-primary action" disabled={busy||calculating} onClick={()=>{const issue=invalidSessionMessage();if(issue){setNotice(issue);return;}if(!currentAssessment){setNotice(capacityError||'Wait for capacity to finish calculating.');return;}if(overCapacity){setNotice('Reduce match quantities to fit session capacity before previewing.');return;}if(unfinished())return;void work(async()=>setPreview(await api<Preview>('scheduling/preview','POST',request())));}}>Preview Schedule</button></div>
 </fieldset>}
 <Dialog open={!!moving} onOpenChange={open=>{if(!open&&!busy)setMoveKey('');}}><DialogContent className="max-w-xl sm:rounded-none" style={{backgroundColor:'var(--color-page-bg)'}}><DialogHeader><DialogTitle>Move Round</DialogTitle><DialogDescription>{moving?.name} · {moving?.round}</DialogDescription></DialogHeader><p className="text-sm">Move the entire unplayed stage from {moving?allocated(moving).map(displayDate).join(', '):''} to {displayDate(date)}. Times and courts will be recalculated. Nothing is saved until you confirm the schedule preview.</p><DialogFooter><button type="button" className="btn-outline action" disabled={busy} onClick={()=>setMoveKey('')}>Cancel</button><button type="button" className="btn-primary action" disabled={busy} onClick={()=>void work(async()=>{const ids=moving!.matches.map(m=>m.id),next=days.map(d=>({...d,matchIds:d.date===date?[...new Set([...d.matchIds,...ids])]:d.matchIds.filter(id=>!ids.includes(id)),sessions:d.sessions.map(w=>({...w,matchIds:d.date===date?[...new Set([...w.matchIds,...ids])]:w.matchIds.filter(id=>!ids.includes(id))}))})),moveIds=[...new Set([...moves,...ids])];const p=await api<Preview>('scheduling/preview','POST',request(next,moveIds));setDays(next);setMoves(moveIds);setPreview(p);setMoveKey('');})}>Preview Move</button></DialogFooter></DialogContent></Dialog>
 </>;
}
