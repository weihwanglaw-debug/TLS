import {capacityReply} from './schedule-capacity-fixture';
import {cleanup,fireEvent,render,screen,within,waitFor} from '@testing-library/react';
import {afterEach,beforeEach,expect,test,vi} from 'vitest';
const service=vi.hoisted(()=>({api:vi.fn(),capacity:vi.fn()}));vi.mock('../src/api',()=>({api:(path:string,...args:any[])=>path==='scheduling/capacity'?service.capacity(args[1]):service.api(path,...args)}));
import EventSchedule from '../src/EventSchedule';
const view={revision:7,timeZone:'Asia/Singapore',eventInfo:{id:'event',name:'Event',startDate:'2026-11-01',endDate:'2026-11-02'},eventPlan:{dailySessions:[],scheduleDays:[],sessions:[{date:'2026-11-01',start:'10:00:00',end:'12:00:00'},{date:'2026-11-02',start:'14:00:00',end:'18:00:00'}],matchMinutes:30,restMinutes:20},eventCourts:[1,2,3,4],closedCourts:[],programs:[{id:'x',name:'Program X',status:'A'},{id:'y',name:'Program Y',status:'A'},{id:'z',name:'Program Z',status:'A'}],plans:[{programId:'x',phase:'K',lastRound:1,matches:[{id:'mx',programId:'x',status:'SC',games:[],phase:'K',round:1,roundLabel:'Semifinals',number:1,start:null,court:null}]},{programId:'y',phase:'K',lastRound:1,matches:[{id:'my',programId:'y',status:'SC',games:[],phase:'K',round:1,roundLabel:'Semifinals',number:2,start:null,court:null}]},{programId:'z',phase:'K',lastRound:1,matches:[{id:'mz',programId:'z',status:'SC',games:[],phase:'K',round:1,roundLabel:'Semifinals',number:3,start:'2026-11-02T14:00:00',court:1}]}]};


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

afterEach(cleanup);beforeEach(()=>{vi.resetAllMocks();service.capacity.mockImplementation(async body=>capacityReply(view as any,body));service.api.mockImplementation(async(path:string)=>path.startsWith('tournament/')?structuredClone(view):path.endsWith('preview')?{token:'preview',revision:7,matches:[],unplaced:[],kept:1}:{revision:8});});

test('date dropdown allows remaining stage matches on another date and Select All preserves saved slots',async()=>{
 const changed=vi.fn();render(<EventSchedule eventId="event" onChanged={changed} onBack={()=>{}}/>);await screen.findByText('Daily Sessions and Courts');await ready();
 expect((screen.getByLabelText('Schedule date') as HTMLSelectElement).options).toHaveLength(2);expect(screen.queryByText('Copy Sessions and Courts to Other Days')).toBeNull();
 fireEvent.click(screen.getByRole('switch',{name:'Program X · Semifinals'}));await ready();fireEvent.change(screen.getByLabelText('Schedule date'),{target:{value:'2026-11-02'}});
 expect((screen.getByRole('switch',{name:'Program X · Semifinals'}) as HTMLButtonElement).disabled).toBe(true);expect(screen.queryByText('Assigned to 01/11/2026')).toBeNull();
 fireEvent.click(screen.getByRole('switch',{name:'Session 1 Select All Programs'}));await ready();await waitFor(()=>expect((screen.getByRole('button',{name:'Preview Schedule'}) as HTMLButtonElement).disabled).toBe(false));await saveSessions();fireEvent.click(screen.getByRole('button',{name:'Preview Schedule'}));await screen.findByText('Review Schedule');
 const request=service.api.mock.calls.find(c=>c[0]==='scheduling/preview')![2];expect(request.days[0].matchIds).toEqual(['mx']);expect(request.days[1].matchIds).toEqual(['mz','my']);expect(request.sessions[1].start).toBe('14:00:00');expect(service.api.mock.calls.some(c=>c[0]==='scheduling/confirm')).toBe(false);
 fireEvent.click(screen.getByRole('button',{name:'Confirm Schedule'}));await screen.findByRole('dialog',{name:'Saved successfully'});expect(changed).toHaveBeenCalledOnce();expect(screen.getByText('Saved Timetable')).toBeTruthy();
});
test('explicit round move is previewed before confirmation and includes the whole round',async()=>{
 render(<EventSchedule eventId="event" onChanged={async()=>{}} onBack={()=>{}}/>);await screen.findByText('Daily Sessions and Courts');await ready();
 fireEvent.change(screen.getByLabelText('Schedule date'),{target:{value:'2026-11-02'}});fireEvent.click(screen.getByRole('switch',{name:'Program Y · Semifinals'}));await ready();
 fireEvent.change(screen.getByLabelText('Schedule date'),{target:{value:'2026-11-01'}});
 await saveSessions();fireEvent.click(screen.getAllByRole('button',{name:'Move Entire Round'})[0]);await screen.findByRole('dialog',{name:'Move Round'});expect(service.api.mock.calls.filter(c=>c[0].startsWith('tournament/'))).toHaveLength(1);
 fireEvent.click(screen.getByRole('button',{name:'Preview Move'}));await screen.findByText('Review Schedule');const request=service.api.mock.calls.find(c=>c[0]==='scheduling/preview')![2];expect(request.moveMatchIds).toEqual(['my']);expect(request.days[0].matchIds).toContain('my');expect(request.days[1].matchIds).not.toContain('my');expect(service.api.mock.calls.some(c=>c[0]==='scheduling/confirm')).toBe(false);
});


