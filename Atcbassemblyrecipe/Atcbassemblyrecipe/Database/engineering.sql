-- =====================================================================
-- OCAPSYS.ENGINEERING - the engineering lot recipe table.
-- RUN THIS ONCE against the OCAPSYS schema.
--
-- One row = one engineering lot. Five columns identify the lot, and
-- THREE columns hold its recipes - one per group:
--
--     RECIPESAWING     the sawing group's recipe
--     RECIPEWIREBOND   the wirebond group's recipe
--     RECIPEMARKER     the marker group's recipe
--
-- That is the whole table. The 23-column version this replaced carried
-- one column per process step (RECIPEFINALTEST, RECIPEWPROBER and the
-- rest); those are gone. If a query still names one of them it fails
-- with ORA-00904: invalid identifier - that is this change, not a
-- broken install. Section 3 below carries the rows over from the old
-- shape if you already created it.
--
-- WHO READS IT:
--   * The Engineering page of the ATCB assembly recipe app. A user sees
--     the five identity columns plus their own group's recipe column;
--     the other two groups' columns are hidden - see
--     engineeringcolumngroup.sql.
--   * AwacsMesService.DBorderUpdate, for a WOID that starts with ENG.
--     An engineering lot is not in MES, so RMS/MES lookup returns
--     nothing for it; the service reads this table by LOTNUMBER and
--     answers with the column its workstation's WSTYPE maps to - see
--     engineeringwstype.sql.
--
-- TWO RESERVED-WORD COLUMNS. Both must be written double-quoted and
-- UPPERCASE - "NO" and "PACKAGE" - everywhere they appear, here and in
-- every query. Unquoted or lowercase raises ORA-00904.
--
-- THE DROP BELOW IS DESTRUCTIVE. If the table already exists, keep a
-- copy first:
--   CREATE TABLE engineering_bak AS SELECT * FROM engineering;
--
-- Run section 5 as well, or Edit and Delete on the page will fail.
-- =====================================================================


-- ---------------------------------------------------------------------
-- 1. The table
-- ---------------------------------------------------------------------
DROP TABLE OCAPSYS.ENGINEERING CASCADE CONSTRAINTS;

CREATE TABLE OCAPSYS.ENGINEERING
(
  TBLROWID        VARCHAR2(50 BYTE),   -- row key: RAWTOHEX(SYS_GUID()), same as AWACSWSTYPE
  LASTUPDATE      DATE,                -- SYSDATE on every write
  LASTUPDATEDBY   VARCHAR2(50 BYTE),   -- sAMAccountName, e.g. NX487878

  "NO"            NUMBER,              -- the engineering request number
  REQUESTOR       VARCHAR2(64 BYTE),   -- who asked for the lot
  LOTNUMBER       VARCHAR2(64 BYTE),   -- ENG... - matched against the MES WOID
  "PACKAGE"       VARCHAR2(64 BYTE),
  PRODUCT         VARCHAR2(64 BYTE),

  -- One recipe per group. All three optional: a lot only fills in the
  -- steps it actually runs.
  RECIPESAWING    VARCHAR2(120 BYTE),
  RECIPEWIREBOND  VARCHAR2(120 BYTE),
  RECIPEMARKER    VARCHAR2(120 BYTE)
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
-- 3. Carrying old rows over
--
-- (a) FROM THE 23-COLUMN VERSION, if you already created it. Back it up
--     first (the DROP above has already removed it otherwise):
--
--     CREATE TABLE engineering_bak AS SELECT * FROM engineering;   -- BEFORE running this script
--
--     then, after the CREATE TABLE above:
--
--     INSERT INTO OCAPSYS.ENGINEERING
--         (TBLROWID, LASTUPDATE, LASTUPDATEDBY, "NO", REQUESTOR, LOTNUMBER,
--          "PACKAGE", PRODUCT, RECIPESAWING, RECIPEWIREBOND, RECIPEMARKER)
--     SELECT TBLROWID, LASTUPDATE, LASTUPDATEDBY, "NO", REQUESTOR, LOTNUMBER,
--            "PACKAGE", PRODUCT,
--            COALESCE(RECIPES1, RECIPES2),        -- sawing
--            COALESCE(RECIPEDA, RECIPECA),        -- wirebond: die attach, else clip attach
--            COALESCE(RECIPEMD, RECIPE2DMARKER)   -- marker
--     FROM   engineering_bak;
--     COMMIT;
--
-- (b) FROM SQL SERVER awacs.dbo.ENGINEERING. Export the five identity
--     columns plus whichever three recipe columns your groups actually
--     use to CSV (SSMS: right-click the database > Tasks > Export Data),
--     rename the three headers to RECIPESAWING / RECIPEWIREBOND /
--     RECIPEMARKER, and upload it on the Engineering page with
--     Import CSV.
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
-- 6. Quick check that it all worked. Eleven columns, no more.
-- ---------------------------------------------------------------------
SELECT column_name, data_type, data_length
FROM   all_tab_columns
WHERE  table_name = 'ENGINEERING'
ORDER  BY column_id;

SELECT constraint_name, search_condition
FROM   all_constraints
WHERE  constraint_name IN ('CK_TBLRECIPEHISTORY_TABLE', 'CK_TBLRECIPETRASH_TABLE');
