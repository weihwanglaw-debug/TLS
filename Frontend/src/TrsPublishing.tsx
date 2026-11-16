import {useEffect,useState} from 'react';
import {api} from './api';
import {EventPanel} from './FoundationUi';
import {Switch} from './components/ui/switch';

type Failure={eventId:string;kind:string;message:string;at:string};
export type PublishingSettings={revision:number;source:string;enabled:boolean;liveUrl:string;resultsUrl:string;intervalSeconds:number;hasCredential:boolean;apiKey:string;failures:Failure[]};

export default function TrsPublishing({settings,onChange,credential,onCredential,editing,busy}:{settings:PublishingSettings;onChange:(settings:PublishingSettings)=>void;credential:string;onCredential:(value:string)=>void;editing:boolean;busy:boolean}){
 if(settings.source!=='TRS')return null;
 return <EventPanel title="Score Publishing"><label className="flex items-center gap-3 mb-5"><Switch aria-label="Enable Score Publishing" checked={settings.enabled} disabled={!editing||busy} onCheckedChange={enabled=>onChange({...settings,enabled})}/>Enable Score Publishing</label>
 {settings.enabled&&<><fieldset disabled={!editing||busy} className="space-y-4">
 <label className="form-label">Live-score API URL<input className="field-input" type="url" required value={settings.liveUrl} onChange={e=>onChange({...settings,liveUrl:e.target.value})} placeholder="https://example.com/api/integration/live-scores"/></label>
 <label className="form-label">Result API URL<input className="field-input" required value={settings.resultsUrl} onChange={e=>onChange({...settings,resultsUrl:e.target.value})} placeholder="https://example.com/api/integration/results"/></label>
 <label className="form-label">API credential<input className="field-input" type="text" autoComplete="off" spellCheck={false} minLength={32} maxLength={512} required={!settings.hasCredential} value={credential} onChange={e=>onCredential(e.target.value)}/></label>
 <label className="form-label">Live update interval (seconds)<input className="field-input" style={{maxWidth:160}} type="number" required min={2} max={120} value={settings.intervalSeconds} onChange={e=>onChange({...settings,intervalSeconds:Number(e.target.value)})}/></label>
 </fieldset></>}
 </EventPanel>;
}

export function TrsPublishingBadge({eventId}:{eventId?:string}){
 const [failures,setFailures]=useState<Failure[]>([]);
 useEffect(()=>{let active=true;const load=()=>api<Failure[]>('trs/publishing/status/all'+(eventId?'?eventId='+encodeURIComponent(eventId):'')).then(f=>{if(active)setFailures(f);}).catch(()=>{});void load();const timer=setInterval(load,5000);return()=>{active=false;clearInterval(timer);};},[eventId]);
 if(!failures.length)return null;
 return <details className="my-3 inline-block border px-3 py-2" style={{borderColor:'var(--color-primary)'}}><summary className="cursor-pointer text-sm font-semibold">TRS: {[...new Set(failures.map(f=>f.kind==='live'?'Live-score publishing failed':'Result publishing failed'))].join(' · ')}</summary>
 <div className="space-y-2 mt-3">{failures.map((f,i)=><p key={i} className="text-sm">{eventId?'':f.eventId+' · '}{f.message} · {new Date(f.at).toLocaleString()}</p>)}</div></details>;
}
