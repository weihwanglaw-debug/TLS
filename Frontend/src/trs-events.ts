import {api} from './api';
import type {Catalog,SourcePackage} from './setup-types';
export async function getTrsEvents():Promise<SourcePackage[]>{return api<SourcePackage[]>('trs/events');}
export function trsEligibility(p:SourcePackage,catalog:Catalog,replaceId=''):string {
  const event=p.events[0];
  const imported=catalog.imports.find(i=>i.source==='TRS'&&i.sourceSystemId===p.sourceSystemId&&i.sourceEventId===event.id);
  if(replaceId&&(!imported||imported.eventId!==replaceId))return 'Choose the original TRS event for replacement.';

  if(imported&&replaceId&&imported.stage!=='P')return 'Existing fixtures protect this event from replacement.';
  if(!p.registrationClosed)return 'Registration is still open in TRS.';
  if(p.hasFixtures)return 'Clear fixtures and any results in TRS before transferring.';
  const parts=new Intl.DateTimeFormat('en-CA',{timeZone:p.sourceTimeZone,year:'numeric',month:'2-digit',day:'2-digit'}).formatToParts(new Date());
  const date=(kind:string)=>parts.find(x=>x.type===kind)!.value;
  if(event.endDate<`${date('year')}-${date('month')}-${date('day')}`)return 'Past event.';
  if(['C','X'].includes(event.status))return 'Event is closed or archived.';
  const active=p.programs.filter(program=>program.status==='A');
  if(!active.length)return 'No active programs.';
  for(const program of active){
    const entries=p.entries.filter(e=>e.programId===program.id&&e.status==='A');
    const count=program.capacityUnit==='P'?entries.reduce((sum,e)=>sum+e.participantIds.length,0):entries.length;
    if(count<program.minCapacity||entries.some(e=>e.participantIds.length<program.minPlayers)||(program.sport.toLowerCase()==='badminton'&&['S','D'].includes(program.entryType)&&entries.length<2))return 'Minimum participation is not met.';
  }
  return '';
}
