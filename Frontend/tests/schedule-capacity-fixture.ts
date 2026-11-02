import type {View} from '../src/tournament-types';
// A predictable server response for screen tests; real placement rules are tested through SQL integration.
export function capacityReply(view:View,body:any,limit=12){
 const pids=view.plans.map(p=>p.programId);
 return {revision:body.revision,matches:[],sessions:body.sessions.map((w:any,index:number)=>{
  let free=limit;
  const fits=new Map(pids.map(pid=>{const n=Math.min(free,w.programLimits?.[pid]||0);free-=n;return [pid,n];}));
  return {index,capacity:limit,programs:pids.map(pid=>({programId:pid,requested:w.programLimits?.[pid]||0,scheduled:fits.get(pid)||0,maximum:Math.max(fits.get(pid)||0,limit-Object.entries(w.programLimits||{}).reduce((n,[key,value])=>n+(key===pid?0:Number(value)),0))}))};
 })};
}
