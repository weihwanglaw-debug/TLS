import {cleanup,fireEvent,render,screen,within} from '@testing-library/react';
import {afterEach,expect,test,vi} from 'vitest';
import AdminLayout from '../src/AdminLayout';
afterEach(cleanup);
const show=(onNavigate=vi.fn())=>render(<AdminLayout page="dashboard" busy={false} onNavigate={onNavigate} theme="a" onToggleTheme={()=>{}} appName="TLS" logoUrl=""><p>Content</p></AdminLayout>);
test('dashboard stands alone and setup modules have distinct groups',()=>{
 show();fireEvent.click(screen.getByRole('button',{name:'Expand sidebar'}));
 const dashboard=screen.getByRole('group',{name:'Dashboard'}),events=screen.getByRole('group',{name:'Event Management'}),configuration=screen.getByRole('group',{name:'Configuration'});
 expect(within(dashboard).getAllByRole('button')).toHaveLength(1);
 expect(within(dashboard).getByRole('button',{name:'Dashboard'}).getAttribute('aria-current')).toBe('page');
 expect(within(events).getAllByRole('button').map(b=>b.textContent)).toEqual(['Event','Participant','Fixture and Result']);
 expect(within(configuration).queryByRole('button',{name:'Dashboard'})).toBeNull();
 expect((within(configuration).getByRole('button',{name:'User Management'}) as HTMLButtonElement).disabled).toBe(true);
});
test('collapsed sidebar keeps labelled navigation and existing destinations',()=>{
 const navigate=vi.fn();show(navigate);
 fireEvent.click(screen.getByRole('button',{name:'Event'}));expect(navigate).toHaveBeenCalledWith('events');
 expect(screen.queryByText('Configuration')).toBeNull();
 expect(screen.getByRole('group',{name:'Configuration'})).toBeTruthy();
});
