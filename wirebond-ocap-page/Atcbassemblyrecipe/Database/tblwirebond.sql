-- =====================================================================
-- OCAPSYS.TBLWIREBOND - table + work-week trigger.
-- RUN THIS ONCE against the OCAPSYS schema.
--
-- One row = one wire bond OCAP record: who raised it, the machine and
-- package it was raised against, the defect, the 4M1E difference that
-- caused it, and the disposition/action taken to close it.
--
-- The DROP below is destructive. On a database that already holds
-- production rows, skip straight to the CREATE OR REPLACE TRIGGER part,
-- or export the rows first:
--   CREATE TABLE tblwirebond_bak AS SELECT * FROM tblwirebond;
-- =====================================================================

DROP TABLE OCAPSYS.TBLWIREBOND CASCADE CONSTRAINTS;

CREATE TABLE OCAPSYS.TBLWIREBOND
(
  TBLROWID           VARCHAR2(50 BYTE),    -- row key: RAWTOHEX(SYS_GUID()), same as AWACSWSTYPE
  LASTUPDATE         DATE,                 -- SYSDATE on every write
  LASTUPDATEDBY      VARCHAR2(50 BYTE),    -- sAMAccountName, e.g. NX487878
  WBOCAPNO           VARCHAR2(100 BYTE),   -- OCAP number - the business identifier of the record
  WBOCAPWWK          VARCHAR2(100 BYTE),   -- work week: SET BY THE TRIGGER, never insert it yourself
  WBISSUEDBY         VARCHAR2(100 BYTE),
  WBBFG              VARCHAR2(100 BYTE),
  WBDATE             DATE,                 -- when the OCAP happened (not when the row was keyed in)
  WBOPERATORID       VARCHAR2(50 BYTE),
  WBPROCESS          VARCHAR2(100 BYTE),
  WBMACHINE          VARCHAR2(100 BYTE),
  WBPACKAGE          VARCHAR2(100 BYTE),
  WBSOQTY            INTEGER,
  WBDEFECT           VARCHAR2(100 BYTE),
  WBDEFECTCAT        VARCHAR2(100 BYTE),
  WBDEFECTOTHERS     VARCHAR2(100 BYTE),
  WBDIFF4M1E         VARCHAR2(100 BYTE),
  WBDIFFAFFECTED     VARCHAR2(100 BYTE),
  WBDIFFFABSITE      VARCHAR2(100 BYTE),
  WBDIFFNO           VARCHAR2(100 BYTE),
  WBDIFFNOTAFFECTED  VARCHAR2(100 BYTE),
  WBDIFFREJECTQTY    INTEGER,
  WBDIFFREMARKS      VARCHAR2(2000 BYTE),
  WBVERIFIEDBY       VARCHAR2(100 BYTE),
  WBACTIONTAKEN      VARCHAR2(2000 BYTE),
  WBDISPOSITION      VARCHAR2(2000 BYTE),
  WBREMARKS          VARCHAR2(500 BYTE),
  WBRCMACHINEERROR   VARCHAR2(1000 BYTE),
  WBMACHINEERROR     VARCHAR2(100 BYTE)
)
TABLESPACE OCAPSYS_DAT
PCTFREE    10
INITRANS   1
MAXTRANS   255
STORAGE    (
            INITIAL          64K
            NEXT             1M
            MINEXTENTS       1
            MAXEXTENTS       UNLIMITED
            PCTINCREASE      0
            BUFFER_POOL      DEFAULT
           )
LOGGING
NOCOMPRESS
NOCACHE;

-- ---------------------------------------------------------------------
-- Work-week trigger
--
-- Fixed against the original version, which called
--   get_wwk_app_cutoff(to_date(sysdate, 'DD-MM-YYYY-HH24:MI:SS'))
-- SYSDATE is already a DATE. TO_DATE wants a string, so Oracle first
-- converts SYSDATE to text using the session's NLS_DATE_FORMAT (default
-- 'DD-MON-RR' -> '10-SEP-26') and then tries to read that text back with
-- the 'DD-MM-YYYY-HH24:MI:SS' mask. That raises ORA-01843 / ORA-01861 and
-- FAILS THE WHOLE INSERT on any session whose NLS_DATE_FORMAT does not
-- happen to match the mask. Passing SYSDATE straight through is both
-- correct and NLS-independent.
--
-- TO_CHAR is explicit about the NUMBER -> VARCHAR2 column conversion, and
-- NVL keeps a work week that the caller supplied on purpose (a backdated
-- record being keyed in late) instead of overwriting it with this week.
-- Drop the NVL if the trigger should always win.
-- ---------------------------------------------------------------------
CREATE OR REPLACE TRIGGER OCAPSYS.OCAP_WIREBOND_WORKWEEK
BEFORE INSERT
ON OCAPSYS.TBLWIREBOND
FOR EACH ROW
DECLARE
   workweek NUMBER;
BEGIN
   workweek := get_wwk_app_cutoff(NVL(:NEW.WBDATE, SYSDATE));
   :NEW.WBOCAPWWK := NVL(:NEW.WBOCAPWWK, TO_CHAR(workweek));
END;
/

-- ---------------------------------------------------------------------
-- Optional, recommended. The table as created has no primary key, no
-- NOT NULL and no unique constraint, so nothing stops a duplicate or an
-- all-NULL row. Run these only after confirming the existing data is
-- clean, and only with DBA agreement:
--
-- ALTER TABLE OCAPSYS.TBLWIREBOND MODIFY (TBLROWID NOT NULL);
-- ALTER TABLE OCAPSYS.TBLWIREBOND
--   ADD CONSTRAINT pk_tblwirebond PRIMARY KEY (TBLROWID);
-- CREATE UNIQUE INDEX ix_tblwirebond_ocapno
--   ON OCAPSYS.TBLWIREBOND (WBOCAPNO);
-- CREATE INDEX ix_tblwirebond_wwk
--   ON OCAPSYS.TBLWIREBOND (WBOCAPWWK, WBDATE);
-- ---------------------------------------------------------------------

-- Quick check that it worked:
-- SELECT table_name FROM all_tables WHERE table_name = 'TBLWIREBOND';
-- SELECT trigger_name, status FROM all_triggers
--  WHERE trigger_name = 'OCAP_WIREBOND_WORKWEEK';
