import {afterEach,beforeEach,expect,test,vi} from 'vitest';
import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import ResendResults from '../src/ResendResults';
import FixtureOptions from '../src/FixtureOptions';
import type {View} from '../src/tournament-types';
const service=vi.hoisted(()=>({api:vi.fn()}));vi.mock('../src/api',()=>service);
const view=(status='C',enabled=true)=>({eventInfo:{id:'E'},programs:[{id:'P'}],resultsPublishingEnabled:enabled,plans:[{programId:'P',matches:[{id:'m',status}]}]} as unknown as View);
function Control({data=view(),program='P'}:{data?:View;program?:string}){return <ResendResults view={data} programId={program}>{option=><FixtureOptions items={[option]}/>}</ResendResults>;}
function open(){fireEvent.click(screen.getByRole('button',{name:'Options'}));return screen.getByRole('menuitem',{name:'Submit Result'}) as HTMLButtonElement;}
beforeEach(()=>{service.api.mockReset();service.api.mockResolvedValue({failures:[]});});afterEach(cleanup);

test.each(['SC','IP','RC','BY'])('empty manual submission prompts without an API request: %s',async status=>{
 render(<Control data={view(status)}/>);
 expect(screen.queryByRole('button',{name:'Resend Results'})).toBeNull();
 expect(screen.queryByRole('button',{name:'Submit Result'})).toBeNull();
 fireEvent.click(open());
 expect(await screen.findByText('No result to submit.')).toBeTruthy();expect(service.api).not.toHaveBeenCalled();
});

test('only results in the selected program count',async()=>{
 const data=view('SC');data.plans.push({programId:'OTHER',matches:[{id:'other',status:'C'}]} as View['plans'][number]);
 render(<Control data={data}/>);fireEvent.click(open());
 expect(await screen.findByText('No result to submit.')).toBeTruthy();expect(service.api).not.toHaveBeenCalled();
});

test('publishing must be configured for the selected program',()=>{
 const page=render(<Control data={view('C',false)}/>);expect(open().disabled).toBe(true);
 page.unmount();render(<Control program="another-program"/>);expect(open().disabled).toBe(true);
});

test.each(['C','WO'])('submit sends only the selected program and reports success: %s',async status=>{
 render(<Control data={view(status)}/>);fireEvent.click(open());
 await waitFor(()=>expect(service.api).toHaveBeenCalledExactlyOnceWith('trs/publishing/E/resend?programId=P','POST'));
 expect(await screen.findByText('Result submitted.')).toBeTruthy();
});

test('receiver failures are shown without reporting success',async()=>{
 service.api.mockResolvedValue({failures:[{message:'Receiver returned HTTP 503.'}]});
 render(<Control/>);fireEvent.click(open());
 expect(await screen.findByText('Receiver returned HTTP 503.')).toBeTruthy();expect(screen.queryByText('Result submitted.')).toBeNull();
});

test('empty-result prompt appears again after dismissal, including a program with no matches',async()=>{
 const data=view();data.plans[0].matches=[];
 render(<Control data={data}/>);fireEvent.click(open());
 expect(await screen.findByText('No result to submit.')).toBeTruthy();
 fireEvent.click(screen.getByRole('button',{name:'OK',exact:true}));
 expect(screen.queryByText('No result to submit.')).toBeNull();
 fireEvent.click(open());expect(await screen.findByText('No result to submit.')).toBeTruthy();
 expect(service.api).not.toHaveBeenCalled();
});
