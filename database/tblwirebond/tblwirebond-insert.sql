-- =====================================================================
-- OCAPSYS.TBLWIREBOND - how to insert values.
--
-- Copy the pattern you need. Every example is complete and runnable in
-- SQL Developer / SQL*Plus as the OCAPSYS user (drop the OCAPSYS.
-- prefix if you connect as OCAPSYS; keep it if you connect as someone
-- else who has INSERT granted).
--
-- Three rules for this table:
--   1. TBLROWID is a normal VARCHAR2(50) column, NOT the Oracle ROWID
--      pseudo-column. Fill it with RAWTOHEX(SYS_GUID()) - the same thing
--      the web app does for AWACSWSTYPE. Never write ROWID in an INSERT.
--   2. Do NOT list WBOCAPWWK. The BEFORE INSERT trigger
--      OCAP_WIREBOND_WORKWEEK computes it from the date on the row. Anything
--      you pass is replaced (or, with the NVL variant in
--      tblwirebond.sql, only kept if you deliberately set it).
--   3. Nothing is committed until you say COMMIT. Until then only your
--      own session can see the row.
-- =====================================================================


-- ---------------------------------------------------------------------
-- 1. The everyday insert - only the columns actually keyed in
-- ---------------------------------------------------------------------
INSERT INTO OCAPSYS.TBLWIREBOND
    (TBLROWID, LASTUPDATE, LASTUPDATEDBY,
     WBOCAPNO, WBISSUEDBY, WBBFG, WBDATE,
     WBOPERATORID, WBPROCESS, WBMACHINE, WBPACKAGE, WBSOQTY,
     WBDEFECT, WBDEFECTCAT)
VALUES
    (RAWTOHEX(SYS_GUID()), SYSDATE, 'NX487878',
     'OCAP-WB-2026-001', 'NX487878', 'BFG1',
     TO_DATE('2026-09-10 14:30', 'YYYY-MM-DD HH24:MI'),
     'OP1023', 'WIREBOND', 'WB-07', 'SOT669', 3000,
     'NON STICK ON PAD', 'PROCESS');

COMMIT;


-- ---------------------------------------------------------------------
-- 2. The full insert - every column, so nothing is forgotten on a
--    record that goes all the way through to disposition
-- ---------------------------------------------------------------------
INSERT INTO OCAPSYS.TBLWIREBOND
    (TBLROWID,
     LASTUPDATE,
     LASTUPDATEDBY,
     WBOCAPNO,
     WBISSUEDBY,
     WBBFG,
     WBDATE,
     WBOPERATORID,
     WBPROCESS,
     WBMACHINE,
     WBPACKAGE,
     WBSOQTY,
     WBDEFECT,
     WBDEFECTCAT,
     WBDEFECTOTHERS,
     WBDIFF4M1E,
     WBDIFFAFFECTED,
     WBDIFFFABSITE,
     WBDIFFNO,
     WBDIFFNOTAFFECTED,
     WBDIFFREJECTQTY,
     WBDIFFREMARKS,
     WBVERIFIEDBY,
     WBACTIONTAKEN,
     WBDISPOSITION,
     WBREMARKS,
     WBRCMACHINEERROR,
     WBMACHINEERROR)
VALUES
    (RAWTOHEX(SYS_GUID()),
     SYSDATE,
     'NX487878',
     'OCAP-WB-2026-002',
     'NX021557',
     'BFG2',
     TO_DATE('2026-09-10 22:05', 'YYYY-MM-DD HH24:MI'),
     'OP1044',
     'WIREBOND',
     'WB-12',
     'SOT1210',
     5000,
     'BOND LIFT',
     'MACHINE',
     NULL,                                   -- WBDEFECTOTHERS: only when WBDEFECT is "OTHERS"
     'MACHINE',                              -- WBDIFF4M1E: MAN / MACHINE / MATERIAL / METHOD / ENVIRONMENT
     'LOT2609001,LOT2609002',
     'ATCB',
     'DIFF-2026-0912',
     'LOT2609003',
     120,
     'Bond lift found on 2 of 5 lots after capillary change.',
     'NX000123',
     'Capillary replaced, machine requalified with 30 units BPT.',
     'Affected lots to 100% visual inspection, not-affected lots released.',
     'Raised on night shift.',
     'E-042 Z-axis search level fault',       -- WBRCMACHINEERROR: root cause detail, up to 1000 chars
     'E-042');                                -- WBMACHINEERROR: the machine error code itself

