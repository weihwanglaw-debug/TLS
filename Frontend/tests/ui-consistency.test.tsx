// @vitest-environment node
import {readFileSync,readdirSync} from 'node:fs';
import {join,relative} from 'node:path';
import {fileURLToPath} from 'node:url';
import ts from 'typescript';
import {expect,test} from 'vitest';

const sourceRoot=fileURLToPath(new URL('../src',import.meta.url));
const nativePickerTypes=new Set(['date','time','datetime-local','month','week']);
// No exceptions are currently needed. Shared-component internals require explicit approval too.
const approvedNativePickerFiles=new Set<string>();
function violations(source:string,name:string){
 const file=ts.createSourceFile(name,source,ts.ScriptTarget.Latest,true,ts.ScriptKind.TSX),found:string[]=[];
 function visit(node:ts.Node){
  if((ts.isJsxSelfClosingElement(node)||ts.isJsxOpeningElement(node))&&node.tagName.getText(file)==='input'){
   for(const attribute of node.attributes.properties){
    if(!ts.isJsxAttribute(attribute)||attribute.name.getText(file)!=='type')continue;
    const value=attribute.initializer;
    const expression=value&&ts.isJsxExpression(value)?value.expression:value;
    if(expression&&(ts.isStringLiteral(expression)||ts.isNoSubstitutionTemplateLiteral(expression))&&nativePickerTypes.has(expression.text.toLowerCase())){
     found.push(`${name}:${file.getLineAndCharacterOfPosition(node.getStart(file)).line+1}: reuse DatePicker, TimePicker or DateTimePicker`);
    }
   }
  }
  ts.forEachChild(node,visit);
 }
 visit(file);return found;
}
function files(directory:string):string[]{return readdirSync(directory,{withFileTypes:true}).flatMap(item=>item.isDirectory()?files(join(directory,item.name)):/\.[jt]sx?$/.test(item.name)?[join(directory,item.name)]:[]);}

test.each(['<input type="time"/>',"<input type={'date'}/>",'<input type={`datetime-local`}/>','<input\n type="month"/>','<input type="week"/>'])('detects native date/time picker syntax: %s',source=>{
 expect(violations(source,'Example.tsx')).toHaveLength(1);
});
test('shared picker usage and ordinary inputs are allowed',()=>{
 expect(violations('<><TimePicker/><DatePicker/><DateTimePicker/><input type="number"/><input type="text"/></>','Example.tsx')).toEqual([]);
});
test('application date/time fields reuse the shared pickers',()=>{
 const found=files(sourceRoot).flatMap(file=>{const name=relative(sourceRoot,file).replaceAll('\\','/');return approvedNativePickerFiles.has(name)?[]:violations(readFileSync(file,'utf8'),name);});
 expect(found,'Native date/time picker found. Reuse the existing shared component; document and approve any necessary exception.').toEqual([]);
});
