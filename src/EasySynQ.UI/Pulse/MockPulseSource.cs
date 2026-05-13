namespace EasySynQ.UI.Pulse;

/// <summary>
/// Hardcoded <see cref="IPulseSource"/> for E2.3 — returns the same
/// tile data the prototype renders (lines 482–496 of
/// <c>docs/UI_PROTOTYPE.html</c>). Real implementations land with
/// their owning module phases; this class disappears once at least
/// one real publisher exists and the host registers a real source.
/// </summary>
/// <remarks>
/// Tiles are constructed once at first call and cached for the
/// lifetime of the instance — the data is static. The async
/// signature is honored only so the consumer-side contract
/// (<see cref="IPulseSource.GetTilesAsync"/>) stays the same shape
/// once a real source needs I/O.
/// </remarks>
public sealed class MockPulseSource : IPulseSource
{
    private readonly IReadOnlyList<PulseTile> _tiles;

    /// <summary>Constructs the mock source.</summary>
    public MockPulseSource()
    {
        _tiles = new PulseTile[]
        {
            // Red — critical.
            new("cal.overdue",     PulseCategory.Calibration, PulseSeverity.Red,   "Calibrations overdue",        "Load TC #1 · 3 days · 2 more",         3),
            new("ncr.open",        PulseCategory.Ncr,         PulseSeverity.Red,   "Open NCRs",                   "NCR-2026-0033 · J-2026-0847 held",     2),
            new("training.locked", PulseCategory.Training,    PulseSeverity.Red,   "Operator production locked",  "D. Park · SOP-HT-001 expired",         1),

            // Amber — review needed.
            new("cal.upcoming",       PulseCategory.Calibration,      PulseSeverity.Amber, "PM / Cal due in 7 days",       "8 tasks across 4 assets",                       8),
            new("production.gates",   PulseCategory.Production,       PulseSeverity.Amber, "QA gates awaiting signature",  "Chart · Inspection · CoC issuance",            12),
            new("capa.pending",       PulseCategory.Capa,             PulseSeverity.Amber, "CAPAs pending verification",   "2 entering 30-day window",                      4),
            new("training.expiring",  PulseCategory.Training,         PulseSeverity.Amber, "Operator quals expiring",      "J. Hayes · L. Martinez · <30d",                 2),
            new("risk.overdue",       PulseCategory.Risk,             PulseSeverity.Amber, "Risk review overdue",          "R-05 · Key person dependency",                  1),
            new("doc.compat",         PulseCategory.Documents,        PulseSeverity.Amber, "Doc compatibility review",     "SOP-HT-014 · ASTM E18 updated",                 1),
            new("supplier.audit",     PulseCategory.Suppliers,        PulseSeverity.Amber, "Supplier audit overdue",       "Coastal Metals Ltd. · 14 months",               1),

            // Green — reverse-pulse / good news.
            new("ncr.streak",          PulseCategory.Ncr,              PulseSeverity.Green, "47 days since last Major NCR",       "Last major: NCR-2026-0019 · Jan 22",           47),
            new("review.cadence",      PulseCategory.ManagementReview, PulseSeverity.Green, "Management Review on cadence",       "Q1 2026 signed Apr 15 · Q2 due Jul 15",        null),
            new("trace.coverage",      PulseCategory.Traceability,     PulseSeverity.Green, "All material lots traceable",        "0 jobs with missing lot reference",            100),
            new("audit.snapshots",     PulseCategory.AuditIntegrity,   PulseSeverity.Green, "Snapshot integrity clean",           "90 daily · 12 weekly · 4 monthly verified",    null),
            new("customer.complaints", PulseCategory.Customer,         PulseSeverity.Green, "No customer complaints YTD",         "Q1 OTD 96.4% · 1 commendation",                0),
        };
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PulseTile>> GetTilesAsync(CancellationToken cancellationToken)
        => Task.FromResult(_tiles);
}