COMMIT;


-- ---------------------------------------------------------------------
-- 3. Several rows in one statement - INSERT ALL
--    (Oracle has no multi-row VALUES (...),(...) syntax.)
--    All rows commit or none do, and the trigger still fires per row.
-- ---------------------------------------------------------------------
INSERT ALL
    INTO OCAPSYS.TBLWIREBOND
        (TBLROWID, LASTUPDATE, LASTUPDATEDBY, WBOCAPNO, WBDATE,
         WBMACHINE, WBPACKAGE, WBSOQTY, WBDEFECT, WBDEFECTCAT)
    VALUES
        (RAWTOHEX(SYS_GUID()), SYSDATE, 'NX487878', 'OCAP-WB-2026-003',
         TO_DATE('2026-09-08', 'YYYY-MM-DD'), 'WB-03', 'SOT669', 3000,
         'NON STICK ON LEAD', 'PROCESS')
    INTO OCAPSYS.TBLWIREBOND
        (TBLROWID, LASTUPDATE, LASTUPDATEDBY, WBOCAPNO, WBDATE,
         WBMACHINE, WBPACKAGE, WBSOQTY, WBDEFECT, WBDEFECTCAT)
    VALUES
        (RAWTOHEX(SYS_GUID()), SYSDATE, 'NX487878', 'OCAP-WB-2026-004',
         TO_DATE('2026-09-09', 'YYYY-MM-DD'), 'WB-05', 'SOT1235', 4500,
         'WIRE SAG', 'MACHINE')
    INTO OCAPSYS.TBLWIREBOND
        (TBLROWID, LASTUPDATE, LASTUPDATEDBY, WBOCAPNO, WBDATE,
         WBMACHINE, WBPACKAGE, WBSOQTY, WBDEFECT, WBDEFECTCAT)
    VALUES
        (RAWTOHEX(SYS_GUID()), SYSDATE, 'NX487878', 'OCAP-WB-2026-005',
         TO_DATE('2026-09-09', 'YYYY-MM-DD'), 'WB-05', 'SOT1235', 4500,
         'BALL SHEAR LOW', 'MATERIAL')
SELECT 1 FROM dual;

COMMIT;


-- ---------------------------------------------------------------------
-- 4. Bind variables - the form the web app and any parameterized client
--    should use. Never build an INSERT by pasting user text into the
--    string; a name with an apostrophe breaks it and it is an injection
--    hole. With Oracle.ManagedDataAccess set command.BindByName = true,
--    then add one OracleParameter per name below.
-- ---------------------------------------------------------------------
INSERT INTO tblwirebond
    (tblrowid, lastupdate, lastupdatedby,
     wbocapno, wbissuedby, wbbfg, wbdate,
     wboperatorid, wbprocess, wbmachine, wbpackage, wbsoqty,
     wbdefect, wbdefectcat, wbdefectothers,
     wbdiff4m1e, wbdiffaffected, wbdifffabsite, wbdiffno,
     wbdiffnotaffected, wbdiffrejectqty, wbdiffremarks,
     wbverifiedby, wbactiontaken, wbdisposition, wbremarks,
     wbrcmachineerror, wbmachineerror)
VALUES
    (RAWTOHEX(SYS_GUID()), SYSDATE, :lastupdatedby,
     :wbocapno, :wbissuedby, :wbbfg, :wbdate,
     :wboperatorid, :wbprocess, :wbmachine, :wbpackage, :wbsoqty,
     :wbdefect, :wbdefectcat, :wbdefectothers,
     :wbdiff4m1e, :wbdiffaffected, :wbdifffabsite, :wbdiffno,
     :wbdiffnotaffected, :wbdiffrejectqty, :wbdiffremarks,
     :wbverifiedby, :wbactiontaken, :wbdisposition, :wbremarks,
     :wbrcmachineerror, :wbmachineerror);


