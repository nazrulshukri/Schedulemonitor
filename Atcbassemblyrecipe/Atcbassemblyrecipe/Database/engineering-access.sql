-- =====================================================================
-- Put a user in a group: which PAGES they see, and which ENGINEERING
-- recipe column they see inside the Engineering page.
--
-- One grant does both jobs. The Sawing / Wirebond / Marker module grant
-- opens that group's own page AND puts that group's recipe column on the
-- Engineering page. There is no separate "engineering group" to keep in
-- step.
--
-- A group profile is four rows:
--
--   AWACSWSTYPE     the machine master - everybody needs it
--   <the group>     Sawing OR Wirebond OR Marker - their own page, and
--                   their column on Engineering
--   Engineering     the engineering lot page
--   AWACSLF         the leadframe master
--
-- So a sawing user's sidebar reads AWACSWSTYPE, Sawing, Engineering,
-- AWACSLF, and Wirebond and Marker are not there at all. A marker user
-- gets AWACSWSTYPE, Marker, Engineering, AWACSLF and no Sawing. Give
-- somebody two group rows and they get both pages and both columns.
--
-- WHO NEEDS THIS: only users who are NOT Super Admins.
-- AccessEvaluator.GetAsync returns ModulePermission.Full to any user in
-- the SuperAdmin role before it ever reads TBLACCESS, so a Super Admin
-- sees every page and every column with no grant at all. That is what
-- makes the Super Admin the one who hands the groups out.
--
-- EASIER THAN RUNNING THIS: open Access Management in the app and tick
-- the boxes on those four rows. It writes exactly what is below. Use
-- this script when the app cannot be reached, or for a batch of users.
--
-- ACCESS_LEVEL is one tier per row, not a list (Models/AccessGrant.cs):
--   READ   view only
--   WRITE  view + add + update
--   ADMIN  view + add + update + delete
--   NONE   nothing
--
-- Run the INSERTs and the COMMIT as SEPARATE statements, or the client
-- reports ORA-00933 against the COMMIT line.
-- =====================================================================


-- ---------------------------------------------------------------------
-- A SAWING user.
-- Sees: AWACSWSTYPE, Sawing, Engineering, AWACSLF.
-- On the Engineering page: No, Requestor, Lot Number, Package, Product
--                          and SAWING. The wirebond and marker
--                          recipe columns are not rendered, not
--                          searched, and cannot be written.
-- ---------------------------------------------------------------------
INSERT INTO OCAPSYS.TBLACCESS
    (USER_ID, USER_NAME, ROLE_NAME, MODULE_NAME, ACCESS_LEVEL,
     GRANTED_BY, GRANTED_DATE, EXPIRY_DATE, STATUS, CREATED_DATE, UPDATED_DATE)
SELECT 'NX021557', 'Sawing User', 'User', module_name, access_level,
       'NX487878', SYSDATE, NULL, 'ACTIVE', SYSDATE, SYSDATE
FROM   (SELECT 'AWACSWSTYPE' AS module_name, 'READ'  AS access_level FROM dual UNION ALL
        SELECT 'Sawing',                     'WRITE'                 FROM dual UNION ALL
        SELECT 'Engineering',                'WRITE'                 FROM dual UNION ALL
        SELECT 'AWACSLF',                    'READ'                  FROM dual);

COMMIT;


-- ---------------------------------------------------------------------
-- A WIREBOND user. Same four rows, Wirebond instead of Sawing.
-- ---------------------------------------------------------------------
-- INSERT INTO OCAPSYS.TBLACCESS
--     (USER_ID, USER_NAME, ROLE_NAME, MODULE_NAME, ACCESS_LEVEL,
--      GRANTED_BY, GRANTED_DATE, EXPIRY_DATE, STATUS, CREATED_DATE, UPDATED_DATE)
-- SELECT 'NX021558', 'Wirebond User', 'User', module_name, access_level,
--        'NX487878', SYSDATE, NULL, 'ACTIVE', SYSDATE, SYSDATE
-- FROM   (SELECT 'AWACSWSTYPE' AS module_name, 'READ'  AS access_level FROM dual UNION ALL
--         SELECT 'Wirebond',                   'WRITE'                 FROM dual UNION ALL
--         SELECT 'Engineering',                'WRITE'                 FROM dual UNION ALL
--         SELECT 'AWACSLF',                    'READ'                  FROM dual);
-- COMMIT;


