import {afterEach,beforeEach,expect,test,vi} from 'vitest';
import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import ResendResults from '../src/ResendResults';
import FixtureOptions from '../src/FixtureOptions';
import type {View} from '../src/tournament-types';
const service=vi.hoisted(()=>({api:vi.fn()}));vi.mock('../src/api',()=>service);
const view=(status='C',enabled=true)=>({eventInfo:{id:'E'},programs:[{id:'P'}],resultsPublishingEnabled:enabled,plans:[{programId:'P',matches:[{id:'m',status}]}]} as unknown as View);
function Control({data=view(),program='P'}:{data?:View;program?:string}){return <ResendResults view={data} programId={program}>{option=><FixtureOptions items={[option]}/>}</ResendResults>;}
function open(){fireEvent.click(screen.getByRole('button',{name:'Options'}));return screen.getByRole('menuitem',{name:'Resend Results'}) as HTMLButtonElement;}
beforeEach(()=>{service.api.mockReset();service.api.mockResolvedValue({failures:[]});});afterEach(cleanup);

test.each(['SC','IP','RC','BY'])('resends the event even if the selected program has no confirmed result: %s',async status=>{
 render(<Control data={view(status)}/>); fireEvent.click(open());
 await waitFor(()=>expect(service.api).toHaveBeenCalledExactlyOnceWith('trs/publishing/E/resend?programId=P','POST'));
 expect(await screen.findByText('Results sent.')).toBeTruthy();
});

test('publishing must be configured for the selected program',()=>{
 const page=render(<Control data={view('C',false)}/>);expect(open().disabled).toBe(true);
 page.unmount();render(<Control program="another-program"/>);expect(open().disabled).toBe(true);
});

test.each(['C','WO'])('resend uses the existing event publishing endpoint and reports success: %s',async status=>{
 render(<Control data={view(status)}/>);fireEvent.click(open());
 await waitFor(()=>expect(service.api).toHaveBeenCalledExactlyOnceWith('trs/publishing/E/resend?programId=P','POST'));
 expect(await screen.findByText('Results sent.')).toBeTruthy();
});

test('receiver failures are shown without reporting success',async()=>{
 service.api.mockResolvedValue({failures:[{message:'Receiver returned HTTP 503.'}]});
 render(<Control/>);fireEvent.click(open());
 expect(await screen.findByText('Receiver returned HTTP 503.')).toBeTruthy();expect(screen.queryByText('Results sent.')).toBeNull();
});
