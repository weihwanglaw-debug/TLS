SET XACT_ABORT ON;
IF EXISTS (SELECT 1 FROM EventOperations WHERE ISJSON(Json)<>1)
    THROW 51000, 'Invalid event JSON; restore or repair before upgrading scheduling.', 1;
UPDATE e SET Json=CASE WHEN JSON_QUERY(e.Json,'$.schedulePlanner') IS NOT NULL
    THEN JSON_MODIFY(JSON_MODIFY(e.Json,'$.planningDraft',NULL),'$.schedulePlanner.draft',NULL)
    ELSE JSON_MODIFY(e.Json,'$.planningDraft',NULL) END
FROM EventOperations e
WHERE EXISTS (SELECT 1 FROM OPENJSON(e.Json) WHERE [key]='planningDraft')
   OR EXISTS (SELECT 1 FROM OPENJSON(e.Json,'$.schedulePlanner') WHERE [key]='draft');
IF @@ROWCOUNT>0 UPDATE SystemSettings SET Revision=Revision+1 WHERE Id=1;
