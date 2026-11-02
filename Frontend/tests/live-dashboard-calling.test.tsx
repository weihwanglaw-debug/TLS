import {afterEach,beforeEach,expect,test,vi} from 'vitest';
import {cleanup,fireEvent,render,screen,within,waitFor} from '@testing-library/react';
import LiveDashboard from '../src/LiveDashboard';
import type {DashboardData,LiveMatch} from '../src/live-types';
const service=vi.hoisted(()=>({liveApi:vi.fn()}));
vi.mock('../src/live-api',()=>({...service,LiveError:class extends Error {status=400;}}));
let data:DashboardData;
globalThis.ResizeObserver=class {observe(){}unobserve(){}disconnect(){}} as unknown as typeof ResizeObserver;
const match=(id:string,court:number)=>({id,number:Number(id.slice(1)),eventId:'e',programId:'p',programName:"Men's Singles",roundLabel:'Quarterfinals',status:'SC',nameA:'Player A '+id,nameB:'Player B '+id,court,plannedStart:'2026-10-26T15:00:00',games:[],rules:{gamesToWin:2,pointsToWin:21},rulesAssigned:true,presetId:'preset',umpireId:'official',reviewAvailable:false} as LiveMatch);
beforeEach(()=>{
 localStorage.clear();localStorage.setItem('tls-dashboard-event','e');
 const a=match('m1',1),b=match('m2',2),c=match('m3',3);
 const queue=[a,b,c].map(m=>({match:m,estimatedStart:'2026-10-26T07:00:00Z',delayed:false,delayMinutes:0,waitingFor:'Ready to call'}));
 data={revision:4,selectedEventId:'e',serverTime:'2026-10-26T06:00:00Z',serverLocalTime:'2026-10-26T14:00:00',timeZone:'Asia/Singapore',events:[{id:'e',name:'Test Event',startDate:'2026-10-26',endDate:'2026-10-28',courts:[1,2,3,4]}],eventContext:{calendar:'O',sessionState:'B',day:1,nextSession:'2026-10-26T15:00:00'},queue,courts:[1,2,3,4].map(number=>({number,status:number===3?'W':number===4?'X':'A',outsideEvent:false,match:number===3?c:null,lastMatch:null,next:queue.find(q=>q.match.court===number)||null,device:{state:'N',name:'',ipAddress:'',lastSeen:null,codeExpired:false}})),programs:[],presets:[],officials:[{eventId:'e',id:'official',name:'Umpire One'}],tabletUrls:['http://localhost/umpire']} as DashboardData;
 service.liveApi.mockReset();service.liveApi.mockImplementation(async(path:string,body?:{matchId:string;court:number})=>body?{code:'TESTCODE',expiresAt:'2026-10-26T07:10:00Z',match:{...data.queue.find(q=>q.match.id===body.matchId)!.match,court:body.court}}:structuredClone(data));
});
afterEach(cleanup);
const show=()=>render(<LiveDashboard onFixtures={()=>{}} onChanged={async()=>{}}/>);
test('court card calls its next match with the court preselected and no override controls',async()=>{
 show();fireEvent.click(await screen.findByRole('button',{name:'Call Next Match on Court 2'}));
 const dialog=screen.getByRole('dialog',{name:'Assign Court / Tablet'});
 expect(within(dialog).getByLabelText('Assigned court').textContent).toBe('Court 2');
 expect(within(dialog).queryByLabelText('Assign court')).toBeNull();
 expect(within(dialog).getByText('#2 · Player A m2 vs Player B m2')).toBeTruthy();
 expect(within(dialog).queryByLabelText('Assignment reason')).toBeNull();
 expect(within(dialog).queryByText(/Override a waiting/)).toBeNull();
 fireEvent.click(within(dialog).getByRole('button',{name:'Assign & Generate Code'}));
 await waitFor(()=>expect(service.liveApi).toHaveBeenCalledWith('dashboard/pair',expect.objectContaining({matchId:'m2',court:2,reason:''})));
 const payload=service.liveApi.mock.calls.find(c=>c[0]==='dashboard/pair')![1];expect(payload).not.toHaveProperty('replaceWaiting');
});
test('queue assignment offers only available courts and moves only the chosen match',async()=>{
 data.queue[0].match.court=3;
 show();const buttons=await screen.findAllByRole('button',{name:'Assign to Court'});fireEvent.click(buttons[0]);
 const dialog=screen.getByRole('dialog',{name:'Assign Court / Tablet'}),select=within(dialog).getByLabelText('Assign court');
 expect(within(select).getAllByRole('option').map(o=>(o as HTMLOptionElement).value)).toEqual(['0','1','2']);
 fireEvent.change(select,{target:{value:'2'}});fireEvent.click(within(dialog).getByRole('button',{name:'Assign & Generate Code'}));
 await waitFor(()=>expect(service.liveApi).toHaveBeenCalledWith('dashboard/pair',expect.objectContaining({matchId:'m1',court:2})));
 expect(service.liveApi.mock.calls.filter(c=>c[1])).toHaveLength(1);
});
test('no available court disables queue assignment and hides calling actions',async()=>{
 data.courts.forEach(c=>{c.status='W';c.match=match('m'+c.number,c.number);});
 show();const buttons=await screen.findAllByRole('button',{name:'Assign to Court'});
 expect(buttons.every(b=>(b as HTMLButtonElement).disabled)).toBe(true);
 expect(screen.queryByRole('button',{name:/Call Next Match/})).toBeNull();
});
test('table view offers the same calling action',async()=>{
 localStorage.setItem('tls-court-view','table');show();
 expect(await screen.findByRole('button',{name:'Call Next Match on Court 1'})).toBeTruthy();
 expect(screen.queryByRole('button',{name:'Call Next Match on Court 3'})).toBeNull();
});
test.each(['SC','IP','RC'])('umpires reserved for another %s match are disabled with the court reason',async status=>{
 data.courts[2].match!.status=status;
 data.officials.push({eventId:'e',id:'busy',name:'Busy Umpire',reservations:[{matchId:'m3',court:3}]});
 data.queue[0].match.umpireId='busy';
 show();fireEvent.click(await screen.findByRole('button',{name:'Call Next Match on Court 1'}));
 const dialog=screen.getByRole('dialog',{name:'Assign Court / Tablet'}),select=within(dialog).getByLabelText('Official 1');
 expect((within(select).getByRole('option',{name:'Busy Umpire · Busy · Court 3'}) as HTMLOptionElement).disabled).toBe(true);
 expect((select as HTMLSelectElement).value).toBe('');
 expect((within(dialog).getByRole('button',{name:'Assign & Generate Code'}) as HTMLButtonElement).disabled).toBe(true);
 fireEvent.change(select,{target:{value:'official'}});
 expect((within(dialog).getByRole('button',{name:'Assign & Generate Code'}) as HTMLButtonElement).disabled).toBe(false);
});
test('renewing the same match keeps its own reserved umpire selectable',async()=>{
 data.officials[0].reservations=[{matchId:'m1',court:1}];
 data.courts[0].status='W';data.courts[0].match=data.queue[0].match;
 show();await screen.findAllByRole('button',{name:'Assign to Court'});
 fireEvent.click(screen.getByRole('button',{name:'Open actions for Court 1'}));
 fireEvent.click(screen.getByRole('menuitem',{name:'Issue / Renew Access Code'}));
 const dialog=screen.getByRole('dialog',{name:'Assign Court / Tablet'});
 expect((within(dialog).getByRole('option',{name:'Umpire One'}) as HTMLOptionElement).disabled).toBe(false);
 expect(within(dialog).getByLabelText('Assigned court').textContent).toBe('Court 1');
});

