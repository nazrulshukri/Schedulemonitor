# Wirebond page (OCAPSYS.TBLWIREBOND)

**One** Wirebond page, reading `OCAPSYS.TBLWIREBOND`. Route `/WireBond`, sidebar
**Operations → Wirebond**, permissions from the existing `Wirebond` module in
Access Management — no second menu entry, no second grant.

The old recipe grid over `AWACSRECIPEBYWSTYPE` (`/Awacs/TableWirebond`) is no
longer in the menu. Its action, view and module check are untouched, so the URL
still works and the link can be put back by reverting one block in
`_Layout.cshtml`.

## Install

Copy over the project, keeping the folder structure, then `dotnet build`.

| File | |
|---|---|
| `Models/WireBond.cs` | new |
| `ViewModels/WireBondInputModel.cs` | new |
| `ViewModels/WireBondViewModel.cs` | new |
| `ViewModels/WireBondPanelModel.cs` | new |
| `Services/WireBondService.cs` | new |
| `Controllers/WireBondController.cs` | new |
| `Views/WireBond/Index.cshtml` | new |
| `Views/WireBond/_WireBondFields.cshtml` | new |
| `Program.cs` | changed — one DI line |
| `Models/AppModule.cs` | changed — the `Wirebond` module now describes TBLWIREBOND |
| `Models/AuditTrail.cs` | changed — `TBLWIREBOND` added to the audited tables |
| `Data/RecipeAuditRepository.cs` | changed — how a TBLWIREBOND row is addressed |
| `Controllers/DiagnosticsController.cs` | changed — adds `/Diagnostics/WireBond` |
| `Views/Shared/_Layout.cshtml` | changed — Wirebond opens this page; OCAP entry removed |

`wirebond-ocap.patch` is the six changed files as a diff against the original
project, if you would rather review than overwrite.

## If the grid says "No TBLWIREBOND records found"

Open **`https://localhost:7029/Diagnostics/WireBond`** (Super Admin only). It
prints, as plain text, what the *app's own* Oracle session can see:

```
=== Who the app is connected as ===
user=OCAPSYS  db=...  service=QAAPPS  schema=OCAPSYS
=== Which TBLWIREBOND the name resolves to ===
owner=OCAPSYS  type=TABLE
=== Rows this session can see ===
committed rows = 0
=== Newest 5 ===
(none)
```

Read it like this:

- **`committed rows = 0` while Toad shows rows** → the rows are still
  **uncommitted** in the Toad session. Toad's Data tab shows its own session's
  pending inserts; no other session can see them, the app included. Press
  Commit in Toad (F10, or run `COMMIT;` in the editor) and reload the page.
  This is the usual cause.
- **`user=` or `service=` different from the connection you inserted from** →
  the app is reading a different schema or database. Line up
  `appsettings.json` (or `appsettings.Development.json`, which overrides it)
  with the connection your client uses.
- **`committed rows = 2` but the grid is still empty** → the query is failing.
  The page now shows the real Oracle error, see below.
- **`FAILED with ORA-…`** → that is the answer; the number says which.

### Oracle errors are no longer masked

`DatabaseErrorMessage.Build` answers every `OracleException` with the same
"check the password, VPN and service name" text. On a connection failure that
is right; on a query error it is actively misleading — an `ORA-00904` from this
page's SQL read as if the database were unreachable, and the grid just looked
empty. `WireBondController.Describe` now keeps that wording for real connection
errors (ORA-01017, 12154, 12541 …) and reports anything else as Oracle stated
it: `ORA-00904: "WBFOO": invalid identifier`.

## What the page does

- **Grid**: OCAP No, Work Week, OCAP Date, Last Updated By, Timestamp — all
  sortable. Search covers OCAP no, work week, machine, package, defect,
  category, difference no, issued by, verified by, operator and last updated by.
  25/50/100 rows per page.
- **Add / Edit**: all 25 writable columns in one full-width panel, grouped
  identity → defect → difference → closure.
- **Delete**: moves the row to Trash first, so the Recycle Bin can restore it.
- **CSV**: template, export of all 28 columns, import. `OCAPNO` and `WBDATE`
  required; header row optional; dates parsed against a fixed format list, never
  the server locale.
- **Validation**: OCAP No required, OCAP Date required and not in the future,
  `Defect = OTHERS` needs Defect (Others), duplicate OCAP No refused.

## Three things about this table

**Rows are addressed by Oracle ROWID, not `TBLROWID`.** That column is NULL on
rows keyed in before this page existed; keying off it would leave exactly those
rows uneditable. New rows still get `RAWTOHEX(SYS_GUID())`.

**`WBOCAPWWK` is never written by the app.** The `BEFORE INSERT` trigger
`OCAP_WIREBOND_WORKWEEK` derives it, so the form shows it read-only. That
trigger does not fire on UPDATE, so editing a saved OCAP date leaves the work
week on the week the record was raised.

**The trigger as originally written fails inserts on most sessions.** It called
`get_wwk_app_cutoff(to_date(sysdate, 'DD-MM-YYYY-HH24:MI:SS'))` — a DATE turned
into text with the session `NLS_DATE_FORMAT` and parsed back with a mask that
does not match, so ORA-01843/ORA-01861 inside a `BEFORE INSERT` trigger kills
the insert. `Database/tblwirebond.sql` has the corrected version.

## Not done

Not compiled — no .NET 10 SDK in the environment this was written in. Every
field name, bind variable, column name and reader index was cross-checked
against the table DDL and the models; run `dotnet build` before trusting it.
