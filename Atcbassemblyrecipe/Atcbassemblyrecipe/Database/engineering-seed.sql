-- =====================================================================
-- ONE STATEMENT that fills both mapping tables and commits itself.
--
-- Use this when the tables exist but are empty - which shows up as:
--
--   the page   yellow banner "ENGINEERINGCOLUMNGROUP could not be read"
--              while the grid still draws its columns (built-in defaults)
--   the MES    RESULT = "WSTYPE:SAWING has no ENGINEERING recipe column.
--              Map it in ENGINEERINGWSTYPE."
--
-- Both mean the same thing: the table was read, and there was nothing in
-- it. The usual reason is that the INSERTs ran but the COMMIT did not.
-- In SQL Developer, Ctrl+Enter (Run Statement) executes ONE statement -
-- so on a script of 20 INSERTs and a COMMIT, it runs the first INSERT
-- and stops. F5 (Run Script) runs the whole file.
--
-- This file avoids that trap entirely: it is a single PL/SQL block, so
-- Ctrl+Enter and F5 both do the same thing, and the COMMIT is inside it.
-- It is also safe to re-run - it clears both tables first.
--
-- >>> RUN IT AS THE USER THE APP CONNECTS AS. <<<
-- appsettings.json -> ConnectionStrings:OCAP -> User ID. OCAPSYS in a
-- stock install. Rows committed by a different user are still only
-- visible to the app if the app can read that user's tables.
-- =====================================================================

SET SERVEROUTPUT ON

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

    n_groups PLS_INTEGER;
    n_wstype PLS_INTEGER;
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

    SELECT COUNT(*) INTO n_groups FROM engineeringcolumngroup;
    SELECT COUNT(*) INTO n_wstype FROM engineeringwstype;

    DBMS_OUTPUT.PUT_LINE('Connected as      : ' || SYS_CONTEXT('USERENV', 'SESSION_USER'));
    DBMS_OUTPUT.PUT_LINE('Current schema    : ' || SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA'));
    DBMS_OUTPUT.PUT_LINE('Column group rows : ' || n_groups || '   (expect 8)');
    DBMS_OUTPUT.PUT_LINE('WSTYPE map rows   : ' || n_wstype || '  (expect 12)');
    DBMS_OUTPUT.PUT_LINE('COMMITTED.');
END;
/


-- ---------------------------------------------------------------------
-- Proof. Run this in a SEPARATE session - a new SQL Developer connection,
-- or after disconnecting and reconnecting. Rows that show up in your own
-- session but not in a fresh one were never committed, and the app is a
-- fresh session every time.
--
-- Expect 8 and 12. Anything less and the block above did not run as this
-- user.
-- ---------------------------------------------------------------------
SELECT 'ENGINEERINGCOLUMNGROUP' AS table_name, COUNT(*) AS rows_committed FROM engineeringcolumngroup
UNION ALL
SELECT 'ENGINEERINGWSTYPE', COUNT(*) FROM engineeringwstype;

-- The exact row the MES service was looking for when it said
-- "WSTYPE:SAWING has no ENGINEERING recipe column". One row = fixed.
SELECT wstype, column_name
FROM   engineeringwstype
WHERE  wstype = 'SAWING';

-- And the column mapping the page needs. Eight rows = the yellow banner
-- goes away on the next refresh (the mapping is cached for 60 seconds).
SELECT group_name, column_name, display_label
FROM   engineeringcolumngroup
ORDER  BY CASE group_name
            WHEN 'SHARED' THEN 0 WHEN 'SAWING' THEN 1
            WHEN 'WIREBOND' THEN 2 ELSE 3
          END, sort_order;
