using System.Globalization;
using System.Net;
using Atcbassemblyrecipe.Models;

namespace Atcbassemblyrecipe.Services
{
    // Builds the HTML for outgoing notifications.
    //
    // Kept out of the controller so the markup has one home, and away from the
    // Razor views because email HTML is a different medium: Outlook renders with
    // the Word engine, which has no flexbox, no grid, no float, and ignores most
    // of a <style> block. So everything here is nested tables with inline styles,
    // fixed at 600px - the shape every mail client has agreed on for twenty years.
    //
    // Colours come from TBLAPPSETTING through the same snapshot the pages use, so
    // changing a THEME row restyles the email as well without a redeploy.
    public static class NotificationEmail
    {
        private const string FontStack = "'Segoe UI',Roboto,'Helvetica Neue',Helvetica,Arial,sans-serif";

        public static string DatabaseChangeRequest(
            Models.DatabaseChangeRequest request,
            long requestId,
            AppSettingsSnapshot ui,
            string? reviewUrl = null)
        {
            var teal = ui[SettingModules.Theme, "nxp-teal"];
            var tealDark = ui[SettingModules.Theme, "nxp-teal-dark"];
            var tealSoft = ui[SettingModules.Theme, "nxp-teal-soft"];
            var orange = ui[SettingModules.Theme, "nxp-orange"];
            var ink = ui[SettingModules.Theme, "ink"];
            var muted = ui[SettingModules.Theme, "muted"];
            var line = ui[SettingModules.Theme, "line"];
            var canvas = ui[SettingModules.Theme, "canvas"];
            var warning = ui[SettingModules.Theme, "warning"];

            var productName = ui[SettingModules.App, "ProductName"];
            var company = ui[SettingModules.App, "CompanyName"];

            var (typeColour, typeNote) = Severity(request.RequestType, ui);

            var requestedOn = (request.RequestedDate == default ? DateTime.Now : request.RequestedDate)
                .ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture);

            var preheader = $"#{requestId} · {request.RequestType} on {request.TargetTable} · raised by {request.RequestedBy}";

            var cta = string.IsNullOrWhiteSpace(reviewUrl)
                ? $"""
                   <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%">
                     <tr><td style="background:{tealSoft};border-left:4px solid {teal};padding:14px 18px;
                                    font:14px/1.5 {FontStack};color:{tealDark}">
                       Open <strong>Settings &rsaquo; DB Validation</strong> in {E(productName)} to approve or reject it.
                     </td></tr>
                   </table>
                   """
                : $"""
                   <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                     <tr><td align="center" bgcolor="{teal}" style="border-radius:6px">
                       <a href="{E(reviewUrl!)}"
                          style="display:inline-block;padding:13px 30px;font:600 15px/1 {FontStack};
                                 color:#ffffff;text-decoration:none;border-radius:6px">Review this request &rarr;</a>
                     </td></tr>
                   </table>
                   """;

