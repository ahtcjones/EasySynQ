using System.Text.Json;
using System.Text.Json.Serialization;

using EasySynQ.Domain.Common;
using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Domain.Enums;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Audit;
using EasySynQ.Services.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EasySynQ.Data.Interceptors;

/// <summary>
/// Writes one <see cref="AuditLogEntry"/> per compliance-critical CUD
/// operation performed in a single <c>SaveChanges</c>, satisfying the
/// audit-log invariant required by SPEC §3.4 and ADR 0002.
/// </summary>
/// <remarks>
/// <para>
/// Runs **after** <c>StandardFieldsInterceptor</c> so that
/// <c>CurrentValues</c> reflects the freshly-stamped audit fields. Audit
/// rows are added to the same <see cref="DbContext"/> and therefore
/// participate in the same transaction as the operational write — if the
/// operational write rolls back, so does the audit row.
/// </para>
/// <para>
/// <b>Action mapping</b> from EF Core entity state:
/// <list type="bullet">
///   <item><c>EntityState.Added</c> → <see cref="AuditAction.Insert"/>;
///   <c>Before</c> is <see langword="null"/>.</item>
///   <item><c>EntityState.Modified</c> on an <see cref="AuditableEntity"/>
///   with <c>IsDeleted</c> transitioning <c>false → true</c> →
///   <see cref="AuditAction.Delete"/> (soft delete).</item>
///   <item><c>EntityState.Modified</c> otherwise →
///   <see cref="AuditAction.Update"/>.</item>
///   <item><c>EntityState.Deleted</c> → <see cref="AuditAction.HardDelete"/>;
///   <c>After</c> is <see langword="null"/> per ADR 0002.</item>
/// </list>
/// </para>
/// <para>
/// <b>Hard-delete signaling.</b> Hard delete is communicated to this
/// interceptor by EF Core's native <see cref="EntityState.Deleted"/> —
/// any entity in that state is treated as a hard delete (the operational
/// row is being physically removed; the prior state is captured as the
/// audit row's <c>Before</c> snapshot). This is cleaner than a marker
/// interface or a context-level set because it reuses the change-tracker
/// vocabulary callers already use.
/// </para>
/// <para>
/// <b>Loop prevention.</b> Entries for <see cref="AuditLogEntry"/> itself
/// are skipped — the table is the trail (SPEC §3.4) and is not
/// audit-of-audit-logged. Without this guard, every audit row we write
/// would itself trigger another audit row, ad infinitum.
/// </para>
/// <para>
/// <b>JSON shape.</b> <c>Before</c> and <c>After</c> are JSON-serialized
/// dictionaries of the entity's mapped scalar properties produced by
/// <see cref="PropertyValues"/>. Property names use
/// <see cref="JsonNamingPolicy.CamelCase"/>; nulls are written
/// explicitly; enums are serialized as strings via
/// <see cref="JsonStringEnumConverter"/>; serialization failures throw
/// rather than silently dropping data. <c>EntityTypeName</c> is the
/// short type name (<c>"User"</c>) rather than the fully-qualified name
/// (<c>"EasySynQ.Domain.Entities.Identity.User"</c>); the short form is
/// stable across namespace refactors and the discriminator is unique in
/// practice, but if multiple entities share a short name we will revisit.
/// </para>
/// </remarks>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly ICurrentUserAccessor _currentUser;
    private readonly IClock _clock;
    private readonly IAuditCorrelationProvider _correlationProvider;

    /// <summary>Constructs the audit interceptor.</summary>
    public AuditSaveChangesInterceptor(
        ICurrentUserAccessor currentUser,
        IClock clock,
        IAuditCorrelationProvider correlationProvider)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(correlationProvider);
        _currentUser = currentUser;
        _clock = clock;
        _correlationProvider = correlationProvider;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        WriteAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        WriteAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void WriteAuditEntries(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Resolve dependencies ONCE per save so all rows produced by a
        // single SaveChanges share the same timestamp and correlation id.
        var timestamp = _clock.UtcNow;
        var correlationId = _correlationProvider.CurrentCorrelationId ?? Guid.NewGuid();
        var userId = _currentUser.UserId;

        // Snapshot the entries before iterating: adding AuditLogEntry rows
        // mid-enumeration would mutate the change tracker we're walking.
        var auditable = context.ChangeTracker
            .Entries()
            .Where(e => e.Entity is not AuditLogEntry)
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        var rowsToAdd = new List<AuditLogEntry>(capacity: auditable.Count);

        foreach (var entry in auditable)
        {
            var action = ResolveAction(entry);
            if (action is null)
            {
                continue;
            }

            var entityTypeName = entry.Entity.GetType().Name;
            var entityId = ResolveEntityId(entry);
            var before = action == AuditAction.Insert ? null : Serialize(entry.OriginalValues);
            var after = action == AuditAction.HardDelete ? null : Serialize(entry.CurrentValues);

            rowsToAdd.Add(new AuditLogEntry(
                Guid.NewGuid(),
                timestamp,
                userId,
                entityTypeName,
                entityId,
                action.Value,
                before,
                after,
                correlationId));
        }

        foreach (var row in rowsToAdd)
        {
            context.Add(row);
        }
    }

    private static AuditAction? ResolveAction(EntityEntry entry) => entry.State switch
    {
        EntityState.Added => AuditAction.Insert,
        EntityState.Deleted => AuditAction.HardDelete,
        EntityState.Modified => DetectSoftDeleteOrUpdate(entry),
        _ => null,
    };

    private static AuditAction DetectSoftDeleteOrUpdate(EntityEntry entry)
    {
        if (entry.Entity is not AuditableEntity)
        {
            return AuditAction.Update;
        }

        var originalDeleted = (bool)(entry.OriginalValues[nameof(AuditableEntity.IsDeleted)] ?? false);
        var currentDeleted = (bool)(entry.CurrentValues[nameof(AuditableEntity.IsDeleted)] ?? false);
        return !originalDeleted && currentDeleted
            ? AuditAction.Delete
            : AuditAction.Update;
    }

    private static string ResolveEntityId(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null || key.Properties.Count == 0)
        {
            return "?";
        }

        if (key.Properties.Count == 1)
        {
            var single = entry.Property(key.Properties[0].Name).CurrentValue;
            return single?.ToString() ?? "null";
        }

        return string.Join(
            "|",
            key.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? "null"));
    }

    private static string Serialize(PropertyValues values)
    {
        var dict = new Dictionary<string, object?>(values.Properties.Count);
        foreach (var prop in values.Properties)
        {
            dict[prop.Name] = values[prop.Name];
        }
        // JsonSerializer throws on un-serializable values by default — we
        // do NOT swallow that. A failed serialization indicates a domain
        // type we cannot audit and must be fixed at the model layer, not
        // silently dropped.
        return JsonSerializer.Serialize(dict, JsonOptions);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // DictionaryKeyPolicy is the load-bearing setting here:
            // PropertyNamingPolicy only renames C# properties, not
            // dictionary KEYS. Since we serialize a
            // Dictionary<string, object?> built from the entity's
            // PropertyValues, the keys are the property names verbatim;
            // DictionaryKeyPolicy.CamelCase converts them on serialization.
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        opts.Converters.Add(new JsonStringEnumConverter());
        return opts;
    }
}
