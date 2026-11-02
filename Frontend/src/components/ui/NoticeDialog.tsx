import {Dialog,DialogContent,DialogHeader,DialogTitle,DialogFooter} from './dialog';
export function NoticeDialog({open,title,description,onOpenChange}:{open:boolean;title:string;description:string;onOpenChange:(open:boolean)=>void}){
return <Dialog open={open} onOpenChange={onOpenChange}><DialogContent className="max-w-md p-7 sm:rounded-none" style={{backgroundColor:'var(--color-page-bg)',border:'1px solid var(--color-table-border)'}}><DialogHeader><DialogTitle>{title}</DialogTitle></DialogHeader><p className="text-sm">{description}</p><DialogFooter><button className="btn-primary action" onClick={()=>onOpenChange(false)}>OK</button></DialogFooter></DialogContent></Dialog>;
}
