import {Switch} from './components/ui/switch';
import {useEffect,useRef,useState} from 'react';
import {ArrowLeft,Download,Upload,FileSpreadsheet} from 'lucide-react';
import {api} from './api';
import {EventDetails} from './FoundationPages';
import {FG,FilterBar,SectionTitle,Table} from './FoundationUi';
import {formatDate,type Preview,type SourcePackage,type View} from './setup-types';
import {getTrsEvents,trsEligibility} from './trs-events';

export default function EventImport({view,kind,replaceId,busy,work,onDone,onBack}:{view:View;kind:'excel'|'trs';replaceId:string;busy:boolean;work:(action:()=>Promise<void>)=>Promise<void>;onDone:(view:View,eventId:string)=>void;onBack:()=>void}) {
  const [file,setFile]=useState<File>(),[preview,setPreview]=useState<Preview>(),[ack,setAck]=useState(false);
  const importing=useRef(false);
  const fileInput=useRef<HTMLInputElement>(null);
  const [events,setEvents]=useState<SourcePackage[]>([]),[loading,setLoading]=useState(false),[loadError,setLoadError]=useState(''),[search,setSearch]=useState('');
  useEffect(()=>{setPreview(undefined);setAck(false);},[view.data.revision]);
  useEffect(()=>{let active=true;if(kind==='trs'){setLoading(true);getTrsEvents().then(p=>{if(active)setEvents(p);}).catch(()=>{if(active)setLoadError('Events could not be loaded. Go back and try again.');}).finally(()=>{if(active)setLoading(false);});}return ()=>{active=false;};},[kind]);
  async function validate(body:Blob|string) {
    setPreview(undefined);setAck(false);
    const query=new URLSearchParams({kind,revision:String(view.data.revision)});if(replaceId)query.set('replaceEventId',replaceId);
    const response=await fetch('/api/setup/preview?'+query,{method:'POST',headers:{'Content-Type':'application/octet-stream'},body});
    const result=await response.json() as Preview&{message?:string};if(!response.ok)throw new Error(result.message||'Event validation failed.');
    if(kind==='excel'&&!replaceId){
      const next=await api<View>('setup/confirm','POST',{revision:result.revision,token:result.token});
      onDone(next,result.data.events[0].id);
    }else setPreview(result);
  }
  async function importExcel(){
    if(importing.current||busy||!file)return;
    importing.current=true;
    try{await work(async()=>{if(file.size>1_500_000)throw new Error('Choose a workbook smaller than 1.5 MB.');await validate(file);});}
    finally{importing.current=false;}
  }
  const replacement=view.data.events.find(e=>e.id===replaceId);
  const eligibleEvents=events.filter(p=>!trsEligibility(p,view.data,replaceId));
  const visibleEvents=eligibleEvents.filter(p=>p.events[0].name.toLowerCase().includes(search.trim().toLowerCase()));
  return <><button disabled={busy} className="btn-outline flex items-center gap-2 px-4 py-2 text-sm mb-6" onClick={onBack}><ArrowLeft className="h-4 w-4"/>Back to events</button>
    {replacement&&<p className="notice">Replacing preparation for {replacement.name}. Use the original source and event ID. Nothing changes until you confirm.</p>}
    {kind==='excel'?<div className="mb-8 p-6 md:p-8" style={{border:'1px solid var(--color-table-border)',backgroundColor:'var(--color-row-hover)'}}><SectionTitle>Import Excel</SectionTitle>
      <div className="flex flex-wrap items-center justify-between gap-4 mb-6"><div><p className="text-sm">Select your completed template, then click {replaceId?'Validate and Review':'Import Event'}.</p><p className="text-xs opacity-70 mt-2">One workbook per event, with Event, Program and Participant sheets.</p></div><a className="btn-outline inline-flex items-center gap-2 px-5 py-2.5 text-sm font-medium" href="/api/setup/template"><Download className="h-4 w-4"/>Download Template</a></div>
      <input ref={fileInput} className="sr-only" aria-label="Excel workbook" tabIndex={-1} type="file" accept=".xlsx" disabled={busy} onChange={e=>{const selected=e.target.files?.[0];if(selected)setFile(selected);setPreview(undefined);setAck(false);}}/>
      <div className="border-2 border-dashed p-6 md:p-8 flex flex-col items-center gap-4 text-center" style={{borderColor:'var(--color-primary)',backgroundColor:'var(--color-page-bg)'}}>
        {file?<FileSpreadsheet className="h-10 w-10" aria-hidden="true" style={{color:'var(--color-primary)'}}/>:<Upload className="h-10 w-10" aria-hidden="true" style={{color:'var(--color-primary)'}}/>}
        <div aria-live="polite" className="min-w-0 max-w-full">{file?<><p className="font-semibold text-sm break-all">{file.name}</p><p className="text-xs opacity-70 mt-2">{Math.max(1,Math.ceil(file.size/1024))} KB · Ready to import</p></>:<><p className="font-semibold">Choose your completed Excel file</p><p className="text-sm opacity-70 mt-2">Excel workbook (.xlsx) · Maximum 1.5 MB</p></>}</div>
        <button type="button" className="btn-outline inline-flex items-center gap-2 px-6 py-3 text-sm font-semibold" disabled={busy} onClick={()=>fileInput.current?.click()}><Upload className="h-4 w-4"/>{file?'Change File':'Select Excel File'}</button>
      </div>
      <div className="flex flex-wrap items-center gap-4 mt-6"><button type="button" className="btn-primary inline-flex items-center gap-2 px-6 py-3 text-sm font-semibold" disabled={busy||!file} onClick={()=>void importExcel()}><Upload className="h-4 w-4"/>{busy?(replaceId?'Validating...':'Importing...'):(replaceId?'Validate and Review':'Import Event')}</button><p className="text-xs opacity-70">{replaceId?'Review the validated workbook before replacing existing preparation.':file?'The file will be checked and imported in one step.':'Select an Excel file to enable import.'}</p></div>
    </div>:<>
      <p className="notice">Select an event supplied by TRS. If no events appear, check the TRS connection or transfer file with your system administrator.</p><FilterBar><FG label="Search"><input aria-label="Search TRS events" className="field-input w-full md:w-72" placeholder="Event name..." value={search} disabled={busy} onChange={e=>setSearch(e.target.value)}/></FG></FilterBar>
      {loading?<p>Loading events...</p>:loadError?<p role="alert">{loadError}</p>:<Table><thead><tr><th>Event Name</th><th>Sport</th><th>Event Dates</th><th>Programs</th><th>Actions</th></tr></thead><tbody>{visibleEvents.map(p=>{const e=p.events[0];return <tr key={p.sourceSystemId+':'+e.id}><td className="font-medium">{e.name}</td><td>{[...new Set(p.programs.map(p=>p.sport))].join(', ')}</td><td>{formatDate(e.startDate)} – {formatDate(e.endDate)}</td><td>{p.programs.length}</td><td><button className="btn-primary px-4 py-2 text-xs font-semibold" disabled={busy} onClick={()=>void work(async()=>{const reason=trsEligibility(p,view.data,replaceId);if(reason)throw new Error(reason);await validate(JSON.stringify(p));})}>{busy?'Please wait...':'Select Event'}</button></td></tr>})}{!visibleEvents.length&&<tr><td colSpan={5} className="text-center py-10 opacity-40">{eligibleEvents.length?"No matching events found.":"No events available for import."}</td></tr>}</tbody></Table>}
    </>}
    {preview&&<section className="mt-8"><SectionTitle>Review Event Before Import</SectionTitle><p className="text-sm opacity-70 mb-5">{preview.data.programs.length} programs · {preview.data.participants.length} participants. Nothing has been saved yet.</p><EventDetails key={preview.token} view={preview} eventId={preview.data.events[0].id}/><div className="p-5 mt-6" style={{border:'1px solid var(--color-table-border)',backgroundColor:'var(--color-row-hover)'}}><label className="flex gap-3 items-start text-sm mb-5"><Switch checked={ack} disabled={busy} onCheckedChange={checked=>setAck(checked)}/>{preview.replacing?'I have checked the event and agree to replace the existing preparation.':'I have checked the event. Imported information will be read-only.'}</label><button className="btn-primary px-5 py-2.5 text-sm font-semibold" disabled={busy||!ack} onClick={()=>void work(async()=>{const next=await api<View>('setup/confirm','POST',{revision:preview.revision,token:preview.token});onDone(next,preview.data.events[0].id);})}>{busy?'Saving...':preview.replacing?'Confirm Replacement':'Confirm Import'}</button></div></section>}
  </>;
}
