# Wirebond page (OCAPSYS.TBLWIREBOND)

The **Wirebond** page of the ATCB Assembly Recipe app: one grid over
`OCAPSYS.TBLWIREBOND`, with add, edit and delete straight from the page.

Route: `/WireBond/Index`. Sidebar: **Operations → Wirebond**.

There is exactly one Wirebond page and one Wirebond module now. The old
"Wirebond OCAP" name is gone — see *What changed* below.

## What changed in this drop

**1. The page could not build at all.** `Views/WireBond/Index.cshtml` asked for
`ModuleNames.WireBondOcap`:

```cshtml
var permission = await Access.GetAsync(ModuleNames.WireBondOcap);   // does not exist
```

`Models/AppModule.cs` never declared that constant — it only has
`TableWirebond = "Wirebond"`, which is what `WireBondController` already
enforces with `[ModuleAccess(...)]`. Razor views are compiled by the build in
.NET 6 and later, so this was a hard compile error: `dotnet build` failed, and
with no build there was no Add button, no Edit button, no Delete button and no
insert. That one line is now:

```cshtml
var permission = await Access.GetAsync(ModuleNames.TableWirebond);  // same constant the controller enforces
```

**2. "OCAP" is gone from the page.** Title, sidebar entry, panel headings,
buttons, the delete dialog and the grid headers now read *Wirebond* and
*Record No* instead of *Wirebond OCAP* and *OCAP No*. The database column names
are untouched (`WBOCAPNO`, `WBOCAPWWK`, …) and so are the CSV headers
(`OCAPNO`, `WBDATE`, …), so existing CSV files and any MES code still work.

**3. A saved row is visibly a saved row.** After a successful add or edit the
page returns to the grid sorted newest-first, flashes the row that was written
and scrolls it into view.

**4. Machine is a pick list, from AWACSWSTYPE.** The Machine field on the add
and edit panels now suggests `AWACSWSTYPE.WSID` where `WSTYPE = 'WIREBOND'`, so
a machine keyed in here matches the workstation master the recipe pages read.
It is a suggestion only — a machine that is not in AWACSWSTYPE still saves, and
if AWACSWSTYPE is empty or unreadable the list is simply empty. It never fails
the page.

**5. `Database/tblwirebond-access.sql` granted a module that does not exist.**
It wrote `MODULE_NAME = 'Wirebond OCAP'`. Nothing in the app reads that name, so
a user granted it saw nothing. It now writes `'Wirebond'`, and carries a repair
block for rows that were already inserted with the old name.

## Install

Copy these files over the project, keeping the folder structure:

| File | |
|---|---|
| `Views/WireBond/Index.cshtml` | changed — **the build fix**, plus the renaming and the row flash |
| `Views/WireBond/_WireBondFields.cshtml` | changed — renaming, Machine pick list |
| `ViewModels/WireBondViewModel.cs` | changed — `MachineOptions`, `Highlight` |
| `Controllers/WireBondController.cs` | changed — loads the machine list, redirects with `highlight` |
| `Services/WireBondService.cs` | changed — `GetMachineOptionsAsync()` |
| `Models/AppModule.cs` | changed — module description wording only |
| `Database/tblwirebond-access.sql` | changed — grants `'Wirebond'`, repairs `'Wirebond OCAP'` |
| `Database/tblwirebond-check.sql` | new — diagnose a save that did not appear |

Then:

1. `dotnet build`
2. `dotnet run` (or F5 in Visual Studio)
3. Sign in and open **Operations → Wirebond**.

Rows already in `TBLWIREBOND` appear immediately, including any inserted by
hand in SQL Developer.

## Testing it end to end

1. Open **Operations → Wirebond**. An empty table shows *No TBLWIREBOND records
   found* with an **Add the first record** button.
2. Click **Add Wirebond Record**. Fill **Record No** and **Date** — those two
   are required, everything else is optional.
3. **Save Wirebond Record**. A green popup names the row and the work week the
   database calculated, and the grid comes back with that row at the top,
   flashed amber.
4. Check the database sees the same thing:
   ```sql
   SELECT wbocapno, wbocapwwk, lastupdatedby, wbdate
   FROM   OCAPSYS.TBLWIREBOND
   ORDER  BY lastupdate DESC NULLS LAST
   FETCH FIRST 5 ROWS ONLY;
   ```
5. Click the pencil on that row, change a field, **Save Changes** — same flash,
   and the old values are on the Change History page.
6. Click the bin, type `DELETE` in the dialog, confirm. The row goes to Trash
   and can be restored from the Recycle Bin.

If a save does not appear, run `Database/tblwirebond-check.sql`. Block 1 answers
the only question that matters first: is the row in the table? If it is, the
problem is page permissions (block 5). If it is not, the insert never
committed (blocks 2–4).

## Permissions

A Super Admin needs no grant: `AccessEvaluator.GetAsync` returns full permission
to the SuperAdmin role before it ever looks at `TBLACCESS`.

Everybody else needs one `TBLACCESS` row with `MODULE_NAME = 'Wirebond'` —
tick the boxes on the **Wirebond** row in Access Management, or run
`Database/tblwirebond-access.sql`. The tier decides which buttons render *and*
which POSTs the server accepts:

| ACCESS_LEVEL | View | Add | Edit | Delete |
|---|---|---|---|---|
| `READ` | yes | | | |
| `WRITE` | yes | yes | yes | |
| `ADMIN` | yes | yes | yes | yes |

Hiding a button is presentation only; every action carries
`[ModuleAccess(ModuleNames.TableWirebond, …)]` as the real check.

## What the page does

- **Grid**: Record No, Work Week, Date, Last Updated By, Timestamp. Sortable on
  every column, searchable across record no, work week, machine, package,
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
- **Validation**: Record No required; Date required and not in the future;
  `Defect = OTHERS` requires Defect (Others); a duplicate record no is refused
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
UPDATE, so editing a saved record's date leaves the work week on the week the
record was first raised.

**The trigger can block every insert.** It calls `get_wwk_app_cutoff`. If that
function is missing from the schema, or not granted to it, the trigger is
INVALID and every insert fails with ORA-04098 — which the page reports in a red
popup. Block 4 of `Database/tblwirebond-check.sql` installs a fail-safe version
that calls the function through dynamic SQL and falls back to the ISO week of
the row's own date, so a broken work-week function can never cost you a record.

## Not compiled here

Written against the source; the container had no .NET 10 SDK to build with.
Every field name, bind variable, column name, reader index and Razor construct
was cross-checked by hand against the table DDL, the models and the existing
pages — but run `dotnet build` before trusting it.
