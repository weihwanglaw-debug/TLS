import {useEffect,useState} from 'react';
import {api} from './api';
import ActionMessage from './ActionMessage';
import {EventPanel} from './FoundationUi';

type Failure={eventId:string;kind:string;message:string;at:string};
type Settings={revision:number;source:string;enabled:boolean;liveUrl:string;resultsUrl:string;intervalSeconds:number;hasCredential:boolean;failures:Failure[]};

export default function TrsPublishing({eventId,onChanged}:{eventId:string;onChanged:()=>Promise<void>}){
 const [settings,setSettings]=useState<Settings>(),[key,setKey]=useState(''),[error,setError]=useState(''),[message,setMessage]=useState(''),[busy,setBusy]=useState(false);
 useEffect(()=>{let active=true;setSettings(undefined);setKey('');api<Settings>('trs/publishing/'+eventId).then(s=>{if(active)setSettings(s);}).catch(e=>{if(active)setError(e.message);});return()=>{active=false;};},[eventId]);
 async function save(){if(!settings)return;setBusy(true);setError('');setMessage('');try{
  const fresh=await api<Settings>('trs/publishing/'+eventId);
  const saved=await api<Settings>('trs/publishing/'+eventId,'PUT',{revision:fresh.revision,enabled:settings.enabled,liveUrl:settings.liveUrl,resultsUrl:settings.resultsUrl,intervalSeconds:settings.intervalSeconds,apiKey:key});
  setSettings(saved);setKey('');await onChanged();setMessage('TRS publishing settings saved.');
 }catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 async function resend(){setBusy(true);setError('');setMessage('');try{const r=await api<{failures:Failure[]}>('trs/publishing/'+eventId+'/resend','POST');setSettings(s=>s?{...s,failures:r.failures}:s);setMessage(r.failures.length?'Publishing failed. Check the dashboard alert.':'Live scores and current program results sent.');}catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 if(!settings)return <ActionMessage message={error}/>;
 if(settings.source!=='TRS')return null;
 return <EventPanel title="TRS Publishing"><form onSubmit={e=>{e.preventDefault();void save();}}><ActionMessage message={error}/><ActionMessage message={message} variant="success"/>
 <fieldset disabled={busy} className="space-y-4"><label className="flex items-center gap-3"><input type="checkbox" checked={settings.enabled} onChange={e=>setSettings({...settings,enabled:e.target.checked})}/>Enable publishing to TRS</label>
 <label className="form-label">Live-score API URL<input className="field-input" type="url" required={settings.enabled} value={settings.liveUrl} onChange={e=>setSettings({...settings,liveUrl:e.target.value})} placeholder="https://trs.example/api/tls/events/123/live"/></label>
 <label className="form-label">Result API URL<input className="field-input" required={settings.enabled} value={settings.resultsUrl} onChange={e=>setSettings({...settings,resultsUrl:e.target.value})} placeholder="https://trs.example/api/tls/events/123/programs/{programId}/results"/><span className="text-xs opacity-70">Use {'{programId}'} where the original TRS program ID belongs.</span></label>
 <label className="form-label">API credential<input className="field-input" type="password" autoComplete="new-password" minLength={32} maxLength={512} required={settings.enabled&&!settings.hasCredential} value={key} onChange={e=>setKey(e.target.value)} placeholder={settings.hasCredential?'Saved — leave blank to keep it':''}/></label>
 <label className="form-label">Live update interval (seconds)<input className="field-input" style={{maxWidth:160}} type="number" required min={2} max={120} value={settings.intervalSeconds} onChange={e=>setSettings({...settings,intervalSeconds:Number(e.target.value)})}/></label>
 <div className="flex gap-3 flex-wrap"><button className="btn-primary action">Save Publishing Settings</button><button type="button" className="btn-outline action" disabled={!settings.enabled} onClick={()=>void resend()}>Resend Results / Live Scores</button></div>
 </fieldset></form></EventPanel>;
}

export function TrsPublishingBadge({eventId}:{eventId?:string}){
 const [failures,setFailures]=useState<Failure[]>([]),[error,setError]=useState(''),[busy,setBusy]=useState(false);
 useEffect(()=>{let active=true;const load=()=>api<Failure[]>('trs/publishing/status/all'+(eventId?'?eventId='+encodeURIComponent(eventId):'')).then(f=>{if(active)setFailures(f);}).catch(()=>{});void load();const timer=setInterval(load,5000);return()=>{active=false;clearInterval(timer);};},[eventId]);
 async function retry(){setBusy(true);setError('');try{for(const id of [...new Set(failures.map(f=>f.eventId))])await api('trs/publishing/'+id+'/resend','POST');setFailures(await api<Failure[]>('trs/publishing/status/all'+(eventId?'?eventId='+encodeURIComponent(eventId):'')));}catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 if(!failures.length)return null;
 return <details className="my-3 inline-block border px-3 py-2" style={{borderColor:'var(--color-primary)'}}><summary className="cursor-pointer text-sm font-semibold">TRS: {[...new Set(failures.map(f=>f.kind==='live'?'Live-score publishing failed':'Result publishing failed'))].join(' · ')}</summary>
 <div className="space-y-2 mt-3">{failures.map((f,i)=><p key={i} className="text-sm">{eventId?'':f.eventId+' · '}{f.message} · {new Date(f.at).toLocaleString()}</p>)}<ActionMessage message={error}/><button type="button" className="btn-outline action" disabled={busy} onClick={()=>void retry()}>{busy?'Sending...':'Retry Publishing'}</button></div></details>;
}
