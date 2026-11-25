import ActionMessage from './ActionMessage';
import TrsPublishing,{type PublishingSettings} from './TrsPublishing';
import EventDatesEditor from './EventDatesEditor';
import {useEffect,useId,useRef,useState,type ReactNode} from 'react';
import {Plus,Trash2} from 'lucide-react';
import {api} from './api';
import {operationId} from './live-api';
import {EventPanel} from './FoundationUi';
import type {EventPlan,View} from './tournament-types';

export default function EventSettings({eventId,onChanged,dateRevision='',details,eventActions,backAction}:{eventActions?:ReactNode;backAction?:ReactNode;eventId:string;onChanged:()=>Promise<void>;dateRevision?:string;details?:(dates:ReactNode)=>ReactNode}){
 const [view,setView]=useState<View>(),[plan,setPlan]=useState<EventPlan>(),[publishing,setPublishing]=useState<PublishingSettings>(),[credential,setCredential]=useState('');
 const [start,setStart]=useState(''),[end,setEnd]=useState(''),[editing,setEditing]=useState(false),[busy,setBusy]=useState(false),[error,setError]=useState(''),[message,setMessage]=useState('');
 const saving=useRef(false);
 const formId=useId();
 function accept(v:View,p:PublishingSettings){setView(v);setPlan({...v.eventPlan});setPublishing(p);setCredential(p.apiKey||'');setStart(v.eventInfo.startDate);setEnd(v.eventInfo.endDate);}
 async function load(){const [v,p]=await Promise.all([api<View>('tournament/'+eventId),api<PublishingSettings>('trs/publishing/'+eventId)]);return {v,p};}
 useEffect(()=>{let active=true;setEditing(false);void load().then(({v,p})=>{if(active)accept(v,p);}).catch(e=>{if(active)setError(e.message);});return()=>{active=false;};},[eventId,dateRevision]);
 async function edit(){setBusy(true);setError('');setMessage('');try{const {v,p}=await load();accept(v,p);setEditing(true);}catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 async function save(){
  if(saving.current||!view||!plan||!publishing||!editing)return;
  saving.current=true;setBusy(true);setError('');setMessage('');
  try{
   const updated=await api<View>('tournament/event-settings','PUT',{revision:view.revision,startDate:start,endDate:end,plan,publishing:['TRS','EXCEL'].includes(publishing.source)?{revision:view.revision,enabled:publishing.enabled,liveUrl:publishing.liveUrl,resultsUrl:publishing.resultsUrl,intervalSeconds:publishing.intervalSeconds,apiKey:credential}:null});
   accept(updated,{...publishing,revision:updated.revision,hasCredential:publishing.hasCredential||!!credential.trim(),apiKey:credential.trim()||publishing.apiKey||''});setEditing(false);setMessage('Event saved.');
   try{await onChanged();}catch{setError('Event saved. Refresh the page to load the latest information.');}
  }catch(e){setError((e as Error).message);}finally{saving.current=false;setBusy(false);}
 }
 if(!view||!plan||!publishing)return <><ActionMessage message={error}/><p>Loading event settings...</p></>;
 const dates=<EventDatesEditor start={start} end={end} editing={editing} onStart={setStart} onEnd={setEnd}/>;
 return <><div className="flex flex-wrap items-center justify-between gap-3 mb-6">{backAction}<div className="flex flex-wrap items-center justify-end gap-3 ml-auto">{eventActions}{editing?<button form={formId} type="submit" className="btn-primary action" disabled={busy}>{busy?'Saving...':'Save Event'}</button>:<button type="button" className="btn-primary action" disabled={busy} onClick={()=>void edit()}>Edit Event</button>}</div></div><form id={formId} onSubmit={e=>{e.preventDefault();void save();}}><ActionMessage message={error}/><ActionMessage message={message} variant="success"/>
 <fieldset disabled={busy}>{details?details(dates):<EventPanel title="Event Dates">{dates}</EventPanel>}</fieldset>
 <fieldset disabled={busy||!editing} aria-label="Event Operating Settings">
 <EventPanel title="Officials"><div className="space-y-4">{plan.officials.map((o,i)=><div className="flex gap-3" key={o.id}><input required aria-label="Official name" maxLength={100} className="field-input" value={o.name} onChange={e=>setPlan({...plan,officials:plan.officials.map((x,n)=>n===i?{...x,name:e.target.value}:x)})}/><button type="button" className="btn-outline action" aria-label="Remove official" onClick={()=>setPlan({...plan,officials:plan.officials.filter((_,n)=>n!==i)})}><Trash2 size={14}/></button></div>)}<button type="button" className="btn-outline action" onClick={()=>setPlan({...plan,officials:[...plan.officials,{id:operationId(),name:''}]})}><Plus size={14}/>Add Official</button></div></EventPanel>
 </fieldset><TrsPublishing settings={publishing} onChange={setPublishing} credential={credential} onCredential={setCredential} editing={editing} busy={busy}/></form></>;
}
