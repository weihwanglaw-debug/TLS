import {useState} from 'react';
import {ChevronDown} from 'lucide-react';
import ActionDropdownPortal from './ActionDropdownPortal';
export type FixtureOption={label:string;onClick:()=>void;disabled?:boolean};
export default function FixtureOptions({items}:{items:FixtureOption[]}){
 const [anchor,setAnchor]=useState<HTMLElement|null>(null);
 return <><button type="button" className="btn-outline action" aria-haspopup="menu" aria-expanded={!!anchor} onClick={e=>setAnchor(anchor?null:e.currentTarget)}>Options<ChevronDown size={14}/></button><ActionDropdownPortal open={!!anchor} anchorEl={anchor} onClose={()=>setAnchor(null)}>{items.map(item=><button type="button" role="menuitem" disabled={item.disabled} key={item.label} onClick={()=>{setAnchor(null);item.onClick();}}>{item.label}</button>)}</ActionDropdownPortal></>;
}
