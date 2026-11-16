import {StrictMode} from 'react';
import {act,cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {afterEach,expect,test,vi} from 'vitest';
import UmpireApp from '../src/UmpireApp';
import {LiveError} from '../src/live-api';
const service=vi.hoisted(()=>({liveApi:vi.fn(),operationId:()=> 'same-operation'}));
vi.mock('../src/live-api',()=>({...service,LiveError:class extends Error{constructor(message:string,public status=0){super(message);}}}));
afterEach(()=>{cleanup();vi.useRealTimers();window.history.replaceState(null,'','/umpire');vi.restoreAllMocks();localStorage.clear();service.liveApi.mockReset();});
async function flush(){await act(async()=>{});}
async function advance(ms=2000){await act(async()=>{await vi.advanceTimersByTimeAsync(ms);});}
test('storage failure retains a pending point and automatically retries it',async()=>{
 vi.useFakeTimers();
 const view={sessionId:'session-test',serverTime:new Date().toISOString(),heartbeatSeconds:30,match:{id:'m',status:'IP',version:1,court:1,games:[],players:[],rules:{gamesToWin:2,pointsToWin:21},scoreA:0,scoreB:0,serving:{serverSide:'A',leftSide:'A'}}};
 localStorage.setItem('tls-umpire-match',JSON.stringify({token:'token',view}));
 vi.spyOn(Storage.prototype,'setItem').mockImplementation(()=>{throw new DOMException('Full','QuotaExceededError');});
 let failed=true;
 service.liveApi.mockImplementation(async(path:string)=>{if(path==='tablet/command'){if(failed)throw new Error('Offline');return {...view,match:{...view.match,version:2,scoreA:1}};}return path==='tablet/state'?view:{};});
 window.history.replaceState(null,'','/umpire/session-test');render(<UmpireApp/>);
 await flush();const point=screen.getByRole('button',{name:'Add point to side A'});
 fireEvent.click(point);await flush();
 expect(screen.getByText('Reconnecting…')).toBeTruthy();expect(screen.queryByRole('dialog')).toBeNull();
 expect(screen.getByText(/Browser storage is unavailable/)).toBeTruthy();
 expect(screen.queryByRole('button',{name:'Retry pending action'})).toBeNull();
 failed=false;await advance();
 const commands=service.liveApi.mock.calls.filter(c=>c[0]==='tablet/command');
 expect(commands).toHaveLength(2);expect(commands[0][1]).toEqual(commands[1][1]);
 expect((screen.getByRole('button',{name:'Add point to side A'}) as HTMLButtonElement).disabled).toBe(false);
});

test('shuttle minus stops at zero and does not change the point score',async()=>{
 let view={sessionId:'shuttles',serverTime:new Date().toISOString(),heartbeatSeconds:30,match:{id:'m',status:'IP',version:1,court:1,games:[],players:[],rules:{gamesToWin:2,pointsToWin:21,countShuttles:true},scoreA:4,scoreB:2,canUndo:true,canUndoPoint:false,resultDetails:{shuttlesUsed:0,cards:[],challenges:[]},serving:{serverSide:'A',leftSide:'A'}}};
 localStorage.setItem('tls-umpire-match',JSON.stringify({token:'token',view}));window.history.replaceState(null,'','/umpire/shuttles');
 service.liveApi.mockImplementation(async(path:string,body:any)=>{
  if(path==='tablet/command')view={...view,match:{...view.match,version:view.match.version+1,resultDetails:{...view.match.resultDetails,shuttlesUsed:view.match.resultDetails.shuttlesUsed+(body.action==='SHUTTLE'?1:-1)}}};
  return path==='tablet/state'||path==='tablet/command'?view:{};
 });
 render(<UmpireApp/>);
 const minus=screen.getByRole('button',{name:'Remove one shuttle'}) as HTMLButtonElement;
 expect(minus.disabled).toBe(true);
 expect((screen.getByRole('button',{name:'Undo point'}) as HTMLButtonElement).disabled).toBe(true);
 const plus=screen.getByRole('button',{name:'+ Shuttle'});
 await waitFor(()=>expect((plus as HTMLButtonElement).disabled).toBe(false));fireEvent.click(plus);
 await waitFor(()=>expect(screen.getByLabelText('Shuttles used').textContent).toBe('1'));
 fireEvent.click(minus);
 await waitFor(()=>expect(screen.getByLabelText('Shuttles used').textContent).toBe('0'));
 expect(minus.disabled).toBe(true);expect(screen.getByLabelText('Side A score').textContent).toBe('4');
 expect(service.liveApi.mock.calls.filter(c=>c[0]==='tablet/command').map(c=>c[1].action)).toEqual(['SHUTTLE','SHUTTLE_MINUS']);
});

test.each(['SHUTTLE_MINUS','UNDO_POINT'])('%s automatically retries the same pending operation after a lost acknowledgement',async action=>{
 vi.useFakeTimers();
 const view={sessionId:'retry-correction',serverTime:new Date().toISOString(),heartbeatSeconds:30,match:{id:'m',status:'IP',version:1,court:1,games:[],players:[],rules:{gamesToWin:2,pointsToWin:21,countShuttles:true},scoreA:4,scoreB:2,canUndoPoint:true,resultDetails:{shuttlesUsed:1,cards:[],challenges:[]},serving:{serverSide:'A',leftSide:'A'}}};
 localStorage.setItem('tls-umpire-match',JSON.stringify({token:'token',view}));window.history.replaceState(null,'','/umpire/retry-correction');
 const fresh={...view,match:{...view.match,version:2,scoreA:action==='UNDO_POINT'?3:4,resultDetails:{...view.match.resultDetails,shuttlesUsed:action==='SHUTTLE_MINUS'?0:1}}};let attempts=0;
 service.liveApi.mockImplementation(async(path:string)=>{if(path==='tablet/command'){if(++attempts===1)throw new Error('Acknowledgement lost');return fresh;}return path==='tablet/state'?view:{};});
 render(<UmpireApp/>);await flush();const button=screen.getByRole('button',{name:action==='SHUTTLE_MINUS'?'Remove one shuttle':'Undo point'});
 fireEvent.click(button);await flush();expect(screen.getByText('Reconnecting…')).toBeTruthy();
 expect(screen.queryByRole('dialog')).toBeNull();expect(screen.queryByRole('button',{name:'Retry pending action'})).toBeNull();
 await advance();
 const commands=service.liveApi.mock.calls.filter(c=>c[0]==='tablet/command');expect(commands).toHaveLength(2);expect(commands[0][1]).toEqual(commands[1][1]);expect(commands[0][1].action).toBe(action);
 expect(screen.getByLabelText('Side A score').textContent).toBe(String(fresh.match.scoreA));expect(screen.getByLabelText('Shuttles used').textContent).toBe(String(fresh.match.resultDetails.shuttlesUsed));
});
test('denied storage reads do not prevent opening the pairing screen',()=>{
 vi.spyOn(Storage.prototype,'getItem').mockImplementation(()=>{throw new DOMException('Denied','SecurityError');});
 render(<UmpireApp/>);expect(screen.getByRole('button',{name:'Connect Tablet'})).toBeTruthy();
});

test('fast acknowledgement still locks both point buttons for 500 ms while Undo remains available',async()=>{
 vi.useFakeTimers();
 const view={sessionId:'lock-test',serverTime:new Date().toISOString(),heartbeatSeconds:30,match:{id:'m',status:'IP',version:1,court:1,games:[],players:[],rules:{gamesToWin:2,pointsToWin:21},scoreA:0,scoreB:0,canUndo:true,canUndoPoint:true,serving:{serverSide:'A',leftSide:'A'}}};
 localStorage.setItem('tls-umpire-match',JSON.stringify({token:'token',view}));window.history.replaceState(null,'','/umpire/lock-test');
 service.liveApi.mockImplementation(async(path:string)=>path==='tablet/state'?view:path==='tablet/command'?{...view,match:{...view.match,version:2,scoreA:1}}:{});
 const {act}=await import('@testing-library/react');
 try{
 render(<UmpireApp/>);await act(async()=>{});
 const a=screen.getByRole('button',{name:'Add point to side A'}) as HTMLButtonElement,b=screen.getByRole('button',{name:'Add point to side B'}) as HTMLButtonElement;
 fireEvent.click(a);await act(async()=>{});fireEvent.click(a);fireEvent.click(b);
 expect(service.liveApi.mock.calls.filter(c=>c[0]==='tablet/command')).toHaveLength(1);expect(a.disabled&&b.disabled).toBe(true);
 expect((screen.getByRole('button',{name:'Undo point'}) as HTMLButtonElement).disabled).toBe(false);
 await act(async()=>{await vi.advanceTimersByTimeAsync(499);});expect(a.disabled).toBe(true);
 await act(async()=>{await vi.advanceTimersByTimeAsync(1);});expect(a.disabled||b.disabled).toBe(false);
 fireEvent.click(b);await act(async()=>{});expect(service.liveApi.mock.calls.filter(c=>c[0]==='tablet/command')).toHaveLength(2);
 }finally{vi.useRealTimers();}
});

function recoverySession(pending=false){
 const view={sessionId:'automatic-recovery',serverTime:new Date().toISOString(),heartbeatSeconds:5,match:{id:'m',status:'IP',version:1,court:1,games:[],players:[],rules:{gamesToWin:2,pointsToWin:21},scoreA:0,scoreB:0,canUndoPoint:true,serving:{serverSide:'A',leftSide:'A'}}};
 localStorage.setItem('tls-umpire-match',JSON.stringify({token:'token',view,...(pending?{pending:{version:1,operationId:'persisted-operation',action:'POINT',side:'A'}}:{})}));
 window.history.replaceState(null,'','/umpire/automatic-recovery');return view;
}
const sent=()=>service.liveApi.mock.calls.filter(c=>c[0]==='tablet/command');

test('refresh restores and automatically confirms a pending action once under StrictMode',async()=>{
 vi.useFakeTimers();const view=recoverySession(true);
 service.liveApi.mockImplementation(async(path:string)=>path==='tablet/command'?{...view,match:{...view.match,version:2,scoreA:1}}:path==='tablet/state'?view:{});
 render(<StrictMode><UmpireApp/></StrictMode>);await flush();
 expect(sent()).toHaveLength(1);expect(sent()[0][1]).toEqual({version:1,operationId:'persisted-operation',action:'POINT',side:'A'});
 expect(screen.getByLabelText('Side A score').textContent).toBe('1');expect(JSON.parse(localStorage.getItem('tls-umpire-match')!).pending).toBeUndefined();
 expect(screen.queryByRole('button',{name:'Retry pending action'})).toBeNull();expect(screen.queryByRole('dialog')).toBeNull();
});

test('repeated lost replies apply one point and recover without popups or extra user taps',async()=>{
 vi.useFakeTimers();const view=recoverySession();const operations=new Set<string>();let attempts=0;
 service.liveApi.mockImplementation(async(path:string,body:any)=>{
  if(path==='tablet/command'){
   operations.add(body.operationId);if(++attempts<=3)throw new Error('Reply lost');
   return {...view,match:{...view.match,version:2,scoreA:operations.size}};
  }
  return path==='tablet/state'?view:{};
 });
 render(<UmpireApp/>);await flush();fireEvent.click(screen.getByRole('button',{name:'Add point to side A'}));await flush();
 expect((screen.getByRole('button',{name:'Undo point'}) as HTMLButtonElement).disabled).toBe(true);
 for(let i=0;i<2;i++){await advance();expect(screen.getByText('Reconnecting…')).toBeTruthy();expect(screen.queryByRole('dialog')).toBeNull();}
 fireEvent.click(screen.getByRole('button',{name:'Add point to side A'}));expect(sent()).toHaveLength(3);
 await advance();expect(sent()).toHaveLength(4);expect(operations.size).toBe(1);
 for(const command of sent())expect(command[1]).toEqual(sent()[0][1]);
 expect(screen.getByLabelText('Side A score').textContent).toBe('1');expect(screen.getByText('Saved to venue')).toBeTruthy();
 expect((screen.getByRole('button',{name:'Undo point'}) as HTMLButtonElement).disabled).toBe(false);
});

test.each([200,408,429,500,503])('an ambiguous/transient HTTP %s keeps the same command for automatic recovery',async status=>{
 vi.useFakeTimers();const view=recoverySession();let attempts=0;
 service.liveApi.mockImplementation(async(path:string)=>{
  if(path==='tablet/command'){if(++attempts===1)throw new LiveError('Temporary response problem',status);return {...view,match:{...view.match,version:2,scoreA:1}};}
  return path==='tablet/state'?view:{};
 });
 render(<UmpireApp/>);await flush();fireEvent.click(screen.getByRole('button',{name:'Add point to side A'}));await flush();
 expect(JSON.parse(localStorage.getItem('tls-umpire-match')!).pending.action).toBe('POINT');expect(screen.queryByRole('dialog')).toBeNull();
 await advance();expect(sent()).toHaveLength(2);expect(sent()[1][1]).toEqual(sent()[0][1]);expect(screen.getByLabelText('Side A score').textContent).toBe('1');
});

test('online events recover promptly without overlapping retries and unmount stops recovery',async()=>{
 vi.useFakeTimers();const view=recoverySession();let acknowledge!:(value:unknown)=>void,attempts=0;
 service.liveApi.mockImplementation(async(path:string)=>{
  if(path==='tablet/command'){if(++attempts===1)throw new Error('Offline');return new Promise(resolve=>{acknowledge=resolve;});}
  return path==='tablet/state'?view:{};
 });
 render(<UmpireApp/>);await flush();fireEvent.click(screen.getByRole('button',{name:'Add point to side A'}));await flush();
 fireEvent(window,new Event('online'));await flush();expect(sent()).toHaveLength(2);
 for(let i=0;i<3;i++)fireEvent(window,new Event('online'));
 await advance(6000);expect(sent()).toHaveLength(2);
 await act(async()=>acknowledge({...view,match:{...view.match,version:2,scoreA:1}}));
 cleanup();await advance(30000);fireEvent(window,new Event('online'));await flush();expect(sent()).toHaveLength(2);
});

test.each([401,403])('automatic command HTTP %s clears obsolete pending state and stops retrying',async status=>{
 vi.useFakeTimers();recoverySession(true);
 service.liveApi.mockImplementation(async(path:string)=>{if(path==='tablet/command')throw new LiveError('Session released.',status);return {};});
 render(<UmpireApp/>);await flush();
 expect(screen.getByText('Session released.')).toBeTruthy();expect(localStorage.getItem('tls-umpire-match')).toBeNull();expect(window.location.pathname).toBe('/umpire');
 await advance(10000);expect(sent()).toHaveLength(1);
});

test('a rejected command is not retried when refreshing the confirmed score also fails',async()=>{
 vi.useFakeTimers();const view=recoverySession(true);let reachable=false;
 service.liveApi.mockImplementation(async(path:string)=>{
  if(path==='tablet/command')throw new LiveError('Score changed. Reload the confirmed score.',409);
  if(path==='tablet/state'){if(!reachable)throw new Error('Offline');return {...view,match:{...view.match,version:5,scoreA:6}};}
  return {};
 });
 render(<UmpireApp/>);await flush();expect(JSON.parse(localStorage.getItem('tls-umpire-match')!).pending).toBeUndefined();
 expect(screen.getByText(/Score changed/)).toBeTruthy();fireEvent.click(screen.getByRole('button',{name:'OK',exact:true}));
 expect((screen.getByRole('button',{name:'Add point to side A'}) as HTMLButtonElement).disabled).toBe(true);
 reachable=true;await advance(10000);expect(sent()).toHaveLength(1);
 expect(screen.getByLabelText('Side A score').textContent).toBe('6');expect((screen.getByRole('button',{name:'Add point to side A'}) as HTMLButtonElement).disabled).toBe(false);
});

test('an incomplete confirmation preserves the pending action and cached score until recovery',async()=>{
 vi.useFakeTimers();const view=recoverySession();let attempts=0;
 service.liveApi.mockImplementation(async(path:string)=>{
  if(path==='tablet/command')return ++attempts===1?{}:{...view,match:{...view.match,version:2,scoreA:1}};
  return path==='tablet/state'?view:{};
 });
 render(<UmpireApp/>);await flush();fireEvent.click(screen.getByRole('button',{name:'Add point to side A'}));await flush();
 const saved=JSON.parse(localStorage.getItem('tls-umpire-match')!);expect(saved.pending.action).toBe('POINT');expect(saved.view.match.scoreA).toBe(0);
 expect(screen.getByText('Reconnecting…')).toBeTruthy();expect(screen.queryByRole('dialog')).toBeNull();
 await advance();expect(sent()).toHaveLength(2);expect(sent()[1][1]).toEqual(sent()[0][1]);expect(screen.getByLabelText('Side A score').textContent).toBe('1');
});
