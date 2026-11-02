import {useEffect,useState} from 'react';
import {api} from './api';
import ActionMessage from './ActionMessage';
import SeedingPage from './SeedingPage';
import TournamentConsole from './TournamentConsole';
import EventSchedule from './EventSchedule';
import {fixtureProgress,type FixtureStep} from './fixture-progress';
import type {View} from './tournament-types';
export default function FixtureWorkspace({eventId,programId='',initialStep,onBack,onChanged,onPresets,onContextChange}:{eventId:string;programId?:string;initialStep?:FixtureStep;onBack:()=>void;onChanged:()=>Promise<void>;onPresets:()=>void;onContextChange?:(program:string,step:FixtureStep)=>void}){
 const [view,setView]=useState<View>(),[program,setProgram]=useState(programId),[step,setStep]=useState<FixtureStep>(initialStep||'seeding'),[error,setError]=useState(''),[ready,setReady]=useState(false);
 async function load(){const v=await api<View>('tournament/'+eventId);setView(v);return v;}
 useEffect(()=>{void load().then(v=>{const id=programId||v.programs.find(p=>p.status==='A')?.id||'';setProgram(id);const progress=fixtureProgress(v.plans.find(p=>p.programId===id));setStep(initialStep==='results'&&!progress.resultsAvailable?progress.nextStep:initialStep||progress.nextStep);setReady(true);}).catch(e=>setError(e.message));},[eventId]);
 useEffect(()=>{if(!ready||!view)return;if(programId&&view.programs.some(p=>p.id===programId))setProgram(programId);if(initialStep){const progress=fixtureProgress(view.plans.find(p=>p.programId===(programId||program)));setStep(initialStep==='results'&&!progress.resultsAvailable?progress.nextStep:initialStep);}},[programId,initialStep]);
 useEffect(()=>{if(ready&&program)onContextChange?.(program,step);},[ready,program,step]);
 async function changed(){await load();await onChanged();}
 if(!view)return <><ActionMessage message={error}/><p>Loading fixture planning...</p></>;
 const selected=view.programs.find(p=>p.id===program),supported=selected?.sport.toLowerCase()==='badminton'&&['S','D'].includes(selected.entryType);
 const plan=view.plans.find(p=>p.programId===program),progress=fixtureProgress(plan);
 const statuses={seeding:progress.seeding,draw:progress.draw,schedule:progress.schedule};
 function selectProgram(id:string){setProgram(id);setStep(view!.programs.some(p=>p.id===id&&p.sport.toLowerCase()==='badminton'&&['S','D'].includes(p.entryType))?fixtureProgress(view!.plans.find(p=>p.programId===id)).nextStep:'seeding');}
 const entries=view.entries.filter(e=>e.programId===program&&e.status==='A').length;
 return <><header className="fixture-workspace-header"><div className="fixture-workspace-toolbar"><button type="button" className="btn-outline action" onClick={onBack}>Program Fixture List</button><div className="fixture-program-choice"><label className="form-label fixture-program-selector">Program<select aria-label="Planning program" disabled={step==='schedule'} className="field-input" value={program} onChange={e=>selectProgram(e.target.value)}>{view.programs.filter(p=>p.status==='A').map(p=><option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
 <section className="fixture-program-details" aria-label="Program details"><p className="text-xs">{selected?.sport} · {view.eventInfo.startDate} – {view.eventInfo.endDate} · {entries} entries{plan?.matches.length?' · '+({KO:'Knockout',GK:'Group + Knockout',RR:'Round Robin'}[plan.format]||plan.format):''}</p><strong className="fixture-program-stage">{progress.stage}</strong></section></div></div>
 <nav aria-label="Fixture planning steps" className="fixture-steps">{(['seeding','draw','schedule','results'] as const).map((key,i)=><button type="button" key={key} className={'fixture-step'+(step===key?' active':'')} aria-current={step===key?'step':undefined} disabled={!program&&key!=='schedule'||key==='draw'&&!supported||key==='results'&&!progress.resultsAvailable} onClick={()=>setStep(key)}><strong>{i+1}. {key==='seeding'?'Seeding':key==='draw'?'Draw':key==='schedule'?'Schedule':'Fixture & Results'}</strong><span>{key==='results'?progress.resultsAvailable?`${progress.scheduled} scheduled · ${progress.unscheduled} pending`:'Schedule a match first':statuses[key]}</span></button>)}</nav></header>
 <div className="fixture-workspace-content">
 {step==='seeding'&&program&&<SeedingPage guided key={program} programId={program} onChanged={changed} onBack={onBack} onContinue={supported?()=>setStep('draw'):undefined}/>}
 {(step==='draw'||step==='results')&&program&&<TournamentConsole key={program+step} resultsOnly={step==='results'} eventId={eventId} programId={program} onChanged={changed} onPresets={onPresets} onEventSettings={()=>{}} onSeeding={()=>setStep('seeding')} onSchedule={()=>setStep('schedule')} onDraw={()=>setStep('draw')} guided/>}
 {step==='schedule'&&<EventSchedule eventId={eventId} programId={program} onChanged={changed} onBack={()=>setStep('draw')} onDone={()=>setStep('results')} guided/>}
 </div></>;
}
