import {useEffect,useState} from 'react';
import {api} from './api';
import ActionMessage from './ActionMessage';
import SeedingPage from './SeedingPage';
import TournamentConsole from './TournamentConsole';
import EventSchedule from './EventSchedule';
import {fixtureProgress,type FixtureStep} from './fixture-progress';
import type {View} from './tournament-types';
export default function FixtureWorkspace({eventId,programId='',initialStep,onBack,onChanged,onPresets,onContextChange,onUnsavedChange,canLeave=()=>true}:{eventId:string;programId?:string;initialStep?:FixtureStep;onBack:()=>void;onChanged:()=>Promise<void>;onPresets:()=>void;onContextChange?:(program:string,step:FixtureStep)=>void;onUnsavedChange?:(dirty:boolean)=>void;canLeave?:()=>boolean}){
 const [view,setView]=useState<View>(),[program,setProgram]=useState(programId),[step,setStep]=useState<FixtureStep>(initialStep||'seeding'),[error,setError]=useState(''),[ready,setReady]=useState(false);
 async function load(){const v=await api<View>('tournament/'+eventId);setView(v);return v;}
 useEffect(()=>{let active=true;void load().then(v=>{if(!active)return;const id=programId||v.programs.find(p=>p.status==='A')?.id||'';setProgram(id);setStep(initialStep||fixtureProgress(v.plans.find(p=>p.programId===id)).nextStep);setReady(true);}).catch(e=>setError(e.message));return()=>{active=false;};},[eventId]);
 useEffect(()=>{if(!ready||!view)return;if(programId&&view.programs.some(p=>p.id===programId))setProgram(programId);if(initialStep)setStep(initialStep);},[programId,initialStep]);
 useEffect(()=>{if(ready&&program)onContextChange?.(program,step);},[ready,program,step]);
 async function changed(){await load();await onChanged();}
 function navigate(next:FixtureStep,id?:string){if(next!==step&&!canLeave())return;if(id)setProgram(id);setStep(next);}
 if(!view)return <><ActionMessage message={error}/><p>Loading fixtures...</p></>;
 const active=view.programs.filter(p=>p.status==='A'),selected=active.find(p=>p.id===program),plan=view.plans.find(p=>p.programId===program),progress=fixtureProgress(plan);
 const supported=selected?.sport.toLowerCase()==='badminton'&&['S','D'].includes(selected.entryType),perProgram=step==='seeding'||step==='draw';
 const statuses={seeding:active.every(p=>view.plans.find(x=>x.programId===p.id)?.seedsReviewed)?'Completed':'Per program',draw:active.every(p=>view.plans.find(x=>x.programId===p.id)?.matches.length)?'Draws ready':'Per program',schedule:view.eventPlan.schedulePlanner?.bookings.length?'Saved':'Whole event',results:'All programs'};
 const entries=view.entries.filter(e=>e.programId===program&&e.status==='A').length;
 const resultsAvailable=view.plans.some(p=>fixtureProgress(p).resultsAvailable);
 const missing=active.find(p=>p.sport.toLowerCase()==='badminton'&&['S','D'].includes(p.entryType)&&!view.plans.find(x=>x.programId===p.id)?.matches.length);
 return <><div className="fixture-workspace-overview"><button type="button" className="btn-outline action" onClick={()=>{if(canLeave())onBack();}}>← Fixture overview</button></div>
 <header className="fixture-workspace-header"><nav aria-label="Fixture planning steps" className="fixture-steps">{(['seeding','draw','schedule','results'] as const).map((key,i)=><button type="button" key={key} className={'fixture-step'+(step===key?' active':'')} aria-current={step===key?'step':undefined} disabled={!program&&key!=='schedule'||key==='draw'&&!supported||key==='results'&&!resultsAvailable} onClick={()=>navigate(key)}><strong>{i+1}. {key==='seeding'?'Seeding':key==='draw'?'Draw':key==='schedule'?'Schedule':'Fixtures & Results'}</strong><span>{statuses[key]}</span></button>)}</nav></header>
 <div className="fixture-workspace-content">
 {perProgram&&<div className="fixture-program-choice"><label className="form-label fixture-program-selector">Program<select aria-label="Planning program" className="field-input" value={program} onChange={e=>setProgram(e.target.value)}>{active.map(p=><option key={p.id} value={p.id}>{p.name}</option>)}</select></label><section className="fixture-program-details" aria-label="Program details"><span>{entries} entries</span><span>{plan?.matches.length?({KO:'Knockout',GK:'Group + Knockout',RR:'Round Robin'}[plan.format]||plan.format):selected?.sport}</span><strong className="fixture-program-stage">{progress.stage}</strong></section></div>}
 {step==='seeding'&&program&&<SeedingPage guided key={program} programId={program} onChanged={changed} onBack={()=>{if(canLeave())onBack();}} onContinue={supported?()=>navigate('draw'):undefined}/>}
 {step==='draw'&&program&&<TournamentConsole key={program+step} eventId={eventId} programId={program} onChanged={changed} onPresets={onPresets} onEventSettings={()=>{}} onSeeding={()=>navigate('seeding')} onSchedule={()=>navigate('schedule')} onDraw={()=>navigate('draw')} guided/>}
 {step==='results'&&<TournamentConsole key="event-results" resultsOnly eventId={eventId} onChanged={changed} onPresets={onPresets} onEventSettings={()=>{}} onSchedule={()=>navigate('schedule')} onDraw={id=>navigate('draw',id)} guided/>}
 {step==='schedule'&&<EventSchedule eventId={eventId} onChanged={changed} onBack={()=>navigate('draw',missing?.id)} onDone={()=>navigate('results')} onUnsavedChange={onUnsavedChange} guided/>}
 </div></>;
}
