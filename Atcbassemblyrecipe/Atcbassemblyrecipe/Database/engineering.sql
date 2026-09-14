-- =====================================================================
-- OCAPSYS.ENGINEERING - the engineering lot recipe table.
-- RUN THIS ONCE against the OCAPSYS schema.
--
-- One row = one engineering lot and every recipe that lot needs, one
-- column per process step. The shape is the SQL Server table
-- awacs.dbo.ENGINEERING, moved into Oracle so the recipe web app and
-- AwacsMesService can read it over the connection they already have
-- instead of each carrying a second driver and a second connection
-- string.
--
-- WHO READS IT:
--   * The Engineering page of the ATCB assembly recipe app (add, edit,
--     delete, export). Which recipe columns a user sees is decided by
--     their TBLACCESS module grants - see engineeringcolumngroup.sql.
--   * AwacsMesService.DBorderUpdate, for a WOID that starts with ENG.
--     An engineering lot is not in MES, so RMS/MES lookup returns
--     nothing for it; the service reads this table by LOTNUMBER instead
--     and answers with the recipe column that matches the workstation's
--     WSTYPE.
--
-- TWO RESERVED-WORD COLUMNS. Both must be written double-quoted and
-- UPPERCASE - "NO" and "PACKAGE" - everywhere they appear, here and in
-- every query. Unquoted or lowercase raises ORA-00904. AWACSRECIPEBYWSTYPE
-- and TBLWIREBOND already do this for "PACKAGE".
--
-- THE DROP BELOW IS DESTRUCTIVE. If the table already exists, keep a
-- copy first:
--   CREATE TABLE engineering_bak AS SELECT * FROM engineering;
--
-- Run section 4 as well, or Edit and Delete on the page will fail.
-- =====================================================================


-- ---------------------------------------------------------------------
-- 1. The table
-- ---------------------------------------------------------------------
DROP TABLE OCAPSYS.ENGINEERING CASCADE CONSTRAINTS;

CREATE TABLE OCAPSYS.ENGINEERING
(
  TBLROWID          VARCHAR2(50 BYTE),   -- row key: RAWTOHEX(SYS_GUID()), same as AWACSWSTYPE
  LASTUPDATE        DATE,                -- SYSDATE on every write
  LASTUPDATEDBY     VARCHAR2(50 BYTE),   -- sAMAccountName, e.g. NX487878

  "NO"              NUMBER,              -- the engineering request number
  REQUESTOR         VARCHAR2(64 BYTE),   -- who asked for the lot
  LOTNUMBER         VARCHAR2(64 BYTE),   -- ENG... - matched against the MES WOID
  "PACKAGE"         VARCHAR2(64 BYTE),
  PRODUCT           VARCHAR2(64 BYTE),

  -- One column per process step. All optional: an engineering lot only
  -- fills in the steps it actually runs, which is why the grid is mostly
  -- empty cells.
  RECIPES1          VARCHAR2(120 BYTE),
  RECIPES2          VARCHAR2(120 BYTE),
  RECIPEAX          VARCHAR2(120 BYTE),
  RECIPEDA          VARCHAR2(120 BYTE),
  RECIPECA          VARCHAR2(120 BYTE),
  RECIPEAOI         VARCHAR2(120 BYTE),
  RECIPEMOLD        VARCHAR2(120 BYTE),
  RECIPERM          VARCHAR2(120 BYTE),
  RECIPETF          VARCHAR2(120 BYTE),
  RECIPEMD          VARCHAR2(120 BYTE),
  RECIPEWPROBER     VARCHAR2(120 BYTE),
  RECIPEFINALTEST   VARCHAR2(120 BYTE),
  RECIPEWAFERTEST   VARCHAR2(120 BYTE),
  RECIPEMCDWB       VARCHAR2(120 BYTE),
  RECIPEWAOI        VARCHAR2(120 BYTE),
  RECIPE2DMARKER    VARCHAR2(120 BYTE),
  RECIPEL200        VARCHAR2(120 BYTE),
  RECIPEMOULD       VARCHAR2(120 BYTE),
  RECIPESTRIPTEST   VARCHAR2(120 BYTE),
  RECIPEBACKGRIND   VARCHAR2(120 BYTE),
  RECIPEWLTR        VARCHAR2(120 BYTE),
  RECIPEPHICOM      VARCHAR2(120 BYTE),
  ADAT              VARCHAR2(120 BYTE)
);

