import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {afterEach,expect,test,vi} from 'vitest';
import UmpireApp from '../src/UmpireApp';
const service=vi.hoisted(()=>({liveApi:vi.fn(),operationId:()=> 'same-operation'}));
vi.mock('../src/live-api',()=>({...service,LiveError:class extends Error{status=0;}}));
afterEach(()=>{cleanup();window.history.replaceState(null,'','/umpire');vi.restoreAllMocks();localStorage.clear();service.liveApi.mockReset();});
test('storage failure retains a pending point and releases the send lock for the same retry',async()=>{
 const view={sessionId:'session-test',serverTime:new Date().toISOString(),heartbeatSeconds:30,match:{id:'m',status:'IP',version:1,court:1,games:[],players:[],rules:{gamesToWin:2,pointsToWin:21},scoreA:0,scoreB:0,serving:{serverSide:'A',leftSide:'A'}}};
 localStorage.setItem('tls-umpire-match',JSON.stringify({token:'token',view}));
 vi.spyOn(Storage.prototype,'setItem').mockImplementation(()=>{throw new DOMException('Full','QuotaExceededError');});
 let failed=true;
 service.liveApi.mockImplementation(async(path:string)=>{if(path==='tablet/command'){if(failed)throw new Error('Offline');return {...view,match:{...view.match,version:2,scoreA:1}};}return path==='tablet/state'?view:{};});
 window.history.replaceState(null,'','/umpire/session-test');render(<UmpireApp/>);
 const point=await screen.findByRole('button',{name:'Add point to side A'});
 await waitFor(()=>expect((point as HTMLButtonElement).disabled).toBe(false));
 fireEvent.click(point);
 await screen.findByText(/Action not acknowledged/);fireEvent.click(screen.getByRole('button',{name:'OK',exact:true}));
 expect(screen.getByText(/Browser storage is unavailable/)).toBeTruthy();
 const retry=screen.getByRole('button',{name:'Retry pending action'});
 expect((retry as HTMLButtonElement).disabled).toBe(false);
 failed=false;fireEvent.click(retry);
 await waitFor(()=>expect(screen.queryByRole('button',{name:'Retry pending action'})).toBeNull());
 const commands=service.liveApi.mock.calls.filter(c=>c[0]==='tablet/command');
 expect(commands).toHaveLength(2);expect(commands[0][1]).toEqual(commands[1][1]);
 await waitFor(()=>expect((screen.getByRole('button',{name:'Add point to side A'}) as HTMLButtonElement).disabled).toBe(false));
});
test('denied storage reads do not prevent opening the pairing screen',()=>{
 vi.spyOn(Storage.prototype,'getItem').mockImplementation(()=>{throw new DOMException('Denied','SecurityError');});
 render(<UmpireApp/>);expect(screen.getByRole('button',{name:'Connect Tablet'})).toBeTruthy();
});

test('fast acknowledgement still locks both point buttons for 500 ms while Undo remains available',async()=>{
 vi.useFakeTimers();
 const view={sessionId:'lock-test',serverTime:new Date().toISOString(),heartbeatSeconds:30,match:{id:'m',status:'IP',version:1,court:1,games:[],players:[],rules:{gamesToWin:2,pointsToWin:21},scoreA:0,scoreB:0,canUndo:true,serving:{serverSide:'A',leftSide:'A'}}};
 localStorage.setItem('tls-umpire-match',JSON.stringify({token:'token',view}));window.history.replaceState(null,'','/umpire/lock-test');
 service.liveApi.mockImplementation(async(path:string)=>path==='tablet/state'?view:path==='tablet/command'?{...view,match:{...view.match,version:2,scoreA:1}}:{});
 const {act}=await import('@testing-library/react');
 try{
 render(<UmpireApp/>);await act(async()=>{});
 const a=screen.getByRole('button',{name:'Add point to side A'}) as HTMLButtonElement,b=screen.getByRole('button',{name:'Add point to side B'}) as HTMLButtonElement;
 fireEvent.click(a);await act(async()=>{});fireEvent.click(a);fireEvent.click(b);
 expect(service.liveApi.mock.calls.filter(c=>c[0]==='tablet/command')).toHaveLength(1);expect(a.disabled&&b.disabled).toBe(true);
 expect((screen.getByRole('button',{name:'Undo last point'}) as HTMLButtonElement).disabled).toBe(false);
 await act(async()=>{await vi.advanceTimersByTimeAsync(499);});expect(a.disabled).toBe(true);
 await act(async()=>{await vi.advanceTimersByTimeAsync(1);});expect(a.disabled||b.disabled).toBe(false);
 fireEvent.click(b);await act(async()=>{});expect(service.liveApi.mock.calls.filter(c=>c[0]==='tablet/command')).toHaveLength(2);
 }finally{vi.useRealTimers();}
});
