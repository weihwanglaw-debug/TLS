import {afterEach,expect,test} from 'vitest';
import {cleanup,fireEvent,render,screen,waitFor,within} from '@testing-library/react';
import FixtureViews from '../src/FixtureViews';
import FixtureWorkspace from '../src/FixtureWorkspace';
import TournamentConsole from '../src/TournamentConsole';
import EventSchedule from '../src/EventSchedule';
import EventSettings from '../src/EventSettings';
import GameSettingPresets from '../src/GameSettingPresets';
import LiveDashboard from '../src/LiveDashboard';
import UmpireApp from '../src/UmpireApp';
import SeedingPage from '../src/SeedingPage';
import {EventDetails} from '../src/FoundationPages';

// All HTTP calls go to the disposable SQL-backed API started by the Python runner.
const base=process.env.TLS_UI_TEST_URL;
const eventId=process.env.TLS_UI_EVENT!;
const programId=process.env.TLS_UI_PROGRAM!;
if(!base||!eventId||!programId)throw new Error('Run tests/frontend_workflow_integration.py to create an isolated SQL test event.');
const network=globalThis.fetch;
globalThis.fetch=(input,init)=>{
 // jsdom's AbortSignal belongs to another realm; the API runner bounds requests/tests.
 const {signal:_,...options}=init||{};
 return network(new URL(String(input),base).href,options);
};
afterEach(cleanup);
const done=async()=>{};
function chooseDate(label:string,value:string){
 fireEvent.click(screen.getByRole('button',{name:label,exact:true}));
 const dialog=screen.getByRole('dialog',{name:label,exact:true});
 const [year,month,day]=value.split('-').map(Number);
 fireEvent.change(dialog.querySelector('select[name="years"]')!,{target:{value:String(year)}});
 fireEvent.change(dialog.querySelector('select[name="months"]')!,{target:{value:String(month-1)}});
 const cell=[...dialog.querySelectorAll<HTMLButtonElement>('button[name="day"]')].find(b=>b.textContent===String(day)&&!b.classList.contains('day-outside'))!;
 fireEvent.click(cell);
}

// jsdom does not implement the browser's dialog methods.
HTMLDialogElement.prototype.showModal=function(){this.setAttribute('open','');};
HTMLDialogElement.prototype.close=function(){this.removeAttribute('open');};
globalThis.ResizeObserver=class {observe(){}unobserve(){}disconnect(){}} as unknown as typeof ResizeObserver;

test('event settings contain officials without court selection',async()=>{
 const {container}=render(<EventSettings eventId={eventId} onChanged={done}/>);
 await screen.findByRole('group',{name:'Event Operating Settings'});
 expect(container.querySelectorAll('input[type=date]')).toHaveLength(0);
 expect(container.querySelectorAll('input[type=time]')).toHaveLength(0);
 expect(screen.queryByText('Playing Sessions')).toBeNull();expect(screen.getByText('Match Timing')).toBeTruthy();fireEvent.change(screen.getByLabelText('Estimated match minutes'),{target:{value:'40'}});fireEvent.change(screen.getByLabelText('Minimum player rest minutes'),{target:{value:'120'}});expect(screen.queryByText('Round Progression')).toBeNull();
 expect(screen.queryByText('Courts with line-call review equipment')).toBeNull();
 expect(screen.queryByText('Event Courts')).toBeNull();expect(screen.queryByText('Use All Courts')).toBeNull();expect(screen.queryByRole('switch')).toBeNull();
 expect(screen.getAllByLabelText('Official name')).toHaveLength(4);const inputs=screen.getAllByLabelText('Official name') as HTMLInputElement[];const original=inputs[1].value;fireEvent.change(inputs[1],{target:{value:inputs[0].value}});
 fireEvent.click(screen.getByRole('button',{name:'Save Event Settings'}));
 await screen.findByRole('dialog',{name:'Unable to complete action'});
 fireEvent.click(screen.getByRole('button',{name:'OK'}));
 fireEvent.change(inputs[1],{target:{value:original}});
 fireEvent.click(screen.getByRole('button',{name:'Save Event Settings'}));
 await screen.findByRole('dialog',{name:'Saved successfully'});
 fireEvent.click(screen.getByRole('button',{name:'OK'}));
});

