-- =====================================================================
-- OCAPSYS.TBLWIREBOND - how to insert values by hand.
--
-- Three rules for this table:
--   1. PACKAGE is a reserved word in Oracle. Write it "PACKAGE" -
--      double-quoted and uppercase - or you get ORA-00904.
--   2. TBLROWID is a normal VARCHAR2(50) column, NOT the Oracle ROWID
--      pseudo-column. Fill it with RAWTOHEX(SYS_GUID()), the same thing
--      the web app does. Never write ROWID in an INSERT.
--   3. Nothing is committed until you say COMMIT. Until then only your
--      own session can see the row.
--
-- HOW TO RUN THESE: each block is one statement followed by its own
-- COMMIT. Most clients only accept ONE statement at a time, so highlight
-- the INSERT by itself (WITHOUT the trailing COMMIT), run it, then run
-- COMMIT on its own. Sending both together gives
--   ORA-00933: SQL command not properly ended
-- pointing at the COMMIT line. In SQL Developer, Run Script (F5) runs a
-- whole ;-separated batch; Run Statement (Ctrl+Enter) does not.
-- =====================================================================


-- ---------------------------------------------------------------------
-- 1. One row
-- ---------------------------------------------------------------------
INSERT INTO OCAPSYS.TBLWIREBOND
    (TBLROWID, LASTUPDATE, LASTUPDATEDBY, "PACKAGE", PRODUCT, LEADFRAME12NC, RECIPE)
VALUES
    (RAWTOHEX(SYS_GUID()), SYSDATE, 'NX487878', 'SOT669',
     'BUK9K6-40E', '934123456789', 'WB_SOT669_STD');

COMMIT;


-- ---------------------------------------------------------------------
-- 2. Several at once
-- ---------------------------------------------------------------------
INSERT ALL
    INTO OCAPSYS.TBLWIREBOND
        (TBLROWID, LASTUPDATE, LASTUPDATEDBY, "PACKAGE", PRODUCT, LEADFRAME12NC, RECIPE)
    VALUES
        (RAWTOHEX(SYS_GUID()), SYSDATE, 'NX487878', 'SOT1210',
         'PSMN012-30YLD', '934198765432', 'WB_SOT1210_STD')
    INTO OCAPSYS.TBLWIREBOND
        (TBLROWID, LASTUPDATE, LASTUPDATEDBY, "PACKAGE", PRODUCT, LEADFRAME12NC, RECIPE)
    VALUES
        (RAWTOHEX(SYS_GUID()), SYSDATE, 'NX487878', 'SOT1235',
         'PSMN4R0-40YS', '934111222333', 'WB_SOT1235_STD')
SELECT 1 FROM dual;

COMMIT;


-- ---------------------------------------------------------------------
-- 3. Copy the existing WIREBOND recipes out of AWACSRECIPEBYWSTYPE
--
-- If the wirebond recipes are already on the old recipe grid, this moves
-- them across instead of keying them in again. Skips anything already in
-- TBLWIREBOND, so it is safe to re-run.
-- ---------------------------------------------------------------------
-- INSERT INTO OCAPSYS.TBLWIREBOND
--     (TBLROWID, LASTUPDATE, LASTUPDATEDBY, "PACKAGE", PRODUCT, LEADFRAME12NC, RECIPE)
-- SELECT RAWTOHEX(SYS_GUID()), NVL(a.lastupdate, SYSDATE), a.lastupdatedby,
--        a."PACKAGE", a.product, a.leadframe12nc, a.recipe
-- FROM   OCAPSYS.AWACSRECIPEBYWSTYPE a
-- WHERE  a.wstype = 'WIREBOND'
--   AND  NOT EXISTS (SELECT 1
--                    FROM   OCAPSYS.TBLWIREBOND w
--                    WHERE  UPPER(w.product)       = UPPER(a.product)
--                      AND  UPPER(w.leadframe12nc) = UPPER(a.leadframe12nc)
--                      AND  UPPER(w.recipe)        = UPPER(a.recipe));
-- COMMIT;


-- ---------------------------------------------------------------------
-- 4. What the page will show
-- ---------------------------------------------------------------------
SELECT ROWIDTOCHAR(ROWID) AS row_handle,
       "PACKAGE",
       product,
       leadframe12nc,
       recipe,
       lastupdatedby,
       TO_CHAR(lastupdate, 'YYYY-MM-DD HH24:MI:SS') AS lastupdate
FROM   OCAPSYS.TBLWIREBOND
ORDER  BY lastupdate DESC NULLS LAST;
