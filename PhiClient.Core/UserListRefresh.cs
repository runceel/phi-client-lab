namespace PhiClient.Core;

public sealed record UserListRefresh(long? LastRequest = null, long? Started = null, long Revision = 0)
{
    public bool CanRequest(long now) => Started is null && (LastRequest is null || now - LastRequest >= 10_000);
    public UserListRefresh Begin(long now, long revision) => new(now, now, revision);
    public UserListRefresh Finish() => this with { Started = null };
    public bool Complete(long now, long revision, long lastResponse) =>
        Started is { } start && (now - start >= 60_000 || revision > Revision && now - lastResponse >= 300);
}
