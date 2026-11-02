import {useRef,useState} from 'react';
import {Clock} from 'lucide-react';
import {Dialog,DialogContent,DialogHeader,DialogTitle,DialogDescription,DialogFooter} from './components/ui/dialog';
import './time-picker.css';

export function timeLabel(value:string){const hour=Number(value.slice(0,2));return `${hour%12||12}:${value.slice(3,5)} ${hour>=12?'PM':'AM'}`;}
export default function TimePicker({label,value,onChange,disabled=false,required=false}:{label:string;value:string;onChange:(value:string)=>void;disabled?:boolean;required?:boolean}){
 const selectedHourButton=useRef<HTMLButtonElement>(null);
 const [open,setOpen]=useState(false),[invalid,setInvalid]=useState(false),[hour,setHour]=useState(10),[minute,setMinute]=useState('00'),[period,setPeriod]=useState('AM');
 function begin(){setInvalid(false);const h=value?Number(value.slice(0,2)):10;setHour(h%12||12);setMinute(value.slice(3,5)||'00');setPeriod(h>=12?'PM':'AM');setOpen(true);}
 const valid=/^\d{1,2}$/.test(minute)&&Number(minute)>=0&&Number(minute)<=59;
 const selected=String(hour%12+(period==='PM'?12:0)).padStart(2,'0')+':'+minute.padStart(2,'0');
 function stepMinute(delta:number){
  const total=((hour%12+(period==='PM'?12:0))*60+Number(minute)+delta+1440)%1440;
  const h=Math.floor(total/60);setHour(h%12||12);setMinute(String(total%60).padStart(2,'0'));setPeriod(h>=12?'PM':'AM');
 }
 return <div className="min-w-0 w-full"><button type="button" className="field-input flex items-center justify-between gap-3 text-left" aria-label={label} disabled={disabled} aria-invalid={invalid||undefined} onClick={begin}><span>{value?timeLabel(value):'Select time'}</span><Clock className="h-4 w-4 shrink-0" style={{color:'var(--color-primary)'}}/></button><input className="sr-only" tabIndex={-1} aria-hidden="true" required={required} disabled={disabled} value={value} onChange={()=>{}} onInvalid={e=>{e.preventDefault();setInvalid(true);}}/>{invalid&&<p role="alert" className="text-xs mt-1" style={{color:'var(--color-primary)'}}>Select a time.</p>}
 <Dialog open={open} onOpenChange={setOpen}><DialogContent showClose={false} onOpenAutoFocus={event=>{event.preventDefault();selectedHourButton.current?.focus();}} className="time-picker-dialog w-[calc(100%_-_2rem)] max-h-[94dvh] overflow-y-auto" style={{backgroundColor:'var(--color-page-bg)'}}><DialogHeader className="sr-only"><DialogTitle>{label}</DialogTitle><DialogDescription>Choose an hour, AM or PM and minutes. Plus and minus adjust by one minute. Set applies this field only.</DialogDescription></DialogHeader>
 <form onSubmit={e=>{e.preventDefault();e.stopPropagation();if(valid){onChange(selected);setOpen(false);}}} className="time-picker-form">
 <div className="time-picker-display"><strong className="time-picker-hour">{hour}</strong><span className="time-picker-colon" aria-hidden="true">:</span><input aria-label="Minute" inputMode="numeric" type="text" pattern="[0-5]?[0-9]" required maxLength={2} value={minute} onChange={e=>setMinute(e.target.value)} onBlur={()=>{if(valid)setMinute(minute.padStart(2,'0'));}}/><span className="time-picker-period">{period}</span></div>
 <fieldset><legend>Hour</legend><div className="time-picker-hours">{Array.from({length:12},(_,i)=>i+1).map(h=><button key={h} ref={hour===h?selectedHourButton:undefined} type="button" aria-pressed={hour===h} className={hour===h?'btn-primary':'btn-outline'} onClick={()=>setHour(h)}>{h}</button>)}</div></fieldset>
 <fieldset><legend>AM or PM</legend><div className="time-picker-periods">{['AM','PM'].map(p=><button type="button" key={p} aria-pressed={period===p} className={period===p?'btn-primary':'btn-outline'} onClick={()=>setPeriod(p)}>{p}</button>)}</div></fieldset>
 <fieldset><legend>Minute</legend><div className="time-picker-minutes"><button type="button" className="btn-outline time-picker-step" aria-label="Decrease time by one minute" disabled={!valid} onClick={()=>stepMinute(-1)}>−</button>{['00','15','30','45'].map(m=><button type="button" key={m} aria-label={'Minute '+m} aria-pressed={valid&&Number(minute)===Number(m)} className={(valid&&Number(minute)===Number(m)?'btn-primary':'btn-outline')} onClick={()=>setMinute(m)}>{m}</button>)}<button type="button" className="btn-outline time-picker-step" aria-label="Increase time by one minute" disabled={!valid} onClick={()=>stepMinute(1)}>+</button></div></fieldset>
 <DialogFooter className="time-picker-footer"><button type="button" className="btn-outline" onClick={()=>setOpen(false)}>Cancel</button><button type="submit" className="btn-primary" disabled={!valid}>{valid?'Set '+timeLabel(selected):'Set time'}</button></DialogFooter>
 </form></DialogContent></Dialog></div>;
}
