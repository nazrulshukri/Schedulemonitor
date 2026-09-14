-- =====================================================================
-- TBLWIREBOND - check the migration landed.
--
-- Run AFTER tblwirebond-migrate.sql. Press F5 (Execute as Script in
-- Toad, Run Script in SQL Developer) - these are plain queries, there is
-- no PL/SQL in this file.
-- =====================================================================


-- 1. The shape.
--    Expect exactly seven rows: TBLROWID, LASTUPDATE, LASTUPDATEDBY,
--    PACKAGE, PRODUCT, LEADFRAME12NC, RECIPE.
--    Still seeing WBOCAPNO or WBDATE? The migration did not finish -
--    re-run it with DBMS Output on and read the skipped lines.
SELECT column_id, column_name, data_type, data_length
FROM   all_tab_columns
WHERE  table_name = 'TBLWIREBOND'
ORDER  BY column_id;


-- 2. The undo tables.
--    BOTH rows must mention TBLWIREBOND. If they do not, Edit and Delete
--    fail with ORA-02290 and roll back, while Add works fine - which is
--    what makes it look like a bug in the page.
SELECT constraint_name, search_condition
FROM   all_constraints
WHERE  constraint_name IN ('CK_TBLRECIPEHISTORY_TABLE', 'CK_TBLRECIPETRASH_TABLE');


-- 3. Leftover OCAP rows.
--    The old rows have no product, leadframe or recipe now, so they show
--    as blank rows in the grid. This counts them.
SELECT COUNT(1) AS blank_rows
FROM   TBLWIREBOND
WHERE  product IS NULL AND leadframe12nc IS NULL AND recipe IS NULL;


-- 4. What the page will show.
SELECT ROWIDTOCHAR(ROWID) AS row_handle,
       "PACKAGE",
       product,
       leadframe12nc,
       recipe,
       lastupdatedby,
       TO_CHAR(lastupdate, 'YYYY-MM-DD HH24:MI') AS lastupdate
FROM   TBLWIREBOND
ORDER  BY lastupdate DESC NULLS LAST
FETCH FIRST 25 ROWS ONLY;


-- ---------------------------------------------------------------------
-- Clean-up, when you are happy. Uncomment the lines you want and run
-- them one at a time.
-- ---------------------------------------------------------------------

-- Clear the blank OCAP rows out (TBLWIREBOND_BAK still has them):
-- DELETE FROM TBLWIREBOND
--  WHERE product IS NULL AND leadframe12nc IS NULL AND recipe IS NULL;
-- COMMIT;

-- Drop the backup once the page is working:
-- DROP TABLE TBLWIREBOND_BAK;
