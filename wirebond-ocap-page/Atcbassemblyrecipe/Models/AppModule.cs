namespace Atcbassemblyrecipe.Models
{
    public class AppModule
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    // MODULE_NAME values stored in TBLACCESS. Referenced by [ModuleAccess] on
    // controller actions and by the views, so a typo becomes a compile error
    // instead of a silently unenforced permission.
    public static class ModuleNames
    {
        public const string AwacsWstype = "AWACSWSTYPE";
        public const string TableSawing = "Sawing";
        // The Wirebond page, which reads OCAPSYS.TBLWIREBOND (the OCAP log).
        public const string TableWirebond = "Wirebond";
        public const string TableMarker = "Marker";
        public const string AwacsLf = "AWACSLF";
        public const string RecycleBin = "Recycle Bin";
        public const string DbValidation = "DB Validation";
        public const string AccessManagement = "Access Management";
    }

    // Catalog of modules that can appear as MODULE_NAME rows in TBLACCESS.
    // Keep this in sync with the app's real sections; Access Management always
    // shows exactly these rows for every user, defaulting to no access.
    public static class AppModules
    {
        public static readonly AppModule[] All =
        [
            new()
            {
                Name = ModuleNames.AwacsWstype,
                Description = "Main SAWING workstation table: WSID, WSTYPE, last updated by, timestamp."
            },
            new()
            {
                Name = ModuleNames.TableSawing,
                Description = "AWACSRECIPEBYWSTYPE rows where WSTYPE is SAWING: package, product, leadframe 12NC, recipe."
            },
            new()
            {
                Name = ModuleNames.TableMarker,
                Description = "AWACSRECIPEBYWSTYPE rows where WSTYPE is MARKER: package, product, leadframe 12NC, recipe."
            },
             new()
            {
                Name = ModuleNames.TableWirebond,
                Description = "TBLWIREBOND OCAP log: OCAP no, work week, machine, defect, 4M1E difference, action taken and disposition."
            },
            new()
            {
                Name = ModuleNames.AwacsLf,
                Description = "AWACSLF leadframe master: LF 12NC, LFSIZE, default WO quantity, package, device."
            },
            new()
            {
                Name = ModuleNames.RecycleBin,
                Description = "Trash and change history: restore deleted rows, revert a wrong update, purge for good."
            },
            new()
            {
                Name = ModuleNames.DbValidation,
                Description = "Validated database change request workflow with approval guardrails."
            },
            new()
            {
                Name = ModuleNames.AccessManagement,
                Description = "Super admin role and module permission review."
            }
        ];
    }
}