test('event dates use a separate save panel before fixtures and refresh the displayed dates',async()=>{
 const initial=await (await fetch('/api/setup/')).json();const original=initial.data.events.find((e:{id:string})=>e.id===eventId);
 let page:ReturnType<typeof render>;
 const refreshed=async()=>{const view=await (await fetch('/api/setup/')).json();page.rerender(<EventDetails view={view} eventId={eventId} onChanged={refreshed}/>);};
 page=render(<EventDetails view={initial} eventId={eventId} onChanged={refreshed}/>);
 const edit=await screen.findByRole('button',{name:'Edit Dates'});fireEvent.click(edit);
 await screen.findByRole('button',{name:'Save Dates'});
 const later=new Date(original.endDate+'T00:00:00Z');later.setUTCDate(later.getUTCDate()+1);
 chooseDate('End Date',later.toISOString().slice(0,10));
 fireEvent.click(screen.getByRole('button',{name:'Save Dates'}));
 await screen.findByRole('dialog',{name:'Saved successfully'});fireEvent.click(screen.getByRole('button',{name:'OK',exact:true}));
 const saved=await (await fetch('/api/setup/')).json();expect(saved.data.events.find((e:{id:string})=>e.id===eventId).endDate).toBe(later.toISOString().slice(0,10));
 fireEvent.click(screen.getByRole('button',{name:'Edit Dates'}));await screen.findByRole('button',{name:'Save Dates'});
 chooseDate('End Date',original.endDate);fireEvent.click(screen.getByRole('button',{name:'Save Dates'}));
 await screen.findByRole('dialog',{name:'Saved successfully'});fireEvent.click(screen.getByRole('button',{name:'OK',exact:true}));
 expect((await (await fetch('/api/setup/')).json()).data.events.find((e:{id:string})=>e.id===eventId).endDate).toBe(original.endDate);
});

test('seeding is reviewed separately, including explicit no-seed and chosen manual seed count',async()=>{
 render(<SeedingPage programId={programId} onChanged={done} onBack={()=>{}}/>);
 await screen.findByText('2 seeded entries');
 expect((screen.getByRole('button',{name:'Suggest Seeds from SBA'}) as HTMLButtonElement).disabled).toBe(true);expect(screen.getByRole('switch',{name:'Use Seeding'}).getAttribute('aria-checked')).toBe('true');
 fireEvent.click(screen.getByRole('switch',{name:'Use Seeding'}));
 fireEvent.click(screen.getByRole('button',{name:'Confirm No Seeding Required'}));
 await screen.findByRole('dialog',{name:'Saved successfully'});fireEvent.click(screen.getByRole('button',{name:'OK'}));
 expect(screen.queryByLabelText('Number of Seeds')).toBeNull();expect(screen.queryByRole('table')).toBeNull();
 const unseeded=await (await fetch('/api/tournament/'+eventId)).json();expect(unseeded.plans[0].seedsReviewed).toBe(true);expect(Object.values(unseeded.plans[0].seeds).every(x=>x===null)).toBe(true);
 fireEvent.click(screen.getByRole('switch',{name:'Use Seeding'}));fireEvent.change(screen.getByLabelText('Number of Seeds'),{target:{value:'2'}});const fields=screen.getAllByRole('spinbutton');fireEvent.change(fields[1],{target:{value:'1'}});fireEvent.change(fields[2],{target:{value:'2'}});fireEvent.click(screen.getByRole('button',{name:'Save Reviewed Seeding'}));
 await screen.findByRole('dialog',{name:'Saved successfully'});fireEvent.click(screen.getByRole('button',{name:'OK'}));expect(screen.getByText('2 seeded entries')).toBeTruthy();
});

