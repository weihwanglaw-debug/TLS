import {cleanup,fireEvent,render,screen} from '@testing-library/react';
import {afterEach,beforeEach,expect,test,vi} from 'vitest';
const service=vi.hoisted(()=>({api:vi.fn()}));vi.mock('../src/api',()=>service);
import EventDatesEditor from '../src/EventDatesEditor';
import type {EventRecord} from '../src/setup-types';
const event={id:'event',name:'Event',startDate:'2026-11-01',endDate:'2026-11-05'} as EventRecord;
const view={revision:1,datesEditable:true,eventInfo:event};
afterEach(cleanup);beforeEach(()=>vi.resetAllMocks());
test('visible date action checks current fixtures each click without saving',async()=>{
 service.api.mockResolvedValue({...view,datesEditable:false});render(<EventDatesEditor event={event} onChanged={async()=>{}}/>);
 expect(service.api).not.toHaveBeenCalled();fireEvent.click(screen.getByRole('button',{name:'Edit Dates'}));await screen.findByRole('dialog',{name:'Unable to complete action'});expect(screen.getByText('Reset Schedule before changing event dates. Started matches and results keep event dates locked.')).toBeTruthy();expect(service.api).toHaveBeenCalledExactlyOnceWith('tournament/event');expect(screen.queryByRole('button',{name:'Save Dates'})).toBeNull();
 fireEvent.click(screen.getByRole('button',{name:'OK'}));service.api.mockResolvedValue(view);fireEvent.click(screen.getByRole('button',{name:'Edit Dates'}));expect(await screen.findByRole('button',{name:'Save Dates'})).toBeTruthy();
});
test('a draw saved in another window closes a stale editor and shows the fixture alert',async()=>{
 service.api.mockResolvedValueOnce(view).mockRejectedValueOnce(new Error('Data changed in another window.')).mockResolvedValueOnce({...view,datesEditable:false});render(<EventDatesEditor event={event} onChanged={async()=>{}}/>);
 fireEvent.click(screen.getByRole('button',{name:'Edit Dates'}));fireEvent.click(await screen.findByRole('button',{name:'Save Dates'}));await screen.findByText('Reset Schedule before changing event dates. Started matches and results keep event dates locked.');expect(screen.queryByRole('button',{name:'Save Dates'})).toBeNull();
 fireEvent.click(screen.getByRole('button',{name:'OK'}));expect(screen.getByRole('button',{name:'Edit Dates'})).toBeTruthy();
});
