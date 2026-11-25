import {useEffect,useState,useRef} from 'react';
import type {FixtureStep} from './fixture-progress';

const paths={dashboard:'/dashboard',events:'/events',participants:'/participants',fixtures:'/fixtures',sba:'/sba-rankings',presets:'/game-presets',settings:'/settings'} as const;
export type AdminPage=keyof typeof paths;
export type AdminRoute={page:AdminPage|'not-found';eventId?:string;programId?:string;step?:FixtureStep;filterEvent?:string;personId?:string;importKind?:'excel'|'trs';replaceId?:string};
const steps:FixtureStep[]=['seeding','draw','schedule','results'];

export function readAdminRoute(pathname=location.pathname,search=location.search):AdminRoute{
  const path=pathname.replace(/\/+$/,'')||'/',query=new URLSearchParams(search);
  if(path==='/')return {page:'dashboard'};
  try{
    const detail=path.match(/^\/(events|fixtures)\/([^/]+)$/);
    const importing=path.match(/^\/events\/import\/(excel|trs)$/);
    if(importing)return {page:'events',importKind:importing[1] as 'excel'|'trs',replaceId:query.get('replace')||undefined};
    const step=query.get('step');
    if(step&&!steps.includes(step as FixtureStep))return {page:'not-found'};
    if(detail)return {page:detail[1] as 'events'|'fixtures',eventId:decodeURIComponent(detail[2]),programId:query.get('program')||undefined,step:step as FixtureStep||undefined};
    const page=(Object.keys(paths) as AdminPage[]).find(key=>paths[key]===path);
    if(!page)return {page:'not-found'};
    return {page,eventId:page==='participants'?query.get('event')||undefined:undefined,filterEvent:page==='fixtures'?query.get('event')||undefined:undefined,programId:query.get('program')||undefined,personId:page==='participants'?query.get('person')||undefined:undefined};
  }catch{return {page:'not-found'};}
}

export function adminUrl(route:AdminRoute){
  if(route.page==='not-found')return '/not-found';
  let path:string=paths[route.page];const query=new URLSearchParams();
  if(route.importKind){path='/events/import/'+route.importKind;if(route.replaceId)query.set('replace',route.replaceId);}
  else if(route.eventId&&(route.page==='events'||route.page==='fixtures'))path+='/'+encodeURIComponent(route.eventId);
  if(route.page==='participants'&&route.eventId)query.set('event',route.eventId);
  if(route.page==='fixtures'&&!route.eventId&&route.filterEvent)query.set('event',route.filterEvent);
  if(route.programId&&(route.page==='participants'||route.page==='fixtures'))query.set('program',route.programId);
  if(route.step&&route.page==='fixtures'&&route.eventId)query.set('step',route.step);
  if(route.personId&&route.page==='participants')query.set('person',route.personId);
  return path+(query.size?'?'+query.toString():'');
}

export function useAdminRoute(canLeave?:()=>boolean){
  const [route,setRoute]=useState(()=>readAdminRoute());
  const guard=useRef(canLeave),index=useRef<number>(history.state?.tlsRouteIndex??0),restoring=useRef(false);guard.current=canLeave;
  useEffect(()=>{
    history.replaceState({...history.state,tlsRouteIndex:index.current},'',location.pathname==='/'?adminUrl({page:'dashboard'}):location.href);
    const restore=(event:PopStateEvent)=>{
      if(restoring.current){restoring.current=false;return;}
      const next=event.state?.tlsRouteIndex;
      if(guard.current&&!guard.current()){restoring.current=true;history.go(typeof next==='number'&&next!==index.current?index.current-next:1);return;}
      index.current=typeof next==='number'?next:index.current-1;setRoute(readAdminRoute());
    };
    window.addEventListener('popstate',restore);return()=>window.removeEventListener('popstate',restore);
  },[]);
  function go(next:AdminRoute,replace=false){
    const url=adminUrl(next);if(url===location.pathname+location.search)return;
    if(!replace)index.current++;
    if(replace)history.replaceState({...history.state,tlsRouteIndex:index.current},'',url);else history.pushState({tlsRouteIndex:index.current},'',url);
    setRoute(readAdminRoute());
  }
  return {route,go};
}
