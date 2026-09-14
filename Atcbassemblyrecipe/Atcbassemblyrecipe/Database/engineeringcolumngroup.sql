-- =====================================================================
-- OCAPSYS.ENGINEERINGCOLUMNGROUP - which group owns which ENGINEERING
-- column.
-- RUN THIS ONCE against the OCAPSYS schema, after engineering.sql.
--
-- Eight rows, because ENGINEERING has eight data columns:
--
--   SHARED    "NO"  REQUESTOR  LOTNUMBER  "PACKAGE"  PRODUCT
--   SAWING    RECIPESAWING
--   WIREBOND  RECIPEWIREBOND
--   MARKER    RECIPEMARKER
--
-- The Engineering page shows a user the SHARED columns plus the recipe
-- column of each group they belong to, and hides the others. A user's
-- group is the Sawing / Wirebond / Marker module grant they already hold
-- in TBLACCESS - granting somebody Sawing is what puts RECIPESAWING on
-- their page. Two grants means two recipe columns. A Super Admin sees
-- all three.
--
-- SHARED is not a team: those five columns are the lot's identity, and
-- everybody who can open the page at all sees them.
--
-- TO MOVE A COLUMN BETWEEN GROUPS, run an UPDATE - no code change, no
-- redeploy. The app re-reads this table when its cache expires (60
-- seconds by default):
--
--   UPDATE OCAPSYS.ENGINEERINGCOLUMNGROUP
--      SET group_name = 'MARKER'
--    WHERE column_name = 'RECIPEWIREBOND';
--   COMMIT;
--
-- The WSTYPE side of the old version of this table has moved out to
-- ENGINEERINGWSTYPE, because several workstation types now share one
-- recipe column and a single table cannot be keyed on both.
-- =====================================================================


DROP TABLE OCAPSYS.ENGINEERINGCOLUMNGROUP CASCADE CONSTRAINTS;

CREATE TABLE OCAPSYS.ENGINEERINGCOLUMNGROUP
(
  COLUMN_NAME    VARCHAR2(30 BYTE) NOT NULL,  -- a real column of OCAPSYS.ENGINEERING
  GROUP_NAME     VARCHAR2(30 BYTE) NOT NULL,  -- SAWING | WIREBOND | MARKER | SHARED
  DISPLAY_LABEL  VARCHAR2(60 BYTE),           -- column heading on the page
  SORT_ORDER     NUMBER DEFAULT 0 NOT NULL,   -- left-to-right order within the group
  CONSTRAINT pk_engcolgroup PRIMARY KEY (COLUMN_NAME),
  CONSTRAINT ck_engcolgroup_group CHECK (GROUP_NAME IN ('SAWING', 'WIREBOND', 'MARKER', 'SHARED'))
);


-- ---------------------------------------------------------------------
-- The eight rows
-- ---------------------------------------------------------------------

-- The lot's identity. Shown to everybody who can open the page.
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, display_label, sort_order) VALUES ('NO',        'SHARED', 'No',         10);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, display_label, sort_order) VALUES ('REQUESTOR', 'SHARED', 'Requestor',  20);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, display_label, sort_order) VALUES ('LOTNUMBER', 'SHARED', 'Lot Number', 30);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, display_label, sort_order) VALUES ('PACKAGE',   'SHARED', 'Package',    40);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, display_label, sort_order) VALUES ('PRODUCT',   'SHARED', 'Product',    50);

-- One recipe per group.
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, display_label, sort_order) VALUES ('RECIPESAWING',   'SAWING',   'Sawing Recipe',   10);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, display_label, sort_order) VALUES ('RECIPEWIREBOND', 'WIREBOND', 'Wirebond Recipe', 10);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, display_label, sort_order) VALUES ('RECIPEMARKER',   'MARKER',   'Marker Recipe',   10);

COMMIT;


-- ---------------------------------------------------------------------
-- Check: every mapped column really exists on ENGINEERING. Any row this
-- returns is a typo - the page drops it and logs a warning.
-- ---------------------------------------------------------------------
SELECT g.column_name, g.group_name
FROM   OCAPSYS.ENGINEERINGCOLUMNGROUP g
WHERE  NOT EXISTS (SELECT 1
                   FROM   all_tab_columns c
                   WHERE  c.table_name  = 'ENGINEERING'
                     AND  c.column_name = g.column_name);

-- And the other way round: ENGINEERING columns nobody has grouped. These
-- are invisible on the page until they are given a group.
SELECT c.column_name
FROM   all_tab_columns c
WHERE  c.table_name = 'ENGINEERING'
  AND  c.column_name NOT IN ('TBLROWID', 'LASTUPDATE', 'LASTUPDATEDBY')
  AND  NOT EXISTS (SELECT 1
                   FROM   OCAPSYS.ENGINEERINGCOLUMNGROUP g
                   WHERE  g.column_name = c.column_name)
ORDER  BY c.column_id;
