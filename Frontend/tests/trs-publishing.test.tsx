import {afterEach,expect,test,vi} from 'vitest';
import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import TrsPublishing,{TrsPublishingBadge} from '../src/TrsPublishing';
const service=vi.hoisted(()=>({api:vi.fn()}));vi.mock('../src/api',()=>service);
afterEach(()=>{cleanup();vi.clearAllMocks();});
test('publishing settings keep saved credential server-side and offer manual resend',async()=>{
 const settings={revision:2,source:'TRS',enabled:true,liveUrl:'https://trs.test/live',resultsUrl:'https://trs.test/{programId}/results',intervalSeconds:5,hasCredential:true,failures:[]};
 service.api.mockImplementation(async(path:string)=>path.endsWith('/resend')?{failures:[]}:settings);
 render(<TrsPublishing eventId="E" onChanged={async()=>{}}/>);
 const key=await screen.findByLabelText('API credential') as HTMLInputElement;expect(key.value).toBe('');
 fireEvent.click(screen.getByRole('button',{name:'Save Publishing Settings'}));
 await waitFor(()=>expect(service.api).toHaveBeenCalledWith('trs/publishing/E','PUT',{revision:2,enabled:true,liveUrl:settings.liveUrl,resultsUrl:settings.resultsUrl,intervalSeconds:5,apiKey:''}));
 fireEvent.click(await screen.findByRole('button',{name:'OK'}));
 fireEvent.click(screen.getByRole('button',{name:'Resend Results / Live Scores'}));
 await waitFor(()=>expect(service.api).toHaveBeenCalledWith('trs/publishing/E/resend','POST'));
});
test('dashboard only shows publishing badge on failure and can retry',async()=>{
 let failed=true;service.api.mockImplementation(async(path:string)=>path.endsWith('/resend')?(failed=false,{failures:[]}):failed?[{eventId:'E',kind:'live',message:'TRS returned HTTP 503.',at:'2026-11-03T10:00:00Z'}]:[]);
 render(<TrsPublishingBadge eventId="E"/>);
 expect(await screen.findByText(/Live-score publishing failed/)).toBeTruthy();
 fireEvent.click(screen.getByText('Retry Publishing'));
 await waitFor(()=>expect(screen.queryByText(/Live-score publishing failed/)).toBeNull());
});
