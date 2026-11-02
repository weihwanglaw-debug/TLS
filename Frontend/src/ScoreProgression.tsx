import type {LiveMatch} from './live-types';
export default function ScoreProgression({match}:{match:LiveMatch}){
 const rallies=match.rallies||[],games=[...new Set(rallies.map(r=>r.game))].sort((a,b)=>a-b);
 return <section className="score-progression" aria-label="Score progression"><h3>Score progression</h3><p><span style={{color:'#087eaf'}}>━ {match.nameA}</span> · <span style={{color:'#b34c00'}}>━ {match.nameB}</span></p>{games.length===0?<p>No recorded rallies yet.</p>:games.map(game=>{
 const records=rallies.map((r,index)=>({...r,index})).filter(r=>r.game===game),max=Math.max(match.rules.pointsToWin,...records.flatMap(r=>[r.a,r.b]));
 const x=(i:number)=>40+i/records.length*540,y=(score:number)=>155-score/max*140;
 const segments:{a:string;b:string}[]=[];
 records.forEach((r,i)=>{
 const prev=records[i-1],gap=!prev||prev.a!==r.beforeA||prev.b!==r.beforeB||(match.resumptions||[]).some(p=>p.game===game&&p.rallyIndex===r.index);
 if(gap)segments.push({a:`M${x(i)},${y(r.beforeA)}`,b:`M${x(i)},${y(r.beforeB)}`});
 const line=segments[segments.length-1];line.a+=` H${x(i+1)} V${y(r.a)}`;line.b+=` H${x(i+1)} V${y(r.b)}`;
 });
 return <div key={game}><h4>Game {game}</h4><svg viewBox="0 0 600 180" role="img" aria-label={`Game ${game} points by recorded rally`}><title>Game {game} score progression for {match.nameA} and {match.nameB}</title>{[0,Math.round(max/2),max].map(n=><g key={n}><line x1="40" x2="580" y1={y(n)} y2={y(n)} stroke="currentColor" opacity=".15"/><text x="30" y={y(n)+4} textAnchor="end" fill="currentColor" fontSize="11">{n}</text></g>)}{segments.map((line,i)=><g key={i}><path d={line.a} fill="none" stroke="#087eaf" strokeWidth="2"/><path d={line.b} fill="none" stroke="#b34c00" strokeWidth="2"/></g>)}<text x="40" y="175" fill="currentColor" fontSize="11">0</text><text x="580" y="175" textAnchor="end" fill="currentColor" fontSize="11">{records.length} recorded rallies</text></svg></div>;
 })}{(!match.rallyHistoryComplete||!!match.resumptions?.length)&&<p>Only recorded rallies are plotted. Gaps indicate paper/manual periods; missing rallies are not reconstructed.</p>}</section>;
}
