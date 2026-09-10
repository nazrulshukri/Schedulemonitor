# Wirebond OCAP page (OCAPSYS.TBLWIREBOND)

A new page for the ATCB Assembly Recipe app that reads and writes
`OCAPSYS.TBLWIREBOND` — the wire bond OCAP log. The existing **Wirebond** page is
unchanged: it stays the WIREBOND *recipe* grid over `AWACSRECIPEBYWSTYPE`. Two
tables, two modules, two grants.

Route: `/WireBond/Index`. Sidebar: **Operations → Wirebond OCAP**.

## Install

Copy the files over the project, keeping the folder structure:

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
| `Database/tblwirebond-access.sql` | new |
| `Program.cs` | changed — one DI line |
| `Models/AppModule.cs` | changed — new module name + catalog entry |
| `Models/AuditTrail.cs` | changed — `TBLWIREBOND` added to the audited tables |
| `Data/RecipeAuditRepository.cs` | changed — how a TBLWIREBOND row is addressed |
| `Views/Shared/_Layout.cshtml` | changed — sidebar link + the menu fix |

`wirebond-ocap.patch` is the same five changed files as a diff against the
original project, if you would rather review the changes than overwrite.

Then:

1. Run `Database/tblwirebond.sql` once, if TBLWIREBOND and its trigger are not
   already in place. If the table already holds rows, run only the
   `CREATE OR REPLACE TRIGGER` part — the corrected trigger matters, see below.
2. `dotnet build`, then run the app.
3. Sign in and open **Operations → Wirebond OCAP**. Rows already in TBLWIREBOND
   appear immediately, including any you inserted by hand.

No `TBLACCESS` row is needed for a Super Admin: `AccessEvaluator.GetAsync`
returns full permission to the SuperAdmin role before it looks at TBLACCESS.
Everyone else needs a grant — tick the boxes on the **Wirebond OCAP** row in
Access Management, or run `Database/tblwirebond-access.sql`.

## What the page does

- **Grid**: OCAP No, Work Week, OCAP Date, Last Updated By, Timestamp. Sortable
  on every column, searchable across OCAP no, work week, machine, package,
  defect, category, difference no, issued by, verified by, operator and last
  updated by. 25/50/100 rows per page.
- **Add / Edit**: all 25 writable columns in one full-width panel, grouped as
  identity → defect → difference → closure. The 28-column table will not fit as
  inline cells, so the panel is one `<td colspan="9">` — the `excel-grid.js`
  contract (`#addRow`, `[data-row-id]`, `[data-edit-panel]`, `[data-edit-row]`,
  `[data-cancel-edit]`) is otherwise unchanged, and column resizing, the CSV
  picker, the template modal and the DELETE-phrase confirmation all still work.
- **Delete**: moves the row to Trash first, so it can be restored from the
  Recycle Bin — the same undo path as the other grids.
- **CSV**: template download, export of all 28 columns, and import. Required
  columns are `OCAPNO` and `WBDATE`; a header row is optional, and a header row
  may name the columns the template leaves out, so an export can be edited and
  uploaded again. Dates are parsed against a fixed list of formats, never the
  server locale, so `10/09/2026` cannot mean September on one machine and
  October on another.
- **Validation**: OCAP No required; OCAP Date required and not in the future;
  `Defect = OTHERS` requires Defect (Others); a duplicate OCAP No is refused
  (the table has no unique constraint, so this check is the only guard), and a
  number repeated inside one CSV file cancels the import.

## Three things about this table worth knowing

**Rows are addressed by Oracle ROWID, not by `TBLROWID`.** `TBLWIREBOND` has a
`TBLROWID` column, but it is NULL on every row keyed in before this page
existed. Keying off it would have left exactly the rows you already inserted
uneditable, so the page uses `ROWID` — the same handle `AWACSLF` and
`AWACSRECIPEBYWSTYPE` already use. New rows still get a `RAWTOHEX(SYS_GUID())`
`TBLROWID` for the MES side.

**`WBOCAPWWK` is never written by the app.** The `BEFORE INSERT` trigger
`OCAP_WIREBOND_WORKWEEK` derives it, so the form shows it read-only and the
success popup reads back what the database chose. That trigger does not fire on
UPDATE, so editing a saved record's OCAP date leaves the work week on the week
the record was first raised — which is what the OCAP number belongs to.

**The trigger as originally written fails every insert on most sessions.** It
called `get_wwk_app_cutoff(to_date(sysdate, 'DD-MM-YYYY-HH24:MI:SS'))`, which
converts a DATE to text with the session `NLS_DATE_FORMAT` and then parses it
back with a mask that does not match — ORA-01843/ORA-01861, inside a
`BEFORE INSERT` trigger, so the insert dies with it. `Database/tblwirebond.sql`
carries the corrected version.

## Not done

Not compiled — this was written against the source, and the container had no
.NET 10 SDK. Every field name, bind variable, column name and reader index was
cross-checked against the table DDL and the models, but run `dotnet build`
before trusting it.

Nothing here changes the existing Wirebond recipe page, its module, or its
grants.
