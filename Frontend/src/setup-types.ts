import type { MasterSettings } from './MasterConfig';

export type EventRecord = {id:string;name:string;venue:string;startDate:string;endDate:string;maxProgramsPerParticipant:number|null;status:string};
export type Program = {id:string;eventId:string;name:string;sport:string;entryType:string;minPlayers:number;maxPlayers:number;minAge:number;maxAge:number;gender:string;minCapacity:number;maxCapacity:number;capacityUnit:string;status:string};
export type Person = {id:string;eventId:string;name:string;dob:string;gender:string;email:string;mobile:string;club:string;nationality:string;sbaId:string};
export type Entry = {id:string;eventId:string;programId:string;participantIds:string[];name:string;seed:number|null;status:string};
export type ImportInfo = {eventId:string;source:string;sourceSystemId:string;sourceEventId:string;sourceTimeZone:string;exportedAt:string;importedAt:string;stage:string};
export type Catalog = MasterSettings & {programPlans?:{programId:string;seeds:Record<string,number|null>;seedsReviewed:boolean;seedingMode?:string;seedCount?:number;format?:string;phase?:string;lastRound?:number;groupCount?:number;advancePerGroup?:number;matches:Partial<import('./tournament-types').Match>[]}[];revision:number;timeZone:string;today:string;events:EventRecord[];programs:Program[];participants:Person[];entries:Entry[];imports:ImportInfo[]};
export type View = {data:Catalog;readiness:{programId:string;count:number;entryCount:number;ready:boolean;reason:string}[]};
export type Preview = View & {token:string;revision:number;source:string;sourceSystemId:string;sourceEventId:string;sourceTimeZone:string;exportedAt:string;replacing:boolean};
export type SourcePackage = {schemaVersion:number;source:string;sourceSystemId:string;sourceTimeZone:string;exportedAt:string;registrationClosed:boolean;hasFixtures:boolean;events:EventRecord[];programs:Program[];participants:Person[];entries:Entry[]};
export const gender:Record<string,string>={M:'Male',F:'Female',X:'Mixed',O:'Open'};
export const entryType:Record<string,string>={S:'Singles',D:'Doubles',T:'Team'};
export function formatDate(value:string) {return value ? value.slice(8,10)+' '+['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'][Number(value.slice(5,7))-1]+' '+value.slice(0,4) : '—';}
