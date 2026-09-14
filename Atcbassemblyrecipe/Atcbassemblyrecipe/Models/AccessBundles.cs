namespace Atcbassemblyrecipe.Models
{
    // Granting a group is one tick, not five.
    //
    // Sawing, Wirebond and Marker are not just pages - they are the line groups
    // this app is organised around, and somebody who works in one of them needs
    // the same supporting pages whichever group it is: the machine master to see
    // their workstations, the engineering lots, the leadframe master, and Trash
    // to put back a row they deleted by mistake. Before this, a Super Admin had
    // to remember to tick all five rows, and a half-granted user got a sidebar
    // with their own page and nothing to use it with.
    //
    // So ticking Sawing hands over the whole working set. The Engineering page
    // then shows that group's recipe column and hides the other two, which is
    // the same grant doing its second job - see EngineeringColumnGroupProvider.
    //
    // THE RULE ONLY EVER RAISES ACCESS. It is applied on save, after the Super
    // Admin's own ticks, and it can add a permission but never take one away -
    // so ticking extra boxes on a companion row always survives, and a user in
    // two groups keeps the higher of the two. Removing access is done by
    // clearing the group row itself, which stops the bundle being applied at
    // all on the next save.
    public static class AccessBundles
    {
        // The three line groups. Holding one of these is what makes somebody
        // "a sawing person" or "a marker person".
        public static readonly string[] GroupModules =
        [
            ModuleNames.TableSawing,
            ModuleNames.TableWirebond,
            ModuleNames.TableMarker
        ];

        public static bool IsGroupModule(string? moduleName)
        {
            return moduleName is not null
                && GroupModules.Contains(moduleName.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        // What a group grant carries with it, on top of the group's own page.
        //
        // AWACSWSTYPE and AWACSLF are reference data - read. Trash is View and
        // Update, which is restore-a-row and revert-a-change; Delete is NOT in
        // the bundle because on Trash that means purge for good, and nobody
        // should get that by side effect. Engineering is the one that mirrors:
        // a group allowed to add and edit its own recipes is allowed to add and
        // edit engineering lots too, and a read-only group gets a read-only
        // Engineering page.
        public static IReadOnlyList<BundledModule> For(IModulePermissionSet group)
        {
            return
            [
                new(ModuleNames.AwacsWstype, CanView: true, CanAdd: false, CanUpdate: false, CanDelete: false),
                new(ModuleNames.Engineering, CanView: true, CanAdd: group.CanAdd, CanUpdate: group.CanUpdate, CanDelete: false),
                new(ModuleNames.AwacsLf, CanView: true, CanAdd: false, CanUpdate: false, CanDelete: false),
                new(ModuleNames.RecycleBin, CanView: true, CanAdd: false, CanUpdate: true, CanDelete: false)
            ];
        }

        // Raises the companion rows for every group the user holds, in place -
        // this is the very list the controller is about to save.
        //
        // Generic over the row type so the caller can hand over the
        // List<TableAccessViewModel> it already has: IList is not covariant, so
        // an IList<IModulePermissionSet> parameter would force a copy, and a copy
        // cannot be raised in place.
        //
        // A bundled module that is not in the posted grid is skipped rather than
        // invented. The grid always carries every row of AppModules.All, so a
        // missing one means the form was tampered with, and conjuring a grant out
        // of that would be the wrong answer.
        public static void Apply<T>(IList<T> modules) where T : class, IModulePermissionSet
        {
            var held = modules
                .Where(module => IsGroupModule(module.Module) && module.CanView)
                .ToList();

            if (held.Count == 0)
            {
                return;
            }

            foreach (var group in held)
            {
                foreach (var bundled in For(group))
                {
                    var target = modules.FirstOrDefault(module =>
                        string.Equals(module.Module, bundled.Module, StringComparison.OrdinalIgnoreCase));

                    if (target is null)
                    {
                        continue;
                    }

                    // Raise only. Anything the Super Admin ticked stays ticked.
                    target.CanView |= bundled.CanView;
                    target.CanAdd |= bundled.CanAdd;
                    target.CanUpdate |= bundled.CanUpdate;
                    target.CanDelete |= bundled.CanDelete;
                }
            }
        }

        // Plain-language summary of the bundle, for the Access Management page
        // to print under the grid so the rule is visible rather than surprising.
        public static string Describe()
        {
            return $"Ticking {ModuleNames.TableSawing}, {ModuleNames.TableWirebond} or {ModuleNames.TableMarker} also grants "
                 + $"{ModuleNames.AwacsWstype} (view), {ModuleNames.Engineering} (matching the group's add and update), "
                 + $"{ModuleNames.AwacsLf} (view) and {ModuleNames.RecycleBin} (view and restore). "
                 + "Tick more on any of those rows and it is kept - the rule only ever adds.";
        }
    }

    public sealed record BundledModule(
        string Module,
        bool CanView,
        bool CanAdd,
        bool CanUpdate,
        bool CanDelete);

    // The shape AccessBundles.Apply works on. TableAccessViewModel implements it
    // so the posted grid can be raised in place without copying it into another
    // list first.
    public interface IModulePermissionSet
    {
        string Module { get; }
        bool CanView { get; set; }
        bool CanAdd { get; set; }
        bool CanUpdate { get; set; }
        bool CanDelete { get; set; }
    }
}