-- No TABLESPACE clause on purpose: naming one that does not exist fails
-- the CREATE with ORA-00959 after the DROP has already succeeded, which
-- leaves you with no table at all. Without it Oracle uses the schema
-- default. Add it back only if your DBA requires a specific tablespace.


-- ---------------------------------------------------------------------
-- 2. Indexes
--
-- LOTNUMBER is the lookup key on the MES side - every DBorderUpdate for
-- an ENG workorder hits it - so index it. Make it UNIQUE only after
-- confirming the existing rows have no duplicate lot numbers; the app
-- checks for a duplicate before every insert, but nothing in the
-- database stops one today.
-- ---------------------------------------------------------------------
CREATE INDEX ix_engineering_lotnumber ON OCAPSYS.ENGINEERING (LOTNUMBER);

-- CREATE UNIQUE INDEX ix_engineering_lotnumber_u ON OCAPSYS.ENGINEERING (LOTNUMBER);


-- ---------------------------------------------------------------------
-- 3. Optional: carry the existing SQL Server rows over
--
-- If awacs.dbo.ENGINEERING already holds the live data, export it to CSV
-- (SSMS: right-click the database > Tasks > Export Data) and upload it
-- on the Engineering page with Import CSV. The page reads the same
-- column names as the headers, so an export of the SELECT you already
-- run loads without editing:
--
--   SELECT [NO],[Requestor],[LOTNUMBER],[PACKAGE],[PRODUCT],[RECIPES1],
--          ... ,[ADAT]
--   FROM   [awacs].[dbo].[ENGINEERING]
-- ---------------------------------------------------------------------


-- ---------------------------------------------------------------------
-- 4. REQUIRED: let the undo tables accept ENGINEERING rows
--
-- Edit copies the old row into TBLRECIPEHISTORY and Delete copies the
-- whole row into TBLRECIPETRASH, both inside the same transaction as the
-- write. Both tables carry a CHECK constraint listing the table names
-- they accept. Without ENGINEERING in that list every Edit and every
-- Delete on the page fails with
--   ORA-02290: check constraint (OCAPSYS.CK_TBLRECIPEHISTORY_TABLE) violated
-- and rolls back - nothing is lost, but nothing saves either.
--
-- Run these once. Database/tblrecipeaudit.sql carries the same list for
-- a fresh install.
-- ---------------------------------------------------------------------
ALTER TABLE OCAPSYS.TBLRECIPEHISTORY DROP CONSTRAINT ck_tblrecipehistory_table;

ALTER TABLE OCAPSYS.TBLRECIPEHISTORY ADD CONSTRAINT ck_tblrecipehistory_table
    CHECK (table_name IN ('AWACSWSTYPE', 'AWACSRECIPEBYWSTYPE', 'AWACSLF', 'TBLWIREBOND', 'ENGINEERING'));

ALTER TABLE OCAPSYS.TBLRECIPETRASH DROP CONSTRAINT ck_tblrecipetrash_table;

ALTER TABLE OCAPSYS.TBLRECIPETRASH ADD CONSTRAINT ck_tblrecipetrash_table
    CHECK (table_name IN ('AWACSWSTYPE', 'AWACSRECIPEBYWSTYPE', 'AWACSLF', 'TBLWIREBOND', 'ENGINEERING'));


-- ---------------------------------------------------------------------
-- 5. AwacsMesService reads this table as a different Oracle user
--
-- The web app connects as OCAPSYS. The MES service connects with the
-- OCAP connection string in its Web.config. If that is a different user,
-- it needs SELECT and a synonym, the same as it has for AWACSWSTYPE:
-- ---------------------------------------------------------------------
-- GRANT SELECT ON OCAPSYS.ENGINEERING TO <mes_user>;
-- CREATE OR REPLACE SYNONYM <mes_user>.ENGINEERING FOR OCAPSYS.ENGINEERING;


-- ---------------------------------------------------------------------
-- 6. Quick check that it all worked
-- ---------------------------------------------------------------------
SELECT column_name, data_type, data_length
FROM   all_tab_columns
WHERE  table_name = 'ENGINEERING'
ORDER  BY column_id;

SELECT constraint_name, search_condition
FROM   all_constraints
WHERE  constraint_name IN ('CK_TBLRECIPEHISTORY_TABLE', 'CK_TBLRECIPETRASH_TABLE');
