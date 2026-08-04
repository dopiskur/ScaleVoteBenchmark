using ScaleTrigger.Interfaces;

namespace ScaleTrigger
{
    /// <summary>Shared 503 body shape for the read endpoints, so the dashboard can tell "can't reach the database" apart from "reachable, but Vote/LoadConfig aren't there".</summary>
    public static class DbErrorResponse
    {
        public static object For(DbFailureKind kind) => new
        {
            reason = kind.ToString(),
            message = kind == DbFailureKind.SchemaMissing
                ? "Database schema not found. Reset the database to initialize it."
                : "Database unavailable."
        };
    }
}
