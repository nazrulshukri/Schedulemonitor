-- =====================================================================
-- Give somebody the new "Wirebond OCAP" module (TBLWIREBOND).
--
-- WHO NEEDS THIS: only users who are NOT Super Admins.
-- AccessEvaluator.GetAsync returns ModulePermission.Full to any user in the
-- SuperAdmin role before it ever looks at TBLACCESS, so a Super Admin sees
-- the page and its Add/Edit/Delete buttons with no grant at all.
--
-- Easier than running this: open Access Management in the app and tick the
-- boxes on the "Wirebond OCAP" row. It writes exactly the row below. Use
-- this script when the app cannot be reached, or to grant a batch of users
-- at once.
--
-- MODULE_NAME must be spelled exactly 'Wirebond OCAP' - it is compared
-- against ModuleNames.WireBondOcap in Models/AppModule.cs. 'WIREBOND OCAP'
-- also matches (the lookup is case-insensitive) but keep the app spelling so
-- the Access Management grid shows one row instead of two.
--
-- ACCESS_LEVEL is one tier per row, not a list (Models/AccessGrant.cs):
--   READ   view only
--   WRITE  view + add + update
--   ADMIN  view + add + update + delete
--   NONE   nothing
--
-- The 'Wirebond' module is the recipe page over AWACSRECIPEBYWSTYPE and is a
-- different grant. Granting one does not grant the other.
--
-- Run the INSERT and the COMMIT as SEPARATE statements, or the client
-- reports ORA-00933 against the COMMIT line.
-- =====================================================================

-- One user, full access.
INSERT INTO OCAPSYS.TBLACCESS
    (USER_ID, USER_NAME, ROLE_NAME, MODULE_NAME, ACCESS_LEVEL,
     GRANTED_BY, GRANTED_DATE, EXPIRY_DATE, STATUS, CREATED_DATE, UPDATED_DATE)
VALUES
    ('NX021557', 'Operator Name', 'User', 'Wirebond OCAP', 'ADMIN',
     'NX487878', SYSDATE, NULL, 'ACTIVE', SYSDATE, SYSDATE);

COMMIT;


-- Change an existing grant instead of adding a second row for the same
-- user and module.
-- UPDATE OCAPSYS.TBLACCESS
--    SET access_level = 'WRITE',
--        granted_by   = 'NX487878',
--        granted_date = SYSDATE,
--        updated_date = SYSDATE
--  WHERE user_id     = 'NX021557'
--    AND module_name = 'Wirebond OCAP';
-- COMMIT;


-- Every user who can already see the Wirebond recipe page, granted the same
-- tier on the OCAP page. Skips anyone who already has an OCAP grant, so it
-- is safe to re-run.
-- INSERT INTO OCAPSYS.TBLACCESS
--     (USER_ID, USER_NAME, ROLE_NAME, MODULE_NAME, ACCESS_LEVEL,
--      GRANTED_BY, GRANTED_DATE, EXPIRY_DATE, STATUS, CREATED_DATE, UPDATED_DATE)
-- SELECT a.user_id, a.user_name, a.role_name, 'Wirebond OCAP', a.access_level,
--        'NX487878', SYSDATE, NULL, 'ACTIVE', SYSDATE, SYSDATE
-- FROM   OCAPSYS.TBLACCESS a
-- WHERE  a.module_name = 'Wirebond'
--   AND  NOT EXISTS (SELECT 1
--                    FROM   OCAPSYS.TBLACCESS b
--                    WHERE  b.user_id = a.user_id
--                      AND  UPPER(b.module_name) = 'WIREBOND OCAP');
-- COMMIT;


-- Who has what. A user with no row here has no access to the module - the
-- ACCOUNT row carries their overall role and status, not module access.
SELECT user_id,
       user_name,
       role_name,
       module_name,
       access_level,
       status,
       granted_by,
       TO_CHAR(granted_date, 'YYYY-MM-DD HH24:MI') AS granted_date
FROM   OCAPSYS.TBLACCESS
WHERE  UPPER(module_name) IN ('WIREBOND OCAP', 'WIREBOND')
ORDER  BY user_id, module_name;
