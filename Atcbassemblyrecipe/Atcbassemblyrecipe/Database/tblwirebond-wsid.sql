-- =====================================================================
-- Relate TBLWIREBOND to AWACSWSTYPE
--
-- AWACSWSTYPE is the parent: one row per machine, keyed by WSID.
-- TBLWIREBOND is the child: one row per recipe. Until now the child had
-- nothing pointing back at the parent, which is why saving a recipe
-- could not touch AWACSWSTYPE - there was no machine on the recipe.
--
-- This adds that pointer. Same shape as TBLSAWING.SAWMACHINE, which
-- already holds WSID values like DS12-004 and 2O1F-002.
--
-- Run the whole file: F5 (Execute as Script in Toad, Run Script in SQL
-- Developer). Every statement is safe to run twice.
-- =====================================================================


-- ---------------------------------------------------------------------
-- 1. The column
--
-- VARCHAR2(16) to match AWACSWSTYPE.WSID exactly. Nullable to start with,
-- so existing recipes are not rejected before you have assigned them a
-- machine; step 4 tightens it once they are.
-- ---------------------------------------------------------------------
ALTER TABLE OCAPSYS.TBLWIREBOND ADD (WSID VARCHAR2(16 BYTE));


-- ---------------------------------------------------------------------
-- 2. Which machines can a recipe name?
--
-- The wire bonders registered in AWACSWSTYPE. If this comes back empty,
-- register them first - the page will not let you save a recipe without
-- one. Operations > AWACSWSTYPE > Add Row, WSTYPE WIREBOND, or
-- Database/awacswstype-wirebond.sql.
-- ---------------------------------------------------------------------
SELECT wsid, wstype, wsdb
FROM   OCAPSYS.AWACSWSTYPE
WHERE  wstype = 'WIREBOND'
ORDER  BY wsid;


-- ---------------------------------------------------------------------
-- 3. Give the existing recipes a machine
--
-- Rows written before this column existed have WSID NULL. They still
-- show on the page, but they cannot be saved again until a machine is
-- picked, so set one now. Replace WB-007 with the right machine.
-- ---------------------------------------------------------------------
SELECT COUNT(1) AS recipes_with_no_machine
FROM   OCAPSYS.TBLWIREBOND
WHERE  wsid IS NULL;

-- UPDATE OCAPSYS.TBLWIREBOND SET wsid = 'WB-007' WHERE wsid IS NULL;
-- COMMIT;


-- ---------------------------------------------------------------------
-- 4. OPTIONAL - make the database enforce the relationship too
--
-- The app already refuses a recipe whose machine is not registered as a
-- wire bonder (WireBondService.MachineIsRegisteredAsync), on the page and
-- on CSV import. These two statements make Oracle refuse it as well, so
-- SQL*Plus and the MES cannot write a dangling row either.
--
-- A foreign key needs a unique key on the parent, and AWACSWSTYPE has
-- none - WSID is not unique across the whole table if the same machine
-- id appears under two WSTYPEs. CHECK THAT FIRST: this must return no
-- rows, or the unique index will fail.
-- ---------------------------------------------------------------------
SELECT wsid, COUNT(*) AS duplicate_rows
FROM   OCAPSYS.AWACSWSTYPE
GROUP  BY wsid
HAVING COUNT(*) > 1
ORDER  BY wsid;

-- Only if the query above returns nothing:
-- ALTER TABLE OCAPSYS.AWACSWSTYPE ADD CONSTRAINT uk_awacswstype_wsid UNIQUE (wsid);
-- ALTER TABLE OCAPSYS.TBLWIREBOND ADD CONSTRAINT fk_tblwirebond_wsid
--     FOREIGN KEY (wsid) REFERENCES OCAPSYS.AWACSWSTYPE (wsid);

-- Note what the foreign key does NOT do: it checks the WSID exists, not
-- that its WSTYPE is WIREBOND. The app checks both. Keep both.


-- ---------------------------------------------------------------------
-- 5. OPTIONAL - require a machine on every recipe
--    Run only after step 3 has left no NULLs.
-- ---------------------------------------------------------------------
-- ALTER TABLE OCAPSYS.TBLWIREBOND MODIFY (WSID NOT NULL);


-- ---------------------------------------------------------------------
-- 6. The relationship, end to end
-- ---------------------------------------------------------------------
SELECT w.wsid          AS machine,
       a.wstype,
       a.wsdb          AS business_group,
       w."PACKAGE",
       w.product,
       w.leadframe12nc,
       w.recipe
FROM   OCAPSYS.TBLWIREBOND w
       LEFT JOIN OCAPSYS.AWACSWSTYPE a
              ON UPPER(a.wsid) = UPPER(w.wsid)
             AND a.wstype = 'WIREBOND'
ORDER  BY w.wsid, w.product;

-- Any recipe whose machine is not a registered wire bonder shows up here
-- with an empty WSTYPE. The app will refuse to save those rows until the
-- machine is registered.
