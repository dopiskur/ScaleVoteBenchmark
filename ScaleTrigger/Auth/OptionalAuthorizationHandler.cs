using Microsoft.AspNetCore.Authorization;

namespace ScaleTrigger.Auth
{
    public class OptionalJwtRequirement : IAuthorizationRequirement
    {
    }

    /// <summary>Bypasses the JWT check entirely when "Auth:Enabled" is false; defaults to disabled if unset.</summary>
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
