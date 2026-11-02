type ScopeView={programs:{id:string}[];entries:{programId:string;participantIds:string[]}[];participants:{id:string}[];plans:{programId:string;matches:{number:number}[]}[];standings:{programId:string}[];warnings:string[]};
export function scopeTournament<T extends ScopeView>(view:T,programId?:string):T {
  if(!programId)return view;
  const entries=view.entries.filter(e=>e.programId===programId);
  const people=new Set(entries.flatMap(e=>e.participantIds));
  const plans=view.plans.filter(p=>p.programId===programId);
  const numbers=plans.flatMap(p=>p.matches.map(m=>m.number));
  return {...view,programs:view.programs.filter(p=>p.id===programId),entries,participants:view.participants.filter(p=>people.has(p.id)),plans,standings:view.standings.filter(g=>g.programId===programId),warnings:view.warnings.filter(w=>numbers.some(n=>new RegExp('match '+n+'(?:\\D|$)','i').test(w)))};
}
