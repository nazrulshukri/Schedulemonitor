-- =====================================================================
-- OCAPSYS.ENGINEERINGCOLUMNGROUP - which user group owns which
-- ENGINEERING recipe column, and which WSTYPE each column answers for.
-- RUN THIS ONCE against the OCAPSYS schema, after engineering.sql.
--
-- ENGINEERING carries 23 recipe columns. Nobody works on all 23: the
-- sawing people care about the sawing recipes, the wirebond people about
-- the wirebond recipes, the marker people about the marker recipes. This
-- table says which is which, so the Engineering page can show a user only
-- their own group's columns and hide the other two.
--
-- WHO READS IT:
--   * EngineeringColumnGroupProvider in the web app, cached per process.
--     A user's group comes from the TBLACCESS module grants they already
--     have - Sawing, Wirebond, Marker - so there is nothing extra to
--     administer: grant somebody the Sawing module and the Engineering
--     page shows them the SAWING columns. A user in more than one group
--     sees the union. A Super Admin sees everything.
--   * AwacsMesService, through the WSTYPE column: a DBorderUpdate for an
--     ENG workorder looks up the workstation's WSTYPE here to find which
--     ENGINEERING column holds that step's recipe.
--
-- TO MOVE A COLUMN BETWEEN GROUPS, run an UPDATE - no code change, no
-- redeploy. The web app re-reads this table when its cache expires
-- (60 seconds) and the MES service on its next call:
--   UPDATE OCAPSYS.ENGINEERINGCOLUMNGROUP
--      SET group_name = 'MARKER'
--    WHERE column_name = 'RECIPERM';
--   COMMIT;
--
-- GROUP_NAME is one of SAWING, WIREBOND, MARKER or SHARED. SHARED columns
-- are shown to everybody who can open the page - they are the row's
-- identity (lot number, package, product), not one team's recipe.
-- =====================================================================


DROP TABLE OCAPSYS.ENGINEERINGCOLUMNGROUP CASCADE CONSTRAINTS;

CREATE TABLE OCAPSYS.ENGINEERINGCOLUMNGROUP
(
  COLUMN_NAME    VARCHAR2(30 BYTE) NOT NULL,  -- a real column of OCAPSYS.ENGINEERING
  GROUP_NAME     VARCHAR2(30 BYTE) NOT NULL,  -- SAWING | WIREBOND | MARKER | SHARED
  WSTYPE         VARCHAR2(30 BYTE),           -- AWACSWSTYPE.WSTYPE this column is the recipe for
  DISPLAY_LABEL  VARCHAR2(60 BYTE),           -- column heading on the page
  SORT_ORDER     NUMBER DEFAULT 0 NOT NULL,   -- left-to-right order within the group
  CONSTRAINT pk_engcolgroup PRIMARY KEY (COLUMN_NAME),
  CONSTRAINT ck_engcolgroup_group CHECK (GROUP_NAME IN ('SAWING', 'WIREBOND', 'MARKER', 'SHARED'))
);

-- One WSTYPE must not name two columns, or the MES lookup has no single
-- answer. NULL WSTYPE is allowed for as many rows as you like: those
-- columns are shown on the page but never answer a DBorderUpdate.
CREATE UNIQUE INDEX ix_engcolgroup_wstype ON OCAPSYS.ENGINEERINGCOLUMNGROUP (WSTYPE);


-- ---------------------------------------------------------------------
-- Seed. This is the starting mapping - change it to match how your
-- groups actually divide the work.
-- ---------------------------------------------------------------------

-- SHARED: the row's identity. Everybody who can open the page sees these.
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('NO',        'SHARED', NULL, 'No',        10);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('REQUESTOR', 'SHARED', NULL, 'Requestor', 20);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('LOTNUMBER', 'SHARED', NULL, 'Lot Number', 30);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('PACKAGE',   'SHARED', NULL, 'Package',   40);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('PRODUCT',   'SHARED', NULL, 'Product',   50);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('ADAT',      'SHARED', 'ADAT', 'ADAT',     60);

-- SAWING: front-end, wafer-side steps.
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPES1',        'SAWING', 'SAWING',     'Sawing 1',    10);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPES2',        'SAWING', NULL,         'Sawing 2',    20);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPEBACKGRIND', 'SAWING', 'BACKGRIND',  'Backgrind',   30);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPEWPROBER',   'SAWING', 'WPROBER',    'Wafer Prober',40);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPEWAFERTEST', 'SAWING', 'WAFERTEST',  'Wafer Test',  50);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPEWAOI',      'SAWING', 'WAOI',       'Wafer AOI',   60);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPEWLTR',      'SAWING', 'WLTR',       'WLTR',        70);

-- WIREBOND: die attach through to the bonded strip.
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPEDA',      'WIREBOND', 'DIEBOND', 'Die Attach',   10);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPECA',      'WIREBOND', 'CLIPATTACH', 'Clip Attach', 20);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPEMCDWB',   'WIREBOND', 'ASMWB',   'MCD Wirebond', 30);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPEAX',      'WIREBOND', 'AX',      'AX',           40);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPEAOI',     'WIREBOND', 'AOI',     'AOI',          50);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPEL200',    'WIREBOND', 'L200',    'L200',         60);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPEPHICOM',  'WIREBOND', 'PHICOM',  'Phicom',       70);

-- MARKER: mould, mark, trim/form and the back-end tests.
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPEMD',        'MARKER', 'MARKER',    'Marker',      10);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPE2DMARKER',  'MARKER', '2DMARKER',  '2D Marker',   20);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPEMOULD',     'MARKER', 'MOULD',     'Mould',       30);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPEMOLD',      'MARKER', 'MOLD',      'Mold',        40);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPETF',        'MARKER', 'TRIMFORM',  'Trim Form',   50);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPERM',        'MARKER', 'RM',        'RM',          60);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPESTRIPTEST', 'MARKER', 'STRIPTEST', 'Strip Test',  70);
INSERT INTO OCAPSYS.ENGINEERINGCOLUMNGROUP (column_name, group_name, wstype, display_label, sort_order) VALUES ('RECIPEFINALTEST', 'MARKER', 'FINALTEST', 'Final Test',  80);

COMMIT;


-- ---------------------------------------------------------------------
-- AwacsMesService reads this table too. Grant it if the service connects
-- as a different Oracle user than OCAPSYS.
-- ---------------------------------------------------------------------
-- GRANT SELECT ON OCAPSYS.ENGINEERINGCOLUMNGROUP TO <mes_user>;
-- CREATE OR REPLACE SYNONYM <mes_user>.ENGINEERINGCOLUMNGROUP FOR OCAPSYS.ENGINEERINGCOLUMNGROUP;


-- ---------------------------------------------------------------------
-- Check: every mapped column really exists on ENGINEERING. Any row this
-- returns is a typo - the page silently drops it and the MES lookup for
-- that WSTYPE answers nothing.
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
