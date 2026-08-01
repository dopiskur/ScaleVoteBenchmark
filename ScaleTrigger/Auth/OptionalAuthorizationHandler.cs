using Microsoft.AspNetCore.Authorization;

namespace ScaleTrigger.Auth
{
    /// <summary>
    /// Marks an authorization requirement as one that can be bypassed
    /// when "Auth:Enabled" is false. Only endpoints using the
    /// "OptionalJwt" policy (see Program.cs) get this requirement -
    /// endpoints using plain [Authorize] (e.g. the destructive database
    /// cleanup action) always require a valid JWT, unaffected by
    /// "Auth:Enabled".
    /// </summary>
    public class OptionalJwtRequirement : IAuthorizationRequirement
    {
    }

    /// <summary>
    /// Satisfies OptionalJwtRequirement either when "Auth:Enabled" is
    /// false in appsettings.json (bypassing the JWT check entirely), or
    /// when it's true and the caller is actually authenticated. Defaults
    /// to disabled (false) if the setting is missing entirely.
    /// </summary>
    public class OptionalAuthorizationHandler : AuthorizationHandler<OptionalJwtRequirement>
    {
        private readonly IConfiguration configuration;

        public OptionalAuthorizationHandler(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context, OptionalJwtRequirement requirement)
        {
            bool authEnabled = bool.TryParse(configuration["Auth:Enabled"], out bool enabled) && enabled;

            if (!authEnabled || context.User.Identity?.IsAuthenticated == true)
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
