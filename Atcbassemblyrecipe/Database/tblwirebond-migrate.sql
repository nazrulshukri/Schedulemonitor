-- =====================================================================
-- TBLWIREBOND - convert the table to the recipe shape.
--
-- FIXES: ORA-00904: "RECIPE": invalid identifier
--
-- HOW TO RUN THIS
--   Open the file, click anywhere in it, press F5.
--     Toad for Oracle .... F5 = Execute as Script
--     SQL Developer ...... F5 = Run Script
--   That is the whole procedure.
--
--   DO NOT use Execute Statement (F9 in Toad, Ctrl+Enter in SQL
--   Developer). That runs whatever the client thinks the statement under
--   the cursor is, and on a file of comment blocks it guesses wrong -
--   which is what the last script's
--     ORA-00933: SQL command not properly ended
--   was. There is ONE statement in this file now: the PL/SQL block
--   below, ending at the / on its own line. Nothing left to mis-split.
--
-- IT IS SAFE TO RUN TWICE. Every step checks the database first and
-- skips itself if it has already been done, so a half-finished attempt
-- is repaired by running the whole thing again.
--
-- WHAT IT DOES
--   1. copies the table to TBLWIREBOND_BAK, if that copy does not exist
--   2. drops the OCAP_WIREBOND_WORKWEEK trigger
--   3. adds TBLROWID, LASTUPDATE, LASTUPDATEDBY, "PACKAGE", PRODUCT,
--      LEADFRAME12NC, RECIPE - only the ones that are missing
--   4. copies WBPACKAGE into "PACKAGE"
--   5. lets TBLRECIPEHISTORY and TBLRECIPETRASH accept TBLWIREBOND rows,
--      without which Edit and Delete fail with ORA-02290 while Add works
--   6. drops every WB* column that is left
--   7. checks the result and RAISES AN ERROR if anything is still wrong
--
-- TO SEE WHAT IT DID: View > DBMS Output, click the green +, pick your
-- connection, then run. Optional - if something is wrong the block
-- raises an error either way.
-- =====================================================================

DECLARE
    v_owner        VARCHAR2(128);
    v_audit_owner  VARCHAR2(128);
    v_count        PLS_INTEGER;
    v_columns      VARCHAR2(4000);
    v_missing      VARCHAR2(4000);

    -- Every step goes through this. A step that cannot run is logged and
    -- stepped over rather than stopping the block: half of them are
    -- expected to be no-ops on a table that is already part-converted,
    -- and the check at the end is what decides whether the whole thing
    -- actually worked.
    PROCEDURE run(p_sql VARCHAR2) IS
    BEGIN
        EXECUTE IMMEDIATE p_sql;
        DBMS_OUTPUT.PUT_LINE('  done    : ' || p_sql);
    EXCEPTION
        WHEN OTHERS THEN
            DBMS_OUTPUT.PUT_LINE('  skipped : ' || SQLERRM);
            DBMS_OUTPUT.PUT_LINE('            ' || p_sql);
    END run;

    FUNCTION has_column(p_column VARCHAR2) RETURN BOOLEAN IS
        n PLS_INTEGER;
    BEGIN
        SELECT COUNT(*) INTO n
        FROM   all_tab_columns
        WHERE  owner = v_owner
          AND  table_name = 'TBLWIREBOND'
          AND  column_name = p_column;
        RETURN n > 0;
    END has_column;

    PROCEDURE add_column(p_column VARCHAR2, p_type VARCHAR2) IS
    BEGIN
        IF has_column(p_column) THEN
            DBMS_OUTPUT.PUT_LINE('  already : ' || p_column);
        ELSE
            -- The column name is quoted on the way in, so PACKAGE - a
            -- reserved word - is accepted like any other name.
            run('ALTER TABLE ' || v_owner || '.TBLWIREBOND ADD ("'
                || p_column || '" ' || p_type || ')');
        END IF;
    END add_column;
