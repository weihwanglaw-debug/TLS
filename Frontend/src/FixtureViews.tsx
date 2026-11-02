import FixtureOptions,{type FixtureOption} from './FixtureOptions';
import ActionMessage from './ActionMessage';
import {Printer} from 'lucide-react';
import {printFixture} from './lib/fixturePrint';
import {useState,useRef} from 'react';
import AdminTabs from './AdminTabs';
import {BracketView} from './components/admin/fixtures/BracketView';
import {ResultsTab} from './components/admin/fixtures/ResultsTab';
import {GroupStandingsTable} from './components/admin/fixtures/GroupStandingsTable';
import {asBracket} from './fixture-adapter';
import type {Match,View} from './TournamentConsole';
import type {MatchEntry} from './types/config';

export default function FixtureViews({view,matches,onResult,onSchedule,onUpdateSchedule,onClear,onPreset,options=[]}:{view:View;matches:Match[];options?:FixtureOption[];onClear?:(m:Match)=>void;onPreset?:(m:Match)=>void;onResult?:(m:Match)=>void;onSchedule?:(m:Match)=>void;onUpdateSchedule?:(id:string,s:{courtNo:string;matchDate:string;startTime:string;endTime:string})=>Promise<void>}){
 const [tab,setTab]=useState('results'),[printError,setPrintError]=useState('');const printRef=useRef<HTMLDivElement>(null);const exportRef=useRef<()=>void>(()=>{});
 const open=(m:MatchEntry)=>{const actual=matches.find(x=>x.id===m.id);if(actual&&!m.resultDisabled)onResult?.(actual);};
 return <div><ActionMessage message={printError}/><div className="flex flex-wrap justify-between items-start gap-3 mb-4"><AdminTabs tabs={[{key:'draw',label:'Bracket View'},{key:'results',label:'Table View'}]} activeKey={tab} onChange={setTab}/><FixtureOptions items={[...options,...(tab==='results'?[{label:'Export Match Results (Selected Round)',onClick:()=>exportRef.current()}]:[]),{label:'Print / Save PDF',disabled:!matches.length,onClick:()=>setPrintError(printFixture(printRef,view.eventInfo.name,view.timeZone)||'')}]}/></div><div ref={printRef}>{view.plans.filter(p=>matches.some(m=>m.programId===p.programId)).map(p=>{
  const bracket=asBracket(view,p,matches.filter(m=>m.programId===p.programId));const program=view.programs.find(x=>x.id===p.programId)!;
  return <section key={p.programId} className="mb-8"><h2 className="font-bold text-sm mb-4">{program.name}</h2>{tab==='results'?<ResultsTab hideExport onExportReady={run=>{exportRef.current=run;}} bracketState={bracket} eventName={view.eventInfo.name} programName={program.name} onOpenScore={open} readOnly={!onResult} onClear={onClear?m=>{const actual=matches.find(x=>x.id===m.id);if(actual)onClear(actual);}:undefined} onPreset={onPreset?m=>{const actual=matches.find(x=>x.id===m.id);if(actual)onPreset(actual);}:undefined} onOpenSchedule={onSchedule?m=>{const actual=matches.find(x=>x.id===m.id);if(actual)onSchedule(actual);}:undefined} onUpdateSchedule={onUpdateSchedule||(async()=>{})}/>:<><div className="grid md:grid-cols-2 gap-4 mb-5">{bracket.groups.map(g=><GroupStandingsTable key={g.id} group={g} advancePerGroup={p.format==='RR'?g.teams.length:p.advancePerGroup}/>)}</div>{p.format!=='RR'&&<BracketView bracketState={bracket} onOpenScore={onResult?open:undefined}/>}</>}</section>;
 })}{!matches.length&&<p className="text-center py-12 text-sm opacity-40">No matches generated yet.</p>}</div></div>;
}