test('draw requires an active default, configured through the master before generating fixtures',async()=>{
 const before=await (await fetch('/api/tournament/'+eventId)).json();render(<TournamentConsole eventId={eventId} programId={programId} onChanged={done} onEventSettings={()=>{}} onPresets={()=>{}}/>);
 await screen.findByText('Set an active default in Game Setting Preset before generating a draw.');expect((screen.getByRole('button',{name:/Preview Draw/}) as HTMLButtonElement).disabled).toBe(true);expect(screen.getByRole('button',{name:'Open Game Setting Preset'})).toBeTruthy();expect(screen.queryByText('Round Game Presets')).toBeNull();expect((await (await fetch('/api/tournament/'+eventId)).json()).revision).toBe(before.revision);
 cleanup();render(<GameSettingPresets/>);
 await screen.findByText('No presets yet. Create one before assigning scoring rules to a match.');
 fireEvent.click(screen.getByRole('button',{name:'Create Preset'}));
 fireEvent.change(screen.getByLabelText('Preset name'),{target:{value:'UI standard rules'}});
 fireEvent.click(screen.getByRole('switch',{name:'Default preset'}));fireEvent.click(screen.getByRole('button',{name:'Save Preset'}));
 await screen.findByText('UI standard rules',{exact:true});
 const saved=await (await fetch('/api/presets/')).json();expect(saved.presets[0].isDefault).toBe(true);
});

test('guided planning preserves the program between seeding, draw and event scheduling',async()=>{
 const before=await (await fetch('/api/tournament/'+eventId)).json();render(<FixtureWorkspace eventId={eventId} programId={programId} initialStep="seeding" onBack={()=>{}} onChanged={done} onPresets={()=>{}}/>);
 await screen.findByRole('button',{name:'Continue to Draw'});const steps=screen.getByRole('navigation',{name:'Fixture planning steps'});
 fireEvent.click(screen.getByRole('button',{name:'Continue to Draw'}));await screen.findByText('Game Format');expect((screen.getByLabelText('Planning program') as HTMLSelectElement).value).toBe(programId);expect(within(steps).getByRole('button',{name:/2\. Draw/}).getAttribute('aria-current')).toBe('step');
 fireEvent.click(within(steps).getByRole('button',{name:/3\. Schedule/}));await screen.findByLabelText('Schedule date');expect((screen.getByLabelText('Planning program') as HTMLSelectElement).disabled).toBe(true);expect(screen.getByRole('button',{name:'Program Fixture List'})).toBeTruthy();expect(screen.queryByText('Choose the date to plan its sessions, courts and program rounds.')).toBeNull();
 fireEvent.click(screen.getByRole('button',{name:'Back to Draw'}));await screen.findByText('Game Format');expect((screen.getByLabelText('Planning program') as HTMLSelectElement).value).toBe(programId);expect((await (await fetch('/api/tournament/'+eventId)).json()).revision).toBe(before.revision);
});

