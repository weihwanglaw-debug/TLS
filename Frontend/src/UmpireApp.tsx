import {useEffect,useRef,useState} from 'react';
import {Clock,FileText,List,RotateCcw,Maximize,Minimize} from 'lucide-react';
import {LiveError,liveApi,operationId} from './live-api';
import {elapsed,type TabletView,type ServingSetup} from './live-types';
import {Dialog,DialogContent,DialogHeader,DialogTitle,DialogDescription} from './components/ui/dialog';
import UmpireChoices from './UmpireChoices';
import PaperScoresheet from './PaperScoresheet';
import './umpire.css';

type Side='A'|'B';
type Command={version:number;operationId:string;action:string;side?:string;participantId?:string;cardType?:string;outcome?:string;remark?:string;serving?:ServingSetup;confirmEarlyStart?:boolean};
type Cache={token:string;view:TabletView;pending?:Command};
type Panel='early'|'setup'|'actions'|'card'|'challenge'|'sheet'|'paper'|'submit'|null;
const other=(side:Side):Side=>side==='A'?'B':'A';
function load():Cache|undefined{try{const v=JSON.parse(localStorage.getItem('tls-umpire-match')||'null');return typeof v?.token==='string'&&v?.view?.match?.id&&v?.view?.match?.rules?v:undefined;}catch{return undefined;}}
function sessionFromUrl(){return /^\/umpire\/([A-Za-z0-9_-]+)\/*$/i.exec(window.location.pathname)?.[1]||'';}
let memoryDeviceId='';
function deviceId(){try{const saved=localStorage.getItem('tls-umpire-device');if(saved)return saved;}catch{}memoryDeviceId||=operationId();try{localStorage.setItem('tls-umpire-device',memoryDeviceId);}catch{}return memoryDeviceId;}

export default function UmpireApp(){
 const [cache,setCache]=useState<Cache|undefined>(load),[code,setCode]=useState(''),[label,setLabel]=useState('Umpire Tablet'),[busy,setBusy]=useState(false),[online,setOnline]=useState(false),[closed,setClosed]=useState(false),[error,setError]=useState(''),[now,setNow]=useState(Date.now());
 const [pointLocked,setPointLocked]=useState(false);
 const pointUntil=useRef(0),pointTimer=useRef<ReturnType<typeof setTimeout>|undefined>(undefined);
 useEffect(()=>()=>clearTimeout(pointTimer.current),[]);
 const [storageWarning,setStorageWarning]=useState(false),[sessionId,setSessionId]=useState(sessionFromUrl),[joining,setJoining]=useState(()=>!sessionFromUrl()||sessionFromUrl()!==cache?.view.sessionId);
 const [fullScreen,setFullScreen]=useState(!!document.fullscreenElement),[fullScreenBusy,setFullScreenBusy]=useState(false);
 const [panel,setPanel]=useState<Panel>(null),[setup,setSetup]=useState<ServingSetup>({serverSide:'A',serverId:'',receiverId:'',leftSide:'A'}),[reason,setReason]=useState('');
 const [cardPlayer,setCardPlayer]=useState(''),[cardType,setCardType]=useState('Y'),[challengeSide,setChallengeSide]=useState<Side>('A'),[outcome,setOutcome]=useState('U');
 const latest=useRef(cache),offset=useRef(0),sending=useRef(false),connectionError=useRef(false),match=joining||sessionId!==cache?.view.sessionId?undefined:cache?.view.match;
 useEffect(()=>{const changed=()=>setFullScreen(!!document.fullscreenElement);document.addEventListener('fullscreenchange',changed);return()=>document.removeEventListener('fullscreenchange',changed);},[]);
 async function toggleFullScreen(){
  if(fullScreenBusy)return;
  setError('');setFullScreenBusy(true);
  try{
   if(document.fullscreenElement)await document.exitFullscreen();
   else if(document.fullscreenEnabled&&document.documentElement.requestFullscreen)await document.documentElement.requestFullscreen();
   else setError('Full screen is not available in this browser. Try opening this page directly in Chrome or Edge.');
  }catch{setError('The browser could not change full-screen mode. Tap the Full Screen button again, or use the browser full-screen controls.');}
  finally{setFullScreen(!!document.fullscreenElement);setFullScreenBusy(false);}
 }
 const fullScreenButton=<button type="button" className="umpire-fullscreen" disabled={fullScreenBusy} aria-label={fullScreen?'Exit Full Screen':'Full Screen'} aria-pressed={fullScreen} onClick={()=>void toggleFullScreen()}>{fullScreen?<Minimize/>:<Maximize/>}<span>{fullScreen?'Exit Full Screen':'Full Screen'}</span></button>;
 function navigateSession(id:string,replace=false){
  const path=id?'/umpire/'+encodeURIComponent(id):'/umpire';
  if(window.location.pathname!==path||window.location.hash)window.history[replace?'replaceState':'pushState'](null,'',path);
  setSessionId(id);setJoining(!id||id!==latest.current?.view.sessionId);setOnline(false);setPanel(null);
 }
 function remember(value:Cache){latest.current=value;setCache(value);offset.current=Date.parse(value.view.serverTime)-Date.now();try{localStorage.setItem('tls-umpire-match',JSON.stringify(value));setStorageWarning(false);}catch{setStorageWarning(true);}}
 useEffect(()=>{try{document.body.dataset.theme=localStorage.getItem('livescore_theme')||'a';}catch{document.body.dataset.theme='a';}const timer=setInterval(()=>setNow(Date.now()),1000);return()=>clearInterval(timer);},[]);
 useEffect(()=>{
  if(joining||!sessionId||sessionId!==cache?.view.sessionId||!cache?.token||cache.view.match.paperMode)return;
  let cancelled=false;let timer:ReturnType<typeof setTimeout>;
  async function heartbeat(){const current=latest.current;if(!current||current.view.match.paperMode)return;
   try{await liveApi('tablet/heartbeat',{},current.token,4000);
    if(!cancelled&&latest.current?.token===current.token&&!sending.current){
     const fresh=await liveApi<TabletView>('tablet/state',undefined,current.token);
     if(!cancelled&&latest.current?.token===current.token&&!sending.current){
      if(fresh.sessionId!==sessionId||fresh.match.id!==current.view.match.id)throw new LiveError('This session does not match the requested match. Enter a new access code.',401);
      if(!latest.current.pending&&fresh.match.version>=latest.current.view.match.version)remember({token:current.token,view:fresh});
      setOnline(true);setClosed(false);connectionError.current=false;
     }
    }}
   catch(e){if(!cancelled&&latest.current?.token===current.token&&!sending.current){setOnline(false);if(e instanceof LiveError&&[401,403].includes(e.status)){setClosed(true);if(!latest.current?.pending){try{localStorage.removeItem('tls-umpire-match');}catch{}latest.current=undefined;setCache(undefined);}navigateSession('',true);if(!connectionError.current)setError(e.message);}else if(!connectionError.current)setError('Connection lost. The court shows the last confirmed score. If play continues, use paper and ask the organiser to reconcile it.');connectionError.current=true;}}
   if(!cancelled)timer=setTimeout(heartbeat,(current.view.heartbeatSeconds||5)*1000);
  }
  void heartbeat();return()=>{cancelled=true;clearTimeout(timer);};
 },[cache?.token,cache?.view.match.paperMode,joining,sessionId]);
 async function pair(invitation=code){if(sending.current)return;if(latest.current?.pending){setError('Retry the pending scoring action before connecting to another match.');return;}setJoining(true);setCode(invitation);sending.current=true;setBusy(true);setError('');try{const result=await liveApi<{token:string;match:TabletView}>('tablet/pair',{code:invitation.trim().toUpperCase(),deviceId:deviceId(),deviceName:label.trim()});remember({token:result.token,view:result.match});navigateSession(result.match.sessionId);setOnline(true);setClosed(false);setJoining(false);setPanel(null);connectionError.current=false;}catch(e){setOnline(false);setError((e as Error).message);}finally{sending.current=false;setBusy(false);}}
 useEffect(()=>{
  function scan(){
  const requested=sessionFromUrl(),saved=latest.current;
  setSessionId(requested);setJoining(!requested||requested!==saved?.view.sessionId);setOnline(false);setPanel(null);
  if(requested&&requested!==saved?.view.sessionId){navigateSession('',true);setError('Enter an access code to connect to this match on this device.');}
  if(requested&&saved?.view.match.paperMode&&!saved.pending){navigateSession('',true);setError('This match continues on paper. Ask the organiser for a new access code when tablet scoring resumes.');}
  if(!requested&&window.location.pathname.replace(/\/+$/,'').toLowerCase()!=='/umpire')navigateSession('',true);
  const invitation=new URLSearchParams(window.location.hash.slice(1)).get('pair');if(!invitation)return;
  window.history.replaceState(null,'',window.location.pathname+window.location.search);
  if(latest.current?.pending){setError('Retry the pending scoring action before connecting to another match.');return;}
  if(!/^[A-Z0-9]{8}$/i.test(invitation)){setError('Invalid pairing link. Scan a new QR code or enter the access code.');return;}
  setCode(invitation.toUpperCase());void pair(invitation);
  }
  scan();window.addEventListener('hashchange',scan);window.addEventListener('popstate',scan);
  return()=>{window.removeEventListener('hashchange',scan);window.removeEventListener('popstate',scan);};
 },[]);
 async function send(action?:string,side?:string,extra:Partial<Command>={}):Promise<boolean>{
  const current=latest.current;if(!current||sending.current)return false;
  if(current.pending&&action){setError('Retry the pending action before entering another action.');return false;}
  if(action==='POINT'){
   if(performance.now()<pointUntil.current)return false;
   pointUntil.current=performance.now()+500;setPointLocked(true);clearTimeout(pointTimer.current);
   pointTimer.current=setTimeout(()=>setPointLocked(false),500);
  }
  sending.current=true;setBusy(true);setError('');
  try{const pending=current.pending||{version:current.view.match.version,operationId:operationId(),action:action!,...extra,...(side?{side}:{})};remember({...current,pending});
  const fresh=await liveApi<TabletView>('tablet/command',pending,current.token,8000);remember({token:current.token,view:{...fresh,sessionId:fresh.sessionId||current.view.sessionId}});setOnline(!fresh.match.paperMode);connectionError.current=false;return true;}
  catch(e){if(e instanceof LiveError&&e.status!==0){setError(e.message);if([401,403].includes(e.status)){setClosed(true);setOnline(false);}else{try{remember({token:current.token,view:await liveApi<TabletView>('tablet/state',undefined,current.token)});}catch{}}}else{setOnline(false);setError('Action not acknowledged. Retry the same action when connected; do not add another point.');}return false;}
  finally{sending.current=false;setBusy(false);}
 }
 function reset(){if(latest.current?.pending){setError('An action is still unacknowledged. Ask the organiser to reconcile it before changing matches.');return;}navigateSession('');setCode('');setError('');}
 const players=(side:Side)=>match?.players.filter(p=>p.side===side)||[];
 const name=(id:string)=>match?.players.find(p=>p.id===id)?.name||'Not set';
 const game=match?Math.min(match.rules.gamesToWin*2-1,Math.max(1,match.games.length+(match.awaitingNextGame?0:1))):1;
 const left:Side=match?.serving?.leftSide||'A',right=other(left),disabled=busy||!online||closed||!!cache?.pending||!!match?.paperMode;
 const canRecord=!disabled&&match?.status==='IP',canPoint=canRecord&&!pointLocked&&!!match?.serving&&!match.awaitingNextGame&&!match.matchWon;
 const remaining=(side:Side)=>match?Math.max(0,match.rules.challengesPerGame-(match.resultDetails?.challenges.filter(c=>c.game===game&&c.side===side&&c.outcome==='U').length||0)):0;
 function openSetup(){if(!match)return;const next=match.awaitingNextGame&&!match.matchWon;const last=match.games[match.games.length-1];const side:Side=next?(last.a>last.b?'A':'B'):match.serving?.serverSide||'A';setSetup({serverSide:side,serverId:next?players(side)[0]?.id||'':match.serverId||players(side)[0]?.id||'',receiverId:next?players(other(side))[0]?.id||'':match.receiverId||players(other(side))[0]?.id||'',leftSide:next?other(left):left});setReason('');setPanel('setup');}
 function card(id?:string){setCardPlayer(id||match?.players[0]?.id||'');setCardType('Y');setReason('');setPanel('card');}
 async function action(command:string,side?:string,extra:Partial<Command>={}){if(await send(command,side,extra))setPanel(null);}
 function slot(side:Side,row:number){const team=players(side);if(!match?.serving)return;const isRight=side===left?row===0:row===1;if(team.length===1){const score=match.serving.serverSide==='A'?match.scoreA:match.scoreB;return isRight===(score%2===0)?team[0]:undefined;}const rightId=side==='A'?match.serving.rightA:match.serving.rightB;return isRight?team.find(p=>p.id===rightId):team.find(p=>p.id!==rightId);}
 const serverRow=match?.serving?((match.serving.serverSide===left)===((match.serving.serverSide==='A'?match.scoreA:match.scoreB)%2===0)?0:1):0;
 const serverOnLeft=match?.serving?.serverSide===left,interval=Math.max(0,Math.ceil(((match?.intervalUntil?Date.parse(match.intervalUntil):0)-(now+offset.current))/1000));
 const previousGames=match?.games.slice(0,match.awaitingNextGame||match.matchWon?-1:undefined)||[];
 const submitted=match?.status==='RC',finished=!!match?.matchWon||submitted;
 const winner=match?match.games.filter(g=>g.a>g.b).length>match.games.filter(g=>g.b>g.a).length?match.nameA:match.nameB:'';
 function court(side:Side){return <section className={'court-team team-'+side} aria-label={'Side '+side+' court'}><div className="court-score-block"><div className="court-game-history" aria-label={(side==='A'?match!.nameA:match!.nameB)+' previous games'}>{previousGames.map((g,i)=><span key={i}><small>G{i+1}</small><b>{side==='A'?g.a:g.b}</b></span>)}</div><strong className="court-score" aria-label={'Side '+side+' score'}>{side==='A'?match!.scoreA:match!.scoreB}</strong></div><div className="court-players">{!match!.serving?<div className="court-unset"><strong>{side==='A'?match!.nameA:match!.nameB}</strong><span>Positions not confirmed</span></div>:[0,1].map(row=>{const p=slot(side,row),cards=match!.resultDetails?.cards.filter(c=>c.participantId===p?.id)||[],warning=cards[cards.length-1];return <div key={row} className="court-position">{p&&<button className="court-player" onClick={()=>{if(canRecord&&match!.rules.cardsEnabled)card(p.id);}} aria-label={p.name+(p.id===match!.serverId?', server':p.id===match!.receiverId?', receiver':'')}><strong>{p.name}</strong><span className="player-markers">{p.id===match!.serverId?<span className="serve-label">Server</span>:p.id===match!.receiverId?<span className="serve-label">Receiver</span>:null}{warning&&<span className={'card-label card-'+warning.type}>{({Y:'Warning',R:'Red card',B:'Disqualified'} as Record<string,string>)[warning.type]}</span>}</span></button>}</div>;})}</div></section>;}
 const sideChoices=(['A','B'] as const).map(side=>({value:side,label:players(side).map(p=>p.name).join(' / ')}));
 const playerChoices=(side:Side)=>players(side).map(p=>({value:p.id,label:p.name}));
 const closePanel=()=>{if(!busy)setPanel(null);};
 function panelFooter(label?:string,onConfirm?:()=>void,blocked=false){return <footer className="umpire-dialog-footer"><button type="button" className="btn-outline" disabled={busy} onClick={closePanel}>{panel==='sheet'||panel==='actions'||panel==='submit'?'Back to scoreboard':'Cancel'}</button>{label&&<button type={onConfirm?'button':'submit'} form={onConfirm?undefined:'umpire-panel-form'} className="btn-primary" disabled={blocked||busy} onClick={onConfirm}>{busy?'Saving…':label}</button>}</footer>;}
 return <main className={'umpire-shell '+(!match?'umpire-join':'')}><Dialog open={!!error} onOpenChange={open=>{if(!open)setError('');}}><DialogContent showClose={false} className="umpire-dialog umpire-feedback" style={{backgroundColor:'var(--color-page-bg)'}}><DialogHeader className="umpire-dialog-heading"><DialogTitle>Unable to complete action</DialogTitle><DialogDescription>Please check the message below.</DialogDescription></DialogHeader><div className="umpire-dialog-body"><p role="alert">{error}</p></div><footer className="umpire-dialog-footer"><button type="button" className="btn-primary" onClick={()=>setError('')}>OK</button></footer></DialogContent></Dialog>
 {storageWarning&&<p role="alert" className="notice">Browser storage is unavailable. Keep this page open until every action is saved to the venue. Pending actions are kept on this page; refreshing would lose them.</p>}
 {!match?<><header className="umpire-header"><strong className="umpire-brand">Umpire</strong><div className="umpire-header-tools">{fullScreenButton}</div></header><form className="umpire-pair" onSubmit={e=>{e.preventDefault();void pair();}}><h1>Join Assigned Match</h1><label className="form-label">Tablet name<input required maxLength={60} className="field-input" value={label} onChange={e=>setLabel(e.target.value)}/></label><label className="form-label">Access code from organiser<input autoComplete="off" required minLength={8} maxLength={8} className="field-input uppercase tracking-widest" value={code} onChange={e=>setCode(e.target.value.toUpperCase())}/></label><button disabled={busy} className="btn-primary action">{busy?'Connecting...':'Connect Tablet'}</button>{cache?.pending&&<><p role="alert">A scoring action still needs acknowledgement. Resume that match before connecting to another.</p><button type="button" className="btn-outline action" disabled={busy||!cache.view.sessionId} onClick={()=>navigateSession(cache.view.sessionId)}>Resume pending match</button></>}</form></>:<>
 <header className="umpire-header"><div className="umpire-identity"><strong>Umpire: {match.umpireName}</strong><small title={match.eventName}>{match.eventName}</small>{cache?.pending?<button disabled={busy||closed} onClick={()=>void send()}>Retry pending action</button>:match.paperMode?<small>Reconcile paper result in console</small>:closed?<button onClick={reset}>Enter new access code</button>:match.status==='RC'?<small>Awaiting organiser confirmation</small>:null}</div><div className="umpire-match"><h1>Court {String(match.court||'').padStart(2,'0')}</h1><p>{match.programName} · {match.roundLabel} · Match {match.number}</p></div><div className="umpire-header-tools"><span className={'venue-status '+(online&&!cache?.pending?'saved':'unsaved')} aria-live="polite">{match.paperMode?'Paper mode':closed?'Session closed':cache?.pending?'Awaiting save':online?'Saved to venue':'Disconnected'}</span>{fullScreenButton}</div></header>
 <div className="umpire-gamebar"><strong>{finished?'Match finished':`Game ${game} of ${match.rules.gamesToWin*2-1}`}</strong><span>{match.rules.pointsToWin} points · {match.rules.gamesToWin} games to win</span><span><Clock size={16}/>Match {elapsed(match,now+offset.current)}{interval>0&&<small> · Interval {Math.floor(interval/60)}:{String(interval%60).padStart(2,'0')}</small>}</span></div>
 <div className="umpire-court">{court(left)}{court(right)}{match.serving&&!match.awaitingNextGame&&!match.matchWon&&<svg className="serve-arrow" viewBox="0 0 100 100" preserveAspectRatio="none" aria-label={'Serve from '+name(match.serverId)+' to '+name(match.receiverId)}><defs><marker id="serve-tip" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse"><path d="M 0 0 L 10 5 L 0 10 z"/></marker></defs><path d={`M ${serverOnLeft?40:60},${serverRow?72:28} L ${serverOnLeft?60:40},${serverRow?28:72}`} markerEnd="url(#serve-tip)"/></svg>}</div>
 <div className="umpire-counters">{(match.rules.challengesEnabled&&match.reviewAvailable?[left,'shuttle',right]:['shuttle']).map(side=>side==='shuttle'?<div key={side} className="counter shuttles"><span>Shuttles used</span><div><strong>{match.resultDetails?.shuttlesUsed??'—'}</strong><button disabled={!canRecord||!match.rules.countShuttles} onClick={()=>void send('SHUTTLE')}>+ Shuttle</button></div></div>:<button key={side} className={'counter team-'+side} disabled={!canRecord||!match.rules.challengesEnabled||!match.reviewAvailable} onClick={()=>{setChallengeSide(side as Side);setOutcome('U');setPanel('challenge');}}><span>Challenges left</span><strong>{match.rules.challengesEnabled&&match.reviewAvailable?remaining(side as Side):'—'}</strong></button>)}</div>
 <div className="umpire-point-row">{finished?<div className="umpire-result-state" role="status"><div><strong>{submitted?'Result submitted — awaiting organiser confirmation':'Match finished — review and submit'}</strong><span>Winner: {winner} · {match.games.map(g=>g.a+'–'+g.b).join(', ')}</span></div>{submitted?<button className="btn-primary" disabled={busy||!!cache?.pending} onClick={reset}>Back to access code</button>:<button className="btn-primary" disabled={disabled} onClick={()=>setPanel('submit')}>Review &amp; submit result</button>}</div>:match.status==='SC'||!match.serving||match.awaitingNextGame&&!match.matchWon?<button className="match-step-button" disabled={disabled} onClick={openSetup}>{match.status==='SC'?'Set up & start match':match.awaitingNextGame?'Set up next game':'Confirm serving positions'}</button>:[left,right].map(side=><button key={side} className={'point-button team-'+side} disabled={!canPoint} aria-label={'Add point to side '+side} onClick={()=>void send('POINT',side)}>+ Point</button>)}</div>
 <nav className="umpire-actions" aria-label="Match controls"><button className="umpire-undo" disabled={!canRecord||!match.canUndo} onClick={()=>void send('UNDO')}><RotateCcw/>Undo last point</button><button disabled={busy} onClick={()=>setPanel('actions')}><List/>Match actions</button><button onClick={()=>setPanel('sheet')}><FileText/>Scoresheet</button><button className="paper-button" disabled={busy||match.paperMode} onClick={()=>{setReason('Continue on paper after a technical problem');setPanel('paper');}}><FileText/>Continue on paper</button></nav>

 </>}
 {match&&panel&&<Dialog open onOpenChange={open=>{if(!open&&!busy)setPanel(null);}}><DialogContent showClose={false} className="umpire-dialog" style={{backgroundColor:'var(--color-page-bg)'}}>
 <div className="umpire-dialog-heading"><DialogHeader><DialogTitle>{({early:'Start match early?',setup:match.status==='SC'?'Set Up Match':match.awaitingNextGame?'Set Up Next Game':'Confirm Serving Positions',actions:'Match Actions',card:'Record Player Card',challenge:'Record Challenge',sheet:'Scoresheet',paper:'Continue on Paper',submit:'Submit Match Result'} as Record<string,string>)[panel]}</DialogTitle><DialogDescription>{match.programName} · Court {match.court} · Match {match.number}</DialogDescription></DialogHeader><button type="button" className="btn-outline" aria-label="Close" disabled={busy} onClick={closePanel}>Close</button></div>
 {panel==='early'?<><div className="umpire-dialog-body"><p>This match is starting {match.earlyStartMinutes} minutes before its scheduled time. Start now?</p></div><footer className="umpire-dialog-footer"><button className="btn-outline" disabled={busy} onClick={()=>setPanel('setup')}>Go back</button><button className="btn-primary" disabled={disabled} onClick={()=>void action('START',undefined,{serving:setup,confirmEarlyStart:true})}>Start match</button></footer></>:panel==='setup'?<>
 <form id="umpire-panel-form" className="umpire-dialog-body umpire-form" onSubmit={e=>{e.preventDefault();if(match.status==='SC'&&(match.earlyStartMinutes||0)>0){setPanel('early');return;}void action(match.status==='SC'?'START':match.awaitingNextGame?'NEXT':'SETUP',undefined,{serving:setup,remark:reason});}}>
 <UmpireChoices label="Serving side" value={setup.serverSide} disabled={busy||match.awaitingNextGame} options={sideChoices} onChange={value=>{const side=value as Side;setSetup({...setup,serverSide:side,serverId:players(side)[0]?.id||'',receiverId:players(other(side))[0]?.id||''});}}/>
 <div className="umpire-choice-columns"><UmpireChoices label="Server" value={setup.serverId} disabled={busy} options={playerChoices(setup.serverSide)} onChange={serverId=>setSetup({...setup,serverId})}/><UmpireChoices label="Receiver" value={setup.receiverId} disabled={busy} options={playerChoices(other(setup.serverSide))} onChange={receiverId=>setSetup({...setup,receiverId})}/></div>
 <UmpireChoices label="Left side from the umpire’s view" value={setup.leftSide} disabled={busy||match.awaitingNextGame&&!!match.serving} options={sideChoices} onChange={value=>setSetup({...setup,leftSide:value as Side})}/>
 {match.status==='IP'&&!match.awaitingNextGame&&match.serving&&<label className="form-label">Reason for correcting positions<input required maxLength={300} className="field-input" value={reason} onChange={e=>setReason(e.target.value)}/></label>}<p>Confirm actual players and ends. These are service positions, not movement during a rally.</p></form>
 {panelFooter(match.status==='SC'?'Confirm & Start Match':match.awaitingNextGame?'Confirm & Start Next Game':'Confirm Positions',undefined,disabled||!setup.serverId||!setup.receiverId)}
 </>:panel==='actions'?<><div className="umpire-dialog-body umpire-action-list"><button disabled={disabled||match.matchWon} onClick={openSetup}>{match.status==='SC'?'Set up & start match':match.awaitingNextGame?'Set up next game':!match.serving?'Confirm current positions':'Correct serving positions / ends'}</button><button disabled={!canRecord||!match.rules.cardsEnabled} onClick={()=>card()}>Record player card</button><button disabled={!canRecord||!match.rules.challengesEnabled||!match.reviewAvailable} onClick={()=>{setChallengeSide('A');setOutcome('U');setPanel('challenge');}}>Record challenge</button>{match.matchWon&&match.status==='IP'&&<button disabled={disabled} onClick={()=>setPanel('submit')}>Submit match result</button>}<button disabled={busy||!!cache?.pending} onClick={reset}>Change match</button><p>{match.rules.name} · {match.rules.pointsToWin} points · cap {match.rules.pointCap}. Card and challenge records do not award points automatically.</p></div>{panelFooter()}</>
 :panel==='card'?<><form id="umpire-panel-form" className="umpire-dialog-body umpire-form" onSubmit={e=>{e.preventDefault();void action('CARD',undefined,{participantId:cardPlayer,cardType,remark:reason});}}><UmpireChoices label="Player" value={cardPlayer} disabled={busy} options={match.players.map(p=>({value:p.id,label:p.name}))} onChange={setCardPlayer}/><UmpireChoices label="Card" value={cardType} disabled={busy} options={[{value:'Y',label:'Yellow — warning'},{value:'R',label:'Red — misconduct fault'},{value:'B',label:'Black — referee decision'}]} onChange={setCardType}/><label className="form-label">Remark<input maxLength={300} className="field-input" value={reason} onChange={e=>setReason(e.target.value)}/></label><p>Game {game}. Apply any resulting score separately.</p></form>{panelFooter('Confirm Card',undefined,!canRecord||!cardPlayer)}</>
 :panel==='challenge'?<><form id="umpire-panel-form" className="umpire-dialog-body umpire-form" onSubmit={e=>{e.preventDefault();void action('CHALLENGE',challengeSide,{outcome});}}><UmpireChoices label="Challenging side" value={challengeSide} disabled={busy} options={sideChoices.map(s=>({...s,label:s.label+' — '+remaining(s.value as Side)+' left'}))} onChange={value=>setChallengeSide(value as Side)}/><UmpireChoices label="Outcome" value={outcome} disabled={busy} options={[{value:'U',label:'Unsuccessful'},{value:'S',label:'Successful'},{value:'C',label:'Cancelled'}]} onChange={setOutcome}/></form>{panelFooter('Confirm Challenge',undefined,!canRecord||outcome==='U'&&remaining(challengeSide)===0)}</>
 :panel==='sheet'?<><div className="umpire-dialog-body umpire-scoresheet"><PaperScoresheet match={match}/></div>{panelFooter()}</>
 :panel==='paper'?<><form id="umpire-panel-form" className="umpire-dialog-body umpire-form" onSubmit={e=>{e.preventDefault();if(online&&!cache?.pending)void action('PAPER',undefined,{remark:reason});else setPanel(null);}}><p>Last confirmed score: {match.scoreA}–{match.scoreB}. Completed games: {match.games.map(g=>g.a+'–'+g.b).join(', ')||'None'}.</p>{online&&!cache?.pending?<><label className="form-label">Reason<input required maxLength={300} className="field-input" value={reason} onChange={e=>setReason(e.target.value)}/></label><p>This revokes tablet scoring and keeps the court occupied. Ask the organiser to reconcile the final paper result.</p></>:<p>The server has not confirmed paper mode. Tell the organiser, write down any unacknowledged action and continue on paper. Reconcile after recovery; do not assume an unsaved point was accepted.</p>}</form>{panelFooter(online&&!cache?.pending?'Confirm Paper Mode':'Understood',undefined,online&&!cache?.pending&&match.status!=='IP')}</>
 :<><div className="umpire-dialog-body umpire-scoresheet"><PaperScoresheet match={match}/></div>{panelFooter('Confirm & Submit Result',()=>void action('SUBMIT'),disabled)}</>}
 </DialogContent></Dialog>}

 </main>;
}
