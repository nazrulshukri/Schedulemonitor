# Engineering lots: the page, the table and the MES lookup

Three pieces, in the order they have to be put in place.

| Piece | Where |
|---|---|
| `OCAPSYS.ENGINEERING` and its two little map tables | `Atcbassemblyrecipe/Atcbassemblyrecipe/Database/engineering*.sql` |
| The Engineering page | `Atcbassemblyrecipe` (controller, service, view) |
| The `ENG` workorder lookup | `awacsMesInterface/AwacsMesService.cs` |

## 1. The database

Run against the **OCAPSYS** schema, in this order:

```
Database/engineering.sql             -- the table, its index, the undo-table constraints
Database/engineeringcolumngroup.sql  -- which group owns which column      (read by the web app)
Database/engineeringwstype.sql       -- which WSTYPE reads which column    (read by AwacsMesService)
Database/engineering-access.sql      -- optional: put a user in a group
Database/engineering-check.sql       -- diagnostic, read only: run it on any ORA-00904
```

`ENGINEERING` has eight data columns and no more:

| | |
|---|---|
| identity | `"NO"` `REQUESTOR` `LOTNUMBER` `"PACKAGE"` `PRODUCT` |
| recipes | `RECIPESAWING` `RECIPEWIREBOND` `RECIPEMARKER` |

plus `TBLROWID` / `LASTUPDATE` / `LASTUPDATEDBY`.

### ORA-00904: "<column>": invalid identifier

It always means one thing: something asked `ENGINEERING` for a column it does not
have, so **the table and the mapping disagree**. Run `Database/engineering-check.sql`
— query 2 lists what the table really has, query 3 lists what the mapping asks for and
cannot find.

Two usual causes:

* **The table is still an older shape.** Per-step names (`RECIPEFINALTEST`,
  `RECIPEWPROBER`, `RECIPEDA`) mean the 23-column draft is still there. Back it up
  (`CREATE TABLE engineering_bak AS SELECT * FROM engineering;`) and run
  `engineering.sql`; its section 3 carries the rows across.
* **`engineering.sql` ran but `engineeringcolumngroup.sql` did not** — or the app
  cannot read that table. The app used to fall back to its built-in column list
  without checking it, which produced exactly this error. It no longer does: every
  column name, from the table or from the fallback, is checked against the data
  dictionary first, and anything missing is dropped and named in the log. The page
  then says what to run instead of failing every query.

`ENGINEERING` is the SQL Server table `awacs.dbo.ENGINEERING` moved into Oracle. It is
in Oracle because both consumers already connect there — the recipe app over its
`OCAP` connection string, `AwacsMesService` over the one in its `Web.config` — so
nothing needs a second driver or a second credential. If the live rows are still in
SQL Server, export that `SELECT` to CSV and upload it with **Import CSV** on the page;
the headers match.

Two of the columns are Oracle reserved words. `"NO"` and `"PACKAGE"` must be written
double-quoted and uppercase in every statement, or the query dies with ORA-00904.
`EngineeringColumns.Quote` does this in the app; the audit repository already quoted
every column name.

## 2. The Engineering page

`/Engineering` — one row per engineering lot, add / edit / delete / export / import,
the same grid as Wirebond, and the same Trash and Change History behind it.

### One grant decides both the pages and the column

A user's group is the `Sawing`, `Wirebond` or `Marker` module grant already in
`TBLACCESS`. That single grant does two jobs: it opens that group's own page, **and**
it puts that group's recipe column on the Engineering page. There is nothing else to
keep in step.

