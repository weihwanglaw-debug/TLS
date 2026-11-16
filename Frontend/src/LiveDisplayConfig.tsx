import {defaultDisplay,type DisplaySettings} from './LiveDisplay';
import MasterSettingRows from './MasterSettingRows';

export const displayLayouts=[{rows:1,columns:2},{rows:2,columns:2},{rows:2,columns:3},{rows:2,columns:4},{rows:3,columns:4},{rows:4,columns:4}];
export default function LiveDisplayConfig({value,busy,onSave}:{value?:DisplaySettings;busy:boolean;onSave:(value:DisplaySettings)=>Promise<boolean|void>}){
 const config=value||defaultDisplay;
 const layout=displayLayouts.find(p=>p.rows===config.rows&&p.columns===config.columns)||defaultDisplay;
 async function save(id:string,value:string){const next={...config,rows:layout.rows,columns:layout.columns,autoRotate:true,eventId:'',courts:[]};if(id==='layout'){const [rows,columns]=value.split('x').map(Number);next.rows=rows;next.columns=columns;}else next.pageSeconds=Number(value);return onSave(next);}
 return <MasterSettingRows title="Live Display" busy={busy} onSave={save} rows={[
  {id:'layout',label:'Layout (rows × columns)',value:`${layout.rows}x${layout.columns}`,options:displayLayouts.map(p=>({value:`${p.rows}x${p.columns}`,label:`${p.rows} × ${p.columns} (${p.rows*p.columns} matches)`}))},
  {id:'pageSeconds',label:'Seconds per page',value:String(config.pageSeconds),type:'number',min:3,max:120}
 ]}/>;
}
