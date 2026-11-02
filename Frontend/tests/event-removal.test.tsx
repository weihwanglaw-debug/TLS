import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {afterEach,beforeEach,expect,test,vi} from 'vitest';
import EventRemovalDialog from '../src/EventRemovalDialog';
const service=vi.hoisted(()=>({api:vi.fn()}));vi.mock('../src/api',()=>service);
const summary={revision:5,name:'Community Open',programs:2,participants:4,entries:4};
beforeEach(()=>service.api.mockReset());afterEach(cleanup);
test('click checks current eligibility; cancellation never deletes',async()=>{
 service.api.mockResolvedValue(summary);render(<EventRemovalDialog eventId="EV000001" onRemoved={vi.fn()}/>);
 expect(screen.getByRole('button',{name:'Remove Event'}).hasAttribute('disabled')).toBe(false);
 fireEvent.click(screen.getByRole('button',{name:'Remove Event'}));await screen.findByRole('button',{name:'Confirm Remove Event'});
 expect(service.api).toHaveBeenCalledWith('setup/events/EV000001/removal');
 fireEvent.click(screen.getByRole('button',{name:'Cancel'}));expect(service.api).toHaveBeenCalledTimes(1);
});
test('fixture validation shows a prompt and the remove button remains usable',async()=>{
 service.api.mockRejectedValueOnce(new Error('This event cannot be removed because fixtures have been generated.')).mockResolvedValue(summary);
 render(<EventRemovalDialog eventId="EV000001" onRemoved={vi.fn()}/>);
 fireEvent.click(screen.getByRole('button',{name:'Remove Event'}));await screen.findByText('This event cannot be removed because fixtures have been generated.');
 expect(screen.queryByRole('button',{name:'Confirm Remove Event'})).toBeNull();
 fireEvent.click(screen.getByRole('button',{name:'OK'}));fireEvent.click(screen.getByRole('button',{name:'Remove Event'}));await screen.findByRole('button',{name:'Confirm Remove Event'});
 expect(service.api).toHaveBeenCalledTimes(2);
});
test('confirmation sends the checked revision and returns refreshed data',async()=>{
 const next={data:{events:[]},readiness:[]},done=vi.fn();service.api.mockResolvedValueOnce(summary).mockResolvedValueOnce(next);
 render(<EventRemovalDialog eventId="EV000001" onRemoved={done}/>);fireEvent.click(screen.getByRole('button',{name:'Remove Event'}));
 fireEvent.click(await screen.findByRole('button',{name:'Confirm Remove Event'}));await waitFor(()=>expect(done).toHaveBeenCalledWith(next,'Community Open'));
 expect(service.api).toHaveBeenLastCalledWith('setup/events/EV000001?revision=5','DELETE');
});
test('a changed event at final confirmation is not reported as removed',async()=>{
 const done=vi.fn();service.api.mockResolvedValueOnce(summary).mockRejectedValueOnce(new Error('Data changed in another window. Refresh and preview again.'));
 render(<EventRemovalDialog eventId="EV000001" onRemoved={done}/>);fireEvent.click(screen.getByRole('button',{name:'Remove Event'}));
 fireEvent.click(await screen.findByRole('button',{name:'Confirm Remove Event'}));await screen.findByText('Data changed in another window. Refresh and preview again.');
 expect(done).not.toHaveBeenCalled();expect(screen.queryByRole('button',{name:'Confirm Remove Event'})).toBeNull();
});