**One tick does it.** In Access Management the three group rows are marked
`group`: ticking Sawing, Wirebond or Marker also grants AWACSWSTYPE (view),
Engineering (matching the group's own add/update), AWACSLF (view) and Trash
(view + restore). The rule runs in the browser so the Super Admin watches it
happen, and again in `AccessController` on save so it holds for a posted form
too. It only ever **raises** — anything extra ticked on a companion row survives,
and a user in two groups keeps the higher of the two. Trash **Delete** (purge for
good) is deliberately not in the bundle; that stays a separate tick.

So a group profile is four grants, and the sidebar follows:

| The user holds | Their sidebar reads | On the Engineering page they see |
|---|---|---|
| AWACSWSTYPE, **Sawing**, Engineering, AWACSLF | AWACSWSTYPE · Sawing · Engineering · AWACSLF | identity + `RECIPESAWING` |
| AWACSWSTYPE, **Wirebond**, Engineering, AWACSLF | AWACSWSTYPE · Wirebond · Engineering · AWACSLF | identity + `RECIPEWIREBOND` |
| AWACSWSTYPE, **Marker**, Engineering, AWACSLF | AWACSWSTYPE · Marker · Engineering · AWACSLF | identity + `RECIPEMARKER` |

A sawing user has no Wirebond or Marker link at all, and no wirebond or marker recipe
column. Two group grants means both pages and both columns. A **Super Admin** sees
every page and all three columns without any grant — which is what makes the Super
Admin the person who hands the groups out, from **Access Management** in the app or
from `engineering-access.sql`.

`SHARED` is the fourth group name and is not a team: `NO`, `REQUESTOR`, `LOTNUMBER`,
`PACKAGE` and `PRODUCT` are the row's identity and are shown to everybody who can open
the page.

Which column belongs to which group is a row in `OCAPSYS.ENGINEERINGCOLUMNGROUP`, not
a line of code. Moving one is an `UPDATE`, visible within the cache window
(`AppSettings:CacheSeconds`, default 60 seconds) with no redeploy:

```sql
UPDATE OCAPSYS.ENGINEERINGCOLUMNGROUP SET group_name = 'MARKER' WHERE column_name = 'RECIPEWIREBOND';
COMMIT;
```

Hiding a column is a permission, not a decoration. `EngineeringService` narrows the
`SELECT`, the search, the `INSERT` and the `UPDATE` to the caller's own columns, so a
hand-made POST naming a column outside their groups writes nothing, and an edit leaves
the other groups' recipes exactly as they were.

Column names reach Oracle as identifiers, which cannot be bound as parameters. They
never come from the request: `EngineeringColumnGroupProvider` checks every configured
name against the real shape of `ENGINEERING` in the data dictionary and against
`^[A-Z][A-Z0-9_]*$`, and drops anything else. Values are always bound.

## 3. The MES lookup

`DBorderUpdate` now recognises an engineering lot by its WOID:

```xml
<mes:DBorderUpdate>
   <mes:WsId>2OIF-002</mes:WsId>
   <mes:Workorder>
      <mes:Woid>ENGXTA54780B</mes:Woid>
   </mes:Workorder>
</mes:DBorderUpdate>
```

A WOID starting with `ENG` is not in MES, so `getFAMESInfo` and `GetLotDetailsFromRms`
both answer nothing for it — which is why every engineering lot used to come back as
`WSID:... not exist in recipe!` whatever the workstation asked for. The ENG branch is
taken **before** those two calls, so an engineering lot costs one Oracle lookup instead
of two web service round trips that cannot succeed.

What it does:

1. `getWSType(WsId)` — the workstation's type, from `AWACSWSTYPE`, exactly as before.
2. `getEngineeringRecipeColumn(wstype)` — the `ENGINEERING` column that machine reads,
   from `ENGINEERINGWSTYPE`. Many workstation types share one column: `SAWING` and
   `WAOI` both answer `RECIPESAWING`; `DIEBOND`, `ASMWB`, `ASMWBM`, `ASMWBD` and `ADAT`
   answer `RECIPEWIREBOND`; `MARKER`, `2DMARKER`, `MOULD`, `TRIMFORM` and `PLATING`
   answer `RECIPEMARKER`. A new machine type is a row in that table, not a code change.
3. `getEngineeringLot(woid, column)` — the row by `LOTNUMBER`, carrying that one cell.
4. Sets `RECIPE`, and `PACKAGE` / `PRODUCT` / `DEVICE` when the row carries them.

Every failure comes back as a `RESULT` attribute the workstation can display, naming
what to do about it:

| Situation | RESULT |
|---|---|
| WSID not in `AWACSWSTYPE` | `WSID:{0} not exist in recipe!` |
| WSTYPE not mapped to a column | `WSTYPE:{0} has no ENGINEERING recipe column. Map it in ENGINEERINGWSTYPE.` |
| Lot not in the table | `Engineering lot {0} is not in the ENGINEERING table. Add it on the Engineering page first.` |
| The cell is empty | `Engineering lot {0} has no {1} recipe for WSTYPE:{2}. Fill that cell in on the Engineering page.` |

### Testing it without a workstation

`Service.asmx` carries a new `EngineeringRecipe(wsid, woid)` web method that runs the
same lookup and returns what it found:

```
WSTYPE=SAWING; COLUMN=RECIPES1; RECIPE=SAW_SOT1235_ENG; PACKAGE=SOT1235; PRODUCT=PSMNR50-40SSH
```

### If the service connects as a different Oracle user than OCAPSYS

It needs read access to both new tables — the grants are commented at the bottom of
each SQL script:

```sql
GRANT SELECT ON OCAPSYS.ENGINEERING         TO <mes_user>;
GRANT SELECT ON OCAPSYS.ENGINEERINGWSTYPE   TO <mes_user>;
CREATE OR REPLACE SYNONYM <mes_user>.ENGINEERING       FOR OCAPSYS.ENGINEERING;
CREATE OR REPLACE SYNONYM <mes_user>.ENGINEERINGWSTYPE FOR OCAPSYS.ENGINEERINGWSTYPE;
```

It does **not** need `ENGINEERINGCOLUMNGROUP` — that one is the web app's.

## The `ENG` prefix

`AwacsMesService.EngineeringWoPrefix`. The lot numbers in the table
(`ENGXTA54780B`, `ENGRFS54807`, `ENG_V8PCN68`, `ENG_DHVQFN16_PCN021`) all carry it.
Change that one constant if the convention ever changes.
