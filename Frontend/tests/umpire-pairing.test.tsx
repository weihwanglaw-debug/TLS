import {LiveError} from '../src/live-api';
import {StrictMode} from 'react';
import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {afterEach,beforeEach,expect,test,vi} from 'vitest';
import UmpireApp from '../src/UmpireApp';
const service=vi.hoisted(()=>({liveApi:vi.fn(),operationId:()=> 'test-device-id'}));
vi.mock('../src/live-api',()=>({...service,LiveError:class extends Error {constructor(message:string,public status=400){super(message);}}}));
beforeEach(()=>{localStorage.clear();window.history.replaceState(null,'','/umpire');service.liveApi.mockReset();service.liveApi.mockRejectedValue(new Error('Access code expired. Generate a new code.'));});
afterEach(()=>{cleanup();window.history.replaceState(null,'','/');});
test('scanning automatically pairs once without asking for a code and removes it from the URL',async()=>{
 window.history.replaceState(null,'','/umpire#pair=ABCD1234');
 render(<StrictMode><UmpireApp/></StrictMode>);
 await waitFor(()=>expect(service.liveApi).toHaveBeenCalledWith('tablet/pair',{code:'ABCD1234',deviceId:'test-device-id',deviceName:'Umpire Tablet'}));
 expect(service.liveApi).toHaveBeenCalledOnce();expect(window.location.hash).toBe('');
 expect(await screen.findByText('Access code expired. Generate a new code.')).toBeTruthy();
});
test('manual URL and code remain available without automatic pairing',async()=>{
 render(<UmpireApp/>);expect(service.liveApi).not.toHaveBeenCalled();
 const inputs=screen.getAllByRole('textbox');fireEvent.change(inputs[0],{target:{value:'Court tablet'}});fireEvent.change(inputs[1],{target:{value:'ABCD1234'}});
 fireEvent.click(screen.getByRole('button',{name:'Connect Tablet'}));
 await waitFor(()=>expect(service.liveApi).toHaveBeenCalledWith('tablet/pair',{code:'ABCD1234',deviceId:'test-device-id',deviceName:'Court tablet'}));
});
test('a valid scan opens the assigned scoreboard and keeps the returned session',async()=>{
 const view={sessionId:'session-test',serverTime:new Date().toISOString(),heartbeatSeconds:5,match:{id:'assigned',court:2,number:3,programName:"Men's Singles",roundLabel:'Quarterfinals',status:'SC',rules:{gamesToWin:2,pointsToWin:21},games:[],players:[],serving:null,scoreA:0,scoreB:0}};
 service.liveApi.mockImplementation(async(path:string)=>path==='tablet/pair'?{token:'new-session',match:view}:path==='tablet/state'?view:{});
 window.history.replaceState(null,'','/umpire#pair=ABCD1234');render(<UmpireApp/>);
 expect(await screen.findByRole('button',{name:'Set up & start match'})).toBeTruthy();
 expect(screen.queryByRole('button',{name:'Connect Tablet'})).toBeNull();
 expect(JSON.parse(localStorage.getItem('tls-umpire-match')!).token).toBe('new-session');
 expect(window.location.pathname).toBe('/umpire/session-test');expect(window.location.href).not.toContain('new-session');
 expect(window.location.hash).toBe('');
});
test('scanning cannot discard an unacknowledged scoring action',async()=>{
 localStorage.setItem('tls-umpire-match',JSON.stringify({token:'old',view:{sessionId:'session-old',match:{id:'old',rules:{},players:[],games:[],paperMode:true}},pending:{action:'POINT'}}));
 window.history.replaceState(null,'','/umpire#pair=ABCD1234');render(<UmpireApp/>);
 expect(await screen.findByText('Retry the pending scoring action before connecting to another match.')).toBeTruthy();
 expect(service.liveApi).not.toHaveBeenCalled();expect(window.location.hash).toBe('');
});

