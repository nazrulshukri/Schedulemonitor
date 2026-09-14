-- =====================================================================
-- Fills both mapping tables and commits itself.
--
-- Use this when the tables exist but are empty - which shows up as:
--
--   the page   yellow banner "ENGINEERINGCOLUMNGROUP could not be read"
--              while the grid still draws its columns (built-in defaults)
--   the MES    RESULT = "WSTYPE:SAWING has no ENGINEERING recipe column.
--              Map it in ENGINEERINGWSTYPE."
--
-- Both mean the same thing: the table was read, and there was nothing in
-- it. The usual reason is that the INSERTs ran but the COMMIT did not. In
-- SQL Developer, Ctrl+Enter (Run Statement) executes ONE statement - so on
-- a script of 20 INSERTs and a COMMIT it runs the first INSERT and stops.
--
-- THERE ARE NO SQL*Plus COMMANDS IN THIS FILE. Not SET SERVEROUTPUT, not
-- anything else. Those are client commands, and a SQL window that is not
-- SQL*Plus sends them to the server, which answers
--   ORA-00922: missing or invalid option
-- and never reaches the real work. Everything below is plain SQL and
-- PL/SQL, so it runs anywhere: SQL Developer, PL/SQL Developer, Toad,
-- SQL*Plus.
--
-- >>> RUN IT AS THE USER THE APP CONNECTS AS. <<<
-- appsettings.json -> ConnectionStrings:OCAP -> User ID. OCAPSYS in a
-- stock install.
-- =====================================================================


-- ---------------------------------------------------------------------
-- STEP 1. Select this whole block - DECLARE down to the / on its own
-- line - and execute it. It is ONE statement, so it cannot half-run, and
-- the COMMIT is inside it. Safe to run again: it clears both tables
-- first.
-- ---------------------------------------------------------------------
DECLARE
    PROCEDURE grp (p_column VARCHAR2, p_group VARCHAR2, p_label VARCHAR2, p_order NUMBER) IS
    BEGIN
        INSERT INTO engineeringcolumngroup (column_name, group_name, display_label, sort_order)
        VALUES (p_column, p_group, p_label, p_order);
    END;

    PROCEDURE ws (p_wstype VARCHAR2, p_column VARCHAR2) IS
    BEGIN
        INSERT INTO engineeringwstype (wstype, column_name)
        VALUES (p_wstype, p_column);
    END;
BEGIN
    DELETE FROM engineeringcolumngroup;
    DELETE FROM engineeringwstype;

    -- ---- which group owns which column (read by the web app) ----
    -- The lot's identity. Everybody who can open the page sees these.
    grp('NO',        'SHARED', 'No',         10);
    grp('REQUESTOR', 'SHARED', 'Requestor',  20);
    grp('LOTNUMBER', 'SHARED', 'Lot Number', 30);
    grp('PACKAGE',   'SHARED', 'Package',    40);
    grp('PRODUCT',   'SHARED', 'Product',    50);

    -- One recipe per group. These column names must match ENGINEERING.
    grp('SAWING',    'SAWING',   'Sawing Recipe',   10);
    grp('WIREBOND',  'WIREBOND', 'Wirebond Recipe', 10);
    grp('MARKER',    'MARKER',   'Marker Recipe',   10);

    -- ---- which column a machine reads (read by AwacsMesService) ----
    -- Many workstation types share one column.
    ws('SAWING',   'SAWING');
    ws('WAOI',     'SAWING');

    ws('DIEBOND',  'WIREBOND');
    ws('ASMWB',    'WIREBOND');
    ws('ASMWBM',   'WIREBOND');
    ws('ASMWBD',   'WIREBOND');
    ws('ADAT',     'WIREBOND');

    ws('MARKER',   'MARKER');
    ws('2DMARKER', 'MARKER');
    ws('MOULD',    'MARKER');
    ws('TRIMFORM', 'MARKER');
    ws('PLATING',  'MARKER');

    COMMIT;
END;
/


-- ---------------------------------------------------------------------
-- STEP 2. The proof, as one SELECT - no DBMS_OUTPUT, so it displays in
-- any client. Run it on its own.
--
-- BEST RUN IN A SEPARATE SESSION: a new connection, or after
-- disconnecting and reconnecting. Rows that appear in the session that
-- inserted them but not in a fresh one were never committed - and the app
-- is a fresh session every time.
--
-- Expect exactly this:
--
--   CONNECTED AS              OCAPSYS
--   ENGINEERINGCOLUMNGROUP    8
--   ENGINEERINGWSTYPE         12
--   SAWING MAPS TO            SAWING
--
-- A count of 0 means step 1 did not run as this user.
-- SAWING MAPS TO showing "** MISSING **" is the exact row the MES service
-- could not find.
-- ---------------------------------------------------------------------
SELECT 'CONNECTED AS'           AS check_name,
       SYS_CONTEXT('USERENV', 'SESSION_USER') AS value
FROM   dual
UNION ALL
SELECT 'CURRENT SCHEMA',
       SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA')
FROM   dual
UNION ALL
SELECT 'ENGINEERINGCOLUMNGROUP',
       TO_CHAR(COUNT(*)) || '   (expect 8)'
FROM   engineeringcolumngroup
UNION ALL
SELECT 'ENGINEERINGWSTYPE',
       TO_CHAR(COUNT(*)) || '  (expect 12)'
FROM   engineeringwstype
UNION ALL
SELECT 'SAWING MAPS TO',
       NVL(MAX(column_name), '** MISSING **')
FROM   engineeringwstype
WHERE  wstype = 'SAWING';


-- ---------------------------------------------------------------------
-- STEP 3. Optional - the full mapping, to read with your own eyes.
-- ---------------------------------------------------------------------
SELECT group_name, column_name, display_label, sort_order
FROM   engineeringcolumngroup
ORDER  BY CASE group_name
            WHEN 'SHARED'   THEN 0
            WHEN 'SAWING'   THEN 1
            WHEN 'WIREBOND' THEN 2
            ELSE 3
          END,
          sort_order;

SELECT wstype, column_name
FROM   engineeringwstype
ORDER  BY column_name, wstype;
