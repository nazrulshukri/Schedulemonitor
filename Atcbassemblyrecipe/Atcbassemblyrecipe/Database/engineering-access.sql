-- =====================================================================
-- Give somebody the Engineering page, and put them in a group.
--
-- TWO grants, not one:
--
--   'Engineering'  opens the page and decides Add / Edit / Delete.
--   'Sawing' | 'Wirebond' | 'Marker'
--                  decides WHICH RECIPE COLUMNS they see inside it.
--                  These are the same three module grants that already
--                  gate the Sawing, Wirebond and Marker pages - there is
--                  no separate "engineering group" to administer.
--
-- So a sawing engineer needs 'Engineering' + 'Sawing'. With only
-- 'Engineering' they can open the page and will see the lot's identity
-- (No, Requestor, Lot Number, Package, Product, ADAT) and no recipes at
-- all - which is what the page tells them.
--
-- WHO NEEDS THIS: only users who are NOT Super Admins.
-- AccessEvaluator.GetAsync returns ModulePermission.Full to any user in
-- the SuperAdmin role before it ever reads TBLACCESS, so a Super Admin
-- sees the page and every column with no grant at all.
--
-- Easier than running this: open Access Management in the app and tick
-- the boxes on the Engineering row and on the group's row. It writes
-- exactly the rows below. Use this script when the app cannot be
-- reached, or to grant a batch of users at once.
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

-- A sawing engineer: may key in engineering lots, sees the SAWING recipe
-- columns only.
INSERT INTO OCAPSYS.TBLACCESS
    (USER_ID, USER_NAME, ROLE_NAME, MODULE_NAME, ACCESS_LEVEL,
     GRANTED_BY, GRANTED_DATE, EXPIRY_DATE, STATUS, CREATED_DATE, UPDATED_DATE)
VALUES
    ('NX021557', 'Operator Name', 'User', 'Engineering', 'WRITE',
     'NX487878', SYSDATE, NULL, 'ACTIVE', SYSDATE, SYSDATE);

INSERT INTO OCAPSYS.TBLACCESS
    (USER_ID, USER_NAME, ROLE_NAME, MODULE_NAME, ACCESS_LEVEL,
     GRANTED_BY, GRANTED_DATE, EXPIRY_DATE, STATUS, CREATED_DATE, UPDATED_DATE)
VALUES
    ('NX021557', 'Operator Name', 'User', 'Sawing', 'READ',
     'NX487878', SYSDATE, NULL, 'ACTIVE', SYSDATE, SYSDATE);

COMMIT;


-- Somebody who works across two groups simply holds both grants and sees
-- the union of the two column sets:
-- INSERT INTO OCAPSYS.TBLACCESS
--     (USER_ID, USER_NAME, ROLE_NAME, MODULE_NAME, ACCESS_LEVEL,
--      GRANTED_BY, GRANTED_DATE, EXPIRY_DATE, STATUS, CREATED_DATE, UPDATED_DATE)
-- VALUES
--     ('NX021557', 'Operator Name', 'User', 'Marker', 'READ',
--      'NX487878', SYSDATE, NULL, 'ACTIVE', SYSDATE, SYSDATE);
-- COMMIT;


-- Change an existing grant rather than adding a second row for the same
-- user and module.
-- UPDATE OCAPSYS.TBLACCESS
--    SET access_level = 'ADMIN',
--        granted_by   = 'NX487878',
--        granted_date = SYSDATE,
--        updated_date = SYSDATE
--  WHERE user_id = 'NX021557'
--    AND UPPER(module_name) = 'ENGINEERING';
-- COMMIT;


-- Who can see what on the Engineering page. A user with no row here has
-- no access to the module - the ACCOUNT row carries their overall role
-- and status, not module access.
SELECT user_id,
       user_name,
       role_name,
       module_name,
       access_level,
       status
FROM   OCAPSYS.TBLACCESS
WHERE  UPPER(module_name) IN ('ENGINEERING', 'SAWING', 'WIREBOND', 'MARKER')
ORDER  BY user_id, module_name;
