import {useRef,useState} from 'react';
import {api} from './api';
import ActionMessage from './ActionMessage';
import type {ReactNode} from 'react';
import type {FixtureOption} from './FixtureOptions';
import type {View} from './tournament-types';

export default function ResendResults({view,programId,children}:{view:View;programId:string;children:(option:FixtureOption)=>ReactNode}){
 const [busy,setBusy]=useState(false),[error,setError]=useState(''),[message,setMessage]=useState(''),[attempt,setAttempt]=useState(0);
 const sending=useRef(false);
 const ready=!!view.resultsPublishingEnabled&&view.programs.some(p=>p.id===programId);
 async function resend(){
  if(!ready||sending.current)return;
  setAttempt(value=>value+1);setError('');setMessage('');
  sending.current=true;setBusy(true);
  try{
   const result=await api<{failures:{message:string}[]}>('trs/publishing/'+encodeURIComponent(view.eventInfo.id)+'/resend?programId='+encodeURIComponent(programId),'POST');
   if(result.failures.length)setError(result.failures.map(f=>f.message).join('\n'));
   else setMessage('Results sent.');
  }catch(e){setError((e as Error).message);}finally{sending.current=false;setBusy(false);}
 }
 return <><ActionMessage key={attempt+':error'} message={error}/><ActionMessage key={attempt+':success'} message={message} variant="success"/>{children({label:busy?'Sending...':'Resend Results',disabled:!ready||busy,onClick:()=>void resend()})}</>;
}
