# Inserting values into OCAPSYS.TBLWIREBOND

Two scripts:

| File | What it does |
|---|---|
| `tblwirebond.sql` | Creates the table and the work-week trigger. Run once. |
| `tblwirebond-insert.sql` | Insert patterns: single row, full row, many rows, bind variables, bulk from staging, verify, correct. |

These are for the `Atcbassemblyrecipe` app's Oracle schema, so they also belong in
that project under `Atcbassemblyrecipe/Database/` alongside `tblappsetting.sql` and
`tbldbrequest.sql`.

## The short answer

```sql
INSERT INTO OCAPSYS.TBLWIREBOND
    (TBLROWID, LASTUPDATE, LASTUPDATEDBY,
     WBOCAPNO, WBISSUEDBY, WBBFG, WBDATE,
     WBOPERATORID, WBPROCESS, WBMACHINE, WBPACKAGE, WBSOQTY,
     WBDEFECT, WBDEFECTCAT)
VALUES
    (RAWTOHEX(SYS_GUID()), SYSDATE, 'NX487878',
     'OCAP-WB-2026-001', 'NX487878', 'BFG1',
     TO_DATE('2026-09-10 14:30', 'YYYY-MM-DD HH24:MI'),
     'OP1023', 'WIREBOND', 'WB-07', 'SOT669', 3000,
     'NON STICK ON PAD', 'PROCESS');

COMMIT;
```

Three things decide whether an insert into this table works:

1. **`TBLROWID` is an ordinary `VARCHAR2(50)` column, not Oracle's `ROWID`
   pseudo-column.** Fill it with `RAWTOHEX(SYS_GUID())` — 32 hex characters, which is
   what `AwacsWstypeService.CreateAsync` already does for `AWACSWSTYPE`. Never put
   `ROWID` in the column list; Oracle assigns that itself and will reject it.
2. **Leave `WBOCAPWWK` out of the statement.** The `BEFORE INSERT` trigger
   `OCAP_WIREBOND_WORKWEEK` sets it from `GET_WWK_APP_CUTOFF`. Listing it just gets
   your value replaced.
3. **`COMMIT`.** Nothing is visible to anyone else until you commit, and closing the
   session without committing rolls the insert back.

## A bug in the trigger, worth fixing before you load data

The trigger as written calls:

```sql
workweek := get_wwk_app_cutoff(to_date(sysdate, 'DD-MM-YYYY-HH24:MI:SS'));
```

`SYSDATE` is already a `DATE`. `TO_DATE` wants a string, so Oracle first converts
`SYSDATE` to text with the session's `NLS_DATE_FORMAT` (default `DD-MON-RR`, giving
`10-SEP-26`) and then tries to read that text back with the
`DD-MM-YYYY-HH24:MI:SS` mask. That raises **ORA-01843** or **ORA-01861** — and
because it happens in a `BEFORE INSERT` trigger, **the insert itself fails**. It only
appears to work on a session whose `NLS_DATE_FORMAT` happens to match the mask, which
is exactly the kind of failure that shows up on one PC and not another.

`tblwirebond.sql` ships the corrected trigger:

```sql
workweek := get_wwk_app_cutoff(NVL(:NEW.WBDATE, SYSDATE));
:NEW.WBOCAPWWK := NVL(:NEW.WBOCAPWWK, TO_CHAR(workweek));
```

- `SYSDATE` passed straight through — no string round-trip, no NLS dependency.
- `NVL(:NEW.WBDATE, SYSDATE)` derives the work week from the date the OCAP actually
  happened, so a record keyed in on Monday for Saturday's event lands in the right
  week. Use plain `SYSDATE` if the work week should always be the keying-in week.
- `TO_CHAR` is explicit about the `NUMBER` → `VARCHAR2(100)` conversion.
- The outer `NVL` keeps a work week the caller deliberately supplied. Drop it if the
  trigger should always win.

## From the web app

Follow `Services/AwacsLfService.cs`: bind every value, verify the row count inside a
transaction, and roll back on anything unexpected.

