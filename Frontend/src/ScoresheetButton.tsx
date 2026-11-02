import {useState} from 'react';
import {liveApi} from './live-api';
import type {LiveMatch} from './live-types';
import PaperScoresheet from './PaperScoresheet';
import {Dialog,DialogContent,DialogHeader,DialogTitle,DialogDescription} from './components/ui/dialog';

export default function ScoresheetButton({matchId}:{matchId:string}){
 const [open,setOpen]=useState(false),[match,setMatch]=useState<LiveMatch>(),[error,setError]=useState(''),[busy,setBusy]=useState(false);
 async function show(){setOpen(true);setMatch(undefined);setError('');setBusy(true);try{setMatch(await liveApi<LiveMatch>('dashboard/scoresheet/'+encodeURIComponent(matchId)));}catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 return <><button type="button" className="btn-outline px-3 py-1.5 text-xs ml-2" onClick={()=>void show()}>View Scoresheet</button>{open&&<Dialog open onOpenChange={setOpen}><DialogContent className="console-scoresheet" style={{backgroundColor:'var(--color-page-bg)'}}><DialogHeader><DialogTitle>Scoresheet</DialogTitle><DialogDescription>Recorded match scores and rally history</DialogDescription></DialogHeader>{busy?<p>Loading scoresheet…</p>:error?<p role="alert">{error}</p>:match?<PaperScoresheet match={match}/>:null}<footer className="flex gap-3"><button className="btn-outline action" onClick={()=>setOpen(false)}>Close</button><button className="btn-primary action" disabled={busy||!match||!!error} onClick={()=>window.print()}>Print Scoresheet</button></footer></DialogContent></Dialog>}</>;
}
