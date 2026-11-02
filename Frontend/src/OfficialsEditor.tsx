export type OfficialRow={officialId:string;position:'U'|'S'|'L'};
type Assignment={umpireId?:string|null;serviceJudgeId?:string|null;lineJudgeIds?:string[]};
type Official={id:string;name:string};
export function officialRows(value:Assignment):OfficialRow[]{return [{officialId:value.umpireId||'',position:'U'},...(value.serviceJudgeId?[{officialId:value.serviceJudgeId,position:'S' as const}]:[]),...(value.lineJudgeIds||[]).map(officialId=>({officialId,position:'L' as const}))];}
export function officialAssignment(rows:OfficialRow[]){return {umpireId:rows.find(r=>r.position==='U')?.officialId||'',serviceJudgeId:rows.find(r=>r.position==='S')?.officialId||null,lineJudgeIds:rows.filter(r=>r.position==='L').map(r=>r.officialId).filter(Boolean)};}
export function officialsError(rows:OfficialRow[],officials:Official[],busy?:(id:string)=>string|undefined){
 if(rows.filter(r=>r.position==='U').length!==1)return 'Assign exactly one umpire.';
 if(rows.filter(r=>r.position==='S').length>1)return 'Assign at most one service judge.';
 if(rows.some(r=>!r.officialId))return 'Choose an official for every position.';
 if(rows.some(r=>!officials.some(o=>o.id===r.officialId)))return 'Choose officials from this event.';
 if(new Set(rows.map(r=>r.officialId)).size!==rows.length)return 'An official can hold only one position in this match.';
 if(rows.some(r=>busy?.(r.officialId)))return 'An assigned official is busy. Choose an available official.';
 return '';
}
export default function OfficialsEditor({rows,officials,onChange,disabled=false,busy}:{rows:OfficialRow[];officials:Official[];onChange:(rows:OfficialRow[])=>void;disabled?:boolean;busy?:(id:string)=>string|undefined}){
 const error=officialsError(rows,officials,busy);
 return <fieldset disabled={disabled} className="space-y-3"><legend className="form-label">Officials</legend>{rows.map((row,index)=><div key={index} className="flex flex-wrap items-end gap-2"><label className="form-label flex-1 min-w-[140px]">Official<select required aria-label={'Official '+(index+1)} className="field-input w-full" value={row.officialId} onChange={e=>onChange(rows.map((r,i)=>i===index?{...r,officialId:e.target.value}:r))}><option value="">Choose official</option>{officials.map(o=>{const occupied=busy?.(o.id),selected=rows.some((r,i)=>i!==index&&r.officialId===o.id);return <option key={o.id} value={o.id} disabled={!!occupied||selected}>{o.name}{occupied?' · Busy'+occupied:selected?' · Already selected':''}</option>;})}</select></label><label className="form-label flex-1 min-w-[120px]">Position<select aria-label={'Position '+(index+1)} className="field-input w-full" value={row.position} onChange={e=>onChange(rows.map((r,i)=>i===index?{...r,position:e.target.value as OfficialRow['position']}:r))}>{([{value:'U',label:'Umpire'},{value:'S',label:'Service judge'},{value:'L',label:'Line judge'}] as const).map(p=><option key={p.value} value={p.value} disabled={p.value!=='L'&&rows.some((r,i)=>i!==index&&r.position===p.value)}>{p.label}</option>)}</select></label><button type="button" className="btn-outline action" aria-label={'Remove official '+(index+1)} onClick={()=>onChange(rows.filter((_,i)=>i!==index))}>Remove</button></div>)}<button type="button" className="btn-outline action" onClick={()=>onChange([...rows,{officialId:'',position:rows.some(r=>r.position==='U')?(rows.some(r=>r.position==='S')?'L':'S'):'U'}])}>+ Add official</button>{error&&<p className="text-xs" role="status">{error}</p>}</fieldset>;
}
