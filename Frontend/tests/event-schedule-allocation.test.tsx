import {capacityReply} from './schedule-capacity-fixture';
import {cleanup,fireEvent,render,screen,waitFor,within} from '@testing-library/react';
import {afterEach,beforeEach,expect,test,vi} from 'vitest';
import EventSchedule from '../src/EventSchedule';
import type {Match,View} from '../src/tournament-types';
const service=vi.hoisted(()=>({api:vi.fn(),capacity:vi.fn()}));vi.mock('../src/api',()=>({api:(path:string,...args:any[])=>path==='scheduling/capacity'?service.capacity(args[1]):service.api(path,...args)}));
globalThis.ResizeObserver=class {observe(){}unobserve(){}disconnect(){}} as unknown as typeof ResizeObserver;
let view:View;
const match=(id:string,pid:string,start:string|null=null)=>({id,number:Number(id.slice(1)),programId:pid,phase:'G',round:1,roundLabel:'Group A',groupId:'G1',status:'SC',start,end:start?start.slice(0,11)+'10:40:00':null,court:start?1:null,durationMinutes:40,actualStartedAt:null,games:[],entryA:id+'a',entryB:id+'b'} as Match);
beforeEach(()=>{
 const a=[match('m1','a','2026-10-27T10:00:00'),match('m2','a'),match('m3','a'),match('m4','a')],b=[match('m5','b'),match('m6','b')];
 view={revision:8,eventInfo:{id:'e',startDate:'2026-10-27',endDate:'2026-10-28'},eventCourts:[1,2,3,4],closedCourts:[],calledMatchIds:[],programs:[{id:'a',name:'Program A',status:'A'},{id:'b',name:'Program B',status:'A'}],plans:[{programId:'a',phase:'G',lastRound:1,matches:a},{programId:'b',phase:'G',lastRound:1,matches:b}],eventPlan:{matchMinutes:40,restMinutes:120,dailySessions:[],sessions:[{date:'2026-10-27',start:'10:00:00',end:'12:00:00',courts:[1,2,3,4],matchIds:a.map(m=>m.id)},{date:'2026-10-28',start:'10:00:00',end:'12:00:00',courts:[1,2,3,4],matchIds:[]}],scheduleDays:[]}} as unknown as View;
 service.capacity.mockReset();service.capacity.mockImplementation(async body=>capacityReply(view,body));
 service.api.mockReset();service.api.mockImplementation(async(path:string)=>path.startsWith('tournament/')?structuredClone(view):{token:'preview-token',revision:8,matches:[],unplaced:[],kept:1,sessions:[]});
});
afterEach(cleanup);


async function ready(){await waitFor(()=>expect(screen.queryByText('Calculating capacity…')).toBeNull());}

async function saveSessions(){
 const selector=screen.getByLabelText('Schedule date') as HTMLSelectElement,selected=selector.value;
 for(const option of [...selector.options]){
  fireEvent.change(selector,{target:{value:option.value}});await ready();
  for(const button of screen.queryAllByRole('button',{name:'Save Session',exact:true})){
   if((button as HTMLButtonElement).disabled)continue;
   fireEvent.click(button);await waitFor(()=>expect((button as HTMLButtonElement).disabled).toBe(true));
  }
 }
 fireEvent.change(selector,{target:{value:selected}});
}

