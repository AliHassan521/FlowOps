namespace FlowOps.Domain.Entities;

// A stable row locks one login family while refresh, replay detection and logout
// change its tokens. Token rows alone cannot serialize insertion of descendants.
public sealed class RefreshSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid LockStamp { get; set; } = Guid.NewGuid();
}
