# Engineering lots: the page, the table and the MES lookup

Three pieces, in the order they have to be put in place.

| Piece | Where |
|---|---|
| `OCAPSYS.ENGINEERING` and its column-group map | `Atcbassemblyrecipe/Atcbassemblyrecipe/Database/engineering*.sql` |
| The Engineering page | `Atcbassemblyrecipe` (controller, service, view) |
| The `ENG` workorder lookup | `awacsMesInterface/AwacsMesService.cs` |

## 1. The database

Run against the **OCAPSYS** schema, in this order:

```
Database/engineering.sql             -- the table, its index, the undo-table constraints
Database/engineeringcolumngroup.sql  -- which group owns which recipe column
Database/engineering-access.sql      -- optional: grant a user the page and a group
```

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

### Who sees which columns

`ENGINEERING` carries 23 recipe columns and nobody works on all 23, so the page shows
a user their own group's columns and hides the rest.

* **Which group** a user is in is not a new thing to administer. It is the `Sawing`,
  `Wirebond` and `Marker` module grants already in `TBLACCESS`: grant somebody Sawing
  and the sawing recipes appear on their Engineering page. Two grants means both sets.
  A Super Admin sees everything.
* **Which columns belong to which group** is a row in `OCAPSYS.ENGINEERINGCOLUMNGROUP`,
  not a line of code. Moving a column is an `UPDATE`, visible within the cache window
  (`AppSettings:CacheSeconds`, default 60 seconds) with no redeploy:

  ```sql
  UPDATE OCAPSYS.ENGINEERINGCOLUMNGROUP SET group_name = 'WIREBOND' WHERE column_name = 'RECIPERM';
  COMMIT;
  ```

`SHARED` is the fourth group and is not a team: `NO`, `REQUESTOR`, `LOTNUMBER`,
`PACKAGE`, `PRODUCT` and `ADAT` are the row's identity and are shown to everybody who
can open the page.

The seeded mapping is a starting point — check it against how the groups really
divide the work:

| Group | Columns |
|---|---|
| SAWING | `RECIPES1` `RECIPES2` `RECIPEBACKGRIND` `RECIPEWPROBER` `RECIPEWAFERTEST` `RECIPEWAOI` `RECIPEWLTR` |
| WIREBOND | `RECIPEDA` `RECIPECA` `RECIPEMCDWB` `RECIPEAX` `RECIPEAOI` `RECIPEL200` `RECIPEPHICOM` |
| MARKER | `RECIPEMD` `RECIPE2DMARKER` `RECIPEMOULD` `RECIPEMOLD` `RECIPETF` `RECIPERM` `RECIPESTRIPTEST` `RECIPEFINALTEST` |
| SHARED | `NO` `REQUESTOR` `LOTNUMBER` `PACKAGE` `PRODUCT` `ADAT` |

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
2. `getEngineeringRecipeColumn(wstype)` — the `ENGINEERING` column for that step, from
   `ENGINEERINGCOLUMNGROUP.WSTYPE`. So `SAWING` → `RECIPES1`, `DIEBOND` → `RECIPEDA`,
   `2DMARKER` → `RECIPE2DMARKER`. A new step is a row in that table, not a code change.
3. `getEngineeringLot(woid, column)` — the row by `LOTNUMBER`, carrying that one cell.
4. Sets `RECIPE`, and `PACKAGE` / `PRODUCT` / `DEVICE` when the row carries them.

Every failure comes back as a `RESULT` attribute the workstation can display, naming
what to do about it:

| Situation | RESULT |
|---|---|
| WSID not in `AWACSWSTYPE` | `WSID:{0} not exist in recipe!` |
| WSTYPE not mapped to a column | `WSTYPE:{0} has no ENGINEERING recipe column. Map it in ENGINEERINGCOLUMNGROUP.` |
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
GRANT SELECT ON OCAPSYS.ENGINEERING             TO <mes_user>;
GRANT SELECT ON OCAPSYS.ENGINEERINGCOLUMNGROUP  TO <mes_user>;
CREATE OR REPLACE SYNONYM <mes_user>.ENGINEERING            FOR OCAPSYS.ENGINEERING;
CREATE OR REPLACE SYNONYM <mes_user>.ENGINEERINGCOLUMNGROUP FOR OCAPSYS.ENGINEERINGCOLUMNGROUP;
```

## The `ENG` prefix

`AwacsMesService.EngineeringWoPrefix`. The lot numbers in the table
(`ENGXTA54780B`, `ENGRFS54807`, `ENG_V8PCN68`, `ENG_DHVQFN16_PCN021`) all carry it.
Change that one constant if the convention ever changes.