const show=()=>render(<EventSchedule eventId="e" onChanged={async()=>{}} onBack={()=>{}}/>);
test('later dates use quantities remaining after earlier requests',async()=>{
 view.eventPlan.sessions[0].programLimits={a:1};
 show();await screen.findByLabelText('Schedule date');await ready();fireEvent.change(screen.getByLabelText('Schedule date'),{target:{value:'2026-10-28'}});
 const a=screen.getByRole('switch',{name:'Program A · Group stage'});expect((a as HTMLButtonElement).disabled).toBe(false);
 expect(screen.getAllByText('2 matches not allocated to sessions')[0]).toBeTruthy();
 fireEvent.click(a);await ready();fireEvent.change(screen.getByLabelText('Session 1 Program A matches'),{target:{value:'2'}});await ready();
 fireEvent.click(screen.getByRole('switch',{name:'Program B · Group stage'}));await ready();fireEvent.change(screen.getByLabelText('Session 1 Program B matches'),{target:{value:'1'}});await ready();
 await waitFor(()=>expect(screen.getByText(/Match capacity for this session:/).textContent).toContain('12'));
 await waitFor(()=>expect((screen.getByRole('button',{name:'Preview Schedule'}) as HTMLButtonElement).disabled).toBe(false));await saveSessions();fireEvent.click(screen.getByRole('button',{name:'Preview Schedule'}));
 await waitFor(()=>expect(service.api).toHaveBeenCalledWith('scheduling/preview','POST',expect.objectContaining({sessions:expect.arrayContaining([expect.objectContaining({date:'2026-10-28',programLimits:{a:2,b:1},matchIds:['m2','m3','m4','m5','m6']})])})));
 const body=service.api.mock.calls.find(c=>c[0]==='scheduling/preview')![2];expect(body.sessions[1].matchIds).not.toContain('m1');expect(body.sessions[0].matchIds).toContain('m1');
});
test('capacity comes from the scheduling response while preview reports compact unscheduled details',async()=>{
 service.api.mockImplementation(async(path:string)=>path.startsWith('tournament/')?structuredClone(view):{token:'t',revision:8,matches:[],unplaced:[{id:'m2',number:2,programId:'a'},{id:'m3',number:3,programId:'a'},{id:'m4',number:4,programId:'a'}],kept:1,sessions:[{index:0,date:'2026-10-27',start:'10:00:00',end:'12:00:00',programs:[{programId:'a',requested:3,scheduled:0}]}]});
 show();await screen.findByLabelText('Schedule date');await ready();await waitFor(()=>expect(screen.getByText(/Match capacity for this session:/).textContent).toContain('12'));
 await waitFor(()=>expect((screen.getByRole('button',{name:'Preview Schedule'}) as HTMLButtonElement).disabled).toBe(false));await saveSessions();fireEvent.click(screen.getByRole('button',{name:'Preview Schedule'}));
 expect(await screen.findByText('Program A · Group stage — 3 matches awaiting scheduling')).toBeTruthy();
 expect(document.querySelectorAll('details')).toHaveLength(1);expect(screen.queryByText(/No space on/)).toBeNull();
});
test('fulfilled quantities do not silently request extra matches when reopening',async()=>{
 view.eventPlan.sessions[0].programLimits={a:0};show();await screen.findByLabelText('Schedule date');await ready();
 expect(screen.getByRole('switch',{name:'Program A · Group stage'}).getAttribute('aria-checked')).toBe('false');
 expect(screen.queryByLabelText('Session 1 Program A matches')).toBeNull();
 expect(screen.getByText('3 matches not allocated to sessions')).toBeTruthy();
});
test('saved slot swap excludes called matches and uses two table selections',async()=>{
 const rows=view.plans[0].matches;rows[1]={...rows[1],start:'2026-10-27T11:00:00',end:'2026-10-27T11:40:00',court:2};rows[2]={...rows[2],start:'2026-10-28T10:00:00',end:'2026-10-28T10:40:00',court:3};view.calledMatchIds=['m1'];
 show();fireEvent.click(await screen.findByRole('button',{name:'View Saved Timetable'}));
 expect(screen.queryByRole('button',{name:'Select match #1 for swap'})).toBeNull();
 fireEvent.click(screen.getByRole('button',{name:'Select match #2 for swap'}));expect(service.api.mock.calls.filter(c=>c[0]==='scheduling/swap')).toHaveLength(0);
 fireEvent.click(screen.getByRole('button',{name:'Swap match #2 with match #3'}));
 await waitFor(()=>expect(service.api).toHaveBeenCalledWith('scheduling/swap','POST',{revision:8,eventId:'e',matchA:'m2',matchB:'m3'}));
});

