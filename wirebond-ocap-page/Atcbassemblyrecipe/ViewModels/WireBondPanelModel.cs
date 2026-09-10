using Atcbassemblyrecipe.Models;

namespace Atcbassemblyrecipe.ViewModels
{
    // Views/WireBond/_WireBondFields.cshtml renders the same 25 fields for the add
    // panel and for every edit panel, so the two can never drift apart. FormId is
    // the form each input posts to through its form="..." attribute; Row is null on
    // the add panel and carries the existing values on an edit.
    public class WireBondPanelModel
    {
        public WireBondPanelModel(string formId, WireBond? row)
        {
            FormId = formId;
            Row = row;
        }

        public string FormId { get; }

        public WireBond? Row { get; }

        public bool IsEdit => Row is not null;
    }
}
