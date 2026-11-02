import {useState} from 'react';
import {api} from './api';
import type {View} from './setup-types';
import ActionMessage from './ActionMessage';
import {Dialog,DialogContent,DialogHeader,DialogTitle,DialogDescription,DialogFooter} from './components/ui/dialog';

type Removal={revision:number;name:string;programs:number;participants:number;entries:number};
export default function EventRemovalDialog({eventId,onRemoved}:{eventId:string;onRemoved:(view:View,name:string)=>void}){
 const [checking,setChecking]=useState(false),[removing,setRemoving]=useState(false),[summary,setSummary]=useState<Removal|null>(null),[error,setError]=useState('');
 const path='setup/events/'+encodeURIComponent(eventId);
 async function check(){
  setChecking(true);setError('');
  try{setSummary(await api<Removal>(path+'/removal'));}catch(e){setError((e as Error).message);}finally{setChecking(false);}
 }
 async function remove(){
  if(!summary)return;
  setRemoving(true);setError('');
  try{const next=await api<View>(path+'?revision='+summary.revision,'DELETE');onRemoved(next,summary.name);}
  catch(e){setSummary(null);setError((e as Error).message);}finally{setRemoving(false);}
 }
 return <><button className="btn-outline px-4 py-2 text-sm" disabled={checking||removing} onClick={()=>void check()}>{checking?'Checking...':'Remove Event'}</button>
  <ActionMessage message={error} title="Unable to remove event"/>
  <Dialog open={!!summary} onOpenChange={open=>{if(!open&&!removing)setSummary(null);}}><DialogContent className="max-w-lg" style={{backgroundColor:'var(--color-page-bg)'}}><DialogHeader><DialogTitle>Remove Event</DialogTitle><DialogDescription>Remove {summary?.name} and its imported records permanently?</DialogDescription></DialogHeader>
   {summary&&<p className="text-sm">{summary.programs} programs · {summary.participants} participants · {summary.entries} entries</p>}
   <p className="text-sm">Master data stays unchanged. To restore the event, import the file again.</p>
   <DialogFooter><button className="btn-outline action" disabled={removing} onClick={()=>setSummary(null)}>Cancel</button><button className="btn-primary action" disabled={removing} onClick={()=>void remove()}>{removing?'Removing...':'Confirm Remove Event'}</button></DialogFooter>
  </DialogContent></Dialog>
 </>;
}
