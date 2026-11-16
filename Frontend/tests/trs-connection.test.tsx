import {afterEach,expect,test,vi} from 'vitest';
import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import TrsConnectionConfig from '../src/TrsConnectionConfig';
const service=vi.hoisted(()=>({api:vi.fn()}));vi.mock('../src/api',()=>service);
afterEach(()=>{cleanup();vi.clearAllMocks();});

test('master TRS connection shows the key and saves only the edited row',async()=>{
 const connection={revision:4,apiBaseUrl:'https://trs.example',apiKey:'integration-key-for-this-test-only'};
 service.api.mockResolvedValue(connection);const changed=vi.fn().mockResolvedValue(undefined);
 render(<TrsConnectionConfig busy={false} onChanged={changed}/>);
 expect(await screen.findByText(connection.apiKey)).toBeTruthy();
 fireEvent.click(screen.getByRole('button',{name:'Edit Integration key'}));
 const input=screen.getByLabelText('Integration key') as HTMLInputElement;
 expect(input.type).toBe('text');expect(input.value).toBe(connection.apiKey);
 fireEvent.change(input,{target:{value:''}});fireEvent.click(screen.getByRole('button',{name:'Save Integration key'}));
 await waitFor(()=>expect(service.api).toHaveBeenCalledWith('trs/connection','PUT',{revision:4,field:'apiKey',value:''}));
 await waitFor(()=>expect(changed).toHaveBeenCalledOnce());
});

test('failed connection save keeps the editor available for correction',async()=>{
 service.api.mockImplementation(async(_path:string,method?:string)=>{if(method==='PUT')throw new Error('Enter a valid TRS API URL.');return {revision:1,apiBaseUrl:'',apiKey:''};});
 render(<TrsConnectionConfig busy={false}/>);
 fireEvent.click(await screen.findByRole('button',{name:'Edit TRS API URL'}));
 fireEvent.change(screen.getByLabelText('TRS API URL'),{target:{value:'invalid'}});
 fireEvent.click(screen.getByRole('button',{name:'Save TRS API URL'}));
 expect(await screen.findByText('Enter a valid TRS API URL.')).toBeTruthy();expect(screen.getByLabelText('TRS API URL')).toBeTruthy();
});
