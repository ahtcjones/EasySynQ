using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

using EasySynQ.Domain.Enums;

namespace EasySynQ.UI.Printing;

/// <summary>
/// Pure-function builder that turns a <see cref="DocumentPrintViewModel"/>
/// into a printable <see cref="FlowDocument"/> (ADR 0008 C7 / SPEC §4.5).
/// Hosted by <see cref="DocumentPrintService"/>'s orchestration but kept
/// here so the print-template shape is independently unit-testable: a
/// fixture DTO + this method = an inspectable Block tree.
/// </summary>
/// <remarks>
/// <para>
/// <b>US Letter at 96 DPI.</b>
/// <see cref="FlowDocument.PageWidth"/>/<see cref="FlowDocument.PageHeight"/>
/// are pinned to 816 × 1056 px (8.5 × 11 inches), with
/// <see cref="FlowDocument.PagePadding"/> = 48 px (0.5 inch margins).
/// The content box is 720 × 960 px after padding.
/// </para>
/// <para>
/// <b>What renders (per SPEC §4.5 + CLAUDE.md "audit trail inlined,
/// signatures highlighted, US Letter").</b>
/// </para>
/// <list type="bullet">
///   <item>Header: Document Number + Title.</item>
///   <item>Metadata block: revision label, lifecycle display,
///   effective-from instant when set, author display, snapshot
///   timestamp.</item>
///   <item>Signatures: author signature row plus per-reviewer rows;
///   Signed rows are highlighted with the <c>BrushGreenBg</c> token
///   per the C7 planning default (SPEC §3.4 audit-anchor fields —
///   user id, UTC timestamp, role-at-time-of-sign, payload hash —
///   inlined into each row).</item>
///   <item>Reviewer comments: chronological listing.</item>
///   <item>Audit trail: chronological table of every audit row for
///   the Document and its revisions (Action, EntityType, EntityId,
///   UtcTimestamp, Actor).</item>
///   <item>Footer: record id, snapshot timestamp, attached-PDF note
///   with SHA-256 prefix (the PDF content itself is printed via
///   PDF.js's own toolbar print button per the C7 planning
///   default).</item>
/// </list>
/// </remarks>
public static class DocumentPrintBuilder
{
    private const double PageWidth = 816.0;
    private const double PageHeight = 1056.0;
    private const double PagePadding = 48.0;
    private const double BodyFontSize = 11.0;
    private const double CaptionFontSize = 9.5;
    private const double HeadingFontSize = 18.0;
    private const double SubheadingFontSize = 13.0;
    // Extra left-indent for bare Paragraph blocks (title, section
    // headings, comment rows). Matches the table cells' inner
    // Padding (8 px) so paragraphs visually align with the table
    // content sitting under them rather than rendering at the bare
    // PagePadding edge.
    private const double BlockIndent = 8.0;

    // Palette mirrors Resources/Colors.xaml. The print FlowDocument
    // lives outside the WPF theme dictionary (it's rendered through
    // PrintDialog, not the live shell), so the palette is referenced
    // via SolidColorBrush literals here. A palette change must touch
    // both Colors.xaml and this constant table.
    private static readonly SolidColorBrush BrushText = Frozen(Color.FromArgb(0xFF, 0x1A, 0x1E, 0x24));
    private static readonly SolidColorBrush BrushTextDim = Frozen(Color.FromArgb(0xFF, 0x55, 0x60, 0x6D));
    private static readonly SolidColorBrush BrushBorder = Frozen(Color.FromArgb(0xFF, 0xC8, 0xCD, 0xD5));
    private static readonly SolidColorBrush BrushHeaderBg = Frozen(Color.FromArgb(0xFF, 0xF4, 0xF5, 0xF7));
    private static readonly SolidColorBrush BrushGreenBg = Frozen(Color.FromArgb(0xFF, 0xE2, 0xF5, 0xE8));
    private static readonly SolidColorBrush BrushAmberBg = Frozen(Color.FromArgb(0xFF, 0xFB, 0xEC, 0xCC));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Builds a printable <see cref="FlowDocument"/> from the supplied
    /// snapshot.
    /// </summary>
    /// <param name="vm">Snapshot DTO assembled by
    /// <see cref="DocumentPrintService"/>.</param>
    /// <returns>A <see cref="FlowDocument"/> ready for paging or sending
    /// to <see cref="System.Windows.Controls.PrintDialog.PrintDocument"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="vm"/> is <see langword="null"/>.</exception>
    public static FlowDocument BuildFlowDocument(DocumentPrintViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);

