-- =====================================================================
-- TBLWIREBOND - "I saved a row on the page and nothing happened"
--
-- Run the blocks in order as the OCAPSYS user. Each block is one
-- statement: in SQL Developer use Run Statement (Ctrl+Enter) on the one
-- you want, or Run Script (F5) for the whole file.
-- =====================================================================


-- ---------------------------------------------------------------------
-- 1. Is the table the new shape?
--
-- Expect exactly: TBLROWID, LASTUPDATE, LASTUPDATEDBY, PACKAGE, PRODUCT,
-- LEADFRAME12NC, RECIPE. If you still see WBOCAPNO / WBDATE, the app is
-- reading columns that are not there and every query fails with
-- ORA-00904: "RECIPE": invalid identifier - which is exactly the error the
-- page shows. Fix it with Database/tblwirebond-migrate.sql.
-- ---------------------------------------------------------------------
SELECT column_id, column_name, data_type, data_length
FROM   all_tab_columns
WHERE  table_name = 'TBLWIREBOND'
ORDER  BY column_id;


-- ---------------------------------------------------------------------
-- 2. Is the row actually there? Newest 20, same order as the page.
-- ---------------------------------------------------------------------
SELECT TO_CHAR(lastupdate, 'YYYY-MM-DD HH24:MI:SS') AS lastupdate,
       lastupdatedby,
       "PACKAGE",
       product,
       leadframe12nc,
       recipe
FROM   OCAPSYS.TBLWIREBOND
ORDER  BY lastupdate DESC NULLS LAST
FETCH FIRST 20 ROWS ONLY;


-- ---------------------------------------------------------------------
-- 3. THE ONE THAT BREAKS EDIT AND DELETE
--
-- Edit copies the old row into TBLRECIPEHISTORY, Delete copies the whole
-- row into TBLRECIPETRASH, both in the same transaction as the write.
-- Both tables CHECK the table name against a fixed list. If TBLWIREBOND
-- is not in that list, every Edit and every Delete on the page fails with
--   ORA-02290: check constraint (...) violated
-- and rolls back - Add still works, which is what makes it confusing.
--
-- Both rows below must mention TBLWIREBOND. If they do not, run section 4
-- of Database/tblwirebond.sql.
-- ---------------------------------------------------------------------
SELECT constraint_name, search_condition
FROM   all_constraints
WHERE  constraint_name IN ('CK_TBLRECIPEHISTORY_TABLE', 'CK_TBLRECIPETRASH_TABLE');


-- ---------------------------------------------------------------------
-- 4. Can the signed-in user see the Add / Edit / Delete buttons?
--
-- Super Admins always can. Everyone else needs a row here with
-- MODULE_NAME 'Wirebond':
--   READ  = view only    WRITE = view + add + update
--   ADMIN = view + add + update + delete
-- Replace NX487878 with the login id that is having the problem.
-- ---------------------------------------------------------------------
SELECT user_id, user_name, role_name, module_name, access_level, status
FROM   OCAPSYS.TBLACCESS
WHERE  user_id = 'NX487878'
ORDER  BY module_name;


-- ---------------------------------------------------------------------
-- 5. End-to-end test, then thrown away. If this raises an error, the
--    page would have raised the same one. Run one statement at a time.
-- ---------------------------------------------------------------------
INSERT INTO OCAPSYS.TBLWIREBOND
    (TBLROWID, LASTUPDATE, LASTUPDATEDBY, "PACKAGE", PRODUCT, LEADFRAME12NC, RECIPE)
VALUES
    (RAWTOHEX(SYS_GUID()), SYSDATE, 'TESTUSER', 'SOT669',
     'SELFTEST-PRODUCT', '934000000000', 'WB_SELFTEST');

SELECT "PACKAGE", product, leadframe12nc, recipe
FROM   OCAPSYS.TBLWIREBOND
WHERE  product = 'SELFTEST-PRODUCT';

-- Put the table back the way it was:
ROLLBACK;
