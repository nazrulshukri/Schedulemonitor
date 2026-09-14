-- =====================================================================
-- AWACSWSTYPE.WSDB - repair rows the app wrote wrongly
--
-- WSDB is the machine's business group: SENSORS, POWER and so on. It
-- matches SAWBFG in TBLSAWING.
--
-- An earlier version of the AWACSWSTYPE page treated WSDB as a table
-- name and wrote 'TBLSAWING' into it - first as a hidden field on the
-- add row, then (briefly) derived from WSTYPE on every save, which also
-- overwrote the real value when an existing row was edited. Both are
-- gone: WSDB is a normal field on the grid now, with the values already
-- in the table offered as a pick list.
--
-- This file finds and fixes the rows that were written wrongly.
--
-- Run one statement at a time, or F5 for the whole file - nothing below
-- changes data unless you uncomment it.
-- =====================================================================


-- ---------------------------------------------------------------------
-- 1. What WSDB values are in the table, and how many rows each?
--    Expect SENSORS and POWER. TBLSAWING or TBLWIREBOND in this list
--    means rows were written by the old page.
-- ---------------------------------------------------------------------
SELECT NVL(wsdb, '(null)') AS wsdb,
       COUNT(*)            AS machines
FROM   OCAPSYS.AWACSWSTYPE
GROUP  BY wsdb
ORDER  BY machines DESC;


-- ---------------------------------------------------------------------
-- 2. The affected rows, with a suggestion.
--
-- TBLSAWING tells you what the machine's real business group is:
-- SAWMACHINE is the WSID and SAWBFG is the group. Where the machine
-- appears there, suggested_wsdb is filled in.
-- ---------------------------------------------------------------------
SELECT a.wsid,
       a.wstype,
       a.wsdb                AS current_wsdb,
       (SELECT MAX(s.sawbfg)
        FROM   OCAPSYS.TBLSAWING s
        WHERE  UPPER(s.sawmachine) = UPPER(a.wsid)) AS suggested_wsdb,
       a.lastupdatedby,
       TO_CHAR(a.lastupdate, 'YYYY-MM-DD HH24:MI')  AS lastupdate
FROM   OCAPSYS.AWACSWSTYPE a
WHERE  a.wsdb IN ('TBLSAWING', 'TBLWIREBOND')
ORDER  BY a.wsid;


-- ---------------------------------------------------------------------
-- 3. Apply the suggestion, where there is one.
--    Leaves alone any row TBLSAWING has never seen - set those by hand
--    with block 4.
-- ---------------------------------------------------------------------
-- UPDATE OCAPSYS.AWACSWSTYPE a
--    SET a.wsdb = (SELECT MAX(s.sawbfg)
--                  FROM   OCAPSYS.TBLSAWING s
--                  WHERE  UPPER(s.sawmachine) = UPPER(a.wsid))
--  WHERE a.wsdb IN ('TBLSAWING', 'TBLWIREBOND')
--    AND EXISTS (SELECT 1
--                FROM   OCAPSYS.TBLSAWING s
--                WHERE  UPPER(s.sawmachine) = UPPER(a.wsid)
--                  AND  s.sawbfg IS NOT NULL);
-- COMMIT;


-- ---------------------------------------------------------------------
-- 4. Set the rest by hand. One machine at a time.
-- ---------------------------------------------------------------------
-- UPDATE OCAPSYS.AWACSWSTYPE
--    SET wsdb = 'SENSORS', lastupdate = SYSDATE
--  WHERE wsid = 'DSDI-002';
-- COMMIT;


-- ---------------------------------------------------------------------
-- 5. Check nothing is left.
--    No rows back means every machine carries a real business group.
-- ---------------------------------------------------------------------
SELECT wsid, wstype, wsdb
FROM   OCAPSYS.AWACSWSTYPE
WHERE  wsdb IN ('TBLSAWING', 'TBLWIREBOND')
    OR wsdb IS NULL
ORDER  BY wstype, wsid;
