import {afterEach,expect,test,vi} from 'vitest';
import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import TrsPublishing,{TrsPublishingBadge} from '../src/TrsPublishing';
globalThis.ResizeObserver=class {observe(){}unobserve(){}disconnect(){}} as unknown as typeof ResizeObserver;
const service=vi.hoisted(()=>({api:vi.fn()}));vi.mock('../src/api',()=>service);
afterEach(()=>{cleanup();vi.clearAllMocks();});
test('score publishing uses a toggle and hides fields when switched off',()=>{
 const settings={revision:2,source:'TRS',enabled:false,liveUrl:'https://receiver.test/live',resultsUrl:'https://receiver.test/{programId}/results',intervalSeconds:5,hasCredential:true,apiKey:'visible-publishing-key-12345678901234567890',failures:[]};
 const changed=vi.fn();const props={settings,onChange:changed,credential:settings.apiKey,onCredential:vi.fn(),editing:true,busy:false};
 const page=render(<TrsPublishing {...props}/>);
 expect(screen.queryByLabelText('Live-score API URL')).toBeNull();
 fireEvent.click(screen.getByRole('switch',{name:'Enable Score Publishing'}));expect(changed).toHaveBeenCalledWith({...settings,enabled:true});
 page.rerender(<TrsPublishing {...props} settings={{...settings,enabled:true}}/>);
 const key=screen.getByLabelText('API credential') as HTMLInputElement;expect(key.value).toBe(settings.apiKey);expect(key.type).toBe('text');
 expect(screen.getByLabelText('Live-score API URL')).toBeTruthy();expect(screen.queryByText(/TRS/)).toBeNull();
 expect(screen.queryByRole('button',{name:/Resend/})).toBeNull();expect(screen.queryByRole('button',{name:'Save Publishing Settings'})).toBeNull();expect(service.api).not.toHaveBeenCalled();
});

test('dashboard shows publishing failures without a manual resend control',async()=>{
 service.api.mockResolvedValue([{eventId:'E',kind:'live',message:'Receiver returned HTTP 503.',at:'2026-11-03T10:00:00Z'}]);
 render(<TrsPublishingBadge eventId="E"/>);
 expect(await screen.findByText(/Live-score publishing failed/)).toBeTruthy();
 expect(screen.queryByRole('button',{name:/Retry|Resend/})).toBeNull();
 expect(service.api.mock.calls.every(([,method])=>method!=='POST')).toBe(true);
});