test('cancel wrong call is visible and releases the waiting assignment without rescheduling',async()=>{
 show();fireEvent.click(await screen.findByRole('button',{name:'Cancel Call / Return to Queue'}));
 const dialog=screen.getByRole('dialog',{name:'Cancel Call / Return to Queue'});
 expect(within(dialog).getByText(/saved schedule unchanged/)).toBeTruthy();
 fireEvent.click(within(dialog).getByRole('button',{name:'Confirm'}));
 await waitFor(()=>expect(service.liveApi).toHaveBeenCalledWith('dashboard/cancel',{revision:4,court:3}));
 expect(service.liveApi.mock.calls.filter(c=>c[1])).toHaveLength(1);
});
test('disconnected tablet can get a fresh invitation while keeping its match and umpire',async()=>{
 data.courts[2].device.state='D';data.courts[2].match!.version=9;
 show();fireEvent.click(await screen.findByRole('button',{name:'Generate New Code'}));
 await waitFor(()=>expect(service.liveApi).toHaveBeenCalledWith('dashboard/pairing',{revision:4,matchId:'m3',version:9}));
 expect(await screen.findByRole('dialog',{name:'Connect Tablet · Match #3'})).toBeTruthy();
});
test('pairing panel closes when the invitation has been consumed',async()=>{
 data.tabletUrls=['http://192.168.1.10:5180/umpire'];show();
 fireEvent.click(await screen.findByRole('button',{name:'Call Next Match on Court 1'}));
 fireEvent.click(within(screen.getByRole('dialog',{name:'Assign Court / Tablet'})).getByRole('button',{name:'Assign & Generate Code'}));
 await screen.findByRole('dialog',{name:'Connect Tablet · Match #1'});
 data.courts[0].match=data.queue[0].match;data.courts[0].status='W';
 data.courts[0].device={...data.courts[0].device,state:'C',pairingPending:false,pairingExpiresAt:'2026-10-26T07:10:00Z'};
 await waitFor(()=>expect(screen.queryByRole('dialog',{name:'Connect Tablet · Match #1'})).toBeNull(),{timeout:4000});
});

