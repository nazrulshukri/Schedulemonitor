-- This script only creates the web app authentication tables.
-- Do not create AWACSWSTYPE or TBLSAWING here; the app expects those existing MES tables.
--
-- TBLACCESS is also a pre-existing MES table and is NOT created here. This is its
-- real, verified structure (confirmed against the live OCAPSYS.TBLACCESS table):
--
--   ACCESS_ID     NUMBER          PK. Identity/sequence-defaulted
--                                  (OCAPSYS.ISEQ$$_603373.nextval). Never include
--                                  it in an INSERT - Oracle assigns it.
--   USER_ID       VARCHAR2(50)    NOT NULL. LDAP sAMAccountName, e.g. NX487878.
--   USER_NAME     VARCHAR2(100)   Display name.
--   ROLE_NAME     VARCHAR2(100)   NOT NULL. SuperAdmin / Admin / User
--                                  (see Models/AppRole.cs). No DB check constraint.
--   MODULE_NAME   VARCHAR2(100)   Either the reserved value 'ACCOUNT' (one row per
--                                  user carrying that user's overall ROLE_NAME and
--                                  STATUS - this is the row login checks), or one
--                                  of the module names in Models/AppModule.cs
--                                  (AWACSWSTYPE, Table Sawing, DB Validation,
--                                  Access Management) carrying that module's
--                                  ACCESS_LEVEL.
--   ACCESS_LEVEL  VARCHAR2(20)    CHECK IN ('READ','WRITE','ADMIN','NONE').
--                                  One tier per row, not a combinable flag list -
--                                  see Models/AccessGrant.cs AccessLevel for the
--                                  View/Add/Update/Delete <-> tier mapping used by
--                                  the Access Management grid. Only meaningful on
--                                  module rows (default 'READ' otherwise unused).
--   GRANTED_BY    VARCHAR2(50)    Username of the admin (or 'LDAP-AutoProvision')
--                                  who created/last changed this grant.
--   GRANTED_DATE  DATE            Refreshed whenever this row's access is
--                                  (re)granted/changed.
--   EXPIRY_DATE   DATE            Not currently set by the app (always NULL);
--                                  reserved for future time-boxed access.
--   STATUS        VARCHAR2(20)    CHECK IN ('ACTIVE','INACTIVE','REVOKED').
--                                  Only meaningful on the ACCOUNT row. The app's
--                                  "Blocked" state is stored as REVOKED (there is
--                                  no literal BLOCKED value) - see
--                                  Models/AccessGrant.cs AccessStatus.
--   CREATED_DATE  DATE            Set once when the row is first inserted.
--   UPDATED_DATE  DATE            Refreshed on every change to the row.
--
-- A user's first successful LDAP login auto-provisions their ACCOUNT row (default
-- role User, status Active), unless their sAMAccountName is listed under
-- Security:SuperAdmins / Security:Admins in appsettings.json, which seeds that
-- one-time default role instead - this is how the very first Super Admin gets
-- created, since Access Management itself requires the Super Admin role to open.
-- Once a user's row exists, TBLACCESS is always the source of truth; the
-- appsettings lists are never consulted again for that user.

CREATE TABLE app_roles (
    role_name VARCHAR2(30) NOT NULL,
    CONSTRAINT pk_app_roles PRIMARY KEY (role_name)
);

INSERT INTO app_roles (role_name) VALUES ('SuperAdmin');
INSERT INTO app_roles (role_name) VALUES ('Admin');
INSERT INTO app_roles (role_name) VALUES ('User');

CREATE TABLE app_users (
    id NUMBER(10) NOT NULL,
    user_name VARCHAR2(80) NOT NULL,
    email VARCHAR2(160) NOT NULL,
    password_hash VARCHAR2(500) NOT NULL,
    role_name VARCHAR2(30) DEFAULT 'User' NOT NULL,
    is_active CHAR(1) DEFAULT 'Y' NOT NULL,
    created_at TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL,
    CONSTRAINT pk_app_users PRIMARY KEY (id),
    CONSTRAINT uq_app_users_user_name UNIQUE (user_name),
    CONSTRAINT uq_app_users_email UNIQUE (email),
    CONSTRAINT fk_app_users_role FOREIGN KEY (role_name) REFERENCES app_roles (role_name),
    CONSTRAINT ck_app_users_active CHECK (is_active IN ('Y', 'N'))
);

CREATE SEQUENCE app_users_seq START WITH 1 INCREMENT BY 1 NOCACHE;
