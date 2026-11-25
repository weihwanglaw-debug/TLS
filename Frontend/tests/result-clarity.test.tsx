import {cleanup,fireEvent,render,screen,waitFor,within} from '@testing-library/react';
import {afterEach,beforeEach,expect,test,vi} from 'vitest';
import UmpireApp from '../src/UmpireApp';
import LiveDashboard from '../src/LiveDashboard';
const service=vi.hoisted(()=>({liveApi:vi.fn(),operationId:()=> 'same-submit-operation'}));
vi.mock('../src/live-api',()=>({...service,LiveError:class extends Error{constructor(message:string,public status=0){super(message);}}}));
let view:any;
function makeMatch(){return {id:'m',eventId:'e',programId:'p',number:7,eventName:'Event',programName:'Singles',roundLabel:'Group A',court:1,umpireName:'Jane',version:1,status:'IP',nameA:'Joel',nameB:'Ryan',scoreA:6,scoreB:12,games:[{a:3,b:21}],gamesWonA:0,gamesWonB:1,matchWon:false,awaitingNextGame:false,rules:{gamesToWin:2,pointsToWin:21},players:[{id:'a',name:'Joel',side:'A'},{id:'b',name:'Ryan',side:'B'}],serving:{serverSide:'B',leftSide:'B',rightA:'a',rightB:'b'},serverId:'b',receiverId:'a',resultDetails:{cards:[],challenges:[]},rallyHistoryComplete:true,rallies:[],startedAt:'2026-10-30T06:00:00Z'};}
function showUmpire(){localStorage.setItem('tls-umpire-match',JSON.stringify({token:'token',view}));window.history.replaceState(null,'','/umpire/public-session');return render(<UmpireApp/>);}
beforeEach(()=>{localStorage.clear();view={sessionId:'public-session',serverTime:new Date().toISOString(),heartbeatSeconds:30,match:makeMatch()};service.liveApi.mockReset();service.liveApi.mockImplementation(async(path:string)=>path==='tablet/state'?view:{});});
afterEach(()=>{cleanup();localStorage.clear();window.history.replaceState(null,'','/umpire');});
test.each(['A','B'])('previous scores follow their players with side %s on the left',left=>{
 view.match.serving.leftSide=left;showUmpire();
 expect(within(screen.getByLabelText('Side A court')).getByLabelText('Joel previous games').textContent).toBe('G13');
 expect(within(screen.getByLabelText('Side B court')).getByLabelText('Ryan previous games').textContent).toBe('G121');
 expect(screen.queryByText('G1: 3–21')).toBeNull();
 const courts=document.querySelectorAll('.court-team');expect(courts[0].getAttribute('aria-label')).toBe('Side '+left+' court');
});
test('doubles and a deciding game retain both earlier scores by team',()=>{
 Object.assign(view.match,{nameA:'Joel / Adrian',nameB:'Ryan / Sean',games:[{a:3,b:21},{a:21,b:15}],scoreA:8,scoreB:9,gamesWonA:1,gamesWonB:1});
 view.match.players.push({id:'a2',name:'Adrian',side:'A'},{id:'b2',name:'Sean',side:'B'});showUmpire();
 expect(screen.getByLabelText('Joel / Adrian previous games').textContent).toBe('G13G221');expect(screen.getByLabelText('Ryan / Sean previous games').textContent).toBe('G121G215');
});
function finish(){Object.assign(view.match,{matchWon:true,awaitingNextGame:true,games:[{a:3,b:21},{a:6,b:21}],scoreA:6,scoreB:21,gamesWonB:2});}
test('umpire reviews shared scoresheet, submits, then returns to access code without confirming the result',async()=>{
 finish();service.liveApi.mockImplementation(async(path:string)=>path==='tablet/command'?(view={...view,match:{...view.match,status:'RC',version:2,finishedAt:new Date().toISOString()}}):path==='tablet/state'?view:{});showUmpire();
 expect(screen.getByText('Match finished — review and submit')).toBeTruthy();expect(screen.queryByRole('button',{name:'Add point to side A'})).toBeNull();
 const review=screen.getByRole('button',{name:'Review & submit result'});await waitFor(()=>expect((review as HTMLButtonElement).disabled).toBe(false));fireEvent.click(review);
 expect(screen.getByRole('article',{name:'Badminton paper scoresheet'})).toBeTruthy();fireEvent.click(screen.getByRole('button',{name:'Back to scoreboard'}));
 expect(service.liveApi.mock.calls.filter(c=>c[0]==='tablet/command')).toHaveLength(0);
 fireEvent.click(review);fireEvent.click(screen.getByRole('button',{name:'Confirm & Submit Result'}));
 await screen.findByText('Result submitted');expect(screen.getByText('Waiting for acknowledgement')).toBeTruthy();expect(screen.queryByLabelText('Side A court')).toBeNull();expect(screen.queryByRole('button',{name:'Match actions'})).toBeNull();expect(screen.queryByRole('button',{name:'Scoresheet'})).toBeNull();fireEvent.click(screen.getByRole('button',{name:'Enter New Access Code'}));
 expect(window.location.pathname).toBe('/umpire');expect(screen.getByRole('button',{name:'Connect Tablet'})).toBeTruthy();
 expect(service.liveApi.mock.calls.filter(c=>c[0]==='tablet/command')).toHaveLength(1);expect(service.liveApi.mock.calls.some(c=>c[0]==='dashboard/confirm')).toBe(false);
 expect(JSON.parse(localStorage.getItem('tls-umpire-match')!).view.match.status).toBe('RC');
});
test('failed submission preserves pending operation and does not offer the submitted return button',async()=>{
 finish();service.liveApi.mockImplementation(async(path:string)=>{if(path==='tablet/command')throw new Error('Offline');return path==='tablet/state'?view:{};});showUmpire();
 const review=screen.getByRole('button',{name:'Review & submit result'});await waitFor(()=>expect((review as HTMLButtonElement).disabled).toBe(false));fireEvent.click(review);fireEvent.click(screen.getByRole('button',{name:'Confirm & Submit Result'}));
 await screen.findByText('Reconnecting…');expect(JSON.parse(localStorage.getItem('tls-umpire-match')!).pending.action).toBe('SUBMIT');
 expect(screen.queryByRole('dialog',{name:'Unable to complete action'})).toBeNull();expect(screen.queryByRole('button',{name:'Retry pending action'})).toBeNull();
 expect(screen.queryByRole('button',{name:'Enter New Access Code'})).toBeNull();expect(window.location.pathname).toBe('/umpire/public-session');
});
test('console aligns prior scores with each player and confirms through the shared scoresheet',async()=>{
 finish();view.match.status='RC';
 const data={revision:4,selectedEventId:'e',serverTime:new Date().toISOString(),serverLocalTime:'2026-10-30T14:00:00',timeZone:'Asia/Singapore',events:[{id:'e',name:'Event',startDate:'2026-10-30',endDate:'2026-10-31',courts:[1]}],eventContext:{calendar:'O',sessionState:'I'},courts:[{number:1,status:'F',outsideEvent:false,match:view.match,next:null,lastMatch:null,device:{state:'C',name:'Tablet',ipAddress:'192.168.1.2',lastSeen:new Date().toISOString()}}],queue:[],programs:[],presets:[],officials:[],tabletUrls:[]};
 service.liveApi.mockImplementation(async()=>structuredClone(data));render(<LiveDashboard onFixtures={()=>{}} onChanged={async()=>{}}/>);
 fireEvent.change(await screen.findByLabelText('Dashboard event'),{target:{value:'e'}});
 const scores=await screen.findByRole('table',{name:'Court match scores'});expect(within(scores).getByLabelText('Side A previous games').textContent).toBe('G13');expect(within(scores).getByLabelText('Side B previous games').textContent).toBe('G121');
 expect((screen.getByText('Device details').parentElement as HTMLDetailsElement).open).toBe(false);
 fireEvent.click(screen.getByRole('button',{name:'Confirm Result & Free Court'}));const dialog=screen.getByRole('dialog',{name:'Confirm Match Result'});
 expect(within(dialog).getByRole('article',{name:'Badminton paper scoresheet'})).toBeTruthy();fireEvent.click(within(dialog).getByRole('button',{name:'Confirm Result & Free Court'}));
 await waitFor(()=>expect(service.liveApi).toHaveBeenCalledWith('dashboard/confirm',{revision:4,matchId:'m',version:1,reason:''}));
});