test('assignment adds service and line judges without the exception or reservation text',async()=>{
 data.officials.push({eventId:'e',id:'service',name:'Service Official'},{eventId:'e',id:'line',name:'Line Official'});
 show();fireEvent.click(await screen.findByRole('button',{name:'Call Next Match on Court 1'}));
 const dialog=screen.getByRole('dialog',{name:'Assign Court / Tablet'});
 expect(within(dialog).queryByText('Court exception')).toBeNull();expect(within(dialog).queryByText(/reserves the selected court/)).toBeNull();expect(within(dialog).queryByRole('switch',{name:'Challenge review available on this court'})).toBeNull();
 fireEvent.click(within(dialog).getByRole('button',{name:'+ Add official'}));fireEvent.change(within(dialog).getByLabelText('Official 2'),{target:{value:'service'}});
 fireEvent.click(within(dialog).getByRole('button',{name:'+ Add official'}));fireEvent.change(within(dialog).getByLabelText('Official 3'),{target:{value:'line'}});
 fireEvent.click(within(dialog).getByRole('button',{name:'Assign & Generate Code'}));
 await waitFor(()=>expect(service.liveApi).toHaveBeenCalledWith('dashboard/pair',expect.objectContaining({umpireId:'official',serviceJudgeId:'service',lineJudgeIds:['line']})));
 expect(service.liveApi.mock.calls.find(c=>c[0]==='dashboard/pair')![1]).not.toHaveProperty('courtOverrideReason');
});
test('saved optional officials load into assignment and challenge switch is relevant to the preset',async()=>{
 data.queue[0].match.serviceJudgeId='service';data.queue[0].match.lineJudgeIds=['line'];data.queue[0].match.rules.challengesEnabled=true;
 data.officials.push({eventId:'e',id:'service',name:'Service Official'},{eventId:'e',id:'line',name:'Line Official'});
 show();fireEvent.click(await screen.findByRole('button',{name:'Call Next Match on Court 1'}));const dialog=screen.getByRole('dialog',{name:'Assign Court / Tablet'});
 expect((within(dialog).getByLabelText('Official 2') as HTMLSelectElement).value).toBe('service');expect((within(dialog).getByLabelText('Official 3') as HTMLSelectElement).value).toBe('line');expect(within(dialog).getByRole('switch',{name:'Challenge review available on this court'})).toBeTruthy();
});