test('TRS sequence: overview, configure, swap preview, Back retains selection, confirm, bracket/table',async()=>{
 render(<TournamentConsole eventId={eventId} programId={programId} onChanged={done} onEventSettings={()=>{}} onPresets={()=>{}}/>);
 await screen.findByText('Game Format');expect(screen.queryByText('No fixture generated for this program.')).toBeNull();
 expect(screen.queryByText('BADMINTON SCORING PROFILE')).toBeNull();

 await screen.findByText('Game Format');
 fireEvent.click(screen.getByText('Knockout',{exact:true}));
 expect(screen.queryByText('Number of Seeds')).toBeNull();
 expect(screen.queryByLabelText('Estimated match minutes')).toBeNull();expect(screen.queryByText('Courts and timing for this round')).toBeNull();
 const heats=screen.getByText('Heats',{exact:true}).closest('button')!;
 expect(heats.disabled).toBe(true);
 expect(screen.queryByText('Playing Sessions')).toBeNull();
 fireEvent.click(screen.getByRole('button',{name:/Preview Draw/}));
 await screen.findByRole('button',{name:/Confirm & Save/});
 const before=await (await fetch('/api/tournament/'+eventId)).json();
 expect(before.plans[0].matches).toHaveLength(0);
 const positions=()=>screen.getAllByTitle('Select for swap').map(b=>b.closest('td')!.querySelector('span.font-medium')!.textContent);
 async function swapFirstPair(){const original=positions();const swaps=screen.getAllByTitle('Select for swap');fireEvent.click(swaps[0]);fireEvent.click(screen.getAllByTitle('Select for swap')[0]);await waitFor(()=>{expect(positions()[0]).toBe(original[1]);expect(positions()[1]).toBe(original[0]);},{timeout:5000});expect(screen.queryByRole('dialog')).toBeNull();expect(screen.queryByText('Draw positions updated')).toBeNull();}
 await swapFirstPair();
 const unsaved=await (await fetch('/api/tournament/'+eventId)).json();expect(unsaved.revision).toBe(before.revision);expect(unsaved.plans[0].matches).toHaveLength(0);
 fireEvent.click(screen.getByRole('button',{name:'Back',exact:true}));
 await screen.findByText('Game Format');expect((await (await fetch('/api/tournament/'+eventId)).json()).plans[0].matches).toHaveLength(0);
 expect(screen.queryByLabelText('Estimated match minutes')).toBeNull();
 expect(screen.getByText('Knockout',{exact:true})).toBeTruthy();
 fireEvent.click(screen.getByRole('button',{name:/Preview Draw/}));
 await screen.findByRole('button',{name:/Confirm & Save/});
 await swapFirstPair();const finalPositions=positions();
 fireEvent.click(screen.getByRole('button',{name:/Confirm & Save/}));
 await screen.findByText('Saved Draw',{exact:true});
 await waitFor(()=>expect(screen.queryByRole('button',{name:/Confirm & Save/})).toBeNull());
 expect(screen.queryByText('Table View')).toBeNull();expect(screen.queryByTitle('Select for swap')).toBeNull();
 const saved=await (await fetch('/api/tournament/'+eventId)).json();
 const savedNames=saved.plans[0].matches.flatMap((m:{entryA:string;entryB:string})=>[m.entryA,m.entryB]).map((id:string)=>saved.entries.find((e:{id:string})=>e.id===id).participantIds.map((id:string)=>saved.participants.find((p:{id:string})=>p.id===id).name).join(' / '));expect(savedNames).toEqual(finalPositions);
 expect(saved.plans[0].format).toBe('KO');
 expect(saved.plans[0].matches.length).toBeGreaterThan(0);
 expect(saved.plans[0].matches.every((m:{rulesAssigned:boolean;start:string|null;court:number|null})=>m.rulesAssigned&&!m.start&&!m.court)).toBe(true);
});

test('saved draw applies to another reviewed program through one dialog and one confirmation',async()=>{
 const initial=await (await fetch('/api/tournament/'+eventId)).json();const other=initial.programs.find((p:{id:string})=>p.id!==programId);const before=initial.plans.find((p:{programId:string})=>p.programId===programId);
 render(<TournamentConsole eventId={eventId} programId={programId} onChanged={done} onEventSettings={()=>{}} onPresets={()=>{}}/>);fireEvent.click(await screen.findByRole('button',{name:'Apply Draw Settings to Other Programs'}));await screen.findByText('Seeding not reviewed');expect((screen.getByRole('switch',{name:other.name}) as HTMLButtonElement).disabled).toBe(true);fireEvent.click(screen.getByRole('button',{name:'Cancel',exact:true}));
 const entries=initial.entries.filter((e:{programId:string})=>e.programId===other.id);const review=await fetch('/api/seeding/'+other.id,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({revision:initial.revision,mode:'N',seedCount:0,seeds:Object.fromEntries(entries.map((e:{id:string})=>[e.id,null]))})});expect(review.ok).toBe(true);
 fireEvent.click(screen.getByRole('button',{name:'Apply Draw Settings to Other Programs'}));await screen.findByText('Ready');fireEvent.click(screen.getByRole('switch',{name:'Select All Eligible Programs'}));fireEvent.click(screen.getByRole('button',{name:'Confirm & Generate'}));await screen.findByRole('dialog',{name:'Saved successfully'});expect(screen.getByText('Draws generated for 1 program.')).toBeTruthy();
 const saved=await (await fetch('/api/tournament/'+eventId)).json();expect(saved.plans.find((p:{programId:string})=>p.programId===programId)).toEqual(before);const target=saved.plans.find((p:{programId:string})=>p.programId===other.id);expect(target.format).toBe(before.format);expect(target.seedingMode).toBe('N');expect(target.matches.length).toBeGreaterThan(0);expect(target.matches.every((m:{start:string|null;court:number|null})=>!m.start&&!m.court)).toBe(true);expect(screen.queryByRole('button',{name:'Prepare Another Program'})).toBeNull();
});