            return $"""
                <!DOCTYPE html>
                <html lang="en"><head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width,initial-scale=1">
                <meta name="x-apple-disable-message-reformatting">
                <title>{E(productName)} — database change request</title>
                </head>
                <body style="margin:0;padding:0;background:{canvas};-webkit-font-smoothing:antialiased">

                <div style="display:none;max-height:0;overflow:hidden;font-size:1px;line-height:1px;color:{canvas};opacity:0">
                  {E(preheader)}
                </div>

                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"
                       style="background:{canvas}">
                <tr><td align="center" style="padding:30px 12px">

                  <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0"
                         style="width:600px;max-width:600px;background:#ffffff;border:1px solid {line};
                                border-radius:12px;overflow:hidden">

                    <tr><td style="height:4px;background:{orange};font-size:0;line-height:0">&nbsp;</td></tr>

                    <tr><td style="background:{teal};padding:24px 32px">
                      <div style="font:600 11px/1 {FontStack};letter-spacing:1.8px;text-transform:uppercase;
                                  color:#bfe3e6">{E(productName)}</div>
                      <div style="font:700 23px/1.3 {FontStack};color:#ffffff;padding-top:7px">
                        Database change request</div>
                      <div style="font:400 14px/1.4 {FontStack};color:#d9f0f2;padding-top:4px">
                        Waiting for validation</div>
                    </td></tr>

                    <tr><td style="padding:26px 32px 4px">
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr>
                        <td style="font:700 30px/1 {FontStack};color:{tealDark}">#{requestId}</td>
                        <td align="right">
                          <span style="display:inline-block;padding:6px 14px;border-radius:20px;
                                       background:#fdf3e4;border:1px solid {warning};
                                       font:700 11px/1 {FontStack};letter-spacing:1.2px;color:#8a5a12">PENDING</span>
                        </td>
                      </tr></table>
                    </td></tr>

                    <tr><td style="padding:20px 32px 0">
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"
                             style="border-top:1px solid {line}">
                        {Row("Type", E(request.RequestType), ink, muted, line, typeColour, typeNote)}
                        {Row("Target table", E(request.TargetTable), ink, muted, line, null, null, mono: true)}
                        {Row("Requested by", E(request.RequestedBy), ink, muted, line, null, null)}
                        {Row("Raised", E(requestedOn), ink, muted, line, null, null)}
                      </table>
                    </td></tr>

                    <tr><td style="padding:22px 32px 0">
                      <div style="font:600 11px/1 {FontStack};letter-spacing:1.4px;text-transform:uppercase;
                                  color:{muted};padding-bottom:8px">Reason given</div>
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                        <tr><td style="background:{canvas};border-left:3px solid {line};padding:14px 16px;
                                       font:15px/1.6 {FontStack};color:{ink}">{E(request.Reason)}</td></tr>
                      </table>
                    </td></tr>

                    <tr><td style="padding:26px 32px 4px">{cta}</td></tr>

                    <tr><td style="padding:22px 32px 26px">
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                        <tr><td style="border-top:1px solid {line};padding-top:16px;
                                       font:13px/1.6 {FontStack};color:{muted}">
                          <strong style="color:{ink}">Nothing has been changed.</strong>
                          This request was only recorded. Structural actions are never executed by the web
                          application &mdash; they stay a manual DBA action after review.
                        </td></tr>
                      </table>
                    </td></tr>

                  </table>

                  <div style="padding:16px 8px 0;font:12px/1.6 {FontStack};color:{muted};max-width:600px">
                    Sent automatically by {E(productName)}{(string.IsNullOrWhiteSpace(company) ? "" : " · " + E(company))}.
                    You are receiving this because you hold the Super Admin role.
                  </div>

                </td></tr>
                </table>
                </body></html>
                """;
        }

        // A cell pair: label on the left, value on the right, with an optional
        // coloured note under the value.
        private static string Row(string label, string value, string ink, string muted, string line,
                                  string? accent, string? note, bool mono = false)
        {
            var valueFont = mono
                ? "600 15px/1.4 Consolas,'Courier New',monospace"
                : $"600 15px/1.4 {FontStack}";

            var noteHtml = string.IsNullOrWhiteSpace(note)
                ? ""
                : $"""<div style="font:600 12px/1.4 {FontStack};color:{accent};padding-top:3px">{note}</div>""";

            return $"""
                <tr>
                  <td width="140" valign="top" style="padding:13px 12px 13px 0;border-bottom:1px solid {line};
                      font:600 11px/1.4 {FontStack};letter-spacing:1.2px;text-transform:uppercase;
                      color:{muted}">{label}</td>
                  <td valign="top" style="padding:13px 0;border-bottom:1px solid {line};
                      font:{valueFont};color:{ink}">{value}{noteHtml}</td>
                </tr>
                """;
        }

        // Not every request type carries the same risk, and the reviewer should see
        // that before opening the page.
        private static (string Colour, string? Note) Severity(string requestType, AppSettingsSnapshot ui) =>
            requestType?.Trim().ToUpperInvariant() switch
            {
                RequestTypes.Truncate =>
                    (ui[SettingModules.Theme, "danger"], "Destructive — removes every row in the table"),
                RequestTypes.DeleteRow =>
                    (ui[SettingModules.Theme, "danger"], "Destructive — removes data"),
                RequestTypes.AddColumn or RequestTypes.CreateTable =>
                    (ui[SettingModules.Theme, "warning"], "Structural — changes the shape of the schema"),
                _ =>
                    (ui[SettingModules.Theme, "nxp-teal"], null)
            };

        private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
