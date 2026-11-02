import {act,cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {afterEach,beforeEach,expect,test,vi} from 'vitest';
import UmpireApp from '../src/UmpireApp';

let fullScreenElement:Element|null;
const original=new Map<string,PropertyDescriptor|undefined>();
function browserProperty(name:string,value:PropertyDescriptor){
 if(!original.has(name))original.set(name,Object.getOwnPropertyDescriptor(document,name));
 Object.defineProperty(document,name,{configurable:true,...value});
}
let request=vi.fn(),exit=vi.fn();
let originalRequest:PropertyDescriptor|undefined;
beforeEach(()=>{
 localStorage.clear();fullScreenElement=null;
 browserProperty('fullscreenElement',{get:()=>fullScreenElement});
 browserProperty('fullscreenEnabled',{value:true});
 originalRequest=Object.getOwnPropertyDescriptor(document.documentElement,'requestFullscreen');
 request=vi.fn(async()=>{fullScreenElement=document.documentElement;document.dispatchEvent(new Event('fullscreenchange'));});
 exit=vi.fn(async()=>{fullScreenElement=null;document.dispatchEvent(new Event('fullscreenchange'));});
 Object.defineProperty(document.documentElement,'requestFullscreen',{configurable:true,value:request});
 browserProperty('exitFullscreen',{value:exit});
});
afterEach(()=>{
 cleanup();
 for(const [name,descriptor] of original){if(descriptor)Object.defineProperty(document,name,descriptor);else Reflect.deleteProperty(document,name);}
 original.clear();
 if(originalRequest)Object.defineProperty(document.documentElement,'requestFullscreen',originalRequest);
 else Reflect.deleteProperty(document.documentElement,'requestFullscreen');
});

test('enters document full screen and exits using the same button',async()=>{
 render(<UmpireApp/>);
 fireEvent.click(screen.getByRole('button',{name:'Full Screen',exact:true}));
 await waitFor(()=>expect(screen.getByRole('button',{name:'Exit Full Screen',exact:true}).disabled).toBe(false));
 expect(request).toHaveBeenCalledOnce();
 expect(fullScreenElement).toBe(document.documentElement);
 fireEvent.click(screen.getByRole('button',{name:'Exit Full Screen',exact:true}));
 await waitFor(()=>expect(screen.getByRole('button',{name:'Full Screen',exact:true}).disabled).toBe(false));
 expect(exit).toHaveBeenCalledOnce();
});

test('browser-driven exit updates the label without a second request',async()=>{
 render(<UmpireApp/>);
 fireEvent.click(screen.getByRole('button',{name:'Full Screen',exact:true}));
 await screen.findByRole('button',{name:'Exit Full Screen',exact:true});
 act(()=>{fullScreenElement=null;document.dispatchEvent(new Event('fullscreenchange'));});
 expect(screen.getByRole('button',{name:'Full Screen',exact:true})).toBeTruthy();
 expect(exit).not.toHaveBeenCalled();
});

test('unsupported browsers show a clear popup and keep normal mode',async()=>{
 browserProperty('fullscreenEnabled',{value:false});
 render(<UmpireApp/>);
 fireEvent.click(screen.getByRole('button',{name:'Full Screen',exact:true}));
 await screen.findByText(/Full screen is not available in this browser/);
 expect(request).not.toHaveBeenCalled();
 expect(fullScreenElement).toBeNull();
});

test('rejected requests show a clear popup and allow retry',async()=>{
 request.mockRejectedValueOnce(new Error('User activation required'));
 render(<UmpireApp/>);
 fireEvent.click(screen.getByRole('button',{name:'Full Screen',exact:true}));
 await screen.findByText(/The browser could not change full-screen mode/);
 fireEvent.click(screen.getByRole('button',{name:'OK',exact:true}));
 fireEvent.click(screen.getByRole('button',{name:'Full Screen',exact:true}));
 await screen.findByRole('button',{name:'Exit Full Screen',exact:true});
 expect(request).toHaveBeenCalledTimes(2);
});
