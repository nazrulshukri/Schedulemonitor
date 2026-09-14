# ATCB Assembly Recipe

ASP.NET Core (net10.0) recipe workspace over the OCAP/MES Oracle schema.

## Before you run it

1. **Connection string.** `appsettings.json` ships with `YOUR_USERNAME` /
   `YOUR_PASSWORD` placeholders. For local work, copy
   `appsettings.Development.example.json` to `appsettings.Development.json` and
   put the real OCAP credentials there - that filename is gitignored, so the
   password stays off GitHub. The app refuses to connect while the placeholders
   are still in place and says so on screen.

2. **One-time SQL.** Two scripts under `Database/` must be run once against the
   OCAP schema. They create only tables this app owns; the MES tables
   (`AWACSWSTYPE`, `AWACSRECIPEBYWSTYPE`, `AWACSLF`, `TBLSAWING`, `TBLACCESS`)
   are pre-existing and are never created or altered from here.

   | Script | Creates | Needed for |
   | --- | --- | --- |
   | `Database/tbldbrequest.sql` | `TBLDBREQUEST` | Settings > DB Validation |
   | `Database/tblrecipeaudit.sql` | `TBLRECIPEHISTORY`, `TBLRECIPETRASH` | **Edit and Delete on every grid**, Trash, Change History |
   | `Database/tblappsetting.sql` | `TBLAPPSETTING` | Editing the branding, media and theme from the database |

   `tblrecipeaudit.sql` is not optional. Until it has been run, Edit and Delete
   are refused with a message pointing at it, because the app will not change or
   remove a row it cannot save the old values for.

3. **Windows only.** Login goes through LDAP via `System.DirectoryServices`, so
   the app throws on start-up anywhere else.

## UI settings (`TBLAPPSETTING`)

Everything the pages used to hard-code - the product name, the logos and
background image, the login wording, the brand colours and the animation
timings - is one row each in `TBLAPPSETTING`. Change a row, reload the page,
done: no rebuild, no redeploy.

| MODULE_NAME | Holds |
| --- | --- |
| `APP` | Product name, browser title suffix, favicon, company name, email subject prefix |
| `LOGIN` | Sign-in logos, headline, kicker, field placeholders, button text, backdrop image **and optional backdrop video** |
| `LAYOUT` | Top-bar logo, brand text, backdrop image for the signed-in shell |
| `THEME` | Design tokens: `nxp-teal`, `nxp-orange`, `ink`, `canvas`, `success`, `danger` …, plus `motion-fast` / `motion-base` / `motion-slow` / `motion-ease` |

`THEME` rows are written into the page head as CSS variables, so they override
the defaults in `wwwroot/css/site.css` without anyone editing the stylesheet.
`LOGIN.BackgroundVideoUrl` is empty by default; put a URL in it and the sign-in
page plays that video behind the form instead of showing the photo.

Reads are cached for `AppSettings:CacheSeconds` in `appsettings.json` (default
60), so a change appears within a minute and a busy grid does not re-read the
table on every partial.

**Nothing breaks if the script has not been run.** The same values live in
`Models/AppSettingDefaults.cs` and are used when the table is missing or the
database is unreachable - the login page must still render when Oracle is down.
The start-up log says which of the two is in use:

```text
info: UI settings loaded from TBLAPPSETTING.
warn: UI settings are the built-in defaults. Run Database/tblappsetting.sql ...
```

Adding a setting means: one `INSERT` in `Database/tblappsetting.sql`, one line in
`AppSettingDefaults`, and `@ui[SettingModules.Login, "YourKey"]` in the view.

## Pages

| Page | Table | Notes |
| --- | --- | --- |
| AWACSWSTYPE | `AWACSWSTYPE` | WSID / WSTYPE / WSDB, WSTYPE `SAWING` |
| Table Sawing | `AWACSRECIPEBYWSTYPE` | rows where `WSTYPE = 'SAWING'` |
| Table Marker | `AWACSRECIPEBYWSTYPE` | rows where `WSTYPE = 'MARKER'` |
| AWACSLF | `AWACSLF` | leadframe master |
| Trash | `TBLRECIPETRASH` | restore a deleted row, or purge it for good |
| Change History | `TBLRECIPEHISTORY` | revert a wrong edit back to the old values |

Each page is its own `MODULE_NAME` row in `TBLACCESS`, so Access Management can
grant View/Add/Update/Delete on them separately. New modules start with no
access for existing users until a Super Admin grants it.

### LFSIZE

`AWACSLF.LFSIZE` is keyed in as `<units across>,<units down>`. The page accepts
any `number,number` value and offers the known leadframes as a pick list:

| Package | LFSIZE |
| --- | --- |
| SOT669 | `20,5` |
| SOT669 HDLF | `36,10` |
| SOT1210 | `30,6` |
| SOT1235 | `24,5` |

In a CSV import, quote it - `"20,5"` - so the comma is not read as a column
separator.

### Undo

* **Delete** copies the whole row into `TBLRECIPETRASH` and removes it, in one
  transaction. Trash > Restore writes it back exactly as it was.
* **Edit** copies the row's previous values into `TBLRECIPEHISTORY` in the same
  transaction as the update. Change History > Revert puts those values back.
* **Purge** in Trash is the only action that really destroys data, and it needs
  Delete permission on the Recycle Bin module (Super Admin by default).

Both snapshots cover every column the table has - the column list is read from
the Oracle data dictionary at run time - so adding a column to one of the MES
tables needs no change here.
