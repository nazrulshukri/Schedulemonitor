namespace Atcbassemblyrecipe.Models
{
    // Module names used as the first half of a settings key. They match the
    // MODULE_NAME values seeded by Database/tblappsetting.sql.
    public static class SettingModules
    {
        public const string App = "APP";
        public const string Login = "LOGIN";
        public const string Layout = "LAYOUT";
        public const string Theme = "THEME";
    }

    // One row of TBLAPPSETTING.
    public sealed record AppSetting(
        string ModuleName,
        string SettingKey,
        string Value,
        string SettingType,
        string? Description);

    // The values the pages used to hard-code, kept here so the app still renders
    // before Database/tblappsetting.sql has been run and if the table cannot be
    // read. They are the same values the script seeds - change both together, or
    // change the row in the database, which is the point of the table.
    public static class AppSettingDefaults
    {
        public static IReadOnlyDictionary<string, string> Values { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["APP:ProductName"] = "ATCB Assembly Recipe",
                ["APP:TitleSuffix"] = "ATCB Assembly Recipe",
                ["APP:FaviconUrl"] = "~/images/app-icon.png",
                ["APP:CompanyName"] = "Nexperia",
                ["APP:EmailSubjectPrefix"] = "[ATCB Recipe]",

                ["LOGIN:LogoUrl"] = "~/images/nexperia-logo.png",
                ["LOGIN:FormLogoUrl"] = "~/images/nexperia-logo.png",
                ["LOGIN:Headline"] = "ATCB Assembly Recipe",
                ["LOGIN:Kicker"] = "EFFICIENCY WINS.",
                ["LOGIN:FormTitle"] = "Sign In",
                ["LOGIN:SubmitText"] = "Sign In",
                ["LOGIN:SubmitBusyText"] = "Signing In",
                ["LOGIN:UserPlaceholder"] = "Nexperia account",
                ["LOGIN:PasswordPlaceholder"] = "Password",
                ["LOGIN:BackgroundImageUrl"] = "~/images/atf-cabuyao.jpg",
                ["LOGIN:BackgroundVideoUrl"] = "",

                ["LAYOUT:LogoUrl"] = "~/images/nexperia-logo.png",
                ["LAYOUT:BrandText"] = "ATCB Assembly Recipe",
                ["LAYOUT:BackgroundImageUrl"] = "~/images/atf-cabuyao.jpg",

                ["THEME:nxp-teal"] = "#007c84",
                ["THEME:nxp-teal-dark"] = "#00636a",
                ["THEME:nxp-teal-soft"] = "#e4f3f4",
                ["THEME:nxp-orange"] = "#ff4f26",
                ["THEME:nxp-orange-dark"] = "#e43d17",
                ["THEME:ink"] = "#202837",
                ["THEME:muted"] = "#667085",
                ["THEME:line"] = "#d8e1e8",
                ["THEME:surface"] = "#ffffff",
                ["THEME:canvas"] = "#f3f7fa",
                ["THEME:success"] = "#12a17a",
                ["THEME:warning"] = "#f2a33a",
                ["THEME:danger"] = "#e83749",
                ["THEME:motion-fast"] = "140ms",
                ["THEME:motion-base"] = "240ms",
                ["THEME:motion-slow"] = "480ms",
                ["THEME:motion-ease"] = "cubic-bezier(0.22, 0.61, 0.36, 1)"
            };

        public static string Get(string module, string key) =>
            Values.TryGetValue($"{module}:{key}", out var value) ? value : string.Empty;
    }
}