-- ---------------------------------------------------------------------
-- 5. Bulk load from a staging table (an uploaded spreadsheet landed in
--    TBLWIREBOND_STG with matching column names). The trigger fills
--    WBOCAPWWK for each row, so it is not selected here either.
-- ---------------------------------------------------------------------
-- INSERT INTO OCAPSYS.TBLWIREBOND
--     (TBLROWID, LASTUPDATE, LASTUPDATEDBY, WBOCAPNO, WBISSUEDBY, WBBFG,
--      WBDATE, WBOPERATORID, WBPROCESS, WBMACHINE, WBPACKAGE, WBSOQTY,
--      WBDEFECT, WBDEFECTCAT)
-- SELECT RAWTOHEX(SYS_GUID()), SYSDATE, 'NX487878',
--        s.wbocapno, s.wbissuedby, s.wbbfg,
--        TO_DATE(s.wbdate_text, 'YYYY-MM-DD HH24:MI'),
--        s.wboperatorid, 'WIREBOND', s.wbmachine, s.wbpackage,
--        TO_NUMBER(s.wbsoqty_text),
--        s.wbdefect, s.wbdefectcat
-- FROM   OCAPSYS.TBLWIREBOND_STG s
-- WHERE  s.wbocapno IS NOT NULL
--   AND  NOT EXISTS (SELECT 1 FROM OCAPSYS.TBLWIREBOND t
--                    WHERE t.wbocapno = s.wbocapno);
-- COMMIT;


-- ---------------------------------------------------------------------
-- 6. Check what landed, before and after COMMIT
-- ---------------------------------------------------------------------
SELECT tblrowid,
       wbocapno,
       wbocapwwk,          -- filled by the trigger
       TO_CHAR(wbdate, 'YYYY-MM-DD HH24:MI') AS wbdate,
       wbmachine,
       wbpackage,
       wbsoqty,
       wbdefect,
       wbdefectcat,
       lastupdatedby,
       TO_CHAR(lastupdate, 'YYYY-MM-DD HH24:MI:SS') AS lastupdate
FROM   OCAPSYS.TBLWIREBOND
ORDER  BY lastupdate DESC NULLS LAST
FETCH FIRST 20 ROWS ONLY;

-- Undo everything since the last COMMIT:
-- ROLLBACK;


-- ---------------------------------------------------------------------
-- 7. Correcting a row you already inserted
--
--    Always address the row by its TBLROWID, never by WBOCAPNO alone -
--    there is no unique constraint on WBOCAPNO, so a typo could update
--    more rows than you meant. Check the row count before committing.
--
--    NOTE: the work-week trigger is BEFORE INSERT only. An UPDATE does
--    not recompute WBOCAPWWK; set it yourself if WBDATE moves.
-- ---------------------------------------------------------------------
-- UPDATE OCAPSYS.TBLWIREBOND
--    SET wbactiontaken = 'Capillary replaced and requalified.',
--        wbdisposition = 'Affected lots 100% inspected, released.',
--        wbverifiedby  = 'NX000123',
--        lastupdate    = SYSDATE,
--        lastupdatedby = 'NX487878'
--  WHERE tblrowid = 'PUT-THE-TBLROWID-HERE';
-- -- rows updated should be 1
-- COMMIT;


-- ---------------------------------------------------------------------
-- Errors you will hit, and what they mean
--
--   ORA-00942  table or view does not exist
--              -> wrong schema, or run tblwirebond.sql first, or no
--                 grant. SELECT * FROM all_tables WHERE table_name =
--                 'TBLWIREBOND';
--   ORA-01843  not a valid month
--   ORA-01861  literal does not match format string
--              -> a date passed as a bare string ('10/09/2026'). Always
--                 TO_DATE with an explicit mask, or bind a real date.
--                 Also the symptom of the unfixed trigger described in
--                 tblwirebond.sql.
--   ORA-12899  value too large for column
--              -> WBREMARKS is 500, WBRCMACHINEERROR 1000,
--                 WBDIFFREMARKS/WBACTIONTAKEN/WBDISPOSITION 2000, most
--                 of the rest 100.
--   ORA-01722  invalid number
--              -> text in WBSOQTY or WBDIFFREJECTQTY (both INTEGER).
--   ORA-01756  quoted string not properly terminated
--              -> an apostrophe in free text. Double it ('Won''t bond')
--                 or use bind variables as in section 4.
--   ORA-04098  trigger is invalid and failed re-validation
--              -> GET_WWK_APP_CUTOFF is missing or not granted to this
--                 schema. SELECT object_name, status FROM all_objects
--                 WHERE object_name = 'GET_WWK_APP_CUTOFF';
-- ---------------------------------------------------------------------
