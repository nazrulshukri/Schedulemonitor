-- =====================================================================
-- Undo storage for the recipe workspace - RUN THIS ONCE against the
-- OCAPSYS schema before using Edit or Delete in the web app.
--
-- TBLRECIPEHISTORY  what a row looked like BEFORE each update, so a wrong
--                   update can be reverted from Settings > Change History.
-- TBLRECIPETRASH    rows that were deleted, so Settings > Trash can put
--                   them back. Delete never removes a row outright: the
--                   copy into TBLRECIPETRASH and the DELETE happen in one
--                   transaction, and if the copy fails the delete is
--                   rolled back with it.
--
-- Both cover AWACSWSTYPE, AWACSRECIPEBYWSTYPE and AWACSLF. The snapshot is
-- the whole row as JSON, read from the data dictionary at run time, so
-- adding a column to one of those MES tables needs no change here.
--
-- Until this script has been run, the app refuses updates and deletes with
-- a message pointing back at it rather than losing the old values.
-- =====================================================================

CREATE TABLE tblrecipehistory (
    history_id    NUMBER GENERATED ALWAYS AS IDENTITY,
    table_name    VARCHAR2(30)  NOT NULL,
    target_label  VARCHAR2(200) NOT NULL,
    row_key       VARCHAR2(64)  NOT NULL,
    old_values    CLOB          NOT NULL,
    new_values    CLOB          NOT NULL,
    changed_by    VARCHAR2(50)  NOT NULL,
    changed_date  DATE DEFAULT SYSDATE NOT NULL,
    reverted      CHAR(1) DEFAULT 'N' NOT NULL,
    reverted_by   VARCHAR2(50),
    reverted_date DATE,
    CONSTRAINT pk_tblrecipehistory PRIMARY KEY (history_id),
    CONSTRAINT ck_tblrecipehistory_table
        CHECK (table_name IN ('AWACSWSTYPE', 'AWACSRECIPEBYWSTYPE', 'AWACSLF')),
    CONSTRAINT ck_tblrecipehistory_reverted
        CHECK (reverted IN ('Y', 'N'))
);

CREATE INDEX ix_tblrecipehistory_open
    ON tblrecipehistory (reverted, changed_date DESC);

CREATE TABLE tblrecipetrash (
    trash_id      NUMBER GENERATED ALWAYS AS IDENTITY,
    table_name    VARCHAR2(30)  NOT NULL,
    target_label  VARCHAR2(200) NOT NULL,
    row_data      CLOB          NOT NULL,
    deleted_by    VARCHAR2(50)  NOT NULL,
    deleted_date  DATE DEFAULT SYSDATE NOT NULL,
    restored      CHAR(1) DEFAULT 'N' NOT NULL,
    restored_by   VARCHAR2(50),
    restored_date DATE,
    CONSTRAINT pk_tblrecipetrash PRIMARY KEY (trash_id),
    CONSTRAINT ck_tblrecipetrash_table
        CHECK (table_name IN ('AWACSWSTYPE', 'AWACSRECIPEBYWSTYPE', 'AWACSLF')),
    CONSTRAINT ck_tblrecipetrash_restored
        CHECK (restored IN ('Y', 'N'))
);

CREATE INDEX ix_tblrecipetrash_open
    ON tblrecipetrash (restored, deleted_date DESC);

-- Quick check that it worked:
-- SELECT table_name, COUNT(1) FROM tblrecipehistory GROUP BY table_name;
-- SELECT table_name, COUNT(1) FROM tblrecipetrash   GROUP BY table_name;
