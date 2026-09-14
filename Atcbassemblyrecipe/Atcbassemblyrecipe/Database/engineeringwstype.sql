-- =====================================================================
-- OCAPSYS.ENGINEERINGWSTYPE - which ENGINEERING recipe column a
-- workstation type reads.
-- RUN THIS ONCE against the OCAPSYS schema, after engineering.sql.
--
-- Read by AwacsMesService only. The web app never looks at it.
--
-- ENGINEERING now holds three recipes, one per group, but a line has
-- many workstation types - SAWING and WAOI are both sawing machines,
-- DIEBOND and the three ASMWB types are all wirebond, MARKER, 2DMARKER,
-- TRIMFORM and MOULD are all marker. So MANY WSTYPES POINT AT ONE
-- COLUMN, which is why this is its own table: ENGINEERINGCOLUMNGROUP is
-- keyed on the column, this one is keyed on the WSTYPE.
--
-- WHAT HAPPENS AT RUN TIME, for a WOID starting with ENG:
--
--   getWSType(WsId)                -> AWACSWSTYPE says the machine is SAWING
--   this table                     -> SAWING reads RECIPESAWING
--   ENGINEERING by LOTNUMBER       -> that lot's RECIPESAWING value
--   -> sent back as the RECIPE attribute
--
-- A WSTYPE that is not in this table gets a RESULT attribute saying so,
-- naming this table. Adding a machine type is an INSERT here, not a code
-- change and not a redeploy.
-- =====================================================================


DROP TABLE OCAPSYS.ENGINEERINGWSTYPE CASCADE CONSTRAINTS;

CREATE TABLE OCAPSYS.ENGINEERINGWSTYPE
(
  WSTYPE       VARCHAR2(30 BYTE) NOT NULL,  -- AWACSWSTYPE.WSTYPE, uppercase
  COLUMN_NAME  VARCHAR2(30 BYTE) NOT NULL,  -- RECIPESAWING | RECIPEWIREBOND | RECIPEMARKER
  CONSTRAINT pk_engwstype PRIMARY KEY (WSTYPE)
);


-- ---------------------------------------------------------------------
-- The workstation types this line runs today. Check them against your
-- own AWACSWSTYPE - the query at the bottom lists any that are missing.
-- ---------------------------------------------------------------------

-- Sawing machines.
INSERT INTO OCAPSYS.ENGINEERINGWSTYPE (wstype, column_name) VALUES ('SAWING',    'RECIPESAWING');
INSERT INTO OCAPSYS.ENGINEERINGWSTYPE (wstype, column_name) VALUES ('WAOI',      'RECIPESAWING');

-- Wirebond, and the die/clip attach machines that feed it.
INSERT INTO OCAPSYS.ENGINEERINGWSTYPE (wstype, column_name) VALUES ('DIEBOND',   'RECIPEWIREBOND');
INSERT INTO OCAPSYS.ENGINEERINGWSTYPE (wstype, column_name) VALUES ('ASMWB',     'RECIPEWIREBOND');
INSERT INTO OCAPSYS.ENGINEERINGWSTYPE (wstype, column_name) VALUES ('ASMWBM',    'RECIPEWIREBOND');
INSERT INTO OCAPSYS.ENGINEERINGWSTYPE (wstype, column_name) VALUES ('ASMWBD',    'RECIPEWIREBOND');
INSERT INTO OCAPSYS.ENGINEERINGWSTYPE (wstype, column_name) VALUES ('ADAT',      'RECIPEWIREBOND');

-- Marking, moulding and trim/form.
INSERT INTO OCAPSYS.ENGINEERINGWSTYPE (wstype, column_name) VALUES ('MARKER',    'RECIPEMARKER');
INSERT INTO OCAPSYS.ENGINEERINGWSTYPE (wstype, column_name) VALUES ('2DMARKER',  'RECIPEMARKER');
INSERT INTO OCAPSYS.ENGINEERINGWSTYPE (wstype, column_name) VALUES ('MOULD',     'RECIPEMARKER');
INSERT INTO OCAPSYS.ENGINEERINGWSTYPE (wstype, column_name) VALUES ('TRIMFORM',  'RECIPEMARKER');
INSERT INTO OCAPSYS.ENGINEERINGWSTYPE (wstype, column_name) VALUES ('PLATING',   'RECIPEMARKER');

COMMIT;


-- ---------------------------------------------------------------------
-- AwacsMesService reads this table. Grant it if the service connects as
-- a different Oracle user than OCAPSYS.
-- ---------------------------------------------------------------------
-- GRANT SELECT ON OCAPSYS.ENGINEERINGWSTYPE TO <mes_user>;
-- CREATE OR REPLACE SYNONYM <mes_user>.ENGINEERINGWSTYPE FOR OCAPSYS.ENGINEERINGWSTYPE;


-- ---------------------------------------------------------------------
-- Check 1: every column named here is a real ENGINEERING column. Any row
-- this returns means that WSTYPE's lookup will refuse to run.
-- ---------------------------------------------------------------------
SELECT w.wstype, w.column_name
FROM   OCAPSYS.ENGINEERINGWSTYPE w
WHERE  NOT EXISTS (SELECT 1
                   FROM   all_tab_columns c
                   WHERE  c.table_name  = 'ENGINEERING'
                     AND  c.column_name = w.column_name);

-- ---------------------------------------------------------------------
-- Check 2: workstation types that exist on the line but have no mapping.
-- An ENG lot on one of these machines comes back with
--   "WSTYPE:... has no ENGINEERING recipe column."
-- Add the missing ones above.
-- ---------------------------------------------------------------------
SELECT DISTINCT UPPER(a.wstype) AS wstype
FROM   OCAPSYS.AWACSWSTYPE a
WHERE  a.wstype IS NOT NULL
  AND  NOT EXISTS (SELECT 1
                   FROM   OCAPSYS.ENGINEERINGWSTYPE w
                   WHERE  w.wstype = UPPER(a.wstype))
ORDER  BY 1;