test('preview table swaps send the preview token without schedule confirmation',async()=>{
 const proposed=[{...match('m2','a','2026-10-27T11:00:00'),end:'2026-10-27T11:40:00',court:2},{...match('m5','b','2026-10-28T10:00:00'),court:3}];
 service.api.mockImplementation(async(path:string)=>path.startsWith('tournament/')?structuredClone(view):{token:'preview-token',revision:8,matches:proposed,unplaced:[],kept:1,sessions:[]});
 show();await screen.findByRole('button',{name:'Preview Schedule'});await ready();await saveSessions();fireEvent.click(screen.getByRole('button',{name:'Preview Schedule'}));
 fireEvent.click(await screen.findByRole('button',{name:'Select match #2 for swap'}));fireEvent.click(screen.getByRole('button',{name:'Swap match #2 with match #5'}));
 await waitFor(()=>expect(service.api).toHaveBeenCalledWith('scheduling/swap','POST',{revision:8,eventId:'e',matchA:'m2',matchB:'m5',token:'preview-token'}));
 expect(service.api.mock.calls.some(c=>c[0]==='scheduling/confirm')).toBe(false);
});

test('quantities cannot exceed the unallocated total and deselection restores availability',async()=>{
 view.eventPlan.sessions[0].programLimits={a:1};
 view.eventPlan.sessions.splice(1,0,{...view.eventPlan.sessions[0],start:'14:00:00',end:'18:00:00',programLimits:{a:3}});
 show();await screen.findByLabelText('Schedule date');await ready();
 const first=screen.getByLabelText('Session 1 Program A matches') as HTMLInputElement;
 const second=screen.getByLabelText('Session 2 Program A matches') as HTMLInputElement;
 expect(second.value).toBe('2');expect(second.max).toBe('2');
 expect(within(screen.getByRole('region',{name:'Session 2'})).getByText('0 matches not allocated to sessions')).toBeTruthy();
 fireEvent.change(first,{target:{value:'2'}});await ready();expect(first.value).toBe('1');expect(second.value).toBe('2');fireEvent.click(screen.getByRole('button',{name:'OK'}));
 fireEvent.change(second,{target:{value:'1'}});await ready();expect(first.max).toBe('2');
 fireEvent.change(first,{target:{value:'2'}});await ready();expect(first.value).toBe('2');expect(second.max).toBe('1');
 fireEvent.click(within(screen.getByRole('region',{name:'Session 1'})).getByRole('switch',{name:'Program A · Group stage'}));await ready();
 expect(second.value).toBe('1');expect(second.max).toBe('3');
 await waitFor(()=>expect((screen.getByRole('button',{name:'Preview Schedule'}) as HTMLButtonElement).disabled).toBe(false));await saveSessions();fireEvent.click(screen.getByRole('button',{name:'Preview Schedule'}));
 await waitFor(()=>expect(service.api.mock.calls.some(c=>c[0]==='scheduling/preview')).toBe(true));
 const body=service.api.mock.calls.find(c=>c[0]==='scheduling/preview')![2];expect(body.sessions[1].programLimits.a).toBe(1);
});