test('seeding remains viewable and locked after the draw is saved',async()=>{
 render(<SeedingPage programId={programId} onChanged={done} onBack={()=>{}}/>);
 await screen.findByText(/Seeding is locked because a draw exists/);
 expect(screen.queryByRole('button',{name:'Save Reviewed Seeding'})).toBeNull();
 expect((screen.getByRole('switch',{name:'Use Seeding'}) as HTMLButtonElement).disabled||!!screen.getByRole('switch',{name:'Use Seeding'}).closest('fieldset[disabled]')).toBe(true);
});

test('event schedule carries daily defaults and confirms slots separately from the draw',async()=>{
 const v=await (await fetch('/api/tournament/'+eventId)).json();expect(v.eventPlan.matchMinutes).toBe(40);expect(v.eventPlan.restMinutes).toBe(120);
 await fetch('/api/tournament/event',{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({revision:v.revision,plan:{...v.eventPlan,dailySessions:[{start:'00:00:00',end:'12:00:00'},{start:'12:00:00',end:'23:59:00'}]}})});
 render(<EventSchedule eventId={eventId} onChanged={done} onBack={()=>{}}/>);await screen.findByText('Daily Sessions and Courts');await waitFor(()=>expect(screen.queryAllByText('Calculating capacity…')).toHaveLength(0),{timeout:10000});expect(screen.queryByLabelText('Estimated match minutes')).toBeNull();expect(screen.queryByLabelText('Minimum player rest minutes')).toBeNull();expect(screen.getAllByRole('button',{name:/Session [12] (start|end) time/})).toHaveLength(4);
 for(const round of screen.getAllByRole('switch',{name:/Test Singles/})){if(!(round as HTMLButtonElement).disabled){fireEvent.click(round);await waitFor(()=>expect(screen.queryAllByText('Calculating capacity…')).toHaveLength(0),{timeout:10000});}}for(const button of screen.getAllByRole('button',{name:'Save Session'})){if(!(button as HTMLButtonElement).disabled){fireEvent.click(button);await waitFor(()=>expect((button as HTMLButtonElement).disabled).toBe(true));await waitFor(()=>expect(screen.queryAllByText('Calculating capacity…')).toHaveLength(0),{timeout:10000});}}await waitFor(()=>expect((screen.getByRole('button',{name:'Preview Schedule'}) as HTMLButtonElement).disabled).toBe(false),{timeout:10000});fireEvent.click(screen.getByRole('button',{name:'Preview Schedule'}));await screen.findByText('Review Schedule').catch(e=>{throw new Error(screen.queryAllByRole('dialog').map(d=>d.textContent).join(' | ')||e.message);});
 const before=await (await fetch('/api/tournament/'+eventId)).json();expect(before.plans[0].matches.every((m:{start:string|null})=>!m.start)).toBe(true);
 fireEvent.click(screen.getByRole('button',{name:'Confirm Schedule'}));await screen.findByRole('dialog',{name:'Saved successfully'});fireEvent.click(screen.getByRole('button',{name:'OK'}));
 const saved=await (await fetch('/api/tournament/'+eventId)).json();expect(saved.plans[0].matches.every((m:{start:string|null;court:number|null})=>m.start&&m.court)).toBe(true);expect(saved.eventPlan.dailySessions).toHaveLength(0);expect(saved.eventPlan.sessions).toHaveLength(2);
});

