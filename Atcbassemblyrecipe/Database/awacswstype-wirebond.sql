-- =====================================================================
-- Register the wire bonders in OCAPSYS.AWACSWSTYPE
--
-- AWACSWSTYPE is the workstation master: one row per machine.
--   WSID    the machine, e.g. DS12-009
--   WSTYPE  what kind of machine it is, e.g. SAWING
--   WSDB    the table holding that machine's recipes, e.g. TBLSAWING
--
-- Until now it only carried SAWING. Wirebond recipes live in
-- TBLWIREBOND, so the wire bonders belong here too, as
-- WSTYPE = 'WIREBOND', WSDB = 'TBLWIREBOND'.
--
-- YOU PROBABLY DO NOT NEED THIS FILE. The AWACSWSTYPE page can now do
-- it: Operations > AWACSWSTYPE > Add Row, type the WSID, pick WIREBOND
-- in the WSTYPE list, save. WSDB is filled in for you. Use this script
-- to load a batch of machines in one go, or when the app cannot be
-- reached.
--
-- Run one statement at a time (Ctrl+Enter in SQL Developer, F9 in
-- Toad), or F5 for the whole file.
-- =====================================================================


-- ---------------------------------------------------------------------
-- 1. What is in there today?
--    Shows every WSTYPE in the table and how many machines carry it, so
--    you can see whether the wire bonders are already listed under some
--    other WSTYPE before adding them again.
-- ---------------------------------------------------------------------
SELECT wstype,
       COUNT(*)        AS machines,
       MIN(wsid)       AS example_wsid,
       MAX(wsdb)       AS wsdb
FROM   OCAPSYS.AWACSWSTYPE
GROUP  BY wstype
ORDER  BY wstype;


-- ---------------------------------------------------------------------
-- 2. One wire bonder.
--    Replace WB-001 with the real WSID. Repeat for each machine, or use
--    block 3.
-- ---------------------------------------------------------------------
INSERT INTO OCAPSYS.AWACSWSTYPE
    (TBLROWID, LASTUPDATE, LASTUPDATEDBY, WSID, WSTYPE, WSDB)
VALUES
    (RAWTOHEX(SYS_GUID()), SYSDATE, 'NX487878', 'WB-001', 'WIREBOND', 'TBLWIREBOND');

COMMIT;


-- ---------------------------------------------------------------------
-- 3. A batch of them.
--    Put your real WSIDs in the list at the top. Add or remove lines as
--    needed - nothing else changes.
--
--    Skips any WSID already registered as WIREBOND, so it is safe to
--    re-run after adding more machines to the list.
-- ---------------------------------------------------------------------
INSERT INTO OCAPSYS.AWACSWSTYPE
    (TBLROWID, LASTUPDATE, LASTUPDATEDBY, WSID, WSTYPE, WSDB)
SELECT RAWTOHEX(SYS_GUID()), SYSDATE, 'NX487878', m.wsid, 'WIREBOND', 'TBLWIREBOND'
FROM   (SELECT 'WB-001' AS wsid FROM dual UNION ALL
        SELECT 'WB-002'         FROM dual UNION ALL
        SELECT 'WB-003'         FROM dual UNION ALL
        SELECT 'WB-007'         FROM dual UNION ALL
        SELECT 'WB-012'         FROM dual) m
WHERE  NOT EXISTS (SELECT 1
                   FROM   OCAPSYS.AWACSWSTYPE a
                   WHERE  UPPER(a.wsid)   = UPPER(m.wsid)
                     AND  UPPER(a.wstype) = 'WIREBOND');

COMMIT;


-- ---------------------------------------------------------------------
-- 4. Check the result.
--    Every WIREBOND row must have WSDB = TBLWIREBOND. The app derives
--    WSDB from WSTYPE on every save, so rows written from the page are
--    always right; this catches rows keyed in by hand.
-- ---------------------------------------------------------------------
SELECT wsid,
       wstype,
       wsdb,
       lastupdatedby,
       TO_CHAR(lastupdate, 'YYYY-MM-DD HH24:MI') AS lastupdate
FROM   OCAPSYS.AWACSWSTYPE
WHERE  wstype IN ('SAWING', 'WIREBOND')
ORDER  BY wstype, wsid;


-- Wrong WSDB on a WIREBOND row? This repairs them:
-- UPDATE OCAPSYS.AWACSWSTYPE
--    SET wsdb = 'TBLWIREBOND', lastupdate = SYSDATE
--  WHERE wstype = 'WIREBOND'
--    AND (wsdb IS NULL OR wsdb <> 'TBLWIREBOND');
-- COMMIT;
