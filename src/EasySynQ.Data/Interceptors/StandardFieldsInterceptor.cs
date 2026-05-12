using System.Security.Cryptography;

using EasySynQ.Domain.Common;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EasySynQ.Data.Interceptors;

/// <summary>
/// Stamps the standard fields inherited from
/// <see cref="AuditableEntity"/> — <c>CreatedBy</c>, <c>CreatedUtc</c>,
/// <c>ModifiedBy</c>, <c>ModifiedUtc</c>, <c>RowVersion</c> — on every
/// save, so domain code (and tests) never need to populate them
/// themselves.
/// </summary>
/// <remarks>
/// <para>
/// Operates only on entities that derive from <see cref="AuditableEntity"/>
/// (this includes <c>SignableEntity</c> by inheritance). Entities that do
/// not inherit <see cref="AuditableEntity"/> — for example,
/// <c>AuditLogEntry</c> — are passed through untouched.
/// </para>
/// <para>
/// Behavior by EF Core entity state:
/// <list type="bullet">
///   <item><c>Added</c>: all four audit fields set; <c>RowVersion</c>
///   assigned 8 fresh cryptographically-random bytes.</item>
///   <item><c>Modified</c>: <c>ModifiedBy</c> / <c>ModifiedUtc</c> updated;
///   <c>RowVersion</c> rotated to fresh bytes. <c>CreatedBy</c> /
///   <c>CreatedUtc</c> are not touched.</item>
///   <item><c>Deleted</c> (hard delete): no fields are stamped — the row
///   is being removed.</item>
/// </list>
/// </para>
/// <para>
/// Registration order matters: this interceptor must run **before**
/// <c>AuditSaveChangesInterceptor</c>. The audit interceptor reads the
/// change tracker's <c>CurrentValues</c> after this interceptor has
/// stamped the fields, so the resulting audit-log <c>After</c> snapshot
/// captures the new values. See
/// <c>EasySynQDbContextOptionsExtensions.UseEasySynQInterceptors</c>
/// where the ordering is established.
/// </para>
/// </remarks>
public sealed class StandardFieldsInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IClock _clock;

    /// <summary>
    /// Constructs the interceptor with the dependencies it reads on every
    /// save.
    /// </summary>
    public StandardFieldsInterceptor(ICurrentUserAccessor currentUser, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(clock);
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var actor = ResolveActor();
        var now = _clock.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    SetProp(entry, nameof(AuditableEntity.CreatedBy), actor);
                    SetProp(entry, nameof(AuditableEntity.CreatedUtc), now);
                    SetProp(entry, nameof(AuditableEntity.ModifiedBy), actor);
                    SetProp(entry, nameof(AuditableEntity.ModifiedUtc), now);
                    SetProp(entry, nameof(AuditableEntity.RowVersion), NewRowVersion());
                    break;

                case EntityState.Modified:
                    SetProp(entry, nameof(AuditableEntity.ModifiedBy), actor);
                    SetProp(entry, nameof(AuditableEntity.ModifiedUtc), now);
                    SetProp(entry, nameof(AuditableEntity.RowVersion), NewRowVersion());
                    break;

                // EntityState.Deleted: hard-delete; no fields to stamp.
                // EntityState.Unchanged / Detached: not in this set; the
                // foreach already filters to AuditableEntity entries with
                // active state changes.
                default:
                    break;
            }
        }
    }

    private static void SetProp(EntityEntry entry, string propertyName, object value)
    {
        entry.Property(propertyName).CurrentValue = value;
    }

    private string ResolveActor()
    {
        var id = _currentUser.UserId;
        return id.HasValue ? id.Value.ToString() : "system";
    }

    private static byte[] NewRowVersion()
    {
        var bytes = new byte[8];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }
}