        var doc = new FlowDocument
        {
            PageWidth = PageWidth,
            PageHeight = PageHeight,
            ColumnWidth = PageWidth - (2 * PagePadding),
            PagePadding = new Thickness(PagePadding),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = BodyFontSize,
            Foreground = BrushText,
            TextAlignment = TextAlignment.Left,
        };

        doc.Blocks.Add(BuildHeaderSection(vm));
        doc.Blocks.Add(BuildMetadataSection(vm));
        doc.Blocks.Add(BuildSignaturesSection(vm));
        if (vm.Comments.Count > 0)
        {
            doc.Blocks.Add(BuildCommentsSection(vm));
        }
        if (vm.AuditTrail.Count > 0)
        {
            doc.Blocks.Add(BuildAuditSection(vm));
        }
        doc.Blocks.Add(BuildFooterSection(vm));

        return doc;
    }

    private static Section BuildHeaderSection(DocumentPrintViewModel vm)
    {
        var section = new Section { Name = "HeaderSection" };

        // 8 px Margin.Left matches the table cells' inner Padding so
        // the header text aligns horizontally with the metadata table
        // values that follow. Without this, the bare Paragraph
        // rendered at PagePadding's edge — visually hugging the
        // printable-area boundary while the tables below sat 8 px
        // further in.
        var titleParagraph = new Paragraph
        {
            FontSize = HeadingFontSize,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(BlockIndent, 0, 0, 2),
        };
        titleParagraph.Inlines.Add(new Run(vm.Document.Title));
        section.Blocks.Add(titleParagraph);

        var numberParagraph = new Paragraph
        {
            FontSize = SubheadingFontSize,
            Foreground = BrushTextDim,
            Margin = new Thickness(BlockIndent, 0, 0, 12),
        };
        numberParagraph.Inlines.Add(new Run(vm.Document.Number));
        section.Blocks.Add(numberParagraph);

        return section;
    }

    private static Section BuildMetadataSection(DocumentPrintViewModel vm)
    {
        var section = new Section { Name = "MetadataSection" };

        var table = NewMetadataTable();

        AppendMetadataRow(table, "Revision", vm.CurrentRevision.RevisionLabel);
        AppendMetadataRow(table, "Lifecycle", vm.CurrentRevisionLifecycleDisplay);
        AppendMetadataRow(table, "Author", vm.AuthorDisplay);

        if (vm.CurrentRevision.EffectiveFromUtc is { } effFrom)
        {
            AppendMetadataRow(table, "Effective from", FormatUtc(effFrom));
        }
        if (vm.CurrentRevision.ApprovedAtUtc is { } approvedAt)
        {
            AppendMetadataRow(table, "Approved on", FormatUtc(approvedAt));
        }
        if (vm.Document.RetiredAtUtc is { } retiredAt)
        {
            AppendMetadataRow(table, "Retired on", FormatUtc(retiredAt));
        }

        section.Blocks.Add(table);
        return section;
    }

    private static Section BuildSignaturesSection(DocumentPrintViewModel vm)
    {
        var section = new Section { Name = "SignaturesSection" };
        section.Blocks.Add(NewSectionHeading("Signatures"));

        // Two-column table: label column + value column. Each signature
        // is its own multi-line "value" with header pill (Author /
        // Reviewer label), display name, role-at-time-of-sign, UTC
        // instant, and (when present) payload-hash prefix.
        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 0, 0, 12),
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(110) });
        table.Columns.Add(new TableColumn { Width = new GridLength(610) });
        table.RowGroups.Add(new TableRowGroup());

        AppendSignatureRow(
            table,
            label: "Author",
            displayName: vm.AuthorDisplay,
            status: "Author submission",
            isSigned: vm.AuthorSignature is not null,
            signature: vm.AuthorSignature);

        foreach (var reviewer in vm.Reviewers)
        {
            var statusLabel = reviewer.Status switch
            {
                DocumentReviewAssignmentStatus.Signed => "Signed",
                DocumentReviewAssignmentStatus.Discarded => "Discarded (returned to Draft)",
                _ => "Pending",
            };
            AppendSignatureRow(
                table,
                label: "Reviewer",
                displayName: reviewer.ReviewerDisplay,
                status: statusLabel,
                isSigned: reviewer.Status == DocumentReviewAssignmentStatus.Signed,
                signature: reviewer.Signature);
        }

        section.Blocks.Add(table);
        return section;
    }

    private static Section BuildCommentsSection(DocumentPrintViewModel vm)
    {
        var section = new Section { Name = "CommentsSection" };
        section.Blocks.Add(NewSectionHeading("Reviewer comments"));

        var ordered = vm.Comments
            .OrderBy(c => c.CreatedAtUtc)
            .ToList();
        foreach (var comment in ordered)
        {
            var author = vm.UserDisplayLookup.TryGetValue(comment.AuthorUserId, out var d)
                ? d
                : "(unknown)";

            var meta = new Paragraph
            {
                FontSize = CaptionFontSize,
                Foreground = BrushTextDim,
                Margin = new Thickness(BlockIndent, 4, 0, 2),
            };
            meta.Inlines.Add(new Run(string.Create(
                CultureInfo.InvariantCulture,
                $"{author} — {FormatUtc(comment.CreatedAtUtc)}")));
            section.Blocks.Add(meta);

            var body = new Paragraph
            {
                Margin = new Thickness(BlockIndent, 0, 0, 6),
            };
            body.Inlines.Add(new Run(comment.BodyText));
            section.Blocks.Add(body);
        }

        return section;
    }

    private static Section BuildAuditSection(DocumentPrintViewModel vm)
    {
        var section = new Section { Name = "AuditSection" };
        section.Blocks.Add(NewSectionHeading("Audit trail"));

        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 0, 0, 12),
        };
        // Columns: timestamp, entity type, action, actor.
        table.Columns.Add(new TableColumn { Width = new GridLength(155) });
        table.Columns.Add(new TableColumn { Width = new GridLength(155) });
        table.Columns.Add(new TableColumn { Width = new GridLength(100) });
        table.Columns.Add(new TableColumn { Width = new GridLength(310) });
        table.RowGroups.Add(new TableRowGroup());

        AppendAuditHeaderRow(table);

        var ordered = vm.AuditTrail
            .OrderBy(a => a.UtcTimestamp)
            .ToList();
        foreach (var entry in ordered)
        {
            var actor = entry.UserId is { } uid
                ? (vm.UserDisplayLookup.TryGetValue(uid, out var d)
                    ? d
                    : uid.ToString("D", CultureInfo.InvariantCulture))
                : "(system)";
            var row = new TableRow();
            row.Cells.Add(NewCaptionCell(FormatUtc(entry.UtcTimestamp)));
            row.Cells.Add(NewCaptionCell(entry.EntityTypeName));
            row.Cells.Add(NewCaptionCell(entry.Action.ToString()));
            row.Cells.Add(NewCaptionCell(actor));
            table.RowGroups[0].Rows.Add(row);
        }

        section.Blocks.Add(table);
        return section;
    }

    private static Section BuildFooterSection(DocumentPrintViewModel vm)
    {
        var section = new Section { Name = "FooterSection" };
        section.Blocks.Add(NewSectionHeading("Record"));

        var table = NewMetadataTable();
        AppendMetadataRow(table, "Document Id", vm.Document.Id.ToString("D", CultureInfo.InvariantCulture));
        AppendMetadataRow(table, "Revision Id", vm.CurrentRevision.Id.ToString("D", CultureInfo.InvariantCulture));
        AppendMetadataRow(table, "Snapshot", FormatUtc(vm.SnapshotTimestampUtc));

        // PDF content is printed through PDF.js's own toolbar print
        // button — the metadata-wrapper print job here is the
        // surrounding record (signatures, audit, comments). Surface
        // the attached PDF's identifier so the auditor can correlate
        // this print with the PDF binary if both are captured.
        var blobIdText = vm.CurrentRevision.VaultBlobId is { } blobId
            ? blobId.ToString("D", CultureInfo.InvariantCulture)
            : "(no PDF attached)";
        AppendMetadataRow(table, "Attached PDF", blobIdText);

        section.Blocks.Add(table);
        return section;
    }

    // ─── helpers ────────────────────────────────────────────────────

    private static Paragraph NewSectionHeading(string text)
    {
        var p = new Paragraph
        {
            FontSize = SubheadingFontSize,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(BlockIndent, 14, 0, 6),
        };
        p.Inlines.Add(new Run(text));
        return p;
    }

    private static Table NewMetadataTable()
    {
        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 0, 0, 8),
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(110) });
        table.Columns.Add(new TableColumn { Width = new GridLength(610) });
        table.RowGroups.Add(new TableRowGroup());
        return table;
    }

    private static void AppendMetadataRow(Table table, string label, string value)
    {
        var row = new TableRow();
        var labelCell = NewCell(label);
        labelCell.FontWeight = FontWeights.SemiBold;
        labelCell.Foreground = BrushTextDim;
        labelCell.FontSize = CaptionFontSize;
        row.Cells.Add(labelCell);
        row.Cells.Add(NewCell(value));
        table.RowGroups[0].Rows.Add(row);
    }

    private static void AppendSignatureRow(
        Table table,
        string label,
        string displayName,
        string status,
        bool isSigned,
        Domain.Entities.Audit.Signature? signature)
    {
        var row = new TableRow();
        var labelCell = NewCell(label);
        labelCell.FontWeight = FontWeights.SemiBold;
        labelCell.Foreground = BrushTextDim;
        labelCell.FontSize = CaptionFontSize;
        row.Cells.Add(labelCell);

        var detailCell = new TableCell
        {
            Padding = new Thickness(8, 4, 8, 4),
            Background = isSigned ? BrushGreenBg : (status.StartsWith("Discarded", StringComparison.Ordinal) ? BrushAmberBg : null),
            BorderBrush = BrushBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        var line1 = new Paragraph { Margin = new Thickness(0, 0, 0, 1) };
        line1.Inlines.Add(new Run(displayName) { FontWeight = FontWeights.SemiBold });
        line1.Inlines.Add(new Run(string.Create(CultureInfo.InvariantCulture, $"  ·  {status}"))
        {
            Foreground = BrushTextDim,
            FontSize = CaptionFontSize,
        });
        detailCell.Blocks.Add(line1);

        if (signature is not null)
        {
            var line2 = new Paragraph
            {
                FontSize = CaptionFontSize,
                Foreground = BrushTextDim,
                Margin = new Thickness(0),
            };
            line2.Inlines.Add(new Run(string.Create(
                CultureInfo.InvariantCulture,
                $"Signed {FormatUtc(signature.UtcTimestamp)} as {signature.RoleAtTimeOfSign}.  Payload SHA-256: {ShortHash(signature.PayloadHash)}")));
            detailCell.Blocks.Add(line2);
        }

        row.Cells.Add(detailCell);
        table.RowGroups[0].Rows.Add(row);
    }

    private static void AppendAuditHeaderRow(Table table)
    {
        var row = new TableRow { Background = BrushHeaderBg };
        row.Cells.Add(NewHeaderCell("Timestamp"));
        row.Cells.Add(NewHeaderCell("Entity"));
        row.Cells.Add(NewHeaderCell("Action"));
        row.Cells.Add(NewHeaderCell("Actor"));
        table.RowGroups[0].Rows.Add(row);
    }

    private static TableCell NewCell(string text)
    {
        var cell = new TableCell
        {
            Padding = new Thickness(8, 4, 8, 4),
            BorderBrush = BrushBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        var p = new Paragraph { Margin = new Thickness(0) };
        p.Inlines.Add(new Run(text));
        cell.Blocks.Add(p);
        return cell;
    }

    private static TableCell NewCaptionCell(string text)
    {
        var cell = NewCell(text);
        cell.FontSize = CaptionFontSize;
        return cell;
    }

    private static TableCell NewHeaderCell(string text)
    {
        var cell = NewCell(text);
        cell.FontSize = CaptionFontSize;
        cell.FontWeight = FontWeights.SemiBold;
        cell.Foreground = BrushTextDim;
        return cell;
    }

    private static string FormatUtc(DateTime utc)
        => utc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static string ShortHash(string hash)
    {
        if (string.IsNullOrEmpty(hash)) return "(none)";
        if (hash.Length <= 16) return hash;
        return hash[..16] + "…";
    }
}
