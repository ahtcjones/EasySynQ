namespace EasySynQ.Domain.Enums;

/// <summary>
/// Action recorded in the global audit log for any compliance-critical
/// change. Per SPEC §3.4 and ADR 0002.
/// </summary>
public enum AuditAction
{
    /// <summary>Entity inserted into the operational store.</summary>
    Insert,

    /// <summary>Entity updated in place.</summary>
    Update,

    /// <summary>
    /// Entity soft-deleted: the operational row's <c>IsDeleted</c> flag was
    /// set to <see langword="true"/>. The row remains queryable through
    /// historical paths.
    /// </summary>
    Delete,

    /// <summary>
    /// Entity hard-deleted: the operational row was physically removed.
    /// Per ADR 0002, the audit row's <c>Before</c> snapshot captures the
    /// full final entity state; <c>After</c> is <see langword="null"/>.
    /// </summary>
    HardDelete,
}
