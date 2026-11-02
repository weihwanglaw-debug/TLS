import {useEffect,useState} from 'react';
import {api} from './api';
import ActionMessage from './ActionMessage';
import {Switch} from './components/ui/switch';
import {Dialog,DialogContent,DialogHeader,DialogTitle,DialogFooter} from './components/ui/dialog';
type Data={revision:number;rounds:{key:string;name:string;reason:string}[]};
export default function ResetScheduleDialog({eventId,onClose,onDone}:{eventId:string;onClose:()=>void;onDone:()=>Promise<void>}){
 const [data,setData]=useState<Data>(),[selected,setSelected]=useState<string[]>([]),[busy,setBusy]=useState(false),[error,setError]=useState('');
 async function load(){setData(await api<Data>('tournament/'+eventId+'/reset-schedule'));setSelected([]);}
 useEffect(()=>{void load().catch(e=>setError(e.message));},[eventId]);
 const eligible=data?.rounds.filter(r=>!r.reason)||[];
 async function confirm(){setBusy(true);setError('');try{await api('tournament/reset-schedule','POST',{revision:data!.revision,eventId,roundKeys:selected});await onDone();}catch(e){setError((e as Error).message);try{await load();}catch{/* Reopen to retry loading. */}}finally{setBusy(false);}}
 return <><Dialog open onOpenChange={open=>{if(!open&&!busy)onClose();}}><DialogContent className="max-w-3xl max-h-[90vh] overflow-y-auto sm:rounded-none" style={{backgroundColor:'var(--color-page-bg)'}}><DialogHeader><DialogTitle>Reset Schedule</DialogTitle></DialogHeader><p className="text-sm">Clear dates, times, courts and session allocations for the selected rounds. Draws, seeds and session hours stay unchanged. Cleared slots must be scheduled again.</p><label className="flex justify-end items-center gap-3 text-sm"><Switch aria-label="Select All Eligible Rounds" disabled={busy||!eligible.length} checked={!!eligible.length&&eligible.every(r=>selected.includes(r.key))} onCheckedChange={checked=>setSelected(checked?eligible.map(r=>r.key):[])}/>Select All</label><div className="space-y-4">{data?.rounds.map(r=><div key={r.key}><label className="flex items-center gap-3 text-sm"><Switch aria-label={r.name} disabled={busy||!!r.reason} checked={selected.includes(r.key)} onCheckedChange={checked=>setSelected(checked?[...selected,r.key]:selected.filter(k=>k!==r.key))}/>{r.name}</label>{r.reason&&<p className="text-xs mt-1 ml-12">{r.reason}</p>}</div>)}{data&&!data.rounds.length&&<p>No generated rounds.</p>}{!data&&<p>Loading rounds...</p>}</div><DialogFooter><button type="button" className="btn-outline action" disabled={busy} onClick={onClose}>Cancel</button><button type="button" className="btn-primary action" disabled={busy||!data||!selected.length} onClick={()=>void confirm()}>{busy?'Resetting...':'Confirm Reset Schedule'}</button></DialogFooter></DialogContent></Dialog><ActionMessage message={error}/></>;
}
