import type {SbaRanking,SbaRankingType} from '@/types/config';
type Result<T>={data:T;error:null}|{data:null;error:{message:string}};
interface ImportSummary {importedRows:number;categories:{rankingType:string;rows:number}[];addedClubs:number;addedClubNames:string[];skippedSheets:string[];}
async function request<T>(path:string,body?:FormData):Promise<Result<T>>{
 const controller=new AbortController();const timer=setTimeout(()=>controller.abort(),60000);
 try {const response=await fetch('/api/sba/'+path,{method:body?'POST':'GET',body,signal:controller.signal});const data=await response.json();return response.ok?{data,error:null}:{data:null,error:{message:data.message||'SBA request failed.'}};}
 catch{return {data:null,error:{message:'Cannot reach the local server. Refresh the ranking list before retrying an import.'}};}finally{clearTimeout(timer);}
}
export const apiGetSbaRankings=(filters?:{type?:string})=>request<SbaRanking[]>('rankings'+(filters?.type?'?type='+encodeURIComponent(filters.type):''));
export const apiGetSbaRankingTypes=()=>request<SbaRankingType[]>('types');
export const apiImportSbaRankings=(file:File)=>{const body=new FormData();body.append('file',file);return request<ImportSummary>('import',body);};