test('saved schedule appears in bracket and table, with print grouped under Options',async()=>{
 const saved=await (await fetch('/api/tournament/'+eventId)).json();const matches=saved.plans.find((p:{programId:string})=>p.programId===programId).matches;const m=matches.find((m:{status:string})=>m.status!=='BY');
 render(<FixtureViews view={saved} matches={matches}/>);expect(screen.getByText(m.start.slice(11,16)+' – '+m.end.slice(11,16))).toBeTruthy();fireEvent.click(screen.getByRole('button',{name:'Bracket View'}));expect(await screen.findByText(new RegExp('Court '+m.court+'.*'+m.start.slice(11,16)))).toBeTruthy();
 fireEvent.click(screen.getByRole('button',{name:'Table View'}));expect(screen.getByText(m.start.slice(11,16)+' – '+m.end.slice(11,16))).toBeTruthy();
 fireEvent.click(screen.getByRole('button',{name:'Options'}));expect(screen.getByRole('menuitem',{name:'Print / Save PDF'})).toBeTruthy();expect(screen.queryByRole('button',{name:'Print / Save PDF'})).toBeNull();expect(screen.getByRole('menuitem',{name:'Export Match Results (Selected Round)'})).toBeTruthy();expect(screen.queryByRole('button',{name:'Export CSV'})).toBeNull();
});

test('Edit Dates stays visible but alerts when a fixture exists in any program',async()=>{
 const saved=await (await fetch('/api/setup/')).json();render(<EventDetails view={saved} eventId={eventId} onChanged={done}/>);
 fireEvent.click(await screen.findByRole('button',{name:'Edit Dates'}));await screen.findByRole('dialog',{name:'Unable to complete action'});expect(screen.getByText('Reset Schedule before changing event dates. Started matches and results keep event dates locked.')).toBeTruthy();expect(screen.queryByRole('button',{name:'Save Dates'})).toBeNull();expect(screen.queryByText('Event Courts')).toBeNull();
});

test('scheduled fixtures open the fourth step with Table View and keep the shared program header',async()=>{
 render(<FixtureWorkspace eventId={eventId} programId={programId} onChanged={done} onBack={()=>{}} onPresets={()=>{}}/>);
 await screen.findByRole('button',{name:'Table View'});const steps=screen.getByRole('navigation',{name:'Fixture planning steps'});expect(within(steps).getAllByRole('button')).toHaveLength(4);expect(within(steps).getByRole('button',{name:/4\. Fixture & Results/}).getAttribute('aria-current')).toBe('step');expect(screen.queryByText('Internal')).toBeNull();expect(screen.getByRole('region',{name:'Program details'})).toBeTruthy();
 fireEvent.click(within(steps).getByRole('button',{name:/1\. Seeding/}));await screen.findByText(/Seeding is locked/);expect(screen.getByRole('switch',{name:'Use Seeding'}).getAttribute('aria-checked')).toBe('true');expect(screen.queryByRole('heading',{name:'Test Singles'})).toBeNull();expect((screen.getByLabelText('Planning program') as HTMLSelectElement).selectedOptions[0].textContent).toBe('Test Singles');
 fireEvent.click(within(steps).getByRole('button',{name:/2\. Draw/}));await screen.findByText('Saved Draw',{exact:true});expect(screen.queryByText('Round Game Presets')).toBeNull();expect(screen.queryByTitle('Select for swap')).toBeNull();expect(screen.queryByText('Bracket View')).toBeNull();
});

