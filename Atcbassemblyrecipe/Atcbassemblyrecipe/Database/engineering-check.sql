-- =====================================================================
-- DIAGNOSTIC. Run this when the Engineering page, or AwacsMesService,
-- answers with ORA-00904: "<something>": invalid identifier.
--
-- ORA-00904 on this feature always means ONE thing: something asked
-- ENGINEERING for a column it does not have. So the table and the
-- mapping disagree. This script shows which side is wrong.
--
-- Nothing here changes anything. Read only.
--
-- >>> RUN IT CONNECTED AS THE USER THE APP CONNECTS AS. <<<
-- That is the User ID in appsettings.json, ConnectionStrings:OCAP -
-- OCAPSYS in a stock install. A table your own SQL Developer session
-- can see is not necessarily one the app can: a different user, a
-- missing synonym, a missing SELECT grant, or an uncommitted CREATE all
-- look exactly like "the table is not there" from the app and exactly
-- like "the table is fine" from your session. Query 1 and query 8 are
-- the ones that settle it.
--
-- WHAT ENGINEERING SHOULD LOOK LIKE - eleven columns, no more:
--
--   TBLROWID  LASTUPDATE  LASTUPDATEDBY          bookkeeping
--   "NO"  REQUESTOR  LOTNUMBER  "PACKAGE"  PRODUCT    identity
--   SAWING  WIREBOND  MARKER        recipes
--
-- If query 2 shows RECIPES1, RECIPEDA, RECIPEFINALTEST or any other
-- per-step column, the table is still the OLD 23-column shape. Back it
-- up and run engineering.sql again:
--
--   CREATE TABLE engineering_bak AS SELECT * FROM engineering;
--
-- then engineering.sql, whose section 3 carries the old rows across.
-- =====================================================================


-- ---------------------------------------------------------------------
-- 1. Which ENGINEERING is the app actually reading?
--
-- More than one row here is the trap: the app resolves the name
-- unqualified, so it reads whichever one its schema or a synonym points
-- at, which may not be the one you edited.
-- ---------------------------------------------------------------------
SELECT owner, table_name, 'TABLE' AS kind
FROM   all_tables
WHERE  table_name IN ('ENGINEERING', 'ENGINEERINGCOLUMNGROUP', 'ENGINEERINGWSTYPE')
UNION ALL
SELECT owner, synonym_name, 'SYNONYM -> ' || table_owner || '.' || table_name
FROM   all_synonyms
WHERE  synonym_name IN ('ENGINEERING', 'ENGINEERINGCOLUMNGROUP', 'ENGINEERINGWSTYPE')
ORDER  BY 2, 1;

-- And who you are connected as, which is what an unqualified name
-- resolves against first:
SELECT SYS_CONTEXT('USERENV', 'SESSION_USER')   AS session_user,
       SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') AS current_schema
FROM   dual;


-- ---------------------------------------------------------------------
-- 2. What columns does ENGINEERING really have?
--
-- THIS IS THE ANSWER TO THE ORA-00904. If the column named in the error
-- is not in this list, the table is the wrong shape.
-- ---------------------------------------------------------------------
SELECT owner, column_id, column_name, data_type, data_length
FROM   all_tab_columns
WHERE  table_name = 'ENGINEERING'
ORDER  BY owner, column_id;


-- ---------------------------------------------------------------------
-- 3. Columns the mapping names that the table does NOT have.
--
-- Every row this returns is a column the page would ask for and Oracle
-- would refuse. Either rebuild the table (engineering.sql) or correct
-- the mapping (engineeringcolumngroup.sql).
-- ---------------------------------------------------------------------
SELECT g.column_name AS mapped_but_missing, g.group_name
FROM   OCAPSYS.ENGINEERINGCOLUMNGROUP g
WHERE  NOT EXISTS (SELECT 1
                   FROM   all_tab_columns c
                   WHERE  c.table_name  = 'ENGINEERING'
                     AND  c.column_name = g.column_name)
ORDER  BY 1;


