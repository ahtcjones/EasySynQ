namespace EasySynQ.Domain.Enums;

/// <summary>
/// Lifecycle stage of a compliance-critical entity with respect to the
/// draft → immutable boundary described in SPEC §3.5.
/// </summary>
public enum EntityLockState
{
    /// <summary>
    /// The entity is still in the author-editable draft state. Hard-delete
    /// is permitted; updates do not require signatures.
    /// </summary>
    Draft,

    /// <summary>
    /// The entity has crossed the boundary by carrying a signature or by
    /// being referenced by a signed record. Hard-delete is rejected;
    /// further changes are restricted to soft-delete-with-reason or to
    /// controlled state transitions per the owning module's lifecycle.
    /// </summary>
    Locked,
}
