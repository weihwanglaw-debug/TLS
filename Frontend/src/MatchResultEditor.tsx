import ExceptionalResultEditor from './ExceptionalResultEditor';
import DateTimePicker from './DateTimePicker';
import {useState} from 'react';
import {ScoreModal} from './components/admin/fixtures/ScoreModal';
import {asMatch} from './fixture-adapter';
import ResultDetailsForm,{emptyDetails} from './ResultDetailsForm';
import type {Match,View} from './tournament-types';
import type {MatchEntry} from './types/config';

export default function MatchResultEditor({view,match,busy,error,onClose,onSave}:{view:View;match:Match;busy:boolean;error:string;onClose:()=>void;onSave:(body:unknown)=>void}){
 const [exceptional,setExceptional]=useState(match.status==='IP'||!!match.resultKind&&match.resultKind!=='N');
 const [draft,setDraft]=useState<MatchEntry>(()=>{const d=asMatch(view,match);if(!d.games.length)d.games=Array.from({length:match.rules.gamesToWin},()=>({p1:'',p2:''}));return d;});
 const [details,setDetails]=useState(()=>structuredClone(match.resultDetails||emptyDetails())),[start,setStart]=useState(match.actualStartedAt?.slice(0,16)||''),[finish,setFinish]=useState(match.actualFinishedAt?.slice(0,16)||''),[reason,setReason]=useState('');
 const players=view.participants.filter(p=>view.entries.filter(e=>e.id===match.entryA||e.id===match.entryB).some(e=>e.participantIds.includes(p.id)));
 if(exceptional)return <ExceptionalResultEditor view={view} match={match} busy={busy} error={error} onClose={onClose} onSave={onSave}/>;
 return <ScoreModal open draft={draft} busy={busy} error={error} isLocked={['C','WO','RC'].includes(match.status)} onClose={onClose} onChangeDraft={patch=>{const d={...draft,...patch};if(!d.walkover){const a=d.games.filter(g=>Number(g.p1)>Number(g.p2)).length,b=d.games.filter(g=>Number(g.p2)>Number(g.p1)).length;d.winner=a>b?'team1':b>a?'team2':null;}setDraft(d);}} onSave={d=>onSave({revision:view.revision,matchId:match.id,matchVersion:match.matchVersion,games:d.walkover?[]:d.games.map(g=>({a:Number(g.p1),b:Number(g.p2)})),walkoverWinner:d.walkover?(d.walkoverWinner==='team1'?match.entryA:match.entryB):null,startedAt:match.actualStartedAt||start||null,finishedAt:finish===match.actualFinishedAt?.slice(0,16)?match.actualFinishedAt:finish||null,details,reason})}>
 <button type="button" className="btn-outline action" disabled={busy} onClick={()=>setExceptional(true)}>Exceptional / Override Official Result</button>
 <p className="text-sm">Game preset: <strong>{match.rules.name}</strong></p>{match.resultSource==='T'&&<p className="notice">Loaded from the umpire tablet. Its scoring history and recorded start time are retained.</p>}<div className="grid grid-cols-2 gap-3"><div className="form-label"><span>Actual start</span><DateTimePicker label="Actual start" value={start} onChange={value=>setStart(value)} disabled={!!match.actualStartedAt}/></div><div className="form-label"><span>Actual finish</span><DateTimePicker label="Actual finish" value={finish} onChange={value=>setFinish(value)}/></div></div><ResultDetailsForm value={details} onChange={setDetails} players={players} maxGames={match.rules.gamesToWin*2-1} disabled={busy}/>{['C','WO','RC'].includes(match.status)&&<label className="form-label">Reason for any correction<input maxLength={300} className="field-input" value={reason} onChange={e=>setReason(e.target.value)}/></label>}
 </ScoreModal>;
}
