-- =====================================================================
-- FIX FOR: ORA-00904: "RECIPE": invalid identifier
--
-- The app is asking TBLWIREBOND for PACKAGE / PRODUCT / LEADFRAME12NC /
-- RECIPE and the table has not been changed yet - it still has the old
-- OCAP columns (WBOCAPNO, WBDATE and the rest). Oracle answers with
-- ORA-00904 on the first column it cannot find, which is RECIPE.
--
-- This script converts the table IN PLACE with ALTER, instead of the
-- DROP + CREATE in tblwirebond.sql. Use this one. It keeps the table's
-- grants, synonyms and privileges, and it cannot fail because a
-- tablespace name is different on your database.
--
-- RUN THE STEPS IN ORDER, ONE AT A TIME.
-- In SQL Developer: click into a statement and press Ctrl+Enter
-- (Run Statement). Do NOT press F5 on the whole file.
--
-- Some steps are expected to fail depending on what your table already
-- looks like - each one says so. A failure that the step tells you to
-- expect is fine: move on to the next step.
-- =====================================================================


-- ---------------------------------------------------------------------
-- STEP 0 - what have you actually got right now?
--
-- Run this first and read it. It decides what the rest of the script
-- does for you.
--
--   * You see WBOCAPNO / WBDATE and NO RECIPE
--       -> the normal case. Run every step below.
--   * You see RECIPE already
--       -> the table is fine. Skip to STEP 5, that is your real problem.
--   * No rows at all
--       -> TBLWIREBOND does not exist, or not in a schema you can see.
--          Run Database/tblwirebond.sql instead of this script.
-- ---------------------------------------------------------------------
SELECT column_id, column_name, data_type, data_length
FROM   all_tab_columns
WHERE  table_name = 'TBLWIREBOND'
ORDER  BY column_id;


-- ---------------------------------------------------------------------
-- STEP 1 - back up what is in there
--
-- Cheap insurance. Drops out of the way after you are happy.
-- ---------------------------------------------------------------------
CREATE TABLE OCAPSYS.TBLWIREBOND_BAK AS SELECT * FROM OCAPSYS.TBLWIREBOND;


-- ---------------------------------------------------------------------
-- STEP 2 - drop the work-week trigger FIRST
--
-- OCAP_WIREBOND_WORKWEEK reads WBDATE and writes WBOCAPWWK. Drop the
-- columns while it still exists and the trigger goes INVALID, and then
-- every insert fails with ORA-04098. So it goes first.
--
-- EXPECTED TO FAIL with ORA-04080 "trigger does not exist" if you have
-- already removed it. That is fine - carry on.
-- ---------------------------------------------------------------------
DROP TRIGGER OCAPSYS.OCAP_WIREBOND_WORKWEEK;


-- ---------------------------------------------------------------------
-- STEP 3 - add the four columns the page needs
--
-- PACKAGE is a RESERVED WORD in Oracle. The double quotes and the
-- uppercase are both required, here and in every query that names it.
-- Write it package or Package and you get ORA-00904 all over again.
--
-- EXPECTED TO FAIL with ORA-01430 "column being added already exists"
-- if you ran this once already. Fine - carry on.
-- ---------------------------------------------------------------------
ALTER TABLE OCAPSYS.TBLWIREBOND ADD
(
  "PACKAGE"      VARCHAR2(64 BYTE),
  PRODUCT        VARCHAR2(64 BYTE),
  LEADFRAME12NC  VARCHAR2(16 BYTE),
  RECIPE         VARCHAR2(120 BYTE)
);


-- ---------------------------------------------------------------------
-- STEP 3b - the three bookkeeping columns, only if they are missing
--
-- STEP 0 will have shown you. The old table had all three, so normally
-- you SKIP this step entirely. Run only the lines you actually need.
-- ---------------------------------------------------------------------
-- ALTER TABLE OCAPSYS.TBLWIREBOND ADD (TBLROWID      VARCHAR2(50 BYTE));
-- ALTER TABLE OCAPSYS.TBLWIREBOND ADD (LASTUPDATE    DATE);
-- ALTER TABLE OCAPSYS.TBLWIREBOND ADD (LASTUPDATEDBY VARCHAR2(50 BYTE));


-- ---------------------------------------------------------------------
-- STEP 4 - carry the old package values across, if you want them
--
-- The only old column that means the same thing in the new shape.
-- Optional: skip it if the old rows are test data you do not want.
-- ---------------------------------------------------------------------
UPDATE OCAPSYS.TBLWIREBOND
   SET "PACKAGE" = WBPACKAGE
 WHERE WBPACKAGE IS NOT NULL
   AND "PACKAGE" IS NULL;

COMMIT;