test('a new pairing hash is handled without reloading the open scoreboard',async()=>{
 const view=(id:string)=>({sessionId:'session-'+id,serverTime:new Date().toISOString(),heartbeatSeconds:30,match:{id,court:2,number:3,programName:id,status:'SC',rules:{gamesToWin:2,pointsToWin:21},games:[],players:[],serving:null,scoreA:0,scoreB:0}});
 localStorage.setItem('tls-umpire-match',JSON.stringify({token:'old-session',view:view('old')}));
 service.liveApi.mockImplementation(async(path:string,_body:unknown,token:string)=>path==='tablet/pair'?{token:'new-session',match:view('new')}:path==='tablet/state'?view(token==='new-session'?'new':'old'):{});
 window.history.replaceState(null,'','/umpire/session-old');render(<UmpireApp/>);
 await waitFor(()=>expect(service.liveApi).toHaveBeenCalledWith('tablet/state',undefined,'old-session'));
 window.history.replaceState(null,'','/umpire#pair=ABCD1234');fireEvent(window,new HashChangeEvent('hashchange'));
 await waitFor(()=>expect(JSON.parse(localStorage.getItem('tls-umpire-match')!).token).toBe('new-session'));
 expect(window.location.hash).toBe('');
});
test('early starts require a confirmation and retain setup selections',async()=>{
 const view={sessionId:'session-test',serverTime:new Date().toISOString(),heartbeatSeconds:30,match:{id:'m',earlyStartMinutes:30,umpireName:'Jane',eventName:'Event',court:1,number:1,status:'SC',version:1,rules:{gamesToWin:2,pointsToWin:21,countShuttles:true},games:[],players:[{id:'a',name:'Alice',side:'A'},{id:'b',name:'Bob',side:'B'}],serving:null,scoreA:0,scoreB:0}};
 localStorage.setItem('tls-umpire-match',JSON.stringify({token:'token',view}));
 service.liveApi.mockImplementation(async(path:string)=>path==='tablet/state'?view:path==='tablet/command'?{...view,match:{...view.match,status:'IP'}}:{});
 window.history.replaceState(null,'','/umpire/session-test');render(<UmpireApp/>);const start=screen.getByRole('button',{name:'Set up & start match'});
 await waitFor(()=>expect((start as HTMLButtonElement).disabled).toBe(false));
 expect(screen.getByText('Umpire: Jane')).toBeTruthy();expect(screen.queryByText('Challenges left')).toBeNull();expect(screen.getByRole('button',{name:'+ Shuttle'})).toBeTruthy();
 fireEvent.click(start);fireEvent.click(screen.getByRole('button',{name:'Confirm & Start Match'}));
 expect(await screen.findByText(/30 minutes before its scheduled time/)).toBeTruthy();
 expect(service.liveApi.mock.calls.filter(c=>c[0]==='tablet/command')).toHaveLength(0);
 fireEvent.click(screen.getByRole('button',{name:'Go back'}));
 fireEvent.click(screen.getByRole('button',{name:'Confirm & Start Match'}));fireEvent.click(screen.getByRole('button',{name:'Start match',exact:true}));
 await waitFor(()=>expect(service.liveApi).toHaveBeenCalledWith('tablet/command',expect.objectContaining({action:'START',confirmEarlyStart:true,serving:{serverSide:'A',serverId:'a',receiverId:'b',leftSide:'A'}}),'token',8000));
});

test('failed new pairing preserves the previous match for recovery',async()=>{
 const view={sessionId:'session-test',serverTime:new Date().toISOString(),match:{id:'old',paperMode:true,rules:{},players:[],games:[]}};
 localStorage.setItem('tls-umpire-match',JSON.stringify({token:'old-session',view}));
 window.history.replaceState(null,'','/umpire#pair=ABCD1234');render(<UmpireApp/>);
 await screen.findByText('Access code expired. Generate a new code.');fireEvent.click(screen.getByRole('button',{name:'OK',exact:true}));
 expect(JSON.parse(localStorage.getItem('tls-umpire-match')!).token).toBe('old-session');
 expect(screen.getByRole('button',{name:'Connect Tablet'})).toBeTruthy();expect(screen.queryByText('Paper mode')).toBeNull();
});
test('umpire scoresheet is view-only',()=>{
 localStorage.setItem('tls-umpire-match',JSON.stringify({token:'old',view:{sessionId:'session-old',match:{id:'old',paperMode:false,rules:{gamesToWin:2},players:[],games:[]}}}));
 window.history.replaceState(null,'','/umpire/session-old');render(<UmpireApp/>);fireEvent.click(screen.getByRole('button',{name:'Scoresheet',exact:true}));
 expect(screen.getByRole('article',{name:'Badminton paper scoresheet'})).toBeTruthy();expect(screen.queryByRole('button',{name:'Print Scoresheet'})).toBeNull();
});

