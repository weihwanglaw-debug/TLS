import {useState} from 'react';
import {QRCodeSVG} from 'qrcode.react';
import type {LiveMatch} from './live-types';

export type PairingInvitation={code:string;expiresAt:string;tabletUrl:string;match:LiveMatch};
export function venueTabletUrls(urls:string[]){return urls.filter(value=>{try{const url=new URL(value);return ['http:','https:'].includes(url.protocol)&&!['localhost','127.0.0.1','[::1]','0.0.0.0'].includes(url.hostname);}catch{return false;}});}
export default function TabletPairing({invitation,urls,now,busy,onRenew}:{invitation:PairingInvitation;urls:string[];now:number;busy:boolean;onRenew:()=>void}){
 const addresses=venueTabletUrls(urls),[selected,setSelected]=useState('');
 const address=addresses.includes(selected)?selected:addresses[0];
 const seconds=Math.max(0,Math.min(180,Math.ceil((Date.parse(invitation.expiresAt)-now)/1000))),expired=seconds===0;
 return <div className="space-y-4">
  <p className="text-sm">Court {invitation.match.court} · {invitation.match.programName}</p>
  <p className="font-semibold" role="status">{expired?'Pairing invitation expired':`Pair within ${Math.floor(seconds/60)}:${String(seconds%60).padStart(2,'0')}`}</p>
  {!expired&&<>
   {addresses.length>1&&<label className="form-label">Venue network address<select aria-label="Venue network address" className="field-input w-full" value={address} onChange={e=>setSelected(e.target.value)}>{addresses.map(url=><option key={url} value={url}>{url}</option>)}</select></label>}
   {address?<div className="flex flex-col items-center gap-2"><QRCodeSVG value={address+'#pair='+encodeURIComponent(invitation.code)} size={220} marginSize={4} level="M" title="Scan to connect umpire tablet"/><p className="text-sm">Scan to open and connect automatically.</p></div>:<p className="notice">No venue network address found. Connect the server to the venue network before pairing another device.</p>}
   <p className="text-sm font-semibold">Cannot scan? Open this address and enter the code:</p>
   <p className="text-sm break-all">{address||invitation.tabletUrl}</p>
   <p className="live-access-code">{invitation.code}</p>
   <p className="text-xs opacity-70">One use. Keep both devices on the same venue network.</p>
  </>}
  {expired&&<button className="btn-primary action" disabled={busy} onClick={onRenew}>Generate New Code</button>}
 </div>;
}
