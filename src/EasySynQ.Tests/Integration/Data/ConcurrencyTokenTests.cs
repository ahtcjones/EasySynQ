using AwesomeAssertions;

using EasySynQ.Domain.Entities.Identity;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data;

public class ConcurrencyTokenTests : IntegrationTestBase
{
    [Fact]
    public async Task StaleRowVersion_OnSave_ThrowsDbUpdateConcurrencyExceptionAsync()
    {
        // Insert a User and capture its RowVersion as the "original" value
        // a long-running scope would have loaded.
        var id = Guid.NewGuid();
        var user = new User(id, "bob", "Bob", "hashed", "salted", 600_000, mustChangePassword: false);

        await using (var setupCtx = NewContext())
        {
            setupCtx.Users.Add(user);
            await setupCtx.SaveChangesAsync(Ct);
        }

        // Scope 1: load the user (tracked entity with current RowVersion).
        await using var ctx1 = NewContext();
        var u1 = await ctx1.Users.SingleAsync(u => u.Id == id, Ct);

        // Simulate a concurrent update from elsewhere by changing RowVersion
        // at the DB level without going through ctx1. EF's interceptor in
        // Chunk B will do this in the normal flow; here we do it via raw SQL
        // to isolate the EF concurrency-check machinery.
        var newRowVersion = new byte[] { 0x42, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 };
        await using (var hijackCtx = NewContext())
        {
            await hijackCtx.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Users SET RowVersion = {newRowVersion} WHERE Id = {id}", Ct);
        }

        // Now modify ctx1's tracked entity and try to save. EF's UPDATE
        // statement includes WHERE RowVersion = @original; the DB row's
        // RowVersion is now [0x42…], so the UPDATE affects 0 rows and EF
        // raises DbUpdateConcurrencyException.
        ctx1.Entry(u1).Property(nameof(User.MustChangePassword)).CurrentValue = true;

        var act = async () => await ctx1.SaveChangesAsync(Ct);
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
