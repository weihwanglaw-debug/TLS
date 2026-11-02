export class LiveError extends Error {constructor(message:string,public status=0){super(message);}}
export async function liveApi<T>(path:string,body?:unknown,token?:string,timeout=5000):Promise<T> {
  const controller=new AbortController(),timer=setTimeout(()=>controller.abort(),timeout);
  try{
    const response=await fetch('/api/'+path,{method:body===undefined?'GET':'POST',signal:controller.signal,headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(token?{Authorization:'Bearer '+token}:{})},body:body===undefined?undefined:JSON.stringify(body)});
    let result:any;try{result=await response.json();}catch{throw new LiveError('The server returned an unexpected response.',response.status);}
    if(!response.ok)throw new LiveError(result.message||'The request was not accepted.',response.status);return result as T;
  }catch(error){if(error instanceof LiveError)throw error;throw new LiveError('No server acknowledgement. Check the confirmed score before retrying.');}finally{clearTimeout(timer);}
}
export function operationId(){const bytes=new Uint8Array(16);crypto.getRandomValues(bytes);bytes[6]=(bytes[6]&15)|64;bytes[8]=(bytes[8]&63)|128;const hex=Array.from(bytes,b=>b.toString(16).padStart(2,'0')).join('');return `${hex.slice(0,8)}-${hex.slice(8,12)}-${hex.slice(12,16)}-${hex.slice(16,20)}-${hex.slice(20)}`;}
