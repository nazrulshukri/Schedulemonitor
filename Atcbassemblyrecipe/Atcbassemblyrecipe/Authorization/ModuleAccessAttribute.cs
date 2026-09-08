using Atcbassemblyrecipe.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Atcbassemblyrecipe.Authorization
{
    public enum ModuleAction
    {
        View,
        Add,
        Update,
        Delete
    }

    // Server-side enforcement of the per-module TBLACCESS grants. Hiding a button in a
    // view is presentation only - anyone can still POST the route directly - so every
    // action that reads or writes a module carries this attribute as the real check.
    public class ModuleAccessAttribute : TypeFilterAttribute
    {
        public ModuleAccessAttribute(string moduleName, ModuleAction action)
            : base(typeof(ModuleAccessFilter))
        {
            Arguments = [moduleName, action];
        }
    }

    public class ModuleAccessFilter : IAsyncAuthorizationFilter
    {
        private readonly IAccessEvaluator _accessEvaluator;
        private readonly string _moduleName;
        private readonly ModuleAction _action;

        public ModuleAccessFilter(IAccessEvaluator accessEvaluator, string moduleName, ModuleAction action)
        {
            _accessEvaluator = accessEvaluator;
            _moduleName = moduleName;
            _action = action;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (context.HttpContext.User.Identity?.IsAuthenticated != true)
            {
                context.Result = new ChallengeResult();
                return;
            }

            var permission = await _accessEvaluator.GetAsync(_moduleName);
            var allowed = _action switch
            {
                ModuleAction.View => permission.CanView,
                ModuleAction.Add => permission.CanAdd,
                ModuleAction.Update => permission.CanUpdate,
                ModuleAction.Delete => permission.CanDelete,
                _ => false
            };

            if (!allowed)
            {
                context.Result = new ForbidResult();
            }
        }
    }
}
