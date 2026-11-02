"""Generate synthetic inputs only for disposable integration tests."""
import io,zipfile,xml.etree.ElementTree as ET
from datetime import date,datetime,timedelta,timezone

def package():
 eventId='EV001';first=(date.today()+timedelta(days=10)).isoformat();last=(date.today()+timedelta(days=11)).isoformat()
 people=[{'id':f'P{i+1:03}','eventId':eventId,'name':f'Test Participant {i+1}','dob':f'{1990+i}-01-10','gender':'M' if i%2==0 else 'F','email':f'test{i+1}@example.com','mobile':f'+658123456{i}','club':'Test Club','nationality':'Singapore','sbaId':''} for i in range(4)]
 programs=[{'id':f'PR{i+1:03}','eventId':eventId,'name':'Test Singles' if i==0 else 'Test Doubles','sport':'Badminton','entryType':'S' if i==0 else 'D','minPlayers':i+1,'maxPlayers':i+1,'minAge':18,'maxAge':65,'gender':'O' if i==0 else 'X','minCapacity':2,'maxCapacity':16 if i==0 else 8,'capacityUnit':'E','status':'A'} for i in range(2)]
 rosters=[['P001'],['P002'],['P001','P002'],['P003','P004']]
 entries=[{'id':f'EN{i+1:03}','eventId':eventId,'programId':'PR001' if i<2 else 'PR002','participantIds':roster,'name':'','seed':i+1 if i<2 else None,'status':'A'} for i,roster in enumerate(rosters)]
 return {'schemaVersion':2,'source':'TRS','sourceSystemId':'trs-test','sourceTimeZone':'Asia/Singapore','exportedAt':datetime.now(timezone.utc).isoformat(),'registrationClosed':True,'hasFixtures':False,'events':[{'id':eventId,'name':'Import Test Event','venue':'Test Hall','startDate':first,'endDate':last,'maxProgramsPerParticipant':2,'status':'D'}],'programs':programs,'participants':people,'entries':entries}

def workbook(template):
 p=package();event=p['events'][0];event.update(startDate=(date.today()+timedelta(days=30)).isoformat(),endDate=(date.today()+timedelta(days=31)).isoformat());people={person['id']:person for person in p['participants']}
 rows={1:[['EventID','EventName','SportsType','StartDate','EndDate'],[event['id'],'Workbook Test Event','Badminton',event['startDate'],event['endDate']]],2:[['ProgramID','ProgramName','GameType','Gender','MinAge','MaxAge']],3:[['ParticipantID','ProgramID','GroupID','Name','Dob','Gender','Email','Mobile','Club/School/Company','Nationality','SbaId','Seed']]}
 for program in p['programs']:rows[2].append([program['id'],program['name'],'Singles' if program['entryType']=='S' else 'Doubles',program['gender'],str(program['minAge']),str(program['maxAge'])])
 for entry in p['entries']:
  for id in entry['participantIds']:
   person=people[id];rows[3].append([id,entry['programId'],entry['id'],person['name'],person['dob'],person['gender'],person['email'],person['mobile'],person['club'],'Singapore/Singapore PR','',str(entry['seed']) if entry['seed'] is not None else ''])
 ns='{http://schemas.openxmlformats.org/spreadsheetml/2006/main}';target=io.BytesIO()
 with zipfile.ZipFile(io.BytesIO(template)) as source,zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED) as destination:
  for name in source.namelist():
   raw=source.read(name)
   for sheet,values in rows.items():
    if name==f'xl/worksheets/sheet{sheet}.xml':
     tree=ET.fromstring(raw);data=tree.find(ns+'sheetData');data.clear()
     for rowNumber,rowValues in enumerate(values,1):
      row=ET.SubElement(data,ns+'row',{'r':str(rowNumber)})
      for column,value in enumerate(rowValues):
       cell=ET.SubElement(row,ns+'c',{'r':chr(65+column)+str(rowNumber),'t':'inlineStr'});ET.SubElement(ET.SubElement(cell,ns+'is'),ns+'t').text=value
     raw=ET.tostring(tree)
   destination.writestr(name,raw)
 return target.getvalue()
