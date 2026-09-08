-- =====================================================================
-- DB Validation request log - RUN THIS ONCE against the OCAPSYS schema
-- before using the DB Validation page in the web app.
--
-- The app records every structural change request here (it still does NOT
-- execute the change - that stays a manual DBA action). Requests are saved
-- BEFORE the notification email is sent, so a mail outage can never lose a
-- request.
--
-- Column sizes below are what Data/DatabaseChangeRequestRepository.cs and
-- ViewModels/DatabaseChangeRequestViewModel.cs are written against - keep
-- them in step if you change either side.
-- =====================================================================

CREATE TABLE tbldbrequest (
    request_id     NUMBER GENERATED ALWAYS AS IDENTITY,
    request_type   VARCHAR2(30)  NOT NULL,
    target_table   VARCHAR2(80)  NOT NULL,
    reason         VARCHAR2(500) NOT NULL,
    requested_by   VARCHAR2(50)  NOT NULL,
    requested_date DATE DEFAULT SYSDATE NOT NULL,
    status         VARCHAR2(20)  DEFAULT 'PENDING' NOT NULL,
    reviewed_by    VARCHAR2(50),
    reviewed_date  DATE,
    review_note    VARCHAR2(500),
    CONSTRAINT pk_tbldbrequest PRIMARY KEY (request_id),
    CONSTRAINT ck_tbldbrequest_status
        CHECK (status IN ('PENDING', 'APPROVED', 'REJECTED')),
    CONSTRAINT ck_tbldbrequest_type
        CHECK (request_type IN ('TRUNCATE', 'ADD COLUMN', 'CREATE TABLE',
                                'INSERT NEW ROW', 'UPDATE ROW', 'DELETE ROW'))
);

CREATE INDEX ix_tbldbrequest_status ON tbldbrequest (status, requested_date DESC);

-- Quick check that it worked:
-- SELECT request_id, request_type, target_table, status FROM tbldbrequest;
