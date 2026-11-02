import OfficialsEditor,{officialRows,officialAssignment,officialsError,type OfficialRow} from './OfficialsEditor';
import RankingDecision from './RankingDecision';
import ApplyDrawSettingsDialog from './ApplyDrawSettingsDialog';
import FixtureOptions from './FixtureOptions';
import {fixtureProgress} from './fixture-progress';
import DateTimePicker from './DateTimePicker';
import ActionMessage from './ActionMessage';
import {Switch} from './components/ui/switch';
import {useEffect,useState,useMemo} from 'react';
import {Download} from 'lucide-react';
import {api} from './api';
import {scopeTournament} from './tournament-scope';
import {FixtureConfiguration} from './components/admin/fixtures/FixtureConfiguration';
import {FixturePreview} from './components/admin/fixtures/FixturePreview';
import {Dialog,DialogContent,DialogHeader,DialogTitle,DialogFooter} from './components/ui/dialog';
import FixtureViews from './FixtureViews';
import MatchResultEditor from './MatchResultEditor';
import {asBracket,seedEntries,formatName} from './fixture-adapter';
import {exportParticipantsCsv} from './lib/exportCsv';
import type {View,Match,Preview} from './tournament-types';
import type {WizardConfig,SeedEntry} from './types/config';
export type {View,Match,Plan} from './tournament-types';

