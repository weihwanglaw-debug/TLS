import ScoreProgression from './ScoreProgression';
import type {LiveMatch} from './live-types';
import {elapsed,venueTime} from './live-types';
import './paper-scoresheet.css';

type Cell={a:number;b:number;winner?:string;label:string;resume?:boolean};
const columns=24;
export default function PaperScoresheet({match}:{match:LiveMatch}){
 const wonA=match.games.filter(g=>g.a>g.b).length,wonB=match.games.filter(g=>g.b>g.a).length;
 const matchWinner=match.matchWon&&wonA!==wonB?(wonA>wonB?'A':'B'):null;
 const names=(side:string)=>match.players.filter(p=>p.side===side).map(p=><span key={p.id}>{p.name}</span>);
 const rallies=match.rallies||[],resumptions=match.resumptions||[];
 const gameCount=Math.max(1,match.games.length+(!match.awaitingNextGame&&!match.matchWon?1:0),...rallies.map(r=>r.game),...resumptions.map(r=>r.game));
 const time=(v:string|null)=>v?venueTime(v,match.timeZone||'Asia/Singapore'):'—';
 return <article className="paper-sheet" aria-label="Badminton paper scoresheet"><header className="paper-sheet-heading"><strong>TLS · BADMINTON SCORESHEET</strong><h2>{match.eventName}</h2><p>{match.programName} · {match.roundLabel}</p></header><div className="paper-sheet-meta"><span>Match: <b>{match.number}</b></span><span>Court: <b>{match.court}</b></span><span>Umpire: <b>{match.umpireName}</b></span><span>Start: <b>{time(match.startedAt)}</b></span><span>Finish: <b>{time(match.finishedAt)}</b></span><span>Duration: <b>{elapsed(match,Date.now())}</b></span><span>Shuttles used: <b>{match.resultDetails?.shuttlesUsed??'Not recorded'}</b></span></div><div className="paper-sheet-summary"><div className={'paper-entry '+(matchWinner==='A'?'paper-match-winner':'')} aria-label="Side A players"><small>Side A {matchWinner==='A'&&<b className="paper-winner-badge">Winner</b>}</small>{names('A')}</div><div className="paper-game-results" aria-label="Game scores">{Array.from({length:match.rules.gamesToWin*2-1},(_,i)=><div key={i}><span>Game {i+1}</span><b>{match.games[i]?`${match.games[i].a} : ${match.games[i].b}`:'— : —'}</b></div>)}</div><div className={'paper-entry paper-side-b '+(matchWinner==='B'?'paper-match-winner':'')} aria-label="Side B players"><small>Side B {matchWinner==='B'&&<b className="paper-winner-badge">Winner</b>}</small>{names('B')}</div></div>{!match.rallyHistoryComplete&&<p className="paper-sheet-note">Paper/manual periods are incomplete in the rally record. Only recorded rallies are shown; resumed scores are marked separately.</p>}
 {Array.from({length:gameCount},(_,i)=>i+1).map(game=>{
  const cells:Cell[]=[];
  for(let j=0;j<=rallies.length;j++){
   for(const r of resumptions.filter(r=>r.game===game&&r.rallyIndex===j))cells.push({a:r.a,b:r.b,resume:true,label:`Resumed at ${r.a}–${r.b}`});
   const r=rallies[j];if(r?.game===game)cells.push({a:r.a,b:r.b,winner:r.winnerSide,label:`Rally ${j+1}: ${r.a}–${r.b}`});
  }
  const strips=Math.max(1,Math.ceil(cells.length/columns));
  const completed=match.games[game-1];
  const gameWinner=completed&&completed.a!==completed.b?(completed.a>completed.b?'A':'B'):null;
  const total=completed||{a:match.scoreA,b:match.scoreB};
  return <section className="paper-game" key={game} aria-label={`Game ${game} scoring grid`}><h3>Game {game} <span>{match.games[game-1]?'Completed':match.matchWon?'Completed':'In progress'} · {total.a}–{total.b}</span></h3>{resumptions.filter(r=>r.game===game).map((r,j)=><p className="paper-sheet-note" key={j}>Resumed at {r.a}–{r.b} · preceding paper rallies not reconstructed.</p>)}{Array.from({length:strips},(_,strip)=>{
   const part=cells.slice(strip*columns,(strip+1)*columns),last=strip===strips-1;
   return <div className={'paper-strip-scroll '+(strip>0?'paper-strip-continuation':'')} key={strip}>{strips>1&&<div className="paper-strip-label">{strip>0?`Game ${game} — continued`:'Rally record'}{!last&&' · continues below ↓'}</div>}<div className="paper-strip" style={{gridTemplateColumns:`190px repeat(${columns}, minmax(26px, 1fr)) 56px`}}>{(['A','B'] as const).map(side=><div className={'paper-score-band '+(side==='B'?'paper-side-b':'')} key={side}><div className={'paper-player-names '+(gameWinner===side?'paper-game-winner':'')} title={gameWinner===side?'Game winner':undefined}>{names(side)}</div>{Array.from({length:columns},(_,col)=>{const cell=part[col],value=cell&&(cell.resume||cell.winner===side)?(side==='A'?cell.a:cell.b):'';return <span className={'paper-rally-cell '+(cell?.resume?'paper-resume-cell':'')} key={col} aria-label={cell?`${cell.label}, side ${side}${value!==''?': '+value:''}`:undefined} title={cell?.label}>{value}{cell?.resume&&<small>↗</small>}</span>;})}{last?<strong className={'paper-strip-total '+(gameWinner===side?'paper-game-winner':'')} aria-label={`Game ${game} side ${side} ${completed?'final':'current'} score${gameWinner===side?', game winner':''}`}>{side==='A'?total.a:total.b}</strong>:<span className="paper-strip-tail" aria-hidden="true"/>}</div>)}</div></div>;
  })}</section>;
 })}{match.resultDetails?.cards.map((c,i)=><p key={'c'+i} className="paper-sheet-note">Game {c.game}: {match.players.find(p=>p.id===c.participantId)?.name||'Player'} · {({Y:'Yellow warning',R:'Red fault',B:'Black disqualification'} as Record<string,string>)[c.type]} {c.remark}</p>)}{match.resultDetails?.challenges.map((c,i)=><p key={'h'+i} className="paper-sheet-note">Game {c.game}: Side {c.side} challenge · {({S:'Successful',U:'Unsuccessful',C:'Cancelled'} as Record<string,string>)[c.outcome]}</p>)}<ScoreProgression match={match}/></article>;
}
