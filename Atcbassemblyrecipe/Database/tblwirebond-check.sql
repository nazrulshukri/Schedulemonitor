-- =====================================================================
-- TBLWIREBOND - "I saved a row on the page and nothing appeared"
--
-- Run the blocks in order as the OCAPSYS user (or as an account with
-- SELECT on OCAPSYS). Each block is one statement: in SQL Developer use
-- Run Statement (Ctrl+Enter) on the block you want, or Run Script (F5)
-- for the whole file.
--
-- The page inserts with LASTUPDATE = SYSDATE and sorts newest first, so
-- a successful save is always the FIRST row of the grid. If block 1
-- finds the row but the grid does not show it, the problem is the page
-- (permissions - see block 5). If block 1 finds nothing, the insert
-- never committed - blocks 2 and 3 say why.
-- =====================================================================


-- ---------------------------------------------------------------------
-- 1. Is the row actually there? Newest 20, same order as the page.
-- ---------------------------------------------------------------------
SELECT TO_CHAR(lastupdate, 'YYYY-MM-DD HH24:MI:SS') AS lastupdate,
       lastupdatedby,
       wbocapno,
       wbocapwwk,
       TO_CHAR(wbdate, 'YYYY-MM-DD HH24:MI') AS wbdate,
       wbmachine,
       wbpackage,
       wbdefect,
       tblrowid
FROM   OCAPSYS.TBLWIREBOND
ORDER  BY lastupdate DESC NULLS LAST
FETCH FIRST 20 ROWS ONLY;


-- ---------------------------------------------------------------------
-- 2. Is the work-week trigger healthy?
--
-- STATUS must be ENABLED and VALID. An INVALID trigger fails EVERY
-- insert with ORA-04098, and the page shows that text in a red popup.
-- The usual cause is that GET_WWK_APP_CUTOFF does not exist in this
-- schema (or is not granted to it), which is a compile-time failure the
-- trigger body cannot catch.
-- ---------------------------------------------------------------------
SELECT trigger_name, status, table_name
FROM   all_triggers
WHERE  trigger_name = 'OCAP_WIREBOND_WORKWEEK';

SELECT object_name, object_type, status
FROM   all_objects
WHERE  object_name = 'OCAP_WIREBOND_WORKWEEK';


-- ---------------------------------------------------------------------
-- 3. Does the work-week function the trigger calls exist here?
--    No rows back = the trigger in tblwirebond.sql cannot compile.
--    Fix it with block 4.
-- ---------------------------------------------------------------------
SELECT owner, object_name, object_type, status
FROM   all_objects
WHERE  object_name = 'GET_WWK_APP_CUTOFF';


-- ---------------------------------------------------------------------
-- 4. FAIL-SAFE WORK-WEEK TRIGGER (optional but recommended)
--
-- Same behaviour as the trigger in tblwirebond.sql, with one difference:
-- GET_WWK_APP_CUTOFF is called through dynamic SQL, so a missing,
-- invalid or un-granted function can never stop an insert. When the
-- function cannot be called the work week falls back to the ISO week of
-- the row's own date (e.g. 2026 week 37 -> '202637').
--
-- A work week the caller supplied on purpose is still kept.
--
-- Run this whole block as a script (F5) - it ends with the / on its own
-- line, which is what tells the client the PL/SQL body is finished.
-- ---------------------------------------------------------------------
CREATE OR REPLACE TRIGGER OCAPSYS.OCAP_WIREBOND_WORKWEEK
BEFORE INSERT
ON OCAPSYS.TBLWIREBOND
FOR EACH ROW
DECLARE
   v_when     DATE := NVL(:NEW.WBDATE, SYSDATE);
   v_workweek NUMBER;
   v_text     VARCHAR2(100);
BEGIN
   -- A work week that was passed in deliberately (a backdated record being
   -- keyed in late) wins. Drop this IF if the trigger should always win.
   IF :NEW.WBOCAPWWK IS NOT NULL THEN
      RETURN;
   END IF;

   BEGIN
      EXECUTE IMMEDIATE 'BEGIN :r := get_wwk_app_cutoff(:d); END;'
        USING OUT v_workweek, IN v_when;
      v_text := TO_CHAR(v_workweek);
   EXCEPTION
      WHEN OTHERS THEN
         -- Missing function, no grant, bad NLS date handling inside it -
         -- none of that is a reason to reject the record.
         v_text := NULL;
   END;

   :NEW.WBOCAPWWK := NVL(v_text,
                         TO_CHAR(v_when, 'IYYY') || TO_CHAR(v_when, 'IW'));
END;
/

-- Confirm it compiled clean (no rows back = no errors):
SELECT line, position, text
FROM   all_errors
WHERE  name = 'OCAP_WIREBOND_WORKWEEK'
ORDER  BY sequence;


-- ---------------------------------------------------------------------
-- 5. Can the signed-in user actually see the Add / Edit / Delete
--    buttons? Super Admins always can. Everyone else needs a row here
--    with MODULE_NAME 'Wirebond':
--      READ  = view only (no Add / Edit / Delete)
--      WRITE = view + add + update
--      ADMIN = view + add + update + delete
--    Replace NX487878 with the login id that is having the problem.
-- ---------------------------------------------------------------------
SELECT user_id, user_name, role_name, module_name, access_level, status
FROM   OCAPSYS.TBLACCESS
WHERE  user_id = 'NX487878'
ORDER  BY module_name;


-- ---------------------------------------------------------------------
-- 6. End-to-end test insert, then thrown away. If this raises an error,
--    the page would have raised the same one. If it succeeds, the write
--    path is fine and the problem is access (block 5).
--
--    Run the three statements one at a time.
-- ---------------------------------------------------------------------
INSERT INTO OCAPSYS.TBLWIREBOND
    (TBLROWID, LASTUPDATE, LASTUPDATEDBY, WBOCAPNO, WBDATE,
     WBPROCESS, WBMACHINE, WBPACKAGE, WBSOQTY, WBDEFECT, WBDEFECTCAT)
VALUES
    (RAWTOHEX(SYS_GUID()), SYSDATE, 'TESTUSER', 'WB-SELFTEST-001', SYSDATE,
     'WIREBOND', 'WB-07', 'SOT669', 3000, 'NON STICK ON PAD', 'PROCESS');

-- The trigger should have filled WBOCAPWWK on the row above:
SELECT wbocapno, wbocapwwk, TO_CHAR(wbdate, 'YYYY-MM-DD HH24:MI') AS wbdate
FROM   OCAPSYS.TBLWIREBOND
WHERE  wbocapno = 'WB-SELFTEST-001';

-- Put the table back the way it was:
ROLLBACK;
