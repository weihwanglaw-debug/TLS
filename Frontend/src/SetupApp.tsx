import EventRemovalDialog from './EventRemovalDialog';
import {useAdminRoute,type AdminPage} from './admin-route';
import FixtureWorkspace from './FixtureWorkspace';
import SbaRankings from './SbaRankings';
import ActionMessage from './ActionMessage';
import GameSettingPresets from './GameSettingPresets';
import LiveDashboard from './LiveDashboard';
import EventDateBadge from './EventDateBadge';
import {useEffect,useState} from 'react';
import {ArrowLeft,Download,RefreshCw,Upload} from 'lucide-react';
import {api} from './api';
import './setup.css';
import AdminLayout from './AdminLayout';
import MasterConfig from './MasterConfig';
import EventImport from './EventImport';
import {EventDetails,EventList,FixtureList,ParticipantList} from './FoundationPages';
import type {View} from './setup-types';

const titles:Record<string,string>={sba:'SBA Rankings',dashboard:'Dashboard',events:'Event',participants:'Participant',fixtures:'Fixture and Result',settings:'Master Setting',presets:'Game Setting Preset',users:'User Management'};
export default function SetupApp() {
  const {route,go}=useAdminRoute();
  const {page}=route,selected=(page==='events'||page==='fixtures')?route.eventId||'':'',fixtureProgram=page==='fixtures'&&selected?route.programId||'':'',fixtureStep=route.step;
  const seedingId=fixtureStep==='seeding'?fixtureProgram:'',scheduleEvent=fixtureStep==='schedule';
  const participantFilter={eventId:page==='participants'?route.eventId||'':'',programId:page==='participants'?route.programId||'':''};
  const importKind=route.importKind||null,replaceId=route.replaceId||'';
  const [view,setView]=useState<View>();
  const [busy,setBusy]=useState(false),[error,setError]=useState(''),[message,setMessage]=useState(''),[refreshCount,setRefresh]=useState(0);
  const [zones,setZones]=useState<{id:string;name:string}[]>([]);
  const [theme,setTheme]=useState(localStorage.getItem('livescore_theme')||'a');
  async function work(action:()=>Promise<void>) {setBusy(true);setError('');setMessage('');try{await action();}catch(e){setError((e as Error).message.replace(/TRS JSON/g,'TRS event data').replace(/JSON structure/g,'event information'));}finally{setBusy(false);}}
  useEffect(()=>{void work(async()=>{setView(await api<View>('setup/'));setZones(await api('setup/zones'));});},[]);
  useEffect(()=>{document.body.dataset.theme=theme;localStorage.setItem('livescore_theme',theme);},[theme]);
  function navigate(next:string){go({page:next as AdminPage});setError('');setMessage('');}
  function openSchedule(eventId:string){go({page:'fixtures',eventId,step:'schedule'});}
  function openParticipants(eventId:string,programId:string){go({page:'participants',eventId,programId});}
  function openFixtures(eventId:string,programId:string){go({page:'fixtures',eventId,programId});}
  function openSeeding(eventId:string,programId:string){go({page:'fixtures',eventId,programId,step:'seeding'});}
  function beginImport(kind:'excel'|'trs',replacement=''){go({page:'events',importKind:kind,replaceId:replacement||undefined});setError('');setMessage('');}
  const event=view?.data.events.find(e=>e.id===selected),metadata=view?.data.imports.find(i=>i.eventId===selected);
  const program=view?.data.programs.find(p=>p.id===fixtureProgram);
  const missing=page==='not-found'||!!view&&((!!selected&&!event)||(!!route.replaceId&&!view.data.events.some(e=>e.id===route.replaceId))||(!!route.programId&&!view.data.programs.some(p=>p.id===route.programId&&(!route.eventId||p.eventId===route.eventId)))||(!!route.personId&&!view.data.participants.some(p=>p.id===route.personId))||(!!route.eventId&&page==='participants'&&!view.data.events.some(e=>e.id===route.eventId))||(!!route.filterEvent&&!view.data.events.some(e=>e.id===route.filterEvent)));
  const title=missing?'Page not available':page==='fixtures'&&event?event.name+' — Fixture Planning':program?program.name+(seedingId?' — Seeding':' — Fixture and Result'):importKind?(replaceId?'Replace Event Preparation':importKind==='excel'?'Import Excel':'Get from TRS'):event?event.name+(scheduleEvent?' — Schedule':''):titles[page];
  return <AdminLayout page={page} busy={busy} onNavigate={navigate} theme={theme} onToggleTheme={()=>setTheme(theme==='a'?'b':'a')} appName={view?.data.appName||'TLS'} logoUrl={view?.data.logoDarkUrl||view?.data.logoLightUrl||''}>
    {page!=='sba'&&<div className="flex flex-wrap items-center justify-between gap-4 mb-8"><div className="flex flex-wrap items-center gap-3"><div className="admin-page-title" style={{marginBottom:0}}><h1>{title}</h1></div>{event&&!importKind&&view&&<EventDateBadge event={event} today={view.data.today}/>}</div><div className="flex flex-wrap gap-3">
      {page==='events'&&!selected&&!importKind&&<><button disabled={busy} className="btn-outline flex items-center gap-2 px-5 py-2.5 text-sm font-semibold" onClick={()=>beginImport('trs')}><Download className="h-4 w-4"/>Get from TRS</button><button disabled={busy} className="btn-primary flex items-center gap-2 px-5 py-2.5 text-sm font-semibold" onClick={()=>beginImport('excel')}><Upload className="h-4 w-4"/>Import Excel</button></>}
      <button className="btn-outline flex items-center gap-2 px-4 py-2.5 text-sm font-medium" disabled={busy} onClick={()=>void work(async()=>{setView(await api<View>('setup/'));setRefresh(n=>n+1);})}><RefreshCw className="h-4 w-4"/>Refresh</button>
    </div></div>}
    <ActionMessage message={error}/><ActionMessage message={message} variant="success"/>
    {!view?<p>{busy?'Loading...':'Cannot connect to the local server. Refresh to try again.'}</p>:missing?<><p>This address is invalid or the selected record is no longer available.</p><button className="btn-outline action mt-4" onClick={()=>navigate('dashboard')}>Go to Dashboard</button></>:<>
      {page==='dashboard'&&<LiveDashboard key={refreshCount} onEventSettings={eventId=>go({page:'events',eventId})} onPresets={()=>navigate('presets')} onFixtures={openFixtures} onChanged={async()=>setView(await api<View>('setup/'))}/>}
      {page==='events'&&(importKind?<EventImport key={importKind+':'+replaceId} view={view} kind={importKind} replaceId={replaceId} busy={busy} work={work} onBack={()=>{go({page:'events',eventId:replaceId||undefined});setError('');}} onDone={(next,id)=>{setView(next);go({page:'events',eventId:id});const imported=next.data.events.find(e=>e.id===id);setMessage(`${imported?.name||'Event'} imported successfully. ${next.data.programs.filter(p=>p.eventId===id).length} programs · ${next.data.entries.filter(e=>e.eventId===id).length} entries · ${next.data.participants.filter(p=>p.eventId===id).length} participants.`);}}/>:selected&&event?<><div className="flex flex-wrap justify-between gap-3 mb-6"><button disabled={busy} className="btn-outline flex gap-2 items-center px-4 py-2 text-sm" onClick={()=>navigate('events')}><ArrowLeft className="h-4 w-4"/>Back to events</button><div className="flex flex-wrap gap-3"><EventRemovalDialog key={selected} eventId={selected} onRemoved={(next,name)=>{setView(next);go({page:'events'});setError('');setMessage(name+' removed successfully.');}}/>{metadata?.stage==='P'&&<button disabled={busy} className="btn-outline px-4 py-2 text-sm" onClick={()=>beginImport(metadata.source==='TRS'?'trs':'excel',selected)}>Replace Preparation</button>}</div></div><EventDetails key={selected} view={view} eventId={selected} onParticipants={openParticipants} onFixtures={openFixtures} onChanged={async()=>setView(await api<View>('setup/'))}/></>:<EventList view={view} onOpen={eventId=>go({page:'events',eventId})}/>)}
      {page==='participants'&&<ParticipantList key={participantFilter.eventId+":"+participantFilter.programId} view={view} eventId={participantFilter.eventId} programId={participantFilter.programId} personId={route.personId} onFilters={(eventId,programId)=>go({page:'participants',eventId:eventId||undefined,programId:programId||undefined})} onPerson={personId=>go({...route,personId:personId||undefined})}/>}
      {page==='fixtures'&&(selected&&event?<FixtureWorkspace onContextChange={(programId,step)=>go({...route,programId,step},!route.programId||!route.step)} key={selected+':'+refreshCount} eventId={selected} programId={fixtureProgram} initialStep={scheduleEvent?'schedule':seedingId?'seeding':fixtureStep} onChanged={async()=>setView(await api<View>('setup/'))} onPresets={()=>navigate('presets')} onBack={()=>go({page:'fixtures',filterEvent:selected})}/>:<FixtureList key={(route.filterEvent||'')+':'+(route.programId||'')} initialEventId={route.filterEvent} initialProgramId={route.programId} onFilters={(filterEvent,programId)=>go({page:'fixtures',filterEvent:filterEvent||undefined,programId:programId||undefined})} view={view} onManage={openFixtures} onSeeding={openSeeding} onSchedule={openSchedule}/>)}
      {page==='sba'&&<SbaRankings key={refreshCount}/>}
      {page==='presets'&&<GameSettingPresets/>}
      {page==='settings'&&<MasterConfig events={view.data.events} onSaveDisplay={async liveDisplay=>{await work(async()=>{await api('setup/settings','PUT',{revision:view.data.revision,timeZone:view.data.timeZone,liveDisplay});setView(await api<View>('setup/'));setMessage('Live display settings saved.');});}} values={view.data} zones={zones} busy={busy} onSave={async(key,value)=>{let saved=false;await work(async()=>{await api('setup/settings','PUT',{revision:view.data.revision,timeZone:view.data.timeZone,...{[key]:key==='courtCount'?Number(value):value}});setView(await api<View>('setup/'));setMessage('Setting saved.');saved=true;});return saved;}}/>}
    </>}
  </AdminLayout>;
}
