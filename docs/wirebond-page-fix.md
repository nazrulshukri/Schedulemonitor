# Wirebond page: why it is empty, and why the Operations menu keeps closing

Two unrelated problems, both on `https://localhost:7029/Awacs/TableWirebond`.

## 1. The Operations menu collapses on the Wirebond page

`Views/Shared/_Layout.cshtml` decides whether the Operations group renders expanded:

```csharp
var operationsOpen = isTableActive || isSawingActive || isMarkerActive || isAwacsLfActive;
```

`isWirebondActive` is computed one screen earlier (line 40) and used for the link's
`active` highlight, but it was never added here. So on the Wirebond page
`operationsOpen` is `false`, the `<details class="nav-group">` renders without `open`,
and the group is closed. Expanding it by hand works until the next page load — which
is exactly what clicking a link inside it causes. Nothing is "closing" the menu; the
server renders it closed every time.

The same omission in `hasGridControls` (line 60) is why the account menu on this page
has no **Table text size** or **Reset Widths** controls, unlike Sawing and Marker.

Fix — add `isWirebondActive` to both lines (`docs/wirebond-menu-fix.patch`):

```csharp
var operationsOpen = isTableActive || isSawingActive || isWirebondActive || isMarkerActive || isAwacsLfActive;
var hasGridControls = isTableActive || isSawingActive || isWirebondActive || isMarkerActive || isAwacsLfActive;
```

Restart the app (or just reload — Razor views recompile) and the group stays open on
Wirebond the way it does on the other Operations pages.

## 2. "No rows found" — the page does not read TBLWIREBOND

The Wirebond page and `OCAPSYS.TBLWIREBOND` are two different things:

| | Wirebond page | TBLWIREBOND |
|---|---|---|
| Table | `AWACSRECIPEBYWSTYPE`, filtered `WSTYPE = 'WIREBOND'` | `TBLWIREBOND` |
| Holds | recipe master: package, product, leadframe 12NC, recipe | wire bond OCAP records: defect, 4M1E difference, disposition |
| Columns shown | WSTYPE, PACKAGE, Product, Leadframe 12NC, Recipe | — no page yet |
| Code | `AwacsController.TableWirebond` → `ShowRecipeGridAsync(RecipeWsTypes.Wirebond)` → `AwacsWstypeService.GetRecipeByWstypeAsync` | none |

The page description says it out loud: *"WIREBOND recipe columns from
AWACSRECIPEBYWSTYPE."* A row inserted into `TBLWIREBOND` cannot appear there, no
matter how the grid is refreshed. `ModuleNames.TableWirebond` ("Wirebond") is a recipe
module; the name collision with the OCAP table is a coincidence.

So pick which one you actually want:

**A. You wanted recipe rows on that page.** Insert into `AWACSRECIPEBYWSTYPE` with
`WSTYPE = 'WIREBOND'` — see `database/tblwirebond/awacsrecipebywstype-wirebond.sql` —
or just use the **+ Add Recipe** button, which runs the same INSERT. If rows exist in
the table but the grid stays empty, check that `WSTYPE` is exactly `'WIREBOND'` with
no trailing spaces (that script has a query for it), and that the search box is clear.

**B. You wanted the OCAP records visible.** `TBLWIREBOND` needs its own page. Nothing
exists for it yet — this is a new feature, not a setting:

1. `Models/WireBond.cs` — one row, addressed by its real `TBLROWID` column.
2. `ViewModels/WireBondInputModel.cs` + `PagedResult<WireBond>` for the grid.
3. `Services/WireBondService.cs` — `GetAsync` / `CreateAsync` / `UpdateAsync` /
   `DeleteAsync`, shaped like `AwacsLfService`, registered in `Program.cs`.
4. `Controllers/WireBondController.cs` with `[ModuleAccess(...)]` on each action.
5. `Views/WireBond/Index.cshtml` — the same Excel-style grid as
   `Views/AwacsLf/Index.cshtml`.
6. A new `ModuleNames` entry + `AppModules.All` description — the existing
   `TableWirebond` module belongs to the recipe page, so this needs its own, e.g.
   `WireBondOcap = "Wirebond OCAP"`.
7. A sidebar link in `_Layout.cshtml` behind its own `canSee…` check.
8. A `TBLACCESS` grant for that module name, or the page is invisible even to a
   Super Admin.
9. Optionally `AuditedTable.For` in `RecipeAuditRepository` with
   `tblrowid = :row_key`, so wire bond rows are restorable from the Recycle Bin.
