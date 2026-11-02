"""Synthetic SBA workbooks for disposable integration tests only."""
import io,os,zipfile,xml.etree.ElementTree as ET

def workbook(rows=None,invalid=False,duplicate=False,padding=0):
 ns='http://schemas.openxmlformats.org/spreadsheetml/2006/main';strings=[]
 data=rows or [("Men's Singles",[['Rank','Player','ID','Year','Points','Tournaments','Club','DOB'],['1','Ranking Name','SBA001','1991','500','3','Ranking Club','1991-01-10'],['2','Second Player','SBA002','','400','2','Other Club','']]),("Men's Doubles",[['Rank','P1','P2','ID1','ID2','Year','Points','Tournaments','Club1','Club2','DOB1','DOB2'],['1','Partner One','Partner Two','SBA001','SBA003','','600','4','ranking club','Pair Club','1991-01-10','1992-02-20']]),('Notes',[['Ignored']])]
 if invalid:data[0][1][1][2]=''
 if duplicate:data[0][1].append(list(data[0][1][1]))
 out=io.BytesIO()
 with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED) as z:
  book=ET.Element('workbook',xmlns=ns);sheets=ET.SubElement(book,'sheets');rels=ET.Element('Relationships',xmlns='http://schemas.openxmlformats.org/package/2006/relationships')
  for i,(name,values) in enumerate(data,1):
   ET.SubElement(sheets,'sheet',{'name':name,'sheetId':str(i),'{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id':'rId'+str(i)})
   ET.SubElement(rels,'Relationship',{'Id':'rId'+str(i),'Target':'worksheets/sheet'+str(i)+'.xml'})
   sheet=ET.Element('worksheet',xmlns=ns);sd=ET.SubElement(sheet,'sheetData')
   for rn,rv in enumerate(values,1):
    row=ET.SubElement(sd,'row',r=str(rn))
    for col,value in enumerate(rv):
     strings.append(str(value));cell=ET.SubElement(row,'c',r=chr(65+col)+str(rn),t='s');ET.SubElement(cell,'v').text=str(len(strings)-1)
   z.writestr('xl/worksheets/sheet'+str(i)+'.xml',ET.tostring(sheet))
  shared=ET.Element('sst',xmlns=ns)
  for value in strings:ET.SubElement(ET.SubElement(shared,'si'),'t').text=value
  z.writestr('xl/sharedStrings.xml',ET.tostring(shared));z.writestr('xl/workbook.xml',ET.tostring(book));z.writestr('xl/_rels/workbook.xml.rels',ET.tostring(rels))
  if padding:z.writestr('extra.bin',os.urandom(padding))
 return out.getvalue()
