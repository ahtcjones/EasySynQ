using AwesomeAssertions;

using EasySynQ.Data.Context;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data;

public class SchemaTests : IntegrationTestBase
{
    private static readonly string[] ExpectedTables =
    [
        "Users", "Roles", "UserRoles", "AuditLogEntries",
        "Signatures", "LockReasons", "Snapshots",
    ];

    private static readonly string[] ExpectedIndexes =
    [
        "IX_Users_Username",
        "IX_Roles_Name",
        "IX_UserRoles_UserId",
        "IX_UserRoles_RoleId",
        "IX_UserRoles_EffectiveFromUtc",
        "IX_UserRoles_EffectiveToUtc",
        "IX_AuditLogEntries_EntityTypeName_EntityId_UtcTimestamp",
        "IX_AuditLogEntries_CorrelationId",
        "IX_Signatures_SignedEntityType_SignedEntityId",
        "IX_Snapshots_Tier_CreatedUtc",
        "IX_LockReasons_LockedEntityType_LockedEntityId",
    ];

    [Fact]
    public void Migration_CreatesAllExpectedTables()
    {
        using var ctx = NewContext();
        var tables = QuerySqliteMaster(ctx, "table");
        tables.Should().Contain(ExpectedTables);
    }

    [Fact]
    public void Migration_CreatesAllExpectedIndexes()
    {
        using var ctx = NewContext();
        var indexes = QuerySqliteMaster(ctx, "index");
        indexes.Should().Contain(ExpectedIndexes);
    }

    [Fact]
    public void Migration_AuditLogEntriesHasNoIsDeletedColumn()
    {
        // SPEC §3.4: the audit log table itself is the trail and does not
        // inherit AuditableEntity. Confirm IsDeleted is not present even by
        // accident, which would silently filter rows.
        using var ctx = NewContext();
        var columns = QueryTableColumns(ctx, "AuditLogEntries");
        columns.Should().NotContain("IsDeleted");
    }

    [Fact]
    public void Migration_UserRolesHasEffectiveDateColumns()
    {
        // Owned-type flattening: EffectiveDateRange becomes two scalar
        // columns on the UserRole row, not a separate table.
        using var ctx = NewContext();
        var columns = QueryTableColumns(ctx, "UserRoles");
        columns.Should().Contain("EffectiveFromUtc");
        columns.Should().Contain("EffectiveToUtc");
    }

    [Fact]
    public void Migration_LockReasonsHasChainAsJsonColumn()
    {
        // OwnsMany(...).ToJson() collapses the chain to a single column
        // rather than emitting a child LockReasonLinks table.
        using var ctx = NewContext();
        var columns = QueryTableColumns(ctx, "LockReasons");
        columns.Should().Contain("Chain");

        var tables = QuerySqliteMaster(ctx, "table");
        tables.Should().NotContain("LockReasonLinks");
    }

    private static List<string> QuerySqliteMaster(EasySynQDbContext ctx, string type)
    {
        var conn = ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            conn.Open();
        }
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT name FROM sqlite_master WHERE type='{type}' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        using var reader = cmd.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }

    private static List<string> QueryTableColumns(EasySynQDbContext ctx, string tableName)
    {
        var conn = ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            conn.Open();
        }
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = cmd.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            // PRAGMA table_info returns: cid, name, type, notnull, dflt_value, pk
            columns.Add(reader.GetString(1));
        }
        return columns;
    }
}
