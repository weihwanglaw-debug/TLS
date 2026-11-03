import ActionMessage from './ActionMessage';
import TrsPublishing from './TrsPublishing';
import {useEffect,useState} from 'react';
import {Plus,Trash2} from 'lucide-react';
import {api} from './api';
import {operationId} from './live-api';
import {EventPanel} from './FoundationUi';
import type {EventPlan,View} from './tournament-types';

export default function EventSettings({eventId,onChanged,dateRevision=''}:{eventId:string;onChanged:()=>Promise<void>;dateRevision?:string}){
 const [view,setView]=useState<View>(),[plan,setPlan]=useState<EventPlan>(),[busy,setBusy]=useState(false),[error,setError]=useState(''),[message,setMessage]=useState('');
 function savedPlan(v:View){return {...v.eventPlan};}
 function accept(v:View){setView(v);setPlan(savedPlan(v));}
 useEffect(()=>{let cancelled=false;api<View>('tournament/'+eventId).then(v=>{if(!cancelled){setView(v);setPlan(previous=>previous?.eventId===eventId?{...v.eventPlan,officials:previous.officials,matchMinutes:previous.matchMinutes,restMinutes:previous.restMinutes}:savedPlan(v));}}).catch(e=>{if(!cancelled)setError(e.message);});return()=>{cancelled=true;};},[eventId,dateRevision]);
 async function save(){setBusy(true);setError('');setMessage('');try{const updated=await api<View>('tournament/event','PUT',{revision:view!.revision,plan});accept(updated);await onChanged();setMessage('Event operating settings saved. Existing fixture times remain unchanged.');}catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 if(!view||!plan)return <><ActionMessage message={error}/><p>Loading event operating settings...</p></>;
 return <><form className="mt-8" onSubmit={e=>{e.preventDefault();void save();}}><ActionMessage message={error}/><ActionMessage message={message} variant="success"/><fieldset disabled={busy} aria-label="Event Operating Settings">
 <EventPanel title="Match Timing"><div className="schedule-fields flex flex-wrap gap-5"><label className="form-label">Estimated match minutes<input aria-label="Estimated match minutes" required className="field-input" style={{width:140}} type="number" min={1} max={360} value={plan.matchMinutes} onChange={e=>setPlan({...plan,matchMinutes:Number(e.target.value)})}/></label><label className="form-label">Rest Between Player Matches (minutes)<input aria-label="Minimum player rest minutes" required className="field-input" style={{width:140}} type="number" min={0} max={240} value={plan.restMinutes} onChange={e=>setPlan({...plan,restMinutes:Number(e.target.value)})}/></label></div></EventPanel>
 <EventPanel title="Officials"><div className="space-y-4"><p className="text-sm opacity-70">Names only. Assign their positions on each match.</p>{plan.officials.map((o,i)=><div className="flex gap-3" key={o.id}><input required aria-label="Official name" maxLength={100} className="field-input" value={o.name} onChange={e=>setPlan({...plan,officials:plan.officials.map((x,n)=>n===i?{...x,name:e.target.value}:x)})}/><button type="button" className="btn-outline action" aria-label="Remove official" onClick={()=>setPlan({...plan,officials:plan.officials.filter((_,n)=>n!==i)})}><Trash2 size={14}/></button></div>)}<button type="button" className="btn-outline action" onClick={()=>setPlan({...plan,officials:[...plan.officials,{id:operationId(),name:''}]})}><Plus size={14}/>Add Official</button></div></EventPanel>
 <button className="btn-primary action" disabled={busy}>{busy?'Saving...':'Save Event Settings'}</button></fieldset></form><TrsPublishing eventId={eventId} onChanged={async()=>{accept(await api<View>('tournament/'+eventId));await onChanged();}}/></>;
}