test('remaining quantities follow time order rather than session display order and keep programs independent',async()=>{
 view.eventPlan.sessions[0]={...view.eventPlan.sessions[0],start:'14:00:00',end:'18:00:00',matchIds:['m2','m3','m4','m5','m6'],programLimits:{a:3,b:2}};
 view.eventPlan.sessions.splice(1,0,{date:'2026-10-27',start:'10:00:00',end:'12:00:00',courts:[1],matchIds:['m2','m3','m4'],programLimits:{a:2}});
 show();await screen.findByLabelText('Schedule date');await ready();
 expect((screen.getByLabelText('Session 1 Program A matches') as HTMLInputElement).value).toBe('1');
 expect((screen.getByLabelText('Session 1 Program B matches') as HTMLInputElement).value).toBe('2');
 await waitFor(()=>expect((screen.getByRole('button',{name:'Preview Schedule'}) as HTMLButtonElement).disabled).toBe(false));await saveSessions();fireEvent.click(screen.getByRole('button',{name:'Preview Schedule'}));
 await waitFor(()=>expect(service.api.mock.calls.some(c=>c[0]==='scheduling/preview')).toBe(true));
 const body=service.api.mock.calls.find(c=>c[0]==='scheduling/preview')![2];
 expect(body.sessions[0].programLimits).toEqual({a:1,b:2});expect(body.sessions[1].programLimits).toEqual({a:2});
});


test('capacity uses the server result instead of a time-times-courts formula',async()=>{
 service.capacity.mockImplementation(async body=>capacityReply(view,body,body.sessions[0].courts.length===4?20:17));
 view.eventPlan.sessions[0]={...view.eventPlan.sessions[0],start:'14:00:00',end:'18:00:00',matchIds:[]};
 show();await screen.findByLabelText('Schedule date');await ready();
 expect(screen.getByText(/Match capacity for this session:/).textContent).toContain('20 matches');
 fireEvent.click(screen.getByRole('switch',{name:'Program A · Group stage'}));await ready();
 expect(screen.getByText(/Match capacity for this session:/).textContent).toContain('20 matches');
 fireEvent.click(screen.getByRole('switch',{name:'Session 1 Court 1'}));await ready();
 expect(screen.getByText(/Match capacity for this session:/).textContent).toContain('17 matches');

 expect(service.api.mock.calls.some(c=>c[0]==='scheduling/estimate')).toBe(false);
});

test('all session labels show total minus all requested quantities, across dates, without feasible caps',async()=>{
 const rows=Array.from({length:24},(_,i)=>match('m'+(i+100),'a'));view.plans[0].matches=rows;
 view.eventPlan.sessions=[{date:'2026-10-27',start:'16:00:00',end:'18:00:00',courts:[1,2,3,4],matchIds:rows.map(m=>m.id),programLimits:{a:10}},{date:'2026-10-27',start:'19:00:00',end:'22:00:00',courts:[1,2,3,4],matchIds:rows.map(m=>m.id),programLimits:{a:8}},{date:'2026-10-28',start:'10:00:00',end:'12:00:00',courts:[1,2,3,4],matchIds:[]}];
 show();await screen.findByLabelText('Schedule date');await ready();
 expect(screen.getAllByText('6 matches not allocated to sessions')).toHaveLength(2);
 const first=screen.getByLabelText('Session 1 Program A matches') as HTMLInputElement;
 expect(first.value).toBe('10');expect(first.max).toBe('12');
 fireEvent.change(first,{target:{value:'12'}});await ready();expect(screen.getAllByText('4 matches not allocated to sessions')).toHaveLength(2);
 fireEvent.change(screen.getByLabelText('Schedule date'),{target:{value:'2026-10-28'}});
 expect(screen.getByText('4 matches not allocated to sessions')).toBeTruthy();
 fireEvent.click(screen.getByRole('switch',{name:'Program A · Group stage'}));await ready();
 expect((screen.getByLabelText('Session 1 Program A matches') as HTMLInputElement).value).toBe('4');
 expect(screen.getAllByText('0 matches not allocated to sessions').length).toBeGreaterThan(0);
 fireEvent.click(screen.getByRole('switch',{name:'Program A · Group stage'}));await ready();expect(screen.getByText('4 matches not allocated to sessions')).toBeTruthy();fireEvent.click(screen.getByRole('button',{name:'Remove session 1'}));
 await saveSessions();fireEvent.click(screen.getByRole('button',{name:'Preview Schedule'}));await screen.findByText('Review Schedule');
 const body=service.api.mock.calls.find(c=>c[0]==='scheduling/preview')![2];expect(body.sessions[0].programLimits.a).toBe(12);expect(body.sessions[1].programLimits.a).toBe(8);
});

