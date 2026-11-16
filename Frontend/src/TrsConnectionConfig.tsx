import {useEffect,useState} from 'react';
import {api} from './api';
import ActionMessage from './ActionMessage';
import MasterSettingRows from './MasterSettingRows';

type Connection={revision:number;apiBaseUrl:string;apiKey:string};
export default function TrsConnectionConfig({busy,onChanged}:{busy:boolean;onChanged?:()=>Promise<void>}){
 const [connection,setConnection]=useState<Connection>(),[error,setError]=useState('');
 useEffect(()=>{let active=true;api<Connection>('trs/connection').then(value=>{if(active)setConnection(value);}).catch(e=>{if(active)setError(e.message);});return()=>{active=false;};},[]);
 async function save(field:string,value:string){const fresh=await api<Connection>('trs/connection');setConnection(await api<Connection>('trs/connection','PUT',{revision:fresh.revision,field,value}));await onChanged?.();return true;}
 if(!connection)return <ActionMessage message={error}/>;
 return <MasterSettingRows title="TRS Connection" busy={busy} onSave={save} rows={[{id:'apiBaseUrl',label:'TRS API URL',value:connection.apiBaseUrl},{id:'apiKey',label:'Integration key',value:connection.apiKey}]}/>;
}
