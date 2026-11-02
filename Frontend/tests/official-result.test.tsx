import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {afterEach,expect,test,vi} from 'vitest';
import ExceptionalResultEditor from '../src/ExceptionalResultEditor';
const service=vi.hoisted(()=>({api:vi.fn()}));vi.mock('../src/api',()=>service);
afterEach(()=>{cleanup();service.api.mockReset();});
const match={id:'m',programId:'p',entryA:'a',entryB:'b',status:'IP',games:[{a:21,b:15}],currentGame:{a:12,b:8},matchVersion:4,rules:{gamesToWin:2,pointCap:30}} as any;
const view={revision:7,plans:[],entries:[{id:'a',participantIds:['pa']},{id:'b',participantIds:['pb']}],participants:[{id:'pa',name:'Alice'},{id:'pb',name:'Bob'}]} as any;
test('retirement reviews actual partial scores before confirming the official winner',async()=>{
 service.api.mockResolvedValue({token:'impact',dangerous:false,matches:[],message:'No downstream matches are affected.'});const save=vi.fn();
 render(<ExceptionalResultEditor view={view} match={match} busy={false} error="" onClose={()=>{}} onSave={save}/>);
 fireEvent.change(screen.getByLabelText('Official winner'),{target:{value:'a'}});fireEvent.change(screen.getByLabelText('Reason'),{target:{value:'Bob retired'}});
 fireEvent.click(screen.getByRole('button',{name:'Review Result'}));await screen.findByText('No downstream matches are affected.');expect(save).not.toHaveBeenCalled();
 fireEvent.click(screen.getByRole('button',{name:'Confirm Official Result'}));expect(save).toHaveBeenCalledWith(expect.objectContaining({outcome:'R',officialWinner:'a',games:[{a:21,b:15},{a:12,b:8}],impactToken:'impact',reason:'Bob retired'}));
});
test('a progressed dependant requires an explicit handling decision',async()=>{
 service.api.mockResolvedValue({token:'impact',dangerous:true,matches:[{id:'f',number:3,entryA:'a',entryB:'b',status:'IP'}],message:'Later matches affected'});const save=vi.fn();
 render(<ExceptionalResultEditor view={view} match={match} busy={false} error="" onClose={()=>{}} onSave={save}/>);
 fireEvent.change(screen.getByLabelText('Official winner'),{target:{value:'b'}});fireEvent.change(screen.getByLabelText('Reason'),{target:{value:'Referee correction'}});fireEvent.click(screen.getByRole('button',{name:'Review Result'}));
 await waitFor(()=>expect((screen.getByRole('button',{name:'Confirm Official Result'}) as HTMLButtonElement).disabled).toBe(true));
 fireEvent.change(screen.getByRole('combobox'),{target:{value:'R'}});fireEvent.click(screen.getByRole('button',{name:'Confirm Official Result'}));expect(save).toHaveBeenCalledWith(expect.objectContaining({downstreamHandling:'R'}));
});