test('standalone preset is selected before dashboard pairing code issuance',async()=>{
 render(<GameSettingPresets/>);
 await screen.findByText('UI standard rules',{exact:true});
 cleanup();
 render(<LiveDashboard onFixtures={()=>{}} onChanged={done}/>);
 await screen.findByText(/Match Queue ·/);
 fireEvent.click(screen.getAllByRole('button',{name:/Assign to Court/}).find(b=>!(b as HTMLButtonElement).disabled)!);
 const select=await screen.findByLabelText('Game preset');
 expect((select as HTMLSelectElement).value).not.toBe('');expect((select as HTMLSelectElement).disabled).toBe(true);
 expect((select as HTMLSelectElement).required).toBe(true);
 const presets=await (await fetch('/api/presets/')).json();
 fireEvent.change(select,{target:{value:presets.presets[0].id}});
 const officials=await (await fetch('/api/tournament/'+eventId)).json();fireEvent.change(screen.getByLabelText('Official 1'),{target:{value:officials.eventPlan.officials[0].id}});fireEvent.click(screen.getByRole('button',{name:'Assign & Generate Code'}));
 await screen.findByText(/Connect Tablet · Match #/);
 const saved=await (await fetch('/api/tournament/'+eventId)).json();
 expect(saved.plans[0].matches[0].rulesAssigned).toBe(true);
 expect(saved.plans[0].matches[0].rules.pointsToWin).toBe(21);
});

test('Programs is the final section in Event Details',async()=>{
 const view=await (await fetch('/api/setup/')).json();
 render(<EventDetails view={view} eventId={eventId} onChanged={done}/>);
 await screen.findByRole('group',{name:'Event Operating Settings'});
 const headings=screen.getAllByRole('heading').map(h=>h.textContent);
 expect(headings[headings.length-1]).toBe('Programs');
});

test('umpire uses saved service setup, point acknowledgements, scoresheet and paper handover',async()=>{
 render(<LiveDashboard onFixtures={()=>{}} onChanged={done}/>);
 await screen.findByText(/Match Queue ·/);
 fireEvent.click(screen.getAllByRole('button',{name:/Assign to Court/}).find(b=>!(b as HTMLButtonElement).disabled)!);
 const presets=await (await fetch('/api/presets/')).json();
 fireEvent.change(await screen.findByLabelText('Game preset'),{target:{value:presets.presets[0].id}});
 const officials=await (await fetch('/api/tournament/'+eventId)).json();fireEvent.change(screen.getByLabelText('Official 1'),{target:{value:officials.eventPlan.officials[0].id}});fireEvent.click(screen.getByRole('button',{name:'Assign & Generate Code'}));
 await screen.findByText(/Connect Tablet · Match #/);
 const code=document.querySelector('.live-access-code')!.textContent;
 const result=await (await fetch('/api/tablet/pair',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({code,deviceId:'react-umpire-device',deviceName:'React umpire tablet'})})).json();
 expect(result.token).toBeTruthy();
 localStorage.setItem('tls-umpire-match',JSON.stringify({token:result.token,view:result.match}));
 cleanup();window.history.replaceState(null,'','/umpire/'+result.match.sessionId);render(<UmpireApp/>);
 await screen.findByText('Saved to venue');
 fireEvent.click(screen.getByRole('button',{name:'Set up & start match'}));
 expect(document.querySelector('.umpire-dialog select')).toBeNull();
 expect(screen.getAllByText('Automatically selected')).toHaveLength(2);
 const servingGroup=screen.getByRole('group',{name:'Serving side'});
 fireEvent.click(within(servingGroup).getAllByRole('button')[1]);
 expect(within(servingGroup).getAllByRole('button')[1].getAttribute('aria-pressed')).toBe('true');
 fireEvent.click(screen.getByRole('button',{name:'Cancel',exact:true}));
 expect(screen.queryByRole('dialog')).toBeNull();
 const beforeStart=await (await fetch('/api/dashboard/')).json();
 expect(beforeStart.courts.find((c:any)=>c.match?.id===result.match.match.id).match.status).toBe('SC');
 fireEvent.click(screen.getByRole('button',{name:'Set up & start match'}));
 fireEvent.click(screen.getByRole('button',{name:'Confirm & Start Match'}));
 await waitFor(()=>expect(screen.queryByRole('button',{name:'Start match',exact:true})||!screen.queryByRole('dialog')).toBeTruthy());
 const early=screen.queryByRole('button',{name:'Start match',exact:true});if(early)fireEvent.click(early);
 await waitFor(()=>expect(screen.queryByRole('dialog')).toBeNull());
 expect(screen.getByText('Server')).toBeTruthy();expect(screen.getByText('Receiver')).toBeTruthy();
 fireEvent.click(screen.getByRole('button',{name:'Add point to side A'}));
 await waitFor(()=>expect(screen.getByLabelText('Side A score').textContent).toBe('1'));
 fireEvent.click(screen.getByRole('button',{name:'Scoresheet',exact:true}));
 await screen.findByRole('dialog',{name:'Scoresheet'});expect(screen.getByLabelText('Rally 1: 1–0, side A: 1')).toBeTruthy();
 fireEvent.click(screen.getByRole('button',{name:'Close'}));
 fireEvent.click(screen.getByRole('button',{name:'Continue on paper',exact:true}));
 fireEvent.click(screen.getByRole('button',{name:'Confirm Paper Mode'}));
 await screen.findByText('Paper mode');
 const dashboard=await (await fetch('/api/dashboard/')).json();expect(dashboard.courts.find((c:any)=>c.match?.id===result.match.match.id).status).toBe('P');
 expect(dashboard.courts.find((c:any)=>c.match?.id===result.match.match.id).match.paperMode).toBe(true);
 localStorage.removeItem('tls-umpire-match');
});

test('dashboard requires one event, remembers selection and does not mix event queues',async()=>{
 const source=await (await fetch('/api/setup/')).json();const data=source.data;
 const event=data.events.find((e:{id:string})=>e.id===eventId);
 const after=new Date(event.endDate+'T00:00:00Z');after.setUTCDate(after.getUTCDate()+2);const secondDate=after.toISOString().slice(0,10);
 const payload={schemaVersion:2,source:'TRS',sourceSystemId:'ui-dashboard-selection',sourceTimeZone:data.timeZone,exportedAt:new Date().toISOString(),registrationClosed:true,hasFixtures:false,
  events:[{...event,id:'SECOND',name:'Second dashboard event',startDate:secondDate,endDate:secondDate}],
  programs:data.programs.filter((p:{eventId:string})=>p.eventId===eventId).map((p:object)=>({...p,eventId:'SECOND'})),
  participants:data.participants.filter((p:{eventId:string})=>p.eventId===eventId).map((p:object)=>({...p,eventId:'SECOND'})),
  entries:data.entries.filter((p:{eventId:string})=>p.eventId===eventId).map((p:object)=>({...p,eventId:'SECOND'}))};
 const previewResponse=await fetch('/api/setup/preview?kind=trs&revision='+data.revision,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)});
 expect(previewResponse.ok).toBe(true);const preview=await previewResponse.json();
 const confirmed=await (await fetch('/api/setup/confirm',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({revision:preview.revision,token:preview.token})})).json();
 const second=confirmed.data.events.find((e:{name:string})=>e.name==='Second dashboard event');expect(second).toBeTruthy();
 localStorage.removeItem('tls-dashboard-event');
 render(<LiveDashboard onFixtures={()=>{}} onChanged={done}/>);
 const select=await screen.findByLabelText('Dashboard event');
 expect((select as HTMLSelectElement).value).toBe('');
 expect(screen.queryByRole('option',{name:'All Events'})).toBeNull();
 expect(screen.getByText('Select an event to view its dashboard.')).toBeTruthy();
 fireEvent.change(select,{target:{value:eventId}});
 await screen.findByText(/Match Queue ·/);
 fireEvent.change(select,{target:{value:second.id}});
 await screen.findByText('Match Queue · 0 Waiting');
 expect(screen.getAllByText('Fixture required')).toHaveLength(2);
 expect(localStorage.getItem('tls-dashboard-event')).toBe(second.id);
 const revision=(await (await fetch('/api/setup/')).json()).data.revision;
 cleanup();render(<LiveDashboard onFixtures={()=>{}} onChanged={done}/>);
 await screen.findByText('Match Queue · 0 Waiting');
 expect((screen.getByLabelText('Dashboard event') as HTMLSelectElement).value).toBe(second.id);
 expect((await (await fetch('/api/setup/')).json()).data.revision).toBe(revision);
 localStorage.removeItem('tls-dashboard-event');
});