-- ---------------------------------------------------------------------
-- 4. The same check for the MES side.
-- ---------------------------------------------------------------------
SELECT w.wstype, w.column_name AS mapped_but_missing
FROM   OCAPSYS.ENGINEERINGWSTYPE w
WHERE  NOT EXISTS (SELECT 1
                   FROM   all_tab_columns c
                   WHERE  c.table_name  = 'ENGINEERING'
                     AND  c.column_name = w.column_name)
ORDER  BY 1;


-- ---------------------------------------------------------------------
-- 5. Table columns nobody has grouped.
--
-- These exist but are invisible on the page - harmless, but if
-- SAWING turns up here it means engineeringcolumngroup.sql has not
-- been run.
-- ---------------------------------------------------------------------
SELECT c.column_name AS present_but_ungrouped
FROM   all_tab_columns c
WHERE  c.table_name = 'ENGINEERING'
  AND  c.column_name NOT IN ('TBLROWID', 'LASTUPDATE', 'LASTUPDATEDBY')
  AND  NOT EXISTS (SELECT 1
                   FROM   OCAPSYS.ENGINEERINGCOLUMNGROUP g
                   WHERE  g.column_name = c.column_name)
ORDER  BY c.column_id;


-- ---------------------------------------------------------------------
-- 6. Do the three config tables exist and carry rows at all?
-- Expect 8 rows in ENGINEERINGCOLUMNGROUP and 12 in ENGINEERINGWSTYPE.
-- ---------------------------------------------------------------------
SELECT 'ENGINEERINGCOLUMNGROUP' AS table_name, COUNT(*) AS rows_found FROM OCAPSYS.ENGINEERINGCOLUMNGROUP
UNION ALL
SELECT 'ENGINEERINGWSTYPE', COUNT(*) FROM OCAPSYS.ENGINEERINGWSTYPE
UNION ALL
SELECT 'ENGINEERING', COUNT(*) FROM OCAPSYS.ENGINEERING;


-- ---------------------------------------------------------------------
-- 8. Can THIS session actually read the three tables?
--
-- The decisive test, and the one that matches what the app does: an
-- unqualified SELECT, resolved the same way the app resolves it. Run it
-- as the app's user. ORA-00942 here and rows in query 1 means the table
-- exists but this user cannot reach it - grant it:
--
--   GRANT SELECT ON <owner>.ENGINEERING            TO <app_user>;
--   GRANT SELECT ON <owner>.ENGINEERINGCOLUMNGROUP TO <app_user>;
--   GRANT SELECT ON <owner>.ENGINEERINGWSTYPE      TO <app_user>;
--   CREATE OR REPLACE SYNONYM <app_user>.ENGINEERING            FOR <owner>.ENGINEERING;
--   CREATE OR REPLACE SYNONYM <app_user>.ENGINEERINGCOLUMNGROUP FOR <owner>.ENGINEERINGCOLUMNGROUP;
--   CREATE OR REPLACE SYNONYM <app_user>.ENGINEERINGWSTYPE      FOR <owner>.ENGINEERINGWSTYPE;
--
-- The app needs INSERT, UPDATE and DELETE on ENGINEERING as well - it is
-- an editable page, not a report.
-- ---------------------------------------------------------------------
SELECT 'ENGINEERING'            AS table_name, COUNT(*) AS readable_rows FROM engineering
UNION ALL
SELECT 'ENGINEERINGCOLUMNGROUP', COUNT(*) FROM engineeringcolumngroup
UNION ALL
SELECT 'ENGINEERINGWSTYPE',      COUNT(*) FROM engineeringwstype;


-- ---------------------------------------------------------------------
-- 7. The undo tables must accept the name ENGINEERING, or every Edit and
-- Delete on the page rolls back with ORA-02290. Both rows must contain
-- 'ENGINEERING'.
-- ---------------------------------------------------------------------
SELECT constraint_name, search_condition
FROM   all_constraints
WHERE  constraint_name IN ('CK_TBLRECIPEHISTORY_TABLE', 'CK_TBLRECIPETRASH_TABLE');
