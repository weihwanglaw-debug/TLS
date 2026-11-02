import {useState} from 'react';
import {afterEach,beforeEach,expect,test,vi} from 'vitest';
import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import EventImport from '../src/EventImport';
import ActionMessage from '../src/ActionMessage';
import type {Preview,View} from '../src/setup-types';
const service=vi.hoisted(()=>({api:vi.fn()}));
vi.mock('../src/api',()=>service);
vi.mock('../src/FoundationPages',()=>({EventDetails:()=> <div>Replacement preview details</div>}));
globalThis.ResizeObserver=class {observe(){}unobserve(){}disconnect(){}} as unknown as typeof ResizeObserver;
const initial={data:{revision:3,events:[],programs:[],participants:[],entries:[],imports:[]},readiness:[]} as unknown as View;
const saved={...initial,data:{...initial.data,revision:4,events:[{id:'e',name:'Badminton Test Event'}]}} as View;
const preview={...saved,token:'validated-token',revision:3,replacing:false} as Preview;
const done=vi.fn();
const upload=(size=20)=>fireEvent.change(screen.getByLabelText('Excel workbook'),{target:{files:[new File([new Uint8Array(size)],'badminton-test.xlsx')]}});
function Harness({replaceId=''}:{replaceId?:string}){
 const [busy,setBusy]=useState(false),[error,setError]=useState('');
 async function work(action:()=>Promise<void>){setBusy(true);setError('');try{await action();}catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 return <><ActionMessage message={error}/><EventImport view={initial} kind="excel" replaceId={replaceId} busy={busy} work={work} onDone={done} onBack={()=>{}}/></>;
}
beforeEach(()=>{vi.resetAllMocks();vi.stubGlobal('fetch',vi.fn().mockResolvedValue({ok:true,json:async()=>preview}));service.api.mockResolvedValue(saved);});
afterEach(()=>{cleanup();vi.unstubAllGlobals();});
test('file selection is prominent, shows the filename and allows changing it',()=>{
 render(<Harness/>);
 const input=screen.getByLabelText('Excel workbook') as HTMLInputElement;
 const choose=vi.spyOn(input,'click');
 fireEvent.click(screen.getByRole('button',{name:'Select Excel File'}));expect(choose).toHaveBeenCalledOnce();
 expect((screen.getByRole('button',{name:'Import Event'}) as HTMLButtonElement).disabled).toBe(true);
 upload();expect(screen.getByText('badminton-test.xlsx')).toBeTruthy();
 expect((screen.getByRole('button',{name:'Import Event'}) as HTMLButtonElement).disabled).toBe(false);
 fireEvent.click(screen.getByRole('button',{name:'Change File'}));expect(choose).toHaveBeenCalledTimes(2);
 fireEvent.change(input,{target:{files:[new File(['test'],'replacement.xlsx')]}});
 expect(screen.getByText('replacement.xlsx')).toBeTruthy();expect(screen.queryByText('badminton-test.xlsx')).toBeNull();
 expect(fetch).not.toHaveBeenCalled();
});
test('valid new Excel import validates and saves in one click without a review screen',async()=>{
 render(<Harness/>);upload();fireEvent.click(screen.getByRole('button',{name:'Import Event',exact:true}));
 await waitFor(()=>expect(done).toHaveBeenCalledWith(saved,'e'));
 expect(fetch).toHaveBeenCalledWith('/api/setup/preview?kind=excel&revision=3',expect.objectContaining({method:'POST'}));
 expect(service.api).toHaveBeenCalledExactlyOnceWith('setup/confirm','POST',{revision:3,token:'validated-token'});
 expect(screen.queryByText('Review Event Before Import')).toBeNull();expect(screen.queryByRole('button',{name:'Confirm Import'})).toBeNull();
});
test.each(['Participant row 7: duplicate participant.','This event has already been imported.'])('validation error is shown and does not confirm: %s',async message=>{
 vi.mocked(fetch).mockResolvedValue({ok:false,json:async()=>({message})} as Response);
 render(<Harness/>);upload();fireEvent.click(screen.getByRole('button',{name:'Import Event',exact:true}));
 expect(await screen.findByText(message)).toBeTruthy();expect(service.api).not.toHaveBeenCalled();expect(done).not.toHaveBeenCalled();
});
test('save failure shows an error without reporting success',async()=>{
 service.api.mockRejectedValue(new Error('Data changed. Refresh before retrying.'));
 render(<Harness/>);upload();fireEvent.click(screen.getByRole('button',{name:'Import Event',exact:true}));
 expect(await screen.findByText('Data changed. Refresh before retrying.')).toBeTruthy();expect(done).not.toHaveBeenCalled();
});
test('oversized workbook is rejected before any request',async()=>{
 render(<Harness/>);upload(1_500_001);fireEvent.click(screen.getByRole('button',{name:'Import Event',exact:true}));
 expect(await screen.findByText('Choose a workbook smaller than 1.5 MB.')).toBeTruthy();expect(fetch).not.toHaveBeenCalled();expect(service.api).not.toHaveBeenCalled();
});
test('repeated clicks cannot start another import while validation or save is pending',async()=>{
 let finishValidation!:(value:unknown)=>void,finishSave!:(value:View)=>void;
 vi.mocked(fetch).mockImplementation(()=>new Promise(resolve=>{finishValidation=resolve;}));
 service.api.mockImplementation(()=>new Promise(resolve=>{finishSave=resolve;}));
 render(<Harness/>);upload();fireEvent.click(screen.getByRole('button',{name:'Import Event',exact:true}));
 const processing=screen.getByRole('button',{name:'Importing...'});fireEvent.click(processing);
 expect((processing as HTMLButtonElement).disabled).toBe(true);expect((screen.getByLabelText('Excel workbook') as HTMLInputElement).disabled).toBe(true);expect(fetch).toHaveBeenCalledTimes(1);
 finishValidation({ok:true,json:async()=>preview});await waitFor(()=>expect(service.api).toHaveBeenCalledOnce());fireEvent.click(processing);expect(service.api).toHaveBeenCalledOnce();finishSave(saved);await waitFor(()=>expect(done).toHaveBeenCalledOnce());
});
test('replacement retains explicit review and acknowledgement before saving',async()=>{
 vi.mocked(fetch).mockResolvedValue({ok:true,json:async()=>({...preview,replacing:true})} as Response);
 render(<Harness replaceId="e"/>);upload();fireEvent.click(screen.getByRole('button',{name:'Validate and Review'}));
 await screen.findByText('Review Event Before Import');expect(service.api).not.toHaveBeenCalled();expect(done).not.toHaveBeenCalled();
 expect((screen.getByRole('button',{name:'Confirm Replacement'}) as HTMLButtonElement).disabled).toBe(true);
 fireEvent.click(screen.getByRole('switch'));fireEvent.click(screen.getByRole('button',{name:'Confirm Replacement'}));await waitFor(()=>expect(done).toHaveBeenCalledWith(saved,'e'));
 expect(fetch).toHaveBeenCalledWith('/api/setup/preview?kind=excel&revision=3&replaceEventId=e',expect.anything());
});
