import { useState, type ReactNode } from 'react';
import { Eye, MoreVertical } from 'lucide-react';
import ActionDropdownPortal from './ActionDropdownPortal';

// Field and section markup is taken from TRS EventEdit; filters from TRS Events.
export function FG({label,children}:{label:string;children:ReactNode}) {
  return <div className="min-w-0"><label className="block text-xs font-semibold mb-1.5 opacity-60">{label}</label>{children}</div>;
}
export function FilterBar({children}:{children:ReactNode}) {
  return <div className="p-5 mb-6" style={{border:'1px solid var(--color-table-border)',backgroundColor:'var(--color-row-hover)'}}><div className="grid grid-cols-1 sm:grid-cols-2 md:flex md:flex-wrap items-end gap-4">{children}</div></div>;
}
export function SectionTitle({children}:{children:ReactNode}) {
  return <h2 className="font-bold text-base mb-5 pb-3" style={{borderBottom:'1px solid var(--color-table-border)',color:'var(--color-heading)'}}>{children}</h2>;
}
export function EventPanel({title,children,headerAction}:{title:string;children:ReactNode;headerAction?:ReactNode}) {
  return <section className="mb-8 p-8" style={{border:'1px solid var(--color-table-border)',background:'linear-gradient(var(--color-row-hover), var(--color-row-hover)), var(--color-page-bg)'}}><div className="flex flex-wrap items-center justify-between gap-3 mb-5 pb-3" style={{borderBottom:'1px solid var(--color-table-border)'}}><h2 className="font-bold text-base" style={{color:'var(--color-heading)'}}>{title}</h2>{headerAction}</div>{children}</section>;
}
export function DetailField({label,value}:{label:string;value:string|number|null|undefined}) {
  return <div><label className="block text-xs font-semibold mb-2 opacity-70">{label}</label><p className="text-sm break-words">{value===null||value===undefined||value===''?'—':value}</p></div>;
}
export function Table({children}:{children:ReactNode}) {
  return <div className="overflow-x-auto" style={{border:'1px solid var(--color-table-border)'}}><table className="trs-table">{children}</table></div>;
}
export function ViewAction({name,onView,label='View Details',actions}:{name:string;onView?:()=>void;label?:string;actions?:{label:string;onClick:()=>void}[]}) {
  const [anchor,setAnchor]=useState<HTMLElement|null>(null);
  return <><button type="button" className="action-trigger" aria-label={'Open actions for '+name} aria-expanded={!!anchor} onClick={e=>setAnchor(anchor?null:e.currentTarget)} onKeyDown={e=>{if(e.key==='Escape')setAnchor(null);}}><MoreVertical className="h-4 w-4"/></button>
    <ActionDropdownPortal open={!!anchor} anchorEl={anchor} onClose={()=>setAnchor(null)}>{(actions||[{label,onClick:onView!}]).map(action=><button key={action.label} role="menuitem" onClick={()=>{setAnchor(null);action.onClick();}}><Eye className="h-4 w-4"/>{action.label}</button>)}</ActionDropdownPortal></>;
}