test('program quantities share session capacity and Select All cannot overfill it',async()=>{
 const rows=Array.from({length:24},(_,i)=>match('m'+(i+100),'a'));view.plans[0].matches=rows;view.eventPlan.sessions[0].matchIds=[];
 show();await screen.findByLabelText('Schedule date');await ready();
 fireEvent.click(screen.getByRole('switch',{name:'Session 1 Select All Programs'}));await ready();
 const a=screen.getByLabelText('Session 1 Program A matches') as HTMLInputElement;
 expect(a.value).toBe('12');expect(a.max).toBe('12');
 const b=screen.getByRole('switch',{name:'Program B · Group stage'}) as HTMLButtonElement;
 expect(b.disabled).toBe(true);expect(screen.queryByLabelText('Session 1 Program B matches')).toBeNull();
 fireEvent.change(a,{target:{value:'10'}});await ready();expect(b.disabled).toBe(false);
 fireEvent.click(b);await ready();const bInput=screen.getByLabelText('Session 1 Program B matches') as HTMLInputElement;
 expect(bInput.value).toBe('2');expect(a.max).toBe('10');
 fireEvent.change(a,{target:{value:'11'}});await ready();expect(a.value).toBe('10');expect(screen.getByRole('dialog',{name:'Session capacity'}).textContent).toContain('This session can hold up to 12 matches.');fireEvent.click(screen.getByRole('button',{name:'OK'}));
 fireEvent.click(screen.getByRole('switch',{name:'Session 1 Court 1'}));await ready();
 fireEvent.click(screen.getByRole('switch',{name:'Session 1 Court 1'}));await ready();
 expect(a.value).toBe('10');expect(bInput.value).toBe('2');expect(screen.getByRole('switch',{name:'Session 1 Court 1'}).getAttribute('aria-checked')).toBe('true');
 expect(screen.queryByRole('alert')).toBeNull();
 await saveSessions();fireEvent.click(screen.getByRole('button',{name:'Preview Schedule'}));await screen.findByText('Review Schedule');
 const body=service.api.mock.calls.find(c=>c[0]==='scheduling/preview')![2];expect(body.sessions[0].programLimits).toEqual({a:10,b:2});
});

test('requests are reduced to the placements returned by the shared calculation',async()=>{
 const rows=Array.from({length:24},(_,i)=>match('m'+(i+100),'a'));view.plans[0].matches=rows;view.eventPlan.sessions[0].matchIds=rows.map(m=>m.id);view.eventPlan.sessions[0].programLimits={a:24};
 service.capacity.mockImplementation(async body=>capacityReply(view,body,8));show();await screen.findByLabelText('Schedule date');await ready();
 await waitFor(()=>expect((screen.getByLabelText('Session 1 Program A matches') as HTMLInputElement).value).toBe('8'));await ready();expect(screen.getByText(/Match capacity for this session:/).textContent).toContain('8 matches');
 expect(screen.getByText('16 matches not allocated to sessions')).toBeTruthy();
});