type Action={enterResult?:boolean;kind:'schedule'|'preset'|'reset'|'reset-round'|'clear';match?:Match};
export default function TournamentConsole({eventId,programId,onChanged,onEventSettings,onPresets,onSeeding,onSchedule,onDraw,guided=false,resultsOnly=false}:{eventId:string;programId?:string;onChanged:()=>Promise<void>;onEventSettings:()=>void;onPresets:()=>void;onSeeding?:()=>void;onSchedule?:()=>void;guided?:boolean;resultsOnly?:boolean;onDraw?:()=>void}){
 const [view,setView]=useState<View>(),[screen,setScreen]=useState<'overview'|'configure'|'preview'|'next'>('overview'),[busy,setBusy]=useState(false),[error,setError]=useState('');
 const [config,setConfig]=useState<WizardConfig>(),[seeds,setSeeds]=useState<SeedEntry[]>(),[preview,setPreview]=useState<Preview>();
 const [earliest,setEarliest]=useState(''),[applying,setApplying]=useState(false);
 const [result,setResult]=useState<Match>(),[action,setAction]=useState<Action>(),[presetId,setPreset]=useState(''),[reason,setReason]=useState(''),[message,setMessage]=useState('');
 const [scheduleOfficials,setScheduleOfficials]=useState<OfficialRow[]>(officialRows({}));
 const [schedule,setSchedule]=useState({court:1,start:'',umpireId:'',serviceJudgeId:'',lineJudgeIds:[] as string[],reviewAvailable:false});
 async function reload(){const v=scopeTournament(await api<View>('tournament/'+eventId),programId);setView(v);return v;}
 useEffect(()=>{void reload().then(v=>{setEarliest(v.eventInfo.startDate+'T'+(v.dailySessions[0]?.start||v.eventPlan.sessions[0]?.start||'10:00').slice(0,5));}).catch(e=>setError(e.message));},[eventId,programId]);
 useEffect(()=>{if(screen!=='overview'||result||action||applying||busy)return;let active=true;const timer=setInterval(()=>api<View>('tournament/'+eventId).then(v=>{if(active)setView(scopeTournament(v,programId));}).catch(()=>{}),3000);return()=>{active=false;clearInterval(timer);};},[eventId,programId,screen,result,action,applying,busy]);
 async function work(f:()=>Promise<void>){setBusy(true);setError('');setMessage('');try{await f();}catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 async function mutate(path:string,body:unknown,method='PUT'){setView(scopeTournament(await api<View>('tournament/'+path,method,body),programId));await onChanged();setMessage('Changes saved.');}
 const participants=useMemo(()=>view&&view.plans[0]?seedEntries(view,view.plans[0]):[],[view]);
 if(!view)return <><ActionMessage message={error}/><p>Loading fixture management...</p></>;
 const plan=view.plans[0],program=view.programs[0];if(!plan||!program)return <p>Program unavailable. Return to All Programs.</p>;
 const matches=plan.matches;
 const waiting=view.progress.find(p=>p.programId===plan.programId)?.waiting;
 const current=matches.filter(m=>plan.phase==='G'?m.phase==='G':m.phase==='K'&&m.round===plan.lastRound);
 const ready=plan.phase==='G'&&plan.format!=='RR'&&current.every(m=>['C','WO'].includes(m.status))||plan.phase==='K'&&current.length>1&&current.every(m=>['C','WO','BY'].includes(m.status));
 
 const hasDefault=view.presets.some(p=>p.isDefault);
 const nextLabel=plan.phase==='G'?'Knockout opening round':'Next knockout round';
 const label=(id:string|null)=>id?view.entries.find(e=>e.id===id)?.participantIds.map(id=>view.participants.find(p=>p.id===id)?.name).join(' / ')||id:'BYE';
 function openResult(m:Match){setError('');if(!m.rulesAssigned){setPreset(view!.presets.find(p=>p.isDefault)?.id||'');setAction({kind:'preset',match:m,enterResult:true});}else setResult(m);}
 function openSchedule(m:Match){setError('');setScheduleOfficials(officialRows(m));setAction({kind:'schedule',match:m});setSchedule({court:m.court||view!.eventCourts[0],start:m.start?.slice(0,16)||earliest,umpireId:m.umpireId||'',serviceJudgeId:m.serviceJudgeId||'',lineJudgeIds:m.lineJudgeIds,reviewAvailable:m.reviewAvailable});}
 async function makePreview(c?:WizardConfig){
  if(c){setConfig(c);setSeeds(participants);}
  const p=await api<Preview>('tournament/preview','POST',{revision:view!.revision,eventId,programIds:[plan.programId],courts:[],drawOnly:true,...(c?{settings:{format:c.format==='knockout'?'KO':c.format==='round_robin'?'RR':'GK',groupCount:c.numGroups||1,advancePerGroup:c.advancePerGroup||1}}:{})});
  setPreview(p);setScreen('preview');
 }
 return <>{!guided&&<div className="p-5 mb-5" style={{border:'1px solid var(--color-table-border)',backgroundColor:'var(--color-row-hover)'}}><div className="flex flex-wrap items-start justify-between gap-4"><div><p className="text-xs opacity-40 mb-0.5">{view.eventInfo.name}</p><h2 className="font-bold text-base mb-1.5">{program.name}</h2><div className="flex gap-2 text-xs"><span className="opacity-50">{program.sport} / {view.eventInfo.startDate} to {view.eventInfo.endDate} / {participants.length} entries</span></div>{matches.length>0&&<p className="mt-3 text-xs font-semibold uppercase">Fixture type: {({KO:'Knockout',GK:'Group + Knockout',RR:'Round Robin'} as Record<string,string>)[plan.format]}</p>}</div><div className="flex flex-wrap gap-2">{!guided&&onSeeding&&<button type="button" className={(plan.seedsReviewed?"btn-outline":"btn-primary")+" action"} onClick={onSeeding}>{plan.seedsReviewed?"View Participants & Seeding":"Review Participants & Seeding"}</button>}{!matches.length&&<FixtureOptions items={[{label:'Export Participants',onClick:()=>exportParticipantsCsv(view.eventInfo.name,program.name,participants,true)}]}/>}</div></div></div>}
 {screen==='overview'&&<RankingDecision view={view} onChanged={async()=>{await reload();await onChanged();}}/>}
 <ActionMessage message={result?'':error}/><ActionMessage message={message} variant="success"/>{view.warnings.map((w,i)=><p key={i} className="notice">{w}</p>)}
 {!hasDefault&&!resultsOnly&&<div className="notice"><p>Set an active default in Game Setting Preset before generating a draw.</p><button type="button" className="btn-outline action mt-3" onClick={onPresets}>Open Game Setting Preset</button></div>}
 {(screen==='configure'||screen==='overview'&&!matches.length)&&<fieldset disabled={busy} className="p-6"><FixtureConfiguration canPreview={plan.seedsReviewed&&hasDefault} count={participants.length} initialConfig={config||{format:(formatName[plan.format]||'group_knockout') as WizardConfig['format'],numSeeds:participants.filter(p=>p.seed!==null).length,numGroups:plan.groupCount,advancePerGroup:plan.advancePerGroup}} onCancel={()=>guided?onSeeding?.():setScreen('overview')} onNext={c=>void work(()=>makePreview(c))}/></fieldset>}
 {screen==='next'&&<form className="p-6 space-y-5" onSubmit={e=>{e.preventDefault();void work(()=>makePreview());}}><h2 className="font-bold">Generate {nextLabel}</h2><div className="flex justify-between"><button type="button" className="btn-outline action" disabled={busy} onClick={()=>setScreen('overview')}>Back</button><button className="btn-primary action" disabled={busy}>Preview Draw</button></div></form>}
 {screen==='preview'&&preview&&<div className="p-6"><FixturePreview bracket={asBracket(preview.view,preview.view.plans.find(p=>p.programId===plan.programId)!,preview.matches)} seeds={seeds||participants} saving={busy} allowSwap={!matches.length} onBack={()=>{setError('');setScreen(matches.length?'next':'configure');}} onSwap={async(a,b)=>{setBusy(true);setError('');try{setPreview(await api<Preview>('tournament/preview-swap','POST',{revision:preview.revision,token:preview.token,entryA:a,entryB:b}));}catch(e){setError((e as Error).message);throw e;}finally{setBusy(false);}}} onConfirm={()=>void work(async()=>{await mutate('confirm',{revision:preview.revision,token:preview.token},'POST');setPreview(undefined);setScreen('overview');})}/></div>}
 {screen==='overview'&&matches.length>0&&<>{waiting&&!resultsOnly&&<p className="notice">{waiting}</p>}
 {resultsOnly?<FixtureViews view={view} matches={matches} options={[
 {label:'Export Participants',onClick:()=>exportParticipantsCsv(view.eventInfo.name,program.name,participants,true)},
 {label:'Reset Draw',disabled:busy||!matches.every(m=>['SC','BY'].includes(m.status)),onClick:()=>{setReason('');setAction({kind:'reset'});}},
 {label:'Reset Latest Round',disabled:busy||!current.some(m=>m.phase==='K')||!current.every(m=>['SC','BY'].includes(m.status)),onClick:()=>setAction({kind:'reset-round'})}
 ]} onResult={openResult} onSchedule={openSchedule} onClear={m=>{setReason('');setAction({kind:'clear',match:m});}} onUpdateSchedule={async(id,s)=>{const m=matches.find(m=>m.id===id)!;await mutate('schedule',{revision:view.revision,matchId:id,court:Number(s.courtNo),start:s.matchDate+'T'+(m.start?.slice(11)||'10:00:00'),umpireId:m.umpireId,serviceJudgeId:m.serviceJudgeId,lineJudgeIds:m.lineJudgeIds,reviewAvailable:m.reviewAvailable});}}/>:<><div className="flex justify-end mb-4"><FixtureOptions items={[
 {label:'Export Participants',onClick:()=>exportParticipantsCsv(view.eventInfo.name,program.name,participants,true)},
 {label:'Reset Draw',disabled:busy||!matches.every(m=>['SC','BY'].includes(m.status)),onClick:()=>{setReason('');setAction({kind:'reset'});}},
 {label:'Reset Latest Round',disabled:busy||!current.some(m=>m.phase==='K')||!current.every(m=>['SC','BY'].includes(m.status)),onClick:()=>setAction({kind:'reset-round'})}
 ]}/></div><FixturePreview saved allowSwap={false} bracket={asBracket(view,plan,current)} seeds={participants} saving={false} onSwap={async()=>{}} onConfirm={()=>{}} onBack={()=>{}}/></>}
 <div className="fixture-actions"><button type="button" className="btn-outline action" disabled={busy} onClick={resultsOnly?onSchedule:onSeeding}>{resultsOnly?'Back to Schedule':'Back to Seeding'}</button><div className="flex flex-wrap gap-3">{!resultsOnly&&<button type="button" className="btn-outline action" disabled={busy||!hasDefault} onClick={()=>setApplying(true)}>Apply Draw Settings to Other Programs</button>}{ready&&!waiting?<button type="button" className="btn-primary action" disabled={busy||!resultsOnly&&!hasDefault} onClick={()=>resultsOnly?onDraw?.():void work(()=>makePreview())}>{resultsOnly?'Prepare Next Round':'Preview '+fixtureProgress(plan).stage+' Draw'}</button>:!resultsOnly&&onSchedule&&<button type="button" className="btn-primary action" disabled={busy} onClick={onSchedule}>Continue to Schedule</button>}</div></div></>}
 {applying&&<ApplyDrawSettingsDialog eventId={eventId} sourceProgramId={plan.programId} onClose={()=>setApplying(false)} onDone={async count=>{setApplying(false);setMessage(`Draws generated for ${count} ${count===1?'program':'programs'}.`);try{await reload();await onChanged();}catch{setMessage('');setError('Draws were saved, but the screen could not refresh. Refresh before continuing.');}}}/>}
 {result&&<MatchResultEditor view={view} match={result} busy={busy} error={error} onClose={()=>{if(!busy)setResult(undefined);}} onSave={body=>void work(async()=>{await mutate('result',body);setResult(undefined);})}/>}
 {action&&<Dialog open onOpenChange={o=>{if(!o&&!busy)setAction(undefined);}}><DialogContent className="max-w-xl max-h-[94vh] overflow-y-auto p-0 sm:rounded-none" style={{backgroundColor:'var(--color-page-bg)'}}><DialogHeader className="px-7 pt-7"><DialogTitle>{action.kind==='preset'?'Choose Game Preset':action.kind==='schedule'?'Schedule and Officials':action.kind==='clear'?'Clear Result':'Reset Unplayed Draw / Round'}</DialogTitle></DialogHeader><form onSubmit={e=>{e.preventDefault();void work(async()=>{
  if(action.kind==='preset'){await api('presets/assign','POST',{revision:view.revision,matchId:action.match!.id,presetId,matchVersion:action.match!.matchVersion,confirmRoundChange:true});const fresh=await reload();await onChanged();const m=fresh.plans.flatMap(p=>p.matches).find(m=>m.id===action.match!.id)!;setAction(undefined);if(action.enterResult)setResult(m);else setMessage('Game preset saved.');}
  else if(action.kind==='schedule'){await mutate('schedule',{revision:view.revision,matchId:action.match!.id,...schedule,...officialAssignment(scheduleOfficials)});setAction(undefined);}
  else if(action.kind==='clear'){await mutate('clear-result',{revision:view.revision,matchId:action.match!.id,matchVersion:action.match!.matchVersion,reason},'POST');setAction(undefined);}
  else{await mutate(action.kind==='reset'?'reset':'reset-latest-round',{revision:view.revision,programId:plan.programId},'POST');setAction(undefined);setConfig(undefined);setSeeds(undefined);}
 });}}><fieldset disabled={busy} className="px-7 py-5 space-y-4">{action.match&&<p className="text-sm">#{action.match.number} · {label(action.match.entryA)} vs {label(action.match.entryB)}</p>}
 {action.kind==='preset'?<><label className="form-label">Game preset<select required className="field-input" value={presetId} onChange={e=>setPreset(e.target.value)}><option value="">Choose a saved preset</option>{view.presets.map(p=><option key={p.id} value={p.id}>{p.name} · {p.rules.gamesToWin} games to win · {p.rules.pointsToWin} points</option>)}</select></label>{!view.presets.length&&<button type="button" className="btn-outline action" onClick={onPresets}>Create a Game Preset</button>}<p className="text-xs opacity-60">Changing this unstarted match’s preset revokes its existing pairing code and tablet session. Pair its tablet again through Dashboard. Started matches retain their saved scoring rules.</p></>:action.kind==='schedule'?<><label className="form-label">Court<select className="field-input" value={schedule.court} onChange={e=>setSchedule({...schedule,court:Number(e.target.value),reviewAvailable:false})}>{view.eventCourts.map(c=><option key={c} value={c}>Court {c}</option>)}</select></label><div className="form-label"><span>Start</span><DateTimePicker label="Start" value={schedule.start} onChange={value=>setSchedule({...schedule,start:value})} required/></div><OfficialsEditor rows={scheduleOfficials} officials={view.eventPlan.officials} onChange={setScheduleOfficials} disabled={busy}/>{action.match!.rules.challengesEnabled&&<label className="flex gap-2 items-center text-sm"><Switch checked={schedule.reviewAvailable} onCheckedChange={checked=>setSchedule({...schedule,reviewAvailable:checked})}/>Challenge review available on this court</label>}</>:action.kind==='clear'?<label className="form-label">Reason for clearing this result<input required maxLength={300} className="field-input" value={reason} onChange={e=>setReason(e.target.value)}/></label>:<p className="text-sm">Reset Schedule first. This removes the selected unplayed draw or latest knockout round and its pairings; seeds remain unchanged. Started matches, results and tablet assignments are protected.</p>}
 </fieldset><DialogFooter className="px-7 pb-7"><button type="button" className="btn-outline action" disabled={busy} onClick={()=>setAction(undefined)}>Cancel</button><button className="btn-primary action" disabled={busy||action.kind==='schedule'&&!!officialsError(scheduleOfficials,view.eventPlan.officials)}>{busy?'Saving...':action.kind==='preset'?(action.enterResult?'Apply Preset and Enter Result':'Confirm Preset Change'):action.kind==='schedule'?'Save':'Confirm'}</button></DialogFooter></form></DialogContent></Dialog>}
 </>;
}
