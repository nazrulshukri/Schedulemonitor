-- =====================================================================
-- Rows that actually show up on the Wirebond page
-- (https://localhost:7029/Awacs/TableWirebond)
--
-- That page is a recipe grid over AWACSRECIPEBYWSTYPE filtered to
-- WSTYPE = 'WIREBOND'. It does NOT read OCAPSYS.TBLWIREBOND - that is a
-- different table (the wire bond OCAP log) with no page of its own yet.
-- See ../../docs/wirebond-page-fix.md.
--
-- So: rows inserted into TBLWIREBOND will never appear on this page, and
-- rows inserted below will never appear in TBLWIREBOND.
--
-- Same shape as AwacsWstypeService.CreateRecipeRowAsync, which is what
-- the "+ Add Recipe" button on the page runs:
--   INSERT INTO awacsrecipebywstype
--       (tblrowid, lastupdate, lastupdatedby, wstype, "PACKAGE",
--        product, leadframe12nc, recipe)
--   VALUES (RAWTOHEX(SYS_GUID()), SYSDATE, :lastupdatedby, :wstype,
--           :package, :product, :leadframe12nc, :recipe)
--
-- PACKAGE is a reserved word in Oracle, so it must stay double-quoted
-- and UPPERCASE - "PACKAGE", never "package".
--
-- Run the INSERT and the COMMIT as SEPARATE statements, or the client
-- reports ORA-00933 against the COMMIT line.
-- =====================================================================

INSERT INTO OCAPSYS.AWACSRECIPEBYWSTYPE
    (TBLROWID, LASTUPDATE, LASTUPDATEDBY, WSTYPE, "PACKAGE",
     PRODUCT, LEADFRAME12NC, RECIPE)
VALUES
    (RAWTOHEX(SYS_GUID()), SYSDATE, 'NX487878', 'WIREBOND', 'SOT669',
     'BUK9K6-40E', '934123456789', 'WB_SOT669_STD');

COMMIT;


-- Several at once. WSTYPE must be exactly 'WIREBOND' on every row or the
-- row lands on a different page (or on none).
INSERT ALL
    INTO OCAPSYS.AWACSRECIPEBYWSTYPE
        (TBLROWID, LASTUPDATE, LASTUPDATEDBY, WSTYPE, "PACKAGE",
         PRODUCT, LEADFRAME12NC, RECIPE)
    VALUES
        (RAWTOHEX(SYS_GUID()), SYSDATE, 'NX487878', 'WIREBOND', 'SOT1210',
         'PSMN012-30YLD', '934198765432', 'WB_SOT1210_STD')
    INTO OCAPSYS.AWACSRECIPEBYWSTYPE
        (TBLROWID, LASTUPDATE, LASTUPDATEDBY, WSTYPE, "PACKAGE",
         PRODUCT, LEADFRAME12NC, RECIPE)
    VALUES
        (RAWTOHEX(SYS_GUID()), SYSDATE, 'NX487878', 'WIREBOND', 'SOT1235',
         'PSMN4R0-40YS', '934111222333', 'WB_SOT1235_STD')
SELECT 1 FROM dual;

COMMIT;


-- What the page will show - if this returns rows and the page still says
-- "No rows found", the problem is the connection string or the search
-- box, not the data.
SELECT ROWIDTOCHAR(ROWID) AS tblrowid,
       wstype,
       "PACKAGE",
       product,
       leadframe12nc,
       recipe,
       lastupdatedby,
       TO_CHAR(lastupdate, 'YYYY-MM-DD HH24:MI:SS') AS lastupdate
FROM   OCAPSYS.AWACSRECIPEBYWSTYPE
WHERE  wstype = 'WIREBOND'
ORDER  BY lastupdate DESC NULLS LAST;

-- Trailing spaces or lowercase in WSTYPE are the usual reason a row was
-- inserted but does not appear. This finds them:
-- SELECT DISTINCT '[' || wstype || ']' AS wstype_exact,
--        COUNT(*) AS rows_found
-- FROM   OCAPSYS.AWACSRECIPEBYWSTYPE
-- GROUP  BY '[' || wstype || ']';