```csharp
private const string InsertSql = """
    INSERT INTO tblwirebond
        (tblrowid, lastupdate, lastupdatedby, wbocapno, wbissuedby, wbbfg, wbdate,
         wboperatorid, wbprocess, wbmachine, wbpackage, wbsoqty,
         wbdefect, wbdefectcat, wbremarks)
    VALUES
        (RAWTOHEX(SYS_GUID()), SYSDATE, :lastupdatedby, :wbocapno, :wbissuedby,
         :wbbfg, :wbdate, :wboperatorid, :wbprocess, :wbmachine, :wbpackage,
         :wbsoqty, :wbdefect, :wbdefectcat, :wbremarks)
    """;

await using var connection = _connectionFactory.CreateConnection();
await connection.OpenAsync();
await using var transaction = connection.BeginTransaction();

try
{
    await using var command = connection.CreateCommand();
    command.BindByName = true;              // required - the binds above are by name
    command.Transaction = transaction;
    command.CommandText = InsertSql;

    command.Parameters.Add(new OracleParameter("lastupdatedby", userName));
    command.Parameters.Add(new OracleParameter("wbocapno", model.OcapNo));
    command.Parameters.Add(new OracleParameter("wbissuedby", (object?)model.IssuedBy ?? DBNull.Value));
    command.Parameters.Add(new OracleParameter("wbbfg", (object?)model.Bfg ?? DBNull.Value));
    command.Parameters.Add(new OracleParameter("wbdate", OracleDbType.Date)
    {
        Value = model.WbDate.HasValue ? (object)model.WbDate.Value : DBNull.Value
    });
    command.Parameters.Add(new OracleParameter("wboperatorid", (object?)model.OperatorId ?? DBNull.Value));
    command.Parameters.Add(new OracleParameter("wbprocess", "WIREBOND"));
    command.Parameters.Add(new OracleParameter("wbmachine", (object?)model.Machine ?? DBNull.Value));
    command.Parameters.Add(new OracleParameter("wbpackage", (object?)model.Package ?? DBNull.Value));
    command.Parameters.Add(new OracleParameter("wbsoqty", OracleDbType.Int32)
    {
        Value = model.SoQty.HasValue ? (object)model.SoQty.Value : DBNull.Value
    });
    command.Parameters.Add(new OracleParameter("wbdefect", (object?)model.Defect ?? DBNull.Value));
    command.Parameters.Add(new OracleParameter("wbdefectcat", (object?)model.DefectCategory ?? DBNull.Value));
    command.Parameters.Add(new OracleParameter("wbremarks", (object?)model.Remarks ?? DBNull.Value));

    if (await command.ExecuteNonQueryTracedAsync() != 1)
    {
        await transaction.RollbackAsync();
        return (false, "Insert verification failed. Insert was rolled back.");
    }

    await transaction.CommitAsync();
    return (true, "TBLWIREBOND row inserted and verified.");
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

Notes for wiring it up the rest of the way:

- Bind a real `DateTime` for `WBDATE` (`OracleDbType.Date`). Do not send
  `"10/09/2026"` as text — that is where ORA-01861 comes from.
- `WBSOQTY` and `WBDIFFREJECTQTY` are `INTEGER`; bind them as numbers, and don't send
  `""` for an empty box — send `DBNull.Value`.
- Uppercase and trim the code-like fields with `InputText.CleanUpper` /
  `CleanUpperOrNull`, the way `AwacsLfService.Normalize` does, so `wb-07` and `WB-07`
  don't both end up in the table.
- The audit/undo side (`RecipeAuditRepository`) only knows the three tables listed in
  `AuditedTable.For`. If wire bond rows should be restorable from the Recycle Bin,
  add `TBLWIREBOND` there with the key predicate `tblrowid = :row_key` — the same
  form `AWACSWSTYPE` uses, since this table has a real `TBLROWID` column.

## Watch out

- The table has **no primary key, no unique constraint and no NOT NULL column**.
  Nothing stops a duplicate `WBOCAPNO` or a completely empty row, and an `UPDATE ...
  WHERE wbocapno = ...` can silently touch several rows. `tblwirebond.sql` has the
  constraint statements ready, commented out.
- The work-week trigger is `BEFORE INSERT` only. An `UPDATE` that changes `WBDATE`
  does **not** recompute `WBOCAPWWK`.
- `GET_WWK_APP_CUTOFF` must be visible to the `OCAPSYS` schema or the trigger goes
  invalid (ORA-04098) and every insert fails.