test('each session keeps its own program and court selections with one preview for the date',async()=>{
 const data=structuredClone(view);data.eventPlan.sessions=[{date:'2026-11-01',start:'10:00:00',end:'12:00:00'},{date:'2026-11-01',start:'14:00:00',end:'16:00:00'}];
 service.api.mockImplementation(async(path:string)=>path.startsWith('tournament/')?data:{token:'preview',revision:7,matches:[],unplaced:[],kept:0});
 render(<EventSchedule eventId="event" onChanged={async()=>{}} onBack={()=>{}}/>);await screen.findByText('Daily Sessions and Courts');await ready();
 const morning=within(screen.getByRole('region',{name:'Session 1'})),afternoon=within(screen.getByRole('region',{name:'Session 2'}));
 fireEvent.click(morning.getByRole('switch',{name:'Program X · Semifinals'}));await ready();
 fireEvent.click(afternoon.getByRole('switch',{name:'Session 2 Select All Programs'}));await ready();
 fireEvent.click(afternoon.getByRole('switch',{name:'Session 2 Court 1'}));await ready();
 expect(morning.getByRole('switch',{name:'Program Y · Semifinals'}).getAttribute('aria-checked')).toBe('false');
 expect(afternoon.getByRole('switch',{name:'Program Y · Semifinals'}).getAttribute('aria-checked')).toBe('true');
 expect(screen.queryByText('Programs and Stages for This Date')).toBeNull();
 await waitFor(()=>expect((screen.getByRole('button',{name:'Preview Schedule'}) as HTMLButtonElement).disabled).toBe(false));await saveSessions();fireEvent.click(screen.getByRole('button',{name:'Preview Schedule'}));await screen.findByText('Review Schedule');
 const request=service.api.mock.calls.find(c=>c[0]==='scheduling/preview')![2];
 expect(request.sessions[0].matchIds).toEqual(['mx']);expect(request.sessions[1].matchIds).toEqual(['my']);
 expect(request.sessions[0].courts).toEqual([1,2,3,4]);expect(request.sessions[1].courts).toEqual([2,3,4]);
 expect(request.days[0].matchIds).toEqual(['mx','my']);expect(service.api.mock.calls.filter(c=>c[0]==='scheduling/preview')).toHaveLength(1);
});

test('saved session selections reload independently rather than becoming whole-day selections',async()=>{
 const data=structuredClone(view) as any;data.eventPlan.sessions=[{date:'2026-11-01',start:'10:00:00',end:'12:00:00',courts:[1],matchIds:['mx']},{date:'2026-11-01',start:'14:00:00',end:'16:00:00',courts:[2],matchIds:['mx','my']}];
 service.api.mockResolvedValue(data);render(<EventSchedule eventId="event" onChanged={async()=>{}} onBack={()=>{}}/>);await screen.findByText('Daily Sessions and Courts');await ready();
 const morning=within(screen.getByRole('region',{name:'Session 1'})),afternoon=within(screen.getByRole('region',{name:'Session 2'}));
 expect(morning.getByRole('switch',{name:'Program X · Semifinals'}).getAttribute('aria-checked')).toBe('true');expect(morning.getByRole('switch',{name:'Program Y · Semifinals'}).getAttribute('aria-checked')).toBe('false');
 expect(afternoon.getByRole('switch',{name:'Program Y · Semifinals'}).getAttribute('aria-checked')).toBe('true');expect(afternoon.getByRole('switch',{name:'Session 2 Court 1'}).getAttribute('aria-checked')).toBe('false');
});