test.each([['A',0,1],['A',1,0],['A',2,1],['B',0,0],['B',1,1],['B',2,0]] as const)('service courts use the player perspective with side %s on the left at score %i', (left,score,row)=>{
 view.match={...makeMatch(),scoreA:score,scoreB:0,games:[],serving:{serverSide:'A',leftSide:left,rightA:'a1',rightB:'b1'},serverId:score%2?'a2':'a1',receiverId:score%2?'b2':'b1',players:[{id:'a1',name:'Server even',side:'A'},{id:'a2',name:'Server odd',side:'A'},{id:'b1',name:'Receiver even',side:'B'},{id:'b2',name:'Receiver odd',side:'B'}]};
 showUmpire();
 const serverCourt=screen.getByLabelText('Side A court'),receiverCourt=screen.getByLabelText('Side B court');
 expect(serverCourt.querySelectorAll('.court-position')[row].textContent).toContain(score%2?'Server odd':'Server even');
 expect(receiverCourt.querySelectorAll('.court-position')[1-row].textContent).toContain(score%2?'Receiver odd':'Receiver even');
 const path=document.querySelector('.serve-arrow>path')!.getAttribute('d');
 expect(path).toBe(`M ${left==='A'?40:60},${row?72:28} L ${left==='A'?60:40},${row?28:72}`);
});

test('reloading an acknowledged submission shows only the completion screen',()=>{
 finish();view.match.status='RC';showUmpire();
 expect(screen.getByText('Result submitted')).toBeTruthy();expect(screen.getByText('Waiting for acknowledgement')).toBeTruthy();
 expect(screen.getAllByRole('button')).toHaveLength(1);expect(screen.getByRole('button',{name:'Enter New Access Code'})).toBeTruthy();
 expect(screen.queryByRole('button',{name:'Undo point'})).toBeNull();
});


test.each([['A',0,1],['A',1,0],['A',2,1],['B',0,0],['B',1,1],['B',2,0]] as const)('singles placement uses the serving score with side %s on the left at %i',(left,score,row)=>{
 view.match={...makeMatch(),games:[],scoreA:score,scoreB:9,serving:{serverSide:'A',leftSide:left,rightA:'a',rightB:'b'},serverId:'a',receiverId:'b'};
 showUmpire();
 const a=screen.getByLabelText('Side A court').querySelectorAll('.court-position');
 const b=screen.getByLabelText('Side B court').querySelectorAll('.court-position');
 expect(a[row].textContent).toContain('Joel');expect(b[1-row].textContent).toContain('Ryan');
 expect(a[1-row].textContent).toBe('');expect(b[row].textContent).toBe('');
});
