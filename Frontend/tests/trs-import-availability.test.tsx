import type {ReactNode} from 'react';
import {afterEach,expect,test,vi} from 'vitest';
import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import SetupApp from '../src/SetupApp';

const service=vi.hoisted(()=>({api:vi.fn(),go:vi.fn()}));
vi.mock('../src/api',()=>({api:service.api}));
vi.mock('../src/admin-route',()=>({useAdminRoute:()=>({route:{page:'events'},go:service.go})}));
vi.mock('../src/AdminLayout',()=>({default:({children}:{children:ReactNode})=><main>{children}</main>}));
const view={data:{revision:1,events:[],programs:[],participants:[],entries:[],imports:[]},readiness:[]};
afterEach(()=>{cleanup();vi.resetAllMocks();});

test.each(['','https://trs.example'])('TRS import availability follows the configured API URL: %s',async apiBaseUrl=>{
 service.api.mockImplementation(async(path:string)=>path==='trs/connection'?{apiBaseUrl}:path==='setup/'?view:[]);
 render(<SetupApp/>);
 await waitFor(()=>expect(service.api).toHaveBeenCalledWith('trs/connection'));
 const button=screen.getByRole('button',{name:'Get from TRS'}) as HTMLButtonElement;
 await waitFor(()=>expect(button.disabled).toBe(!apiBaseUrl));
 fireEvent.click(button);
 if(apiBaseUrl)expect(service.go).toHaveBeenCalledWith({page:'events',importKind:'trs',replaceId:undefined});
 else expect(service.go).not.toHaveBeenCalled();
});