test('first Add Session caps the automatically selected current program, then another program uses the free slots',async()=>{
 const rows=Array.from({length:24},(_,i)=>match('m'+(i+100),'a'));view.plans[0].matches=rows;view.eventPlan.sessions=[];
 render(<EventSchedule eventId="e" programId="a" onChanged={async()=>{}} onBack={()=>{}}/>);await screen.findByLabelText('Schedule date');await ready();
 fireEvent.click(screen.getByRole('button',{name:'Add Session'}));await ready();
 const a=screen.getByLabelText('Session 1 Program A matches') as HTMLInputElement;expect(a.value).toBe('12');
 expect(screen.getByText(/Match capacity for this session:/).textContent).toContain('12 matches');expect(screen.queryByRole('alert')).toBeNull();
 fireEvent.keyDown(a,{key:'ArrowUp'});expect(screen.getByRole('dialog',{name:'Session capacity'}).textContent).toContain('12 matches');fireEvent.click(screen.getByRole('button',{name:'OK'}));
 fireEvent.change(a,{target:{value:'10'}});await ready();fireEvent.click(screen.getByRole('switch',{name:'Program B · Group stage'}));await ready();
 expect((screen.getByLabelText('Session 1 Program B matches') as HTMLInputElement).value).toBe('2');
 await saveSessions();fireEvent.click(screen.getByRole('button',{name:'Preview Schedule'}));await screen.findByText('Review Schedule');
 const body=service.api.mock.calls.find(c=>c[0]==='scheduling/preview')![2];expect(body.sessions[0].programLimits).toEqual({a:10,b:2});
});

test('automatic focused selection respects capacity when sessions already exist',async()=>{
 const rows=Array.from({length:24},(_,i)=>match('m'+(i+100),'a'));view.plans[0].matches=rows;view.eventPlan.sessions[0].matchIds=[];
 render(<EventSchedule eventId="e" programId="a" onChanged={async()=>{}} onBack={()=>{}}/>);await screen.findByLabelText('Schedule date');await ready();
 expect((screen.getByLabelText('Session 1 Program A matches') as HTMLInputElement).value).toBe('12');expect(screen.queryByRole('alert')).toBeNull();
});

async function setSessionTime(label:string,hour:string,period:string){
 fireEvent.click(screen.getByRole('button',{name:label}));const dialog=within(screen.getByRole('dialog',{name:label}));
 fireEvent.click(dialog.getByRole('button',{name:hour,exact:true}));fireEvent.click(dialog.getByRole('button',{name:period,exact:true}));fireEvent.click(dialog.getByRole('button',{name:/^Set /i}));await ready();
}

test('the entire new session is configurable before Save Session, with no separate edit or cancel steps',async()=>{
 view.eventPlan.sessions=[];show();await screen.findByLabelText('Schedule date');await ready();fireEvent.click(screen.getByRole('button',{name:'Add Session'}));await ready();
 expect(screen.queryByRole('button',{name:'Add',exact:true})).toBeNull();expect(screen.queryByRole('button',{name:'Cancel',exact:true})).toBeNull();expect(screen.queryByRole('button',{name:/Edit session/})).toBeNull();
 fireEvent.click(screen.getByRole('switch',{name:'Program A · Group stage'}));await ready();fireEvent.change(screen.getByLabelText('Session 1 Program A matches'),{target:{value:'2'}});await ready();
 fireEvent.click(screen.getByRole('switch',{name:'Program B · Group stage'}));await ready();
 expect((screen.getByLabelText('Session 1 Program B matches') as HTMLInputElement).value).toBe('2');expect(service.api.mock.calls.some(c=>c[0]==='scheduling/draft')).toBe(false);
 await ready();fireEvent.click(screen.getByRole('button',{name:'Save Session'}));await waitFor(()=>expect((screen.getByRole('button',{name:'Save Session'}) as HTMLButtonElement).disabled).toBe(true));
 expect(service.api).toHaveBeenCalledWith('scheduling/draft','POST',expect.objectContaining({sessionIndex:0,sessions:[expect.objectContaining({programLimits:{a:2,b:2},courts:[1,2,3,4]})]}));
 expect(service.api.mock.calls.find(c=>c[0]==='scheduling/draft')![2].sessions[0]).not.toHaveProperty('dirty');
 fireEvent.change(screen.getByLabelText('Session 1 Program A matches'),{target:{value:'1'}});await ready();expect((screen.getByRole('button',{name:'Save Session'}) as HTMLButtonElement).disabled).toBe(false);
 expect(screen.getByRole('button',{name:'Remove session 1'}).hasAttribute('disabled')).toBe(false);
});