function swapData(){
 const data=structuredClone(view) as any;
 const match=(id:string,number:number,hour:string,entryA:string,entryB:string)=>({id,number,programId:'x',status:'SC',games:[],phase:'K',round:1,roundLabel:'Semifinals',start:'2026-11-01T'+hour+':00:00',end:'2026-11-01T'+hour+':30:00',court:1,entryA,entryB,actualStartedAt:null,durationMinutes:30});
 data.plans=[{programId:'x',phase:'K',lastRound:1,matches:[match('first',1,'10','single-a','single-b'),match('second',2,'11','double-a','double-b')]}];
 data.entries=[{id:'single-a',name:'Entry A',participantIds:['a']},{id:'single-b',name:'Entry B',participantIds:['b']},{id:'double-a',name:'Team AB',participantIds:['c','d']},{id:'double-b',name:'Team CD',participantIds:['e','f']}];
 data.participants=['Alice','Bob','Claire','David','Eve','Frank'].map((name,i)=>({id:'abcdef'[i],name}));
 data.eventPlan.sessions=[{date:'2026-11-01',start:'10:00:00',end:'12:00:00',courts:[1],matchIds:['first','second']}];return data;
}
function configureSwap(data:any,handler:(body:any)=>Promise<any>){
 service.capacity.mockImplementation(async body=>capacityReply(data,body));
 service.api.mockImplementation(async(path:string,_method:string,body:any)=>path.startsWith('tournament/')?structuredClone(data):path==='scheduling/swap'?handler(body):{revision:data.revision});
}
async function savedTable(){
 const changed=vi.fn();render(<EventSchedule eventId="event" onChanged={changed} onBack={()=>{}}/>);
 fireEvent.click(await screen.findByRole('button',{name:'View Saved Timetable'}));await screen.findByText('Saved Timetable');return changed;
}

test('table shows both opponents, supports deselection and saves validated swaps without dropdowns',async()=>{
 const data=swapData();let finish!:(value:any)=>void;configureSwap(data,()=>new Promise(resolve=>{finish=resolve;}));const changed=await savedTable();
 const row=screen.getByText('#1').closest('tr')!;
 expect(within(row).getByText('Alice')).toBeTruthy();expect(within(row).getByText('Bob')).toBeTruthy();
 expect(screen.getByText('Claire / David')).toBeTruthy();expect(screen.getByText('Eve / Frank')).toBeTruthy();expect(screen.queryByText('Entry A')).toBeNull();
 expect(screen.queryByRole('button',{name:'Swap Slots',exact:true})).toBeNull();expect(screen.queryByLabelText('First match')).toBeNull();
 fireEvent.click(screen.getByRole('button',{name:'Select match #1 for swap'}));
 expect(row.style.boxShadow).toContain('inset');expect(screen.getByRole('button',{name:'Clear swap selection for match #1'}).getAttribute('aria-pressed')).toBe('true');
 expect(service.api.mock.calls.some(c=>c[0]==='scheduling/swap')).toBe(false);
 fireEvent.click(screen.getByRole('button',{name:'Clear swap selection for match #1'}));expect(screen.queryByRole('button',{name:'Swap match #1 with match #2'})).toBeNull();
 fireEvent.click(screen.getByRole('button',{name:'Select match #1 for swap'}));fireEvent.click(screen.getByRole('button',{name:'Swap match #1 with match #2'}));
 expect(service.api).toHaveBeenCalledWith('scheduling/swap','POST',{revision:7,eventId:'event',matchA:'first',matchB:'second'});
 expect((screen.getByRole('button',{name:'Swap match #1 with match #2'}) as HTMLButtonElement).disabled).toBe(true);
 const [a,b]=data.plans[0].matches;[a.start,b.start]=[b.start,a.start];[a.end,b.end]=[b.end,a.end];data.revision=8;finish({revision:8});
 await screen.findByRole('dialog',{name:'Saved successfully'});fireEvent.click(screen.getByRole('button',{name:'OK'}));
 expect(changed).toHaveBeenCalledOnce();expect(within(screen.getByText('#1').closest('tr')!).getByText('01/11/2026 · 11:00–11:30')).toBeTruthy();
 expect(within(screen.getByText('#2').closest('tr')!).getByText('01/11/2026 · 10:00–10:30')).toBeTruthy();
 expect(screen.getByRole('button',{name:'Select match #1 for swap'}).getAttribute('aria-pressed')).toBe('false');
 expect(service.api.mock.calls.filter(c=>c[0]==='scheduling/swap')).toHaveLength(1);expect(service.api.mock.calls.some(c=>c[0]==='scheduling/confirm')).toBe(false);
});

