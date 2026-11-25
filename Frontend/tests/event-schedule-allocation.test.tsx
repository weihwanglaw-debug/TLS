import {expect,test} from 'vitest';
import {courtOpenings} from '../src/SchedulePlanner';
const session={date:'2030-01-01',start:'14:00:00',end:'16:00:00',courts:[1,2]};
const booking=(time:string,minutes:number,court=1)=>({slotId:time,start:session.date+'T'+time+':00',minutes,court,manual:false});
test('5-minute steps leave 40 usable minutes after two 40-minute matches',()=>{expect(courtOpenings(session,[booking('14:00',40),booking('14:40',40)],5,1)).toEqual([{start:920,end:960}]);});
test('15-minute steps account for unusable gaps between placements',()=>{expect(courtOpenings(session,[booking('14:00',40),booking('14:45',40)],15,1)).toEqual([{start:930,end:960}]);});
test('10-minute steps align a non-grid session opening upward',()=>{expect(courtOpenings({...session,start:'14:03:00'},[],10,1)).toEqual([{start:850,end:960}]);});
test('separate 30-minute court openings do not become one hour-long opening',()=>{const rows=[booking('14:00',90),booking('14:00',90,2)];expect(courtOpenings(session,rows,15,1)).toEqual([{start:930,end:960}]);expect(courtOpenings(session,rows,15,2)).toEqual([{start:930,end:960}]);});
test('keeps genuine free openings before and after booked matches',()=>{expect(courtOpenings(session,[booking('14:30',30)],15,1)).toEqual([{start:840,end:870},{start:900,end:960}]);});