function cachedSession(pending=false){
 const view={sessionId:'public-session-1',serverTime:new Date().toISOString(),heartbeatSeconds:30,match:{id:'match-1',version:1,status:'IP',court:1,umpireName:'Jane',rules:{gamesToWin:2,pointsToWin:21},games:[],players:[],scoreA:0,scoreB:0,serving:{serverSide:'A',leftSide:'A'}}};
 localStorage.setItem('tls-umpire-match',JSON.stringify({token:'secret-scoring-token',view,...(pending?{pending:{version:1,operationId:'same-operation',action:'POINT',side:'A'}}:{})}));
 service.liveApi.mockImplementation(async(path:string)=>path==='tablet/state'?view:{});return view;
}
test('bare umpire URL always asks for access code even with a cached assignment',()=>{
 cachedSession();render(<UmpireApp/>);
 expect(screen.getByRole('button',{name:'Connect Tablet'})).toBeTruthy();expect(screen.queryByLabelText('Side A score')).toBeNull();expect(service.liveApi).not.toHaveBeenCalled();
 expect(JSON.parse(localStorage.getItem('tls-umpire-match')!).token).toBe('secret-scoring-token');
});
test('refreshing the matching session validates it with the server before enabling points',async()=>{
 cachedSession();window.history.replaceState(null,'','/umpire/public-session-1');render(<UmpireApp/>);
 const point=screen.getByRole('button',{name:'Add point to side A'});expect((point as HTMLButtonElement).disabled).toBe(true);
 await waitFor(()=>expect((point as HTMLButtonElement).disabled).toBe(false));
 expect(service.liveApi).toHaveBeenCalledWith('tablet/state',undefined,'secret-scoring-token');
 cleanup();render(<UmpireApp/>);await screen.findByText('Saved to venue');expect(window.location.pathname).toBe('/umpire/public-session-1');expect(window.location.href).not.toContain('secret-scoring-token');
});
test('a different session URL cannot restore the cached match',async()=>{
 cachedSession();window.history.replaceState(null,'','/umpire/some-other-session');render(<UmpireApp/>);
 await screen.findByText('Enter an access code to connect to this match on this device.');
 expect(window.location.pathname).toBe('/umpire');expect(screen.queryByLabelText('Side A score')).toBeNull();expect(service.liveApi).not.toHaveBeenCalled();
});
test('a copied session URL on an unpaired browser requires an access code',async()=>{
 window.history.replaceState(null,'','/umpire/public-session-1');render(<UmpireApp/>);
 await screen.findByText('Enter an access code to connect to this match on this device.');expect(window.location.pathname).toBe('/umpire');expect(service.liveApi).not.toHaveBeenCalled();
});
test('released session returns to access-code URL and clears only the expired credential',async()=>{
 cachedSession();service.liveApi.mockRejectedValue(new LiveError('Session released.',401));
 window.history.replaceState(null,'','/umpire/public-session-1');render(<UmpireApp/>);
 await screen.findByText('Session released.');expect(window.location.pathname).toBe('/umpire');expect(localStorage.getItem('tls-umpire-match')).toBeNull();
});
test('released session retains an unacknowledged score for organiser recovery',async()=>{
 cachedSession(true);service.liveApi.mockRejectedValue(new LiveError('Session released.',401));
 window.history.replaceState(null,'','/umpire/public-session-1');render(<UmpireApp/>);
 await screen.findByText('Session released.');expect(window.location.pathname).toBe('/umpire');expect(JSON.parse(localStorage.getItem('tls-umpire-match')!).pending.operationId).toBe('same-operation');
});
test('pending score on the access page blocks pairing and can resume its own URL',async()=>{
 const view=cachedSession(true);render(<UmpireApp/>);
 fireEvent.change(screen.getAllByRole('textbox')[1],{target:{value:'ABCD1234'}});fireEvent.click(screen.getByRole('button',{name:'Connect Tablet'}));
 await screen.findByText('Retry the pending scoring action before connecting to another match.');expect(service.liveApi).not.toHaveBeenCalled();
 fireEvent.click(screen.getByRole('button',{name:'OK',exact:true}));fireEvent.click(screen.getByRole('button',{name:'Resume pending match'}));
 expect(window.location.pathname).toBe('/umpire/public-session-1');await screen.findByText('Awaiting save');
 service.liveApi.mockImplementation(async(path:string)=>path==='tablet/command'?{...view,match:{...view.match,version:2,scoreA:1}}:path==='tablet/state'?view:{});
 fireEvent.click(screen.getByRole('button',{name:'Retry pending action'}));
 await waitFor(()=>expect(JSON.parse(localStorage.getItem('tls-umpire-match')!).pending).toBeUndefined());
 expect(service.liveApi).toHaveBeenCalledWith('tablet/command',expect.objectContaining({operationId:'same-operation',action:'POINT'}),'secret-scoring-token',8000);
});
test('browser back to access page and forward to session follow the URL',async()=>{
 cachedSession();window.history.replaceState(null,'','/umpire/public-session-1');render(<UmpireApp/>);await screen.findByText('Saved to venue');
 window.history.replaceState(null,'','/umpire');fireEvent(window,new PopStateEvent('popstate'));expect(screen.getByRole('button',{name:'Connect Tablet'})).toBeTruthy();
 window.history.replaceState(null,'','/umpire/public-session-1');fireEvent(window,new PopStateEvent('popstate'));await screen.findByText('Saved to venue');expect(screen.getByRole('button',{name:'Add point to side A'})).toBeTruthy();
});