test('session times can change in either order; Save Session validates the complete edited card',async()=>{
 view.eventPlan.sessions=[{date:'2026-10-27',start:'10:00:00',end:'12:00:00',courts:[1,2,3,4],matchIds:['m2','m3','m4']},{date:'2026-10-27',start:'18:00:00',end:'20:00:00',courts:[1,2,3,4],matchIds:[]}];
 show();await screen.findByLabelText('Schedule date');await ready();
 await setSessionTime('Session 1 start time','2','PM');expect(screen.queryByRole('dialog')).toBeNull();
 const save=()=>fireEvent.click(within(screen.getByRole('region',{name:'Session 1'})).getByRole('button',{name:'Save Session'}));
 save();expect(screen.getByRole('dialog').textContent).toContain('End time must be later');fireEvent.click(screen.getByRole('button',{name:'OK'}));
 await setSessionTime('Session 1 end time','2','PM');save();expect(screen.getByRole('dialog').textContent).toContain('End time must be later');fireEvent.click(screen.getByRole('button',{name:'OK'}));
 await setSessionTime('Session 1 end time','7','PM');save();expect(screen.getByRole('dialog').textContent).toContain('overlaps Session 2');fireEvent.click(screen.getByRole('button',{name:'OK'}));
 await setSessionTime('Session 1 end time','6','PM');save();await waitFor(()=>expect((within(screen.getByRole('region',{name:'Session 1'})).getByRole('button',{name:'Save Session'}) as HTMLButtonElement).disabled).toBe(true));
 const body=service.api.mock.calls.find(c=>c[0]==='scheduling/draft')![2];expect(body.sessions[0]).toMatchObject({start:'14:00:00',end:'18:00:00'});
});

test('unsaved full-session changes block preview and survive date navigation; Remove discards a new card',async()=>{
 view.eventPlan.sessions=[];show();await screen.findByLabelText('Schedule date');await ready();fireEvent.click(screen.getByRole('button',{name:'Add Session'}));await ready();
 fireEvent.click(screen.getByRole('switch',{name:'Program A · Group stage'}));await ready();fireEvent.change(screen.getByLabelText('Session 1 Program A matches'),{target:{value:'1'}});await ready();
 fireEvent.click(screen.getByRole('button',{name:'Preview Schedule'}));expect(screen.getByRole('dialog').textContent).toContain('Save Session or remove');expect(service.api.mock.calls.some(c=>c[0]==='scheduling/preview')).toBe(false);fireEvent.click(screen.getByRole('button',{name:'OK'}));
 fireEvent.change(screen.getByLabelText('Schedule date'),{target:{value:'2026-10-28'}});fireEvent.change(screen.getByLabelText('Schedule date'),{target:{value:'2026-10-27'}});
 expect((screen.getByLabelText('Session 1 Program A matches') as HTMLInputElement).value).toBe('1');
 fireEvent.click(screen.getByRole('button',{name:'Remove session 1'}));expect(screen.queryByRole('region',{name:'Session 1'})).toBeNull();expect(service.api.mock.calls.some(c=>c[0]==='scheduling/draft')).toBe(false);
});

