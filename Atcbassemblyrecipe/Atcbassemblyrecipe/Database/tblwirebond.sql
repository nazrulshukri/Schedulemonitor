-- =====================================================================
-- OCAPSYS.TBLWIREBOND - the wirebond recipe table.
-- RUN THIS ONCE against the OCAPSYS schema.
--
-- One row = one wirebond recipe: package, product, leadframe 12NC and
-- the recipe name. Same shape as AWACSRECIPEBYWSTYPE minus WSTYPE -
-- every row in this table is wirebond, so there is nothing to filter on.
--
-- PREFER Database/tblwirebond-migrate.sql IF THE TABLE ALREADY EXISTS.
-- That one converts the table in place with ALTER: it keeps the grants,
-- the synonyms and the privileges, and it cannot fail because your
-- tablespace is named something else. Use THIS script only for a table
-- that does not exist yet, or one you are happy to lose.
--
-- THE DROP BELOW IS DESTRUCTIVE and this script REPLACES the old OCAP
-- shape of the table (WBOCAPNO, WBOCAPWWK, WBDATE and the rest). Keep a
-- copy of whatever is in there first:
--   CREATE TABLE tblwirebond_bak AS SELECT * FROM tblwirebond;
--
-- Run section 4 as well, or Edit and Delete on the page will fail.
-- =====================================================================


-- ---------------------------------------------------------------------
-- 1. The table
--
-- PACKAGE is a RESERVED WORD in Oracle. It must be double-quoted and
-- uppercase - "PACKAGE" - everywhere it appears, here and in every
-- query. Unquoted or lowercase raises ORA-00904. This is the same thing
-- AWACSRECIPEBYWSTYPE does.
-- ---------------------------------------------------------------------
DROP TABLE OCAPSYS.TBLWIREBOND CASCADE CONSTRAINTS;

CREATE TABLE OCAPSYS.TBLWIREBOND
(
  TBLROWID       VARCHAR2(50 BYTE),   -- row key: RAWTOHEX(SYS_GUID()), same as AWACSWSTYPE
  LASTUPDATE     DATE,                -- SYSDATE on every write
  LASTUPDATEDBY  VARCHAR2(50 BYTE),   -- sAMAccountName, e.g. NX487878
  "PACKAGE"      VARCHAR2(64 BYTE),   -- optional
  PRODUCT        VARCHAR2(64 BYTE),
  LEADFRAME12NC  VARCHAR2(16 BYTE),
  RECIPE         VARCHAR2(120 BYTE)
);

-- The storage clause the old table carried. Left out above on purpose:
-- TABLESPACE OCAPSYS_DAT fails with ORA-00959 on any database where that
-- tablespace is named something else, and it takes the CREATE TABLE down
-- with it - which leaves you with no table at all and a DROP that already
-- succeeded. Without it Oracle uses the schema's default tablespace,
-- which is what you want in almost every case. Add it back if your DBA
-- says the table must live in a specific tablespace:
--
-- TABLESPACE OCAPSYS_DAT
-- PCTFREE    10
-- INITRANS   1
-- MAXTRANS   255
-- STORAGE    (INITIAL 64K NEXT 1M MINEXTENTS 1 MAXEXTENTS UNLIMITED
--             PCTINCREASE 0 BUFFER_POOL DEFAULT)
-- LOGGING NOCOMPRESS NOCACHE


-- ---------------------------------------------------------------------
-- 2. The old work-week trigger is gone with the old columns
--
-- OCAP_WIREBOND_WORKWEEK filled WBOCAPWWK from WBDATE. Neither column
-- exists any more, so the trigger cannot compile and would fail every
-- insert with ORA-04098. The DROP TABLE above already removed it; this
-- is here so nobody puts it back.
-- ---------------------------------------------------------------------
-- DROP TRIGGER OCAPSYS.OCAP_WIREBOND_WORKWEEK;


-- ---------------------------------------------------------------------
-- 3. Indexes. Optional, recommended.
--
-- The app checks for a duplicate product/leadframe/recipe before every
-- insert, but nothing in the database stops one - add the unique index
-- only after confirming the existing rows are clean.
-- ---------------------------------------------------------------------
-- CREATE INDEX ix_tblwirebond_product ON OCAPSYS.TBLWIREBOND (PRODUCT);
-- CREATE INDEX ix_tblwirebond_lf      ON OCAPSYS.TBLWIREBOND (LEADFRAME12NC);
-- CREATE UNIQUE INDEX ix_tblwirebond_recipe
--   ON OCAPSYS.TBLWIREBOND (PRODUCT, LEADFRAME12NC, RECIPE);


-- ---------------------------------------------------------------------
-- 4. REQUIRED: let the undo tables accept TBLWIREBOND rows
--
-- Edit copies the old row into TBLRECIPEHISTORY and Delete copies the
-- whole row into TBLRECIPETRASH, both inside the same transaction as the
-- write itself. Both tables carry a CHECK constraint listing which table
-- names are allowed, and TBLWIREBOND was never in it - so every Edit and
-- every Delete on this page fails with
--   ORA-02290: check constraint (OCAPSYS.CK_TBLRECIPEHISTORY_TABLE) violated
-- and rolls back. Nothing is lost, but nothing saves either.
--
-- These two statements replace the constraint with one that includes it.
-- Run them once. Database/tblrecipeaudit.sql carries the same list for a
-- fresh install.
-- ---------------------------------------------------------------------
ALTER TABLE OCAPSYS.TBLRECIPEHISTORY DROP CONSTRAINT ck_tblrecipehistory_table;

ALTER TABLE OCAPSYS.TBLRECIPEHISTORY ADD CONSTRAINT ck_tblrecipehistory_table
    CHECK (table_name IN ('AWACSWSTYPE', 'AWACSRECIPEBYWSTYPE', 'AWACSLF', 'TBLWIREBOND'));

ALTER TABLE OCAPSYS.TBLRECIPETRASH DROP CONSTRAINT ck_tblrecipetrash_table;

ALTER TABLE OCAPSYS.TBLRECIPETRASH ADD CONSTRAINT ck_tblrecipetrash_table
    CHECK (table_name IN ('AWACSWSTYPE', 'AWACSRECIPEBYWSTYPE', 'AWACSLF', 'TBLWIREBOND'));


-- ---------------------------------------------------------------------
-- 5. Quick check that it all worked
-- ---------------------------------------------------------------------
SELECT column_name, data_type, data_length
FROM   all_tab_columns
WHERE  table_name = 'TBLWIREBOND'
ORDER  BY column_id;

SELECT constraint_name, search_condition
FROM   all_constraints
WHERE  constraint_name IN ('CK_TBLRECIPEHISTORY_TABLE', 'CK_TBLRECIPETRASH_TABLE');