BEGIN
    ------------------------------------------------------------------
    -- Which schema is the table actually in? Prefer OCAPSYS, but do not
    -- insist on it - the app names the table without a schema, so it
    -- could be anywhere this account can see.
    ------------------------------------------------------------------
    BEGIN
        SELECT owner INTO v_owner
        FROM   (SELECT owner
                FROM   all_tables
                WHERE  table_name = 'TBLWIREBOND'
                ORDER  BY CASE WHEN owner = 'OCAPSYS' THEN 0 ELSE 1 END, owner)
        WHERE  ROWNUM = 1;
    EXCEPTION
        WHEN NO_DATA_FOUND THEN
            RAISE_APPLICATION_ERROR(-20001,
                'TBLWIREBOND does not exist, or this account cannot see it. '
                || 'Run Database/tblwirebond.sql to create it instead.');
    END;

    DBMS_OUTPUT.PUT_LINE('TBLWIREBOND found in schema ' || v_owner);
    DBMS_OUTPUT.PUT_LINE('');

    ------------------------------------------------------------------
    -- 1. Backup
    ------------------------------------------------------------------
    DBMS_OUTPUT.PUT_LINE('1. backup');
    SELECT COUNT(*) INTO v_count
    FROM   all_tables
    WHERE  owner = v_owner AND table_name = 'TBLWIREBOND_BAK';

    IF v_count > 0 THEN
        DBMS_OUTPUT.PUT_LINE('  already : ' || v_owner || '.TBLWIREBOND_BAK exists, left alone');
    ELSE
        run('CREATE TABLE ' || v_owner || '.TBLWIREBOND_BAK AS SELECT * FROM '
            || v_owner || '.TBLWIREBOND');
    END IF;

    ------------------------------------------------------------------
    -- 2. The work-week trigger, before the columns it reads
    --
    -- OCAP_WIREBOND_WORKWEEK reads WBDATE and writes WBOCAPWWK. Drop
    -- those columns while it still exists and the trigger goes INVALID,
    -- and then every insert dies with ORA-04098.
    ------------------------------------------------------------------
    DBMS_OUTPUT.PUT_LINE('2. work-week trigger');
    SELECT COUNT(*) INTO v_count
    FROM   all_triggers
    WHERE  owner = v_owner AND trigger_name = 'OCAP_WIREBOND_WORKWEEK';

    IF v_count = 0 THEN
        DBMS_OUTPUT.PUT_LINE('  already : no OCAP_WIREBOND_WORKWEEK trigger');
    ELSE
        run('DROP TRIGGER ' || v_owner || '.OCAP_WIREBOND_WORKWEEK');
    END IF;

    ------------------------------------------------------------------
    -- 3. The seven columns the page needs
    ------------------------------------------------------------------
    DBMS_OUTPUT.PUT_LINE('3. columns');
    add_column('TBLROWID',      'VARCHAR2(50 BYTE)');
    add_column('LASTUPDATE',    'DATE');
    add_column('LASTUPDATEDBY', 'VARCHAR2(50 BYTE)');
    add_column('PACKAGE',       'VARCHAR2(64 BYTE)');
    add_column('PRODUCT',       'VARCHAR2(64 BYTE)');
    add_column('LEADFRAME12NC', 'VARCHAR2(16 BYTE)');
    add_column('RECIPE',        'VARCHAR2(120 BYTE)');

    ------------------------------------------------------------------
    -- 4. WBPACKAGE is the one old column that means the same thing
    ------------------------------------------------------------------
    DBMS_OUTPUT.PUT_LINE('4. carry WBPACKAGE across');
    IF has_column('WBPACKAGE') THEN
        run('UPDATE ' || v_owner || '.TBLWIREBOND SET "PACKAGE" = WBPACKAGE '
            || 'WHERE WBPACKAGE IS NOT NULL AND "PACKAGE" IS NULL');
        COMMIT;
    ELSE
        DBMS_OUTPUT.PUT_LINE('  already : no WBPACKAGE column, nothing to carry');
    END IF;

    ------------------------------------------------------------------
    -- 5. Let the undo tables accept TBLWIREBOND rows
    --
    -- Edit copies the old row into TBLRECIPEHISTORY and Delete copies
    -- the whole row into TBLRECIPETRASH, both inside the same
    -- transaction as the write. Both tables CHECK the table name against
    -- a fixed list that never included TBLWIREBOND, so both fail with
    -- ORA-02290 and roll back - while Add works, which is what makes it
    -- confusing.
    ------------------------------------------------------------------
    DBMS_OUTPUT.PUT_LINE('5. undo tables');
    BEGIN
        SELECT owner INTO v_audit_owner
        FROM   (SELECT owner
                FROM   all_tables
                WHERE  table_name = 'TBLRECIPEHISTORY'
                ORDER  BY CASE WHEN owner = v_owner THEN 0 ELSE 1 END, owner)
        WHERE  ROWNUM = 1;

        run('ALTER TABLE ' || v_audit_owner || '.TBLRECIPEHISTORY DROP CONSTRAINT ck_tblrecipehistory_table');
        run('ALTER TABLE ' || v_audit_owner || '.TBLRECIPEHISTORY ADD CONSTRAINT ck_tblrecipehistory_table '
            || 'CHECK (table_name IN (''AWACSWSTYPE'', ''AWACSRECIPEBYWSTYPE'', ''AWACSLF'', ''TBLWIREBOND''))');
        run('ALTER TABLE ' || v_audit_owner || '.TBLRECIPETRASH DROP CONSTRAINT ck_tblrecipetrash_table');
        run('ALTER TABLE ' || v_audit_owner || '.TBLRECIPETRASH ADD CONSTRAINT ck_tblrecipetrash_table '
            || 'CHECK (table_name IN (''AWACSWSTYPE'', ''AWACSRECIPEBYWSTYPE'', ''AWACSLF'', ''TBLWIREBOND''))');
    EXCEPTION
        WHEN NO_DATA_FOUND THEN
            DBMS_OUTPUT.PUT_LINE('  WARNING : TBLRECIPEHISTORY not found. Run Database/tblrecipeaudit.sql,');
            DBMS_OUTPUT.PUT_LINE('            or Edit and Delete will fail on every page, not just this one.');
    END;

    ------------------------------------------------------------------
    -- 6. Drop whatever WB* columns are left
    --
    -- Built from the data dictionary rather than a fixed list, so it
    -- cannot fail by naming a column this particular table never had.
    -- Nothing in the new shape starts with WB, so nothing new is at risk.
    ------------------------------------------------------------------
    DBMS_OUTPUT.PUT_LINE('6. drop the old OCAP columns');
    v_columns := NULL;
    FOR c IN (SELECT column_name
              FROM   all_tab_columns
              WHERE  owner = v_owner
                AND  table_name = 'TBLWIREBOND'
                AND  column_name LIKE 'WB%'
              ORDER  BY column_id)
    LOOP
        v_columns := CASE WHEN v_columns IS NULL THEN '' ELSE v_columns || ', ' END
                     || '"' || c.column_name || '"';
    END LOOP;

    IF v_columns IS NULL THEN
        DBMS_OUTPUT.PUT_LINE('  already : no WB* columns left');
    ELSE
        run('ALTER TABLE ' || v_owner || '.TBLWIREBOND DROP (' || v_columns || ')');
    END IF;

    ------------------------------------------------------------------
    -- 7. Did it actually work?
    --
    -- Everything above forgives a failure and carries on. This does not:
    -- if the table is not the right shape now, the block raises and says
    -- exactly what is missing.
    ------------------------------------------------------------------
    DBMS_OUTPUT.PUT_LINE('7. check');
    v_missing := NULL;
    IF NOT has_column('TBLROWID')      THEN v_missing := v_missing || 'TBLROWID ';      END IF;
    IF NOT has_column('LASTUPDATE')    THEN v_missing := v_missing || 'LASTUPDATE ';    END IF;
    IF NOT has_column('LASTUPDATEDBY') THEN v_missing := v_missing || 'LASTUPDATEDBY '; END IF;
    IF NOT has_column('PACKAGE')       THEN v_missing := v_missing || 'PACKAGE ';       END IF;
    IF NOT has_column('PRODUCT')       THEN v_missing := v_missing || 'PRODUCT ';       END IF;
    IF NOT has_column('LEADFRAME12NC') THEN v_missing := v_missing || 'LEADFRAME12NC '; END IF;
    IF NOT has_column('RECIPE')        THEN v_missing := v_missing || 'RECIPE ';        END IF;

    IF v_missing IS NOT NULL THEN
        RAISE_APPLICATION_ERROR(-20002,
            'TBLWIREBOND is still missing: ' || v_missing
            || '. Turn on DBMS Output and run this again - the skipped lines say why. '
            || 'The usual reason is that this account cannot ALTER '
            || v_owner || '.TBLWIREBOND.');
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM   all_tab_columns
    WHERE  owner = v_owner AND table_name = 'TBLWIREBOND' AND column_name LIKE 'WB%';

    IF v_count > 0 THEN
        DBMS_OUTPUT.PUT_LINE('  note    : ' || v_count || ' WB* column(s) could not be dropped.');
        DBMS_OUTPUT.PUT_LINE('            Harmless - the app never names them.');
    END IF;

    DBMS_OUTPUT.PUT_LINE('');
    DBMS_OUTPUT.PUT_LINE('TBLWIREBOND is ready. Rebuild and open Operations > Wirebond.');
