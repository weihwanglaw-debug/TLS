import type {ReactNode} from 'react';
import {Dialog,DialogContent,DialogHeader,DialogTitle,DialogDescription} from './components/ui/dialog';
export default function LiveDialog({title,busy,onClose,children,wide=false}:{title:string;busy:boolean;onClose:()=>void;children:ReactNode;wide?:boolean}) {
 return <Dialog open onOpenChange={open=>{if(!open&&!busy)onClose();}}><DialogContent className={'w-[calc(100%_-_2rem)] max-h-[94dvh] overflow-y-auto sm:rounded-none '+(wide?'live-result-dialog':'max-w-xl')} style={{backgroundColor:'var(--color-page-bg)'}}><DialogHeader className="text-left pr-6"><DialogTitle className="leading-snug">{title}</DialogTitle><DialogDescription>Venue match control</DialogDescription></DialogHeader>{children}</DialogContent></Dialog>;
}
