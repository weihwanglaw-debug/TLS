import DatePicker from './DatePicker';
import {formatDate} from './setup-types';

export default function EventDatesEditor({start,end,editing,onStart,onEnd}:{start:string;end:string;editing:boolean;onStart:(value:string)=>void;onEnd:(value:string)=>void}){
 return <div className="md:col-span-2"><div className="grid md:grid-cols-2 gap-6">{editing?<><div className="form-label"><span>Start Date</span><DatePicker label="Start Date" value={start} onChange={onStart} required/></div><div className="form-label"><span>End Date</span><DatePicker label="End Date" value={end} onChange={onEnd} required min={start}/></div></>:<>{[['Start Date',start],['End Date',end]].map(([label,value])=><div key={label}><label className="block text-xs font-semibold mb-2 opacity-70">{label}</label><p className="text-sm">{formatDate(value)}</p></div>)}</>}</div></div>;
}