-- ---------------------------------------------------------------------
-- A MARKER user.
-- ---------------------------------------------------------------------
-- INSERT INTO OCAPSYS.TBLACCESS
--     (USER_ID, USER_NAME, ROLE_NAME, MODULE_NAME, ACCESS_LEVEL,
--      GRANTED_BY, GRANTED_DATE, EXPIRY_DATE, STATUS, CREATED_DATE, UPDATED_DATE)
-- SELECT 'NX021559', 'Marker User', 'User', module_name, access_level,
--        'NX487878', SYSDATE, NULL, 'ACTIVE', SYSDATE, SYSDATE
-- FROM   (SELECT 'AWACSWSTYPE' AS module_name, 'READ'  AS access_level FROM dual UNION ALL
--         SELECT 'Marker',                     'WRITE'                 FROM dual UNION ALL
--         SELECT 'Engineering',                'WRITE'                 FROM dual UNION ALL
--         SELECT 'AWACSLF',                    'READ'                  FROM dual);
-- COMMIT;


-- ---------------------------------------------------------------------
-- MOVE somebody from one group to another. Do NOT just add the new row -
-- leaving the old one behind means they keep the old page and the old
-- recipe column as well.
-- ---------------------------------------------------------------------
-- DELETE FROM OCAPSYS.TBLACCESS
--  WHERE user_id = 'NX021557'
--    AND UPPER(module_name) = 'SAWING';
--
-- INSERT INTO OCAPSYS.TBLACCESS
--     (USER_ID, USER_NAME, ROLE_NAME, MODULE_NAME, ACCESS_LEVEL,
--      GRANTED_BY, GRANTED_DATE, EXPIRY_DATE, STATUS, CREATED_DATE, UPDATED_DATE)
-- VALUES
--     ('NX021557', 'Sawing User', 'User', 'Marker', 'WRITE',
--      'NX487878', SYSDATE, NULL, 'ACTIVE', SYSDATE, SYSDATE);
-- COMMIT;


-- ---------------------------------------------------------------------
-- Change a tier rather than adding a second row for the same user and
-- module.
-- ---------------------------------------------------------------------
-- UPDATE OCAPSYS.TBLACCESS
--    SET access_level = 'ADMIN',
--        granted_by   = 'NX487878',
--        granted_date = SYSDATE,
--        updated_date = SYSDATE
--  WHERE user_id = 'NX021557'
--    AND UPPER(module_name) = 'ENGINEERING';
-- COMMIT;


-- ---------------------------------------------------------------------
-- Who is in which group. A user with no row for a module has no access
-- to it - the ACCOUNT row carries their overall role and status, not
-- module access.
-- ---------------------------------------------------------------------
SELECT user_id,
       user_name,
       role_name,
       module_name,
       access_level,
       status
FROM   OCAPSYS.TBLACCESS
WHERE  UPPER(module_name) IN ('AWACSWSTYPE', 'SAWING', 'WIREBOND', 'MARKER', 'ENGINEERING', 'AWACSLF')
ORDER  BY user_id,
          CASE UPPER(module_name)
            WHEN 'AWACSWSTYPE' THEN 1
            WHEN 'SAWING'      THEN 2
            WHEN 'WIREBOND'    THEN 3
            WHEN 'MARKER'      THEN 4
            WHEN 'ENGINEERING' THEN 5
            ELSE 6
          END;
