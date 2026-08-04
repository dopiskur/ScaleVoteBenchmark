namespace ScaleTrigger.Interfaces
{
    /// <summary>Distinguishes "can't reach the database" from "reached it, but Vote/LoadConfig aren't there" so callers can tell a user to reset instead of just retrying.</summary>
    public enum DbFailureKind
    {
        ConnectionFailure,
        SchemaMissing
    }
}
