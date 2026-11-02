import type {EventRecord} from './setup-types';

export type EventDateStatus='U'|'O'|'C';
export function eventDateStatus(event:Pick<EventRecord,'startDate'|'endDate'>,today:string):EventDateStatus {
  return today<event.startDate?'U':today>event.endDate?'C':'O';
}
const styles:Record<EventDateStatus,{bg:string;text:string;label:string}>={
  U:{bg:'var(--badge-soon-bg)',text:'var(--badge-soon-text)',label:'Upcoming'},
  O:{bg:'var(--badge-open-bg)',text:'var(--badge-open-text)',label:'Ongoing'},
  C:{bg:'var(--badge-closed-bg)',text:'var(--badge-closed-text)',label:'Event Over'},
};
// Markup and theme variables reused from TRS StatusBadge. Today is supplied by the TLS server in its master timezone.
export default function EventDateBadge({event,today}:{event:Pick<EventRecord,'startDate'|'endDate'>;today:string}) {
  const s=styles[eventDateStatus(event,today)];
  return <span className="inline-flex items-center px-2.5 py-1 text-xs font-semibold" style={{backgroundColor:s.bg,color:s.text}}>{s.label}</span>;
}
