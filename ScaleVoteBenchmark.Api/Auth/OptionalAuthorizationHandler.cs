using Microsoft.AspNetCore.Authorization;

namespace ScaleVoteBenchmark.Api.Auth
{
    /// <summary>
    /// Lets [Authorize]-protected endpoints be reached without a JWT
    /// token when "Auth:Enabled" in appsettings.json is set to false -
    /// succeeds every pending authorization requirement so the
    /// requirement never actually gets evaluated. When enabled, this
    /// handler does nothing and normal JWT authorization applies.
    /// Defaults to disabled (false) if the setting is missing entirely.
    /// </summary>
    public class OptionalAuthorizationHandler : IAuthorizationHandler
    {
        private readonly IConfiguration configuration;

        public OptionalAuthorizationHandler(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        public Task HandleAsync(AuthorizationHandlerContext context)
        {
            bool authEnabled = bool.TryParse(configuration["Auth:Enabled"], out bool enabled) && enabled;
            if (!authEnabled)
            {
                foreach (var requirement in context.PendingRequirements.ToList())
                {
                    context.Succeed(requirement);
                }
            }

            return Task.CompletedTask;
        }
    }
}
