import {useEffect,useState} from 'react';
import {ActionFeedbackDialog,type ActionFeedbackVariant} from './components/ui/ActionFeedbackDialog';

export default function ActionMessage({message,variant='error',title}:{message:string;variant?:ActionFeedbackVariant;title?:string}){
 const [dismissed,setDismissed]=useState(false);
 useEffect(()=>setDismissed(false),[message]);
 return <ActionFeedbackDialog open={!!message&&!dismissed} variant={variant} title={title||(variant==='success'?'Saved successfully':'Unable to complete action')} description={message} onOpenChange={open=>{if(!open)setDismissed(true);}}/>;
}
