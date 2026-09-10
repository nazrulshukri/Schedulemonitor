-- =====================================================================
-- Give somebody the "Wirebond" module (the TBLWIREBOND page).
--
-- WHO NEEDS THIS: only users who are NOT Super Admins.
-- AccessEvaluator.GetAsync returns ModulePermission.Full to any user in the
-- SuperAdmin role before it ever looks at TBLACCESS, so a Super Admin sees
-- the page and its Add/Edit/Delete buttons with no grant at all.
--
-- Easier than running this: open Access Management in the app and tick the
-- boxes on the "Wirebond" row. It writes exactly the row below. Use this
-- script when the app cannot be reached, or to grant a batch of users at
-- once.
--
-- MODULE_NAME must be spelled exactly 'Wirebond' - it is compared against
-- ModuleNames.TableWirebond in Models/AppModule.cs. 'WIREBOND' also matches
-- (the lookup is case-insensitive) but keep the app spelling so the Access
-- Management grid shows one row instead of two.
--
-- THERE IS NO 'Wirebond OCAP' MODULE. An earlier draft of this script told
-- you to grant that name; nothing in the app ever reads it, so a user with
-- only a 'Wirebond OCAP' row sees no Wirebond page and no Add/Edit/Delete
-- buttons. The last block below repairs any such rows.
--
-- ACCESS_LEVEL is one tier per row, not a list (Models/AccessGrant.cs):
--   READ   view only
--   WRITE  view + add + update
--   ADMIN  view + add + update + delete
--   NONE   nothing
--
-- Run the INSERT and the COMMIT as SEPARATE statements, or the client
-- reports ORA-00933 against the COMMIT line.
-- =====================================================================

-- One user, full access (view + add + update + delete).
INSERT INTO OCAPSYS.TBLACCESS
    (USER_ID, USER_NAME, ROLE_NAME, MODULE_NAME, ACCESS_LEVEL,
     GRANTED_BY, GRANTED_DATE, EXPIRY_DATE, STATUS, CREATED_DATE, UPDATED_DATE)
VALUES
    ('NX021557', 'Operator Name', 'User', 'Wirebond', 'ADMIN',
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
--    AND UPPER(module_name) = 'WIREBOND';
-- COMMIT;


-- REPAIR: fold any old 'Wirebond OCAP' grants into the real 'Wirebond'
-- module, keeping the tier that was already granted. Safe to re-run.
-- UPDATE OCAPSYS.TBLACCESS a
--    SET a.module_name  = 'Wirebond',
--        a.updated_date = SYSDATE
--  WHERE UPPER(a.module_name) = 'WIREBOND OCAP'
--    AND NOT EXISTS (SELECT 1
--                    FROM   OCAPSYS.TBLACCESS b
--                    WHERE  b.user_id = a.user_id
--                      AND  UPPER(b.module_name) = 'WIREBOND');
-- COMMIT;

-- Then drop the leftovers of users who already had both.
-- DELETE FROM OCAPSYS.TBLACCESS
--  WHERE UPPER(module_name) = 'WIREBOND OCAP';
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
WHERE  UPPER(module_name) IN ('WIREBOND', 'WIREBOND OCAP')
ORDER  BY user_id, module_name;
