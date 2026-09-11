# Wirebond page (OCAPSYS.TBLWIREBOND)

The **Wirebond** page: one recipe grid over `OCAPSYS.TBLWIREBOND`, the same
shape as the Sawing and Marker grids.

Route: `/WireBond/Index`. Sidebar: **Operations → Wirebond**.

The columns:

| | # | WSTYPE | Machine | PACKAGE | Product | Leadframe 12NC | Recipe | Last Updated By | Timestamp | Edit | Delete |
|---|---|---|---|---|---|---|---|---|---|---|---|

## AWACSWSTYPE is the parent, TBLWIREBOND is the child

`AWACSWSTYPE` holds one row per machine, keyed by `WSID`. `TBLWIREBOND` holds
one row per recipe, and **Machine** is that recipe's `WSID` — the child's
reference back to the parent, the same way `TBLSAWING.SAWMACHINE` points at a
`WSID`.

Run `Database/tblwirebond-wsid.sql` to add the column. After that:

- The Machine cell is a dropdown of the wire bonders registered in AWACSWSTYPE
  (`WSTYPE = 'WIREBOND'`). Register them first — Operations → AWACSWSTYPE → Add
  Row — or the grid tells you there is nothing to pick.
- **The reference is checked in the database on every save**, not just in the
  dropdown: add, edit and CSV import all refuse a machine that is not registered
  as a wire bonder, so a hand-made POST cannot write a dangling row either.
- Step 4 of that script adds a real foreign key as well, if AWACSWSTYPE has no
  duplicate WSIDs. Worth doing — but keep the app check too, because a foreign
  key can only say the WSID exists, not that its WSTYPE is WIREBOND.

Saving a recipe still does not *write* to AWACSWSTYPE, and should not — the
machine record did not change. What changed is that the recipe now names its
machine, so the two tables can be joined.

## A correction: WSDB is not a table name

`AWACSWSTYPE.WSDB` is the machine's **business group** — `SENSORS`, `POWER` —
matching `SAWBFG` in TBLSAWING. An earlier version of this app treated it as a
table name and wrote `TBLSAWING` into it, and for one build derived it from
WSTYPE on every save, which overwrote the real value whenever an existing row
was edited.

That is gone. WSDB is a normal field on the AWACSWSTYPE grid now, with the
values already in the table offered as a pick list.
`Database/awacswstype-wsdb-repair.sql` finds the rows that were written wrongly
and suggests the right value from TBLSAWING.

**WSTYPE always reads `WIREBOND` and cannot be edited** — it is a read-only cell
on the add row and on every edit row, exactly like `SAWING` on the Sawing grid.

It is *not* a column in TBLWIREBOND. Every row in this table is a wirebond row
by definition, so storing the same word on all of them would buy nothing and
give somebody a way to set it wrong. The page states the value instead. Nothing
posts it, so nothing can change it.

If you do want it in the table — say the MES joins on it — one statement does it,
and the page keeps working either way because it never reads the column:

```sql
ALTER TABLE OCAPSYS.TBLWIREBOND ADD (WSTYPE VARCHAR2(16 BYTE) DEFAULT 'WIREBOND' NOT NULL);
UPDATE OCAPSYS.TBLWIREBOND SET WSTYPE = 'WIREBOND';
COMMIT;
```

## The database change comes first

Until the table is changed the page cannot load at all — the app asks for
`RECIPE` and Oracle answers **ORA-00904: "RECIPE": invalid identifier**.

Two ways to change it. **Use the first one** if TBLWIREBOND already exists:

| Script | |
|---|---|
| `Database/tblwirebond-migrate.sql` | **Recommended.** Converts the table in place with `ALTER`. Keeps the grants, synonyms and privileges, and cannot fail over a tablespace name. Step by step, one statement at a time. |
| `Database/tblwirebond.sql` | `DROP` + `CREATE`. For a table that does not exist yet, or one you are happy to lose. |

Either way, take a copy first:

```sql
CREATE TABLE tblwirebond_bak AS SELECT * FROM tblwirebond;
```

The new shape:

| Column | Type | |
|---|---|---|
| `TBLROWID` | `VARCHAR2(50)` | `RAWTOHEX(SYS_GUID())` on insert, for the MES side |
| `LASTUPDATE` | `DATE` | `SYSDATE` on every write |
| `LASTUPDATEDBY` | `VARCHAR2(50)` | sAMAccountName, e.g. `NX487878` |
| `"PACKAGE"` | `VARCHAR2(64)` | optional |
| `PRODUCT` | `VARCHAR2(64)` | required |
| `LEADFRAME12NC` | `VARCHAR2(16)` | required |
| `RECIPE` | `VARCHAR2(120)` | required |

**`PACKAGE` is a reserved word in Oracle.** It has to be written `"PACKAGE"` —
double-quoted and uppercase — everywhere it appears, or Oracle answers
ORA-00904. `AWACSRECIPEBYWSTYPE` already does this; the app now does it too.

The old OCAP columns (`WBOCAPNO`, `WBOCAPWWK`, `WBDATE`, the 4M1E and
disposition fields) are gone, and so is the `OCAP_WIREBOND_WORKWEEK` trigger —
it derived `WBOCAPWWK` from `WBDATE`, and neither column exists any more.

### Section 4 of that script is not optional

