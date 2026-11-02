import DatePicker from './DatePicker';
import TimePicker from './TimePicker';

// Keep venue wall-clock values intact: never convert through UTC or the device timezone.
export default function DateTimePicker({label,value,onChange,disabled=false,required=false}:{label:string;value:string;onChange:(value:string)=>void;disabled?:boolean;required?:boolean}){
 const [date='',rawTime='']=value.split('T');const time=rawTime.slice(0,5);
 function change(d:string,t:string){onChange(d||t?d+'T'+t:'');}
 return <div className="min-w-0 space-y-2"><div className="grid grid-cols-1 sm:grid-cols-2 gap-3 min-w-0"><DatePicker label={label+' date'} value={date} disabled={disabled} required={required||!!time} onChange={d=>change(d,time)}/><TimePicker label={label+' time'} value={time} disabled={disabled} required={required||!!date} onChange={t=>change(date,t)}/></div>{!required&&!!value&&<button type="button" disabled={disabled} className="btn-outline action" aria-label={'Clear '+label} onClick={()=>onChange('')}>Clear</button>}</div>;
}