test('rejected swaps retain original slots and first selection; leaving the table clears selection',async()=>{
 const data=swapData();configureSwap(data,async()=>{throw new Error('Swap conflicts with player rest.');});const changed=await savedTable();
 fireEvent.click(screen.getByRole('button',{name:'Select match #1 for swap'}));fireEvent.click(screen.getByRole('button',{name:'Swap match #1 with match #2'}));
 await screen.findByText('Swap conflicts with player rest.');fireEvent.click(screen.getByRole('button',{name:'OK'}));
 expect(changed).not.toHaveBeenCalled();expect(screen.getByRole('button',{name:'Clear swap selection for match #1'}).getAttribute('aria-pressed')).toBe('true');
 expect(within(screen.getByText('#1').closest('tr')!).getByText('01/11/2026 · 10:00–10:30')).toBeTruthy();
 fireEvent.click(screen.getByRole('button',{name:'Back to Planning'}));fireEvent.click(screen.getByRole('button',{name:'View Saved Timetable'}));
 expect(screen.getByRole('button',{name:'Select match #1 for swap'}).getAttribute('aria-pressed')).toBe('false');
});

test('called, started, completed and unscheduled matches have no selection control',async()=>{
 const data=swapData(),base=data.plans[0].matches[0];
 data.plans[0].matches.push({...base,id:'called',number:3},{...base,id:'started',number:4,status:'IP',actualStartedAt:'2026-11-01T10:00:00'},{...base,id:'completed',number:5,status:'C',games:[{a:21,b:10}]},{...base,id:'pending',number:6,start:null,end:null,court:null});data.calledMatchIds=['called'];
 configureSwap(data,async()=>({}));await savedTable();
 for(const n of [3,4,5,6])expect(within(screen.getByText('#'+n).closest('tr')!).queryByRole('button')).toBeNull();
 expect(screen.getByRole('button',{name:'Select match #1 for swap'})).toBeTruthy();
});

test('preview swaps display new slots and Confirm Schedule uses the returned token',async()=>{
 const data=swapData(),matches=structuredClone(data.plans[0].matches);for(const m of data.plans[0].matches){m.start=null;m.end=null;m.court=null;}
 const result={token:'preview-one',revision:7,matches,unplaced:[],kept:0};
 configureSwap(data,async()=>{const next=structuredClone(matches);[next[0].start,next[1].start]=[next[1].start,next[0].start];[next[0].end,next[1].end]=[next[1].end,next[0].end];return {...result,token:'preview-two',matches:next};});
 const regular=service.api.getMockImplementation()!;service.api.mockImplementation(async(path:string,...args:any[])=>path==='scheduling/preview'?result:regular(path,...args));
 render(<EventSchedule eventId="event" onChanged={async()=>{}} onBack={()=>{}}/>);await screen.findByLabelText('Schedule date');await ready();await saveSessions();fireEvent.click(screen.getByRole('button',{name:'Preview Schedule'}));await screen.findByText('Review Schedule');
 expect(screen.getByText('Claire / David')).toBeTruthy();fireEvent.click(screen.getByRole('button',{name:'Select match #1 for swap'}));fireEvent.click(screen.getByRole('button',{name:'Swap match #1 with match #2'}));
 await screen.findByText('Preview slots exchanged. Confirm Schedule to save.');fireEvent.click(screen.getByRole('button',{name:'OK'}));
 expect(service.api).toHaveBeenCalledWith('scheduling/swap','POST',{revision:7,eventId:'event',matchA:'first',matchB:'second',token:'preview-one'});
 expect(service.api.mock.calls.some(c=>c[0]==='scheduling/confirm')).toBe(false);expect(data.plans[0].matches.every((m:any)=>m.start===null)).toBe(true);
 expect(within(screen.getByText('#1').closest('tr')!).getByText('2026-11-01 11:00:00')).toBeTruthy();
 fireEvent.click(screen.getByRole('button',{name:'Confirm Schedule'}));await screen.findByText('Saved Timetable');
 expect(service.api).toHaveBeenCalledWith('scheduling/confirm','POST',{revision:7,token:'preview-two'});
});