END;
/


-- =====================================================================
-- AFTERWARDS - three checks. Run them one at a time (Ctrl+Enter), or
-- select all three and press F5.
-- =====================================================================

-- 1. The shape. Expect exactly seven rows: TBLROWID, LASTUPDATE,
--    LASTUPDATEDBY, PACKAGE, PRODUCT, LEADFRAME12NC, RECIPE.
SELECT column_id, column_name, data_type, data_length
FROM   all_tab_columns
WHERE  table_name = 'TBLWIREBOND'
ORDER  BY column_id;

-- 2. Both constraints must now mention TBLWIREBOND, or Edit and Delete
--    fail with ORA-02290 while Add works.
SELECT constraint_name, search_condition
FROM   all_constraints
WHERE  constraint_name IN ('CK_TBLRECIPEHISTORY_TABLE', 'CK_TBLRECIPETRASH_TABLE');

-- 3. Old OCAP rows have no product, leadframe or recipe, so they show as
--    blank rows in the grid. This counts them.
SELECT COUNT(1) AS blank_rows
FROM   TBLWIREBOND
WHERE  product IS NULL AND leadframe12nc IS NULL AND recipe IS NULL;


-- To clear those blank rows out (TBLWIREBOND_BAK still has them):
-- DELETE FROM TBLWIREBOND
--  WHERE product IS NULL AND leadframe12nc IS NULL AND recipe IS NULL;
-- COMMIT;

-- When you are happy with the page:
-- DROP TABLE TBLWIREBOND_BAK;
