namespace EasySynQ.UI.Pulse;

/// <summary>
/// Top-level grouping of Pulse tiles. SPEC §4.2 lists Calibration,
/// NCR/Quality, CAPA, Documents, Training, Risk, Suppliers, etc. — the
/// "etc." invites extension as the system grows. The values below
/// match the categories the UI prototype already surfaces in its
/// drawer data, with the SPEC's "NCR/Quality" split into
/// <see cref="Ncr"/> and <see cref="Capa"/> because they are distinct
/// workflows (a finding vs. its corrective action).
/// </summary>
/// <remarks>
/// No catch-all <c>Other</c> value. Every tile must be categorizable
/// at emit time — if a publisher cannot place a tile into one of the
/// existing values, that is a signal to add a new value, not to
/// shovel it into a misc bucket. Categorization is a load-bearing
/// decision for grouping and navigation routing; making it
/// non-trivial is the point.
/// </remarks>
public enum PulseCategory
{
    /// <summary>Calibrations and PM tasks.</summary>
    Calibration,

    /// <summary>Nonconformance reports.</summary>
    Ncr,

    /// <summary>Corrective and preventive actions.</summary>
    Capa,

    /// <summary>Document Controller events — revisions, compatibility reviews.</summary>
    Documents,

    /// <summary>Operator competency / training expirations.</summary>
    Training,

    /// <summary>Risk register reviews and overdue mitigations.</summary>
    Risk,

    /// <summary>Supplier audits, scorecards, approvals.</summary>
    Suppliers,

    /// <summary>Production-control signature gates and QA-review backlogs.</summary>
    Production,

    /// <summary>Management Review cadence and follow-up actions.</summary>
    ManagementReview,

    /// <summary>Material lot traceability — typically reverse-pulse.</summary>
    Traceability,

    /// <summary>Snapshot integrity and audit-log health — typically reverse-pulse.</summary>
    AuditIntegrity,

    /// <summary>Customer concessions / complaints — typically reverse-pulse.</summary>
    Customer,
}