-- ---------------------------------------------------------------------
-- STEP 5 - let the undo tables accept TBLWIREBOND rows
--
-- THIS IS NOT OPTIONAL, and it is a completely separate problem from
-- ORA-00904 - it will bite you the moment the grid starts working.
--
-- Edit copies the old row into TBLRECIPEHISTORY; Delete copies the whole
-- row into TBLRECIPETRASH. Both happen inside the same transaction as
-- the write. Both tables CHECK the table name against a fixed list, and
-- TBLWIREBOND was never in it, so Edit and Delete both fail with
--   ORA-02290: check constraint (OCAPSYS.CK_TBLRECIPEHISTORY_TABLE) violated
-- and roll back - while Add works fine, which is what makes it
-- confusing. These four statements fix it. Run all four.
-- ---------------------------------------------------------------------
ALTER TABLE OCAPSYS.TBLRECIPEHISTORY DROP CONSTRAINT ck_tblrecipehistory_table;

ALTER TABLE OCAPSYS.TBLRECIPEHISTORY ADD CONSTRAINT ck_tblrecipehistory_table
    CHECK (table_name IN ('AWACSWSTYPE', 'AWACSRECIPEBYWSTYPE', 'AWACSLF', 'TBLWIREBOND'));

ALTER TABLE OCAPSYS.TBLRECIPETRASH DROP CONSTRAINT ck_tblrecipetrash_table;

ALTER TABLE OCAPSYS.TBLRECIPETRASH ADD CONSTRAINT ck_tblrecipetrash_table
    CHECK (table_name IN ('AWACSWSTYPE', 'AWACSRECIPEBYWSTYPE', 'AWACSLF', 'TBLWIREBOND'));


-- =====================================================================
-- STOP HERE AND TEST THE PAGE.
--
-- The four new columns exist now, so the grid will load. The old OCAP
-- columns are still sitting there unused, which harms nothing - the app
-- never names them. Open Operations > Wirebond, add a row, edit it,
-- delete it.
--
-- Only once all three work, come back and run STEP 6 to tidy up.
-- =====================================================================


-- ---------------------------------------------------------------------
-- STEP 6 - drop the old OCAP columns
--
-- Point of no return, which is why it is last. TBLWIREBOND_BAK from
-- STEP 1 still has everything if you change your mind.
--
-- If a column name in here is not in your table, Oracle rejects the
-- whole statement with ORA-00904 naming it - delete that name from the
-- list and run it again.
-- ---------------------------------------------------------------------
ALTER TABLE OCAPSYS.TBLWIREBOND DROP
(
  WBOCAPNO, WBOCAPWWK, WBISSUEDBY, WBBFG, WBDATE, WBOPERATORID,
  WBPROCESS, WBMACHINE, WBPACKAGE, WBSOQTY, WBDEFECT, WBDEFECTCAT,
  WBDEFECTOTHERS, WBDIFF4M1E, WBDIFFAFFECTED, WBDIFFFABSITE, WBDIFFNO,
  WBDIFFNOTAFFECTED, WBDIFFREJECTQTY, WBDIFFREMARKS, WBVERIFIEDBY,
  WBACTIONTAKEN, WBDISPOSITION, WBREMARKS, WBRCMACHINEERROR,
  WBMACHINEERROR
);


-- ---------------------------------------------------------------------
-- STEP 7 - old rows now have no product, leadframe or recipe
--
-- They show as blank rows in the grid. This clears them out. Check what
-- you are about to delete with the SELECT first.
-- ---------------------------------------------------------------------
SELECT COUNT(1) AS blank_rows
FROM   OCAPSYS.TBLWIREBOND
WHERE  product IS NULL AND leadframe12nc IS NULL AND recipe IS NULL;

-- DELETE FROM OCAPSYS.TBLWIREBOND
--  WHERE product IS NULL AND leadframe12nc IS NULL AND recipe IS NULL;
-- COMMIT;


-- ---------------------------------------------------------------------
-- STEP 8 - confirm the final shape
--
-- Expect exactly seven rows, in any order: TBLROWID, LASTUPDATE,
-- LASTUPDATEDBY, PACKAGE, PRODUCT, LEADFRAME12NC, RECIPE.
-- ---------------------------------------------------------------------
SELECT column_id, column_name, data_type, data_length
FROM   all_tab_columns
WHERE  table_name = 'TBLWIREBOND'
ORDER  BY column_id;

-- And that both constraints now mention TBLWIREBOND:
SELECT constraint_name, search_condition
FROM   all_constraints
WHERE  constraint_name IN ('CK_TBLRECIPEHISTORY_TABLE', 'CK_TBLRECIPETRASH_TABLE');


-- ---------------------------------------------------------------------
-- STEP 9 - when you are happy, drop the backup
-- ---------------------------------------------------------------------
-- DROP TABLE OCAPSYS.TBLWIREBOND_BAK;
