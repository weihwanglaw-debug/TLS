import {act,cleanup,fireEvent,render,screen} from '@testing-library/react';
import {afterEach,beforeEach,expect,test,vi} from 'vitest';
import UmpireApp from '../src/UmpireApp';
import {RulesFields} from '../src/GameSettingPresets';
import {newRules} from '../src/tournament-types';

const service=vi.hoisted(()=>({liveApi:vi.fn(),operationId:()=> 'timer-operation'}));
vi.mock('../src/live-api',()=>({...service,LiveError:class extends Error{status=0;}}));

beforeEach(()=>{vi.useFakeTimers();localStorage.clear();service.liveApi.mockReset();});
afterEach(()=>{cleanup();vi.useRealTimers();localStorage.clear();window.history.replaceState(null,'','/umpire');});

function session(warmupSeconds:number|undefined=120,intervalSeconds?:number){
 let match={id:'timer-match',status:intervalSeconds===undefined?'SC':'IP',version:1,court:1,number:1,umpireName:'Jane',eventName:'Event',programName:'Singles',earlyStartMinutes:0,
  rules:{...newRules(),warmupSeconds},games:intervalSeconds===undefined?[]:[{a:21,b:10}],players:[{id:'a',name:'Alice',side:'A'},{id:'b',name:'Bob',side:'B'}],
  scoreA:intervalSeconds===undefined?0:21,scoreB:intervalSeconds===undefined?0:10,awaitingNextGame:intervalSeconds!==undefined,matchWon:false,canUndoPoint:true,
  serving:intervalSeconds===undefined?null:{serverSide:'A',leftSide:'A',rightA:'a',rightB:'b',decidingEndsChanged:false},intervalUntil:intervalSeconds===undefined?null:new Date(Date.now()+intervalSeconds*1000).toISOString(),gamePhase:null as string|null,phaseUntil:null as string|null,startedAt:null as string|null};
 const view=()=>({sessionId:'timer-session',serverTime:new Date().toISOString(),heartbeatSeconds:30,match});
 localStorage.setItem('tls-umpire-match',JSON.stringify({token:'token',view:view()}));
 window.history.replaceState(null,'','/umpire/timer-session');
 service.liveApi.mockImplementation(async(path:string,body:any)=>{
  if(path==='tablet/command'){
   if(body.action==='WARMUP')match={...match,gamePhase:'WU',phaseUntil:new Date(Date.now()+(warmupSeconds??120)*1000).toISOString(),version:match.version+1};
   if(body.action==='RESUME_INTERVAL')match={...match,gamePhase:'IP',intervalUntil:null,phaseUntil:null,version:match.version+1};
   if(body.action==='START')match={...match,status:'IP',gamePhase:'IP',phaseUntil:null,startedAt:new Date().toISOString(),serving:{serverSide:'A',leftSide:'A',rightA:'a',rightB:'b',decidingEndsChanged:false},version:match.version+1};
   if(body.action==='NEXT')match={...match,awaitingNextGame:false,scoreA:0,scoreB:0,intervalUntil:null,version:match.version+1};
  }
  return path==='tablet/state'||path==='tablet/command'?view():{};
 });
 return {view,updateMatch(patch:Partial<typeof match>){match={...match,...patch};},setInterval(seconds:number){match={...match,intervalUntil:new Date(Date.now()+seconds*1000).toISOString()};}};
}
async function open(){render(<UmpireApp/>);await act(async()=>{});}
async function advance(seconds:number){await act(async()=>{await vi.advanceTimersByTimeAsync(seconds*1000);});}
const commands=()=>service.liveApi.mock.calls.filter(c=>c[0]==='tablet/command');

test('server-confirmed warm-up counts down on the tablet and the same enabled button can start the match early',async()=>{
 session();await open();
 fireEvent.click(screen.getByRole('button',{name:'Start Warm-up'}));await act(async()=>{});
 expect((screen.getByRole('button',{name:'Start Game · 2:00'}) as HTMLButtonElement).disabled).toBe(false);
 expect(commands()).toHaveLength(1);
 await advance(15);
 fireEvent.click(screen.getByRole('button',{name:'Start Game · 1:45'}));
 expect(screen.getByRole('dialog',{name:'Set Up Match'})).toBeTruthy();
 fireEvent.click(screen.getByRole('button',{name:'Confirm & Start Match'}));await act(async()=>{});
 expect(commands()).toHaveLength(2);expect(commands()[1][1]).toEqual(expect.objectContaining({action:'START',serving:{serverSide:'A',serverId:'a',receiverId:'b',leftSide:'A'}}));
 expect(screen.getByRole('button',{name:'Add point to side A'})).toBeTruthy();
 expect(JSON.parse(localStorage.getItem('tls-umpire-match')!).view.match.startedAt).toBe(new Date().toISOString());
});

test('warm-up warns at thirty seconds and expiry waits for an explicit start',async()=>{
 session();await open();fireEvent.click(screen.getByRole('button',{name:'Start Warm-up'}));await act(async()=>{});
 await advance(89);expect(screen.queryByText('Ready to play')).toBeNull();
 await advance(1);expect(screen.getByText('Ready to play')).toBeTruthy();
 expect((screen.getByRole('button',{name:/Start Game · 0:30/}) as HTMLButtonElement).disabled).toBe(false);
 await advance(30);expect(screen.getByRole('button',{name:'Start Game',exact:true})).toBeTruthy();
 expect(screen.queryByText('Ready to play')).toBeNull();expect(commands()).toHaveLength(1);
 expect(JSON.parse(localStorage.getItem('tls-umpire-match')!).view.match.status).toBe('SC');
});