Edit copies the old row into `TBLRECIPEHISTORY`; Delete copies the whole row
into `TBLRECIPETRASH`. Both happen inside the same transaction as the write.
Both tables carry a CHECK constraint listing which table names are allowed:

```sql
CHECK (table_name IN ('AWACSWSTYPE', 'AWACSRECIPEBYWSTYPE', 'AWACSLF'))
```

`TBLWIREBOND` was never in that list. So **every Edit and every Delete on this
page fails** with `ORA-02290: check constraint violated` and rolls back — while
Add works fine, which is what makes it hard to spot. Section 4 replaces both
constraints with a list that includes `TBLWIREBOND`, and
`Database/tblrecipeaudit.sql` carries the same list for a fresh install.

## Install

| File | |
|---|---|
| `Models/WireBond.cs` | rewritten — 7 properties |
| `ViewModels/WireBondInputModel.cs` | rewritten — package, product, leadframe, recipe |
| `ViewModels/WireBondViewModel.cs` | rewritten |
| `Services/WireBondService.cs` | rewritten |
| `Controllers/WireBondController.cs` | rewritten |
| `Views/WireBond/Index.cshtml` | rewritten — the ten-column grid |
| `Database/tblwirebond-migrate.sql` | new — **run this**, converts the table in place |
| `Database/tblwirebond.sql` | rewritten — drop + create, the alternative to the above |
| `Database/tblrecipeaudit.sql` | changed — TBLWIREBOND added to both CHECK constraints |
| `Database/tblwirebond-insert.sql` | rewritten — by-hand inserts, plus a copy-from-AWACSRECIPEBYWSTYPE block |
| `Database/tblwirebond-check.sql` | rewritten — diagnose a save that did nothing |
| `Database/tblwirebond-access.sql` | changed — grants `'Wirebond'`, repairs `'Wirebond OCAP'` |
| `Models/AppModule.cs` | changed — module description wording |

Delete these two — nothing references them any more:

- `Views/WireBond/_WireBondFields.cshtml`
- `ViewModels/WireBondPanelModel.cs`

Then: run `Database/tblwirebond-migrate.sql`, `dotnet build`, `dotnet run`.

## Testing it

1. **Operations → Wirebond** → **Add Recipe**. A `New` row opens at the top of
   the grid with an input in each column.
2. Product, Leadframe 12NC and Recipe are required; Package is optional.
3. Click the 💾 icon under **Edit**. The row saves, the grid comes back with it
   at the top, flashed amber.
4. Check the database:
   ```sql
   SELECT "PACKAGE", product, leadframe12nc, recipe, lastupdatedby
   FROM   OCAPSYS.TBLWIREBOND
   ORDER  BY lastupdate DESC NULLS LAST FETCH FIRST 5 ROWS ONLY;
   ```
5. Pencil icon → edit in place → 💾. The old values land on the Change History
   page.
6. Bin icon → type `DELETE` → confirm. The row goes to Trash and can be
   restored from the Recycle Bin.

If a save does nothing, run `Database/tblwirebond-check.sql`. Block 1 checks the
table shape, block 2 whether the row is there, **block 3 the CHECK constraint
that breaks Edit and Delete**, block 4 the user's grants.

## Permissions

A Super Admin needs no grant: `AccessEvaluator.GetAsync` returns full permission
to the SuperAdmin role before it ever looks at `TBLACCESS`.

Everybody else needs one `TBLACCESS` row with `MODULE_NAME = 'Wirebond'` — tick
the boxes on the **Wirebond** row in Access Management, or run
`Database/tblwirebond-access.sql`.

| ACCESS_LEVEL | View | Add | Edit | Delete |
|---|---|---|---|---|
| `READ` | yes | | | |
| `WRITE` | yes | yes | yes | |
| `ADMIN` | yes | yes | yes | yes |

Hiding a button is presentation only; every action carries
`[ModuleAccess(ModuleNames.TableWirebond, …)]` as the real check.

## What the page does

- **Grid**: PACKAGE, Product, Leadframe 12NC, Recipe, Last Updated By,
  Timestamp. Every header sorts, search covers all four data columns plus the
  updating user. 25/50/100 rows per page, with column resizing.
- **Add / Edit**: one inline row, an input per column, 💾 to save and ✕ to
  cancel — the same interaction as the Sawing grid.
- **Delete**: moves the row to Trash first, so it can be restored from the
  Recycle Bin.
- **CSV**: template, export and import. Required columns `PRODUCT`,
  `LEADFRAME12NC`, `RECIPE`; `PACKAGE` optional; the header row is optional. A
  `LEADFRAME12NC` that Excel saved as `3.4E+11` is rejected rather than stored.
- **Duplicates**: product + leadframe + recipe must be unique. The table has no
  unique constraint, so this check before every insert is the only guard; the
  same product/leadframe/recipe twice in one CSV cancels the whole import.

## Not compiled here

Written against the source; the container had no .NET 10 SDK to build with.
Every identifier, bind variable, column name, reader index and Razor construct
was cross-checked by hand: `row.*` in the view matches `WireBond` exactly, the
input `name=` values match `WireBondInputModel` exactly, the sort keys match the
service whitelist exactly, and colgroup / header / add row / display row / edit
row are 10 cells each. Run `dotnet build` before trusting it.