test('Save Session requires programs and courts; server errors retain every field for correction',async()=>{
 view.eventPlan.sessions=[];show();await screen.findByLabelText('Schedule date');await ready();fireEvent.click(screen.getByRole('button',{name:'Add Session'}));await ready();
 await ready();fireEvent.click(screen.getByRole('button',{name:'Save Session'}));expect(screen.getByRole('dialog').textContent).toContain('Select at least one program');fireEvent.click(screen.getByRole('button',{name:'OK'}));
 fireEvent.click(screen.getByRole('switch',{name:'Program A · Group stage'}));await ready();
 for(let c=1;c<=4;c++)fireEvent.click(screen.getByRole('switch',{name:'Session 1 Court '+c}));await ready();fireEvent.click(screen.getByRole('button',{name:'Save Session'}));expect(screen.getByRole('dialog').textContent).toContain('Select at least one court');fireEvent.click(screen.getByRole('button',{name:'OK'}));
 fireEvent.click(screen.getByRole('switch',{name:'Session 1 Court 1'}));await ready();service.api.mockRejectedValueOnce(new Error('Current venue time validation failed.'));
 await ready();fireEvent.click(screen.getByRole('button',{name:'Save Session'}));await screen.findByText('Current venue time validation failed.');fireEvent.click(screen.getByRole('button',{name:'OK'}));expect((screen.getByLabelText('Session 1 Program A matches') as HTMLInputElement).value).toBe('3');expect((screen.getByRole('button',{name:'Save Session'}) as HTMLButtonElement).disabled).toBe(false);
});

test('Remove is available but does not discard saved match slots',async()=>{
 show();await screen.findByLabelText('Schedule date');await ready();fireEvent.click(screen.getByRole('button',{name:'Remove session 1'}));
 expect(screen.getByRole('dialog').textContent).toContain('contains saved matches');expect(screen.getByRole('region',{name:'Session 1',hidden:true})).toBeTruthy();expect(service.api.mock.calls.some(c=>c[0]==='scheduling/confirm')).toBe(false);
});

test('Add Session uses the latest end time, and prevents an unusable end-of-day session',async()=>{
 view.eventPlan.sessions=[{date:'2026-10-27',start:'21:00:00',end:'23:30:00',courts:[1],matchIds:[]}];
 show();await screen.findByLabelText('Schedule date');await ready();fireEvent.click(screen.getByRole('button',{name:'Add Session'}));await ready();
 expect(screen.getByRole('dialog',{name:'Session time'}).textContent).toContain('Choose another playing date');expect(screen.queryByRole('region',{name:'Session 2'})).toBeNull();
});

test('legacy overlapping sessions cannot be previewed until corrected',async()=>{
 view.plans[0].matches[0]={...view.plans[0].matches[0],start:null,end:null,court:null};
 view.eventPlan.sessions=[{date:'2026-10-27',start:'10:00:00',end:'12:00:00',courts:[1],matchIds:[]},{date:'2026-10-27',start:'10:00:00',end:'12:00:00',courts:[2],matchIds:[]}];
 show();await screen.findByLabelText('Schedule date');await ready();fireEvent.click(screen.getByRole('button',{name:'Preview Schedule'}));
 expect(screen.getByRole('dialog').textContent).toContain('overlaps Session');expect(service.api.mock.calls.some(c=>c[0]==='scheduling/preview')).toBe(false);
 fireEvent.click(screen.getByRole('button',{name:'OK'}));fireEvent.click(screen.getByRole('button',{name:'Remove session 2'}));await ready();fireEvent.click(screen.getByRole('button',{name:'Preview Schedule'}));await screen.findByText('Review Schedule');
});


test('a late response from an old session configuration cannot replace the current capacity',async()=>{
 const pending:{body:any;resolve:(value:any)=>void}[]=[];
 service.capacity.mockImplementation(body=>new Promise(resolve=>pending.push({body,resolve})));
 show();await screen.findByLabelText('Schedule date');await waitFor(()=>expect(pending).toHaveLength(1));
 expect(screen.getByText('Calculating capacity…')).toBeTruthy();
 fireEvent.click(screen.getByRole('switch',{name:'Session 1 Court 1'}));await waitFor(()=>expect(pending).toHaveLength(2));
 pending[1].resolve(capacityReply(view,pending[1].body,5));await ready();expect(screen.getByText(/Match capacity for this session:/).textContent).toContain('5 matches');
 pending[0].resolve(capacityReply(view,pending[0].body,99));await waitFor(()=>expect(screen.getByText(/Match capacity for this session:/).textContent).toContain('5 matches'));
});