test.each([0,5,180])('saved warm-up duration %s controls the button',async seconds=>{
 session(seconds);await open();
 if(seconds===0){fireEvent.click(screen.getByRole('button',{name:'Start Game',exact:true}));expect(screen.getByRole('dialog',{name:'Set Up Match'})).toBeTruthy();}
 else{
  fireEvent.click(screen.getByRole('button',{name:'Start Warm-up'}));await act(async()=>{});
  expect(screen.getByRole('button',{name:new RegExp('Start Game · '+Math.floor(seconds/60)+':'+String(seconds%60).padStart(2,'0'))})).toBeTruthy();
  await advance(seconds);expect(screen.getByRole('button',{name:'Start Game',exact:true})).toBeTruthy();
 }
 expect(commands()).toHaveLength(seconds===0?0:1);
});

test('legacy missing warm-up uses 120 seconds and refresh retains the server countdown',async()=>{
 session();const saved=JSON.parse(localStorage.getItem('tls-umpire-match')!);delete saved.view.match.rules.warmupSeconds;
 localStorage.setItem('tls-umpire-match',JSON.stringify(saved));
 await open();fireEvent.click(screen.getByRole('button',{name:'Start Warm-up'}));await act(async()=>{});await advance(10);
 expect(screen.getByRole('button',{name:'Start Game · 1:50'})).toBeTruthy();
 cleanup();await open();expect(screen.getByRole('button',{name:'Start Game · 1:50'})).toBeTruthy();expect(commands()).toHaveLength(1);
});

test('cancelling serving setup and using Match actions preserves the running warm-up',async()=>{
 session();await open();fireEvent.click(screen.getByRole('button',{name:'Start Warm-up'}));await act(async()=>{});await advance(10);
 fireEvent.click(screen.getByRole('button',{name:'Start Game · 1:50'}));fireEvent.click(screen.getByRole('button',{name:'Cancel',exact:true}));
 await advance(10);fireEvent.click(screen.getByRole('button',{name:'Match actions'}));
 fireEvent.click(screen.getByRole('button',{name:'Start Game · 1:40'}));expect(screen.getByRole('dialog',{name:'Set Up Match'})).toBeTruthy();
 expect(commands()).toHaveLength(1);
});

test('between-game countdown gates both entry points and waits for the umpire',async()=>{
 session(120,45);await open();
 expect((screen.getByRole('button',{name:'0:45'}) as HTMLButtonElement).disabled).toBe(true);
 fireEvent.click(screen.getByRole('button',{name:'Match actions'}));
 expect(screen.getAllByRole('button',{name:'0:45'}).every(b=>(b as HTMLButtonElement).disabled)).toBe(true);
 fireEvent.click(screen.getByRole('button',{name:'Close',exact:true}));await advance(45);
 expect(commands()).toHaveLength(0);
 fireEvent.click(screen.getByRole('button',{name:'Start Next Game'}));
 fireEvent.click(screen.getByRole('button',{name:'Confirm & Start Next Game'}));await act(async()=>{});
 expect(commands()[0][1].action).toBe('NEXT');
});

test('mid-game interval remains paused at zero and through a reload',async()=>{
 const state=session(120,30);
 state.updateMatch({awaitingNextGame:false,games:[],scoreA:11,scoreB:8,gamePhase:'IN'});
 await open();
 expect((screen.getByRole('button',{name:'0:30'}) as HTMLButtonElement).disabled).toBe(true);
 await advance(30);cleanup();await open();
 expect(screen.queryByRole('button',{name:'Add point to side A'})).toBeNull();
 fireEvent.click(screen.getByRole('button',{name:'Resume'}));await act(async()=>{});
 expect(commands()[0][1].action).toBe('RESUME_INTERVAL');
 expect(screen.getByRole('button',{name:'Add point to side A'})).toBeTruthy();
 expect(screen.getByLabelText('Side A score').textContent).toBe('11');
});

test('a fresh 120-second deadline starts at 2:00 between display ticks',async()=>{
 const state=session(120,0);await open();await advance(.25);state.setInterval(120);
 fireEvent.click(screen.getByRole('button',{name:'+ Shuttle'}));await act(async()=>{});
 expect(screen.getByRole('button',{name:'2:00'})).toBeTruthy();
 expect(screen.queryByText(/2:01/)).toBeNull();
});

test('retaining a pending command does not rewind the server clock or countdown',async()=>{
 const state=session(120,120);await open();await advance(10);
 let acknowledge!:(value:unknown)=>void;
 const previous=service.liveApi.getMockImplementation()!;
 service.liveApi.mockImplementation((path:string,body:any)=>path==='tablet/command'?new Promise(resolve=>{acknowledge=resolve;}):previous(path,body));
 fireEvent.click(screen.getByRole('button',{name:'+ Shuttle'}));await act(async()=>{});
 expect(screen.getByRole('button',{name:'1:50'})).toBeTruthy();
 await advance(3);expect(screen.getByRole('button',{name:'1:47'})).toBeTruthy();
 await act(async()=>acknowledge(state.view()));
 expect((screen.getByRole('button',{name:'1:47'}) as HTMLButtonElement).disabled).toBe(true);
});

test('preset form defaults warm-up to 120 and passes zero through unchanged',()=>{
 const onChange=vi.fn();render(<RulesFields rules={newRules()} onChange={onChange}/>);
 const input=screen.getByLabelText('Warm-up seconds') as HTMLInputElement;
 expect(input.value).toBe('120');expect(input.min).toBe('0');expect(input.max).toBe('600');
 fireEvent.change(input,{target:{value:'0'}});expect(onChange).toHaveBeenCalledWith(expect.objectContaining({warmupSeconds:0}));
});
