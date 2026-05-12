using AwesomeAssertions;

using EasySynQ.Data.Repositories;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Tests.Integration.Data.Interceptors;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data.Repositories;

public class UnitOfWorkTests : InterceptorIntegrationTestBase
{
    [Fact]
    public async Task ExecuteInTransactionAsync_CommitsOnSuccessAsync()
    {
        var roleId = Guid.NewGuid();

        await using (var ctx = NewContext())
        {
            var uow = new UnitOfWork(ctx);
            await uow.ExecuteInTransactionAsync(async ct =>
            {
                ctx.Roles.Add(new Role(roleId, "ViaTx", "Inside a transaction."));
                await ctx.SaveChangesAsync(ct);
            }, Ct);
        }

        await using (var queryCtx = NewContext())
        {
            var exists = await queryCtx.Roles.AnyAsync(r => r.Id == roleId, Ct);
            exists.Should().BeTrue();
        }
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_RollsBackOnExceptionAsync()
    {
        var roleId = Guid.NewGuid();

        await using (var ctx = NewContext())
        {
            var uow = new UnitOfWork(ctx);

            var act = async () => await uow.ExecuteInTransactionAsync(async ct =>
            {
                ctx.Roles.Add(new Role(roleId, "TxRollback", "Will be rolled back."));
                await ctx.SaveChangesAsync(ct);
                throw new InvalidOperationException("intentional rollback");
            }, Ct);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*intentional rollback*");
        }

        await using (var queryCtx = NewContext())
        {
            // Row was inserted-and-saved inside the transaction, then the
            // transaction rolled back. The row is gone — including from
            // the IgnoreQueryFilters view.
            var exists = await queryCtx.Roles.IgnoreQueryFilters().AnyAsync(r => r.Id == roleId, Ct);
            exists.Should().BeFalse();
        }
    }

    [Fact]
    public async Task TwoSaveChangesInOneTransaction_BothRollBackAtomicallyAsync()
    {
        var roleA = Guid.NewGuid();
        var roleB = Guid.NewGuid();

        await using (var ctx = NewContext())
        {
            var uow = new UnitOfWork(ctx);

            var act = async () => await uow.ExecuteInTransactionAsync(async ct =>
            {
                ctx.Roles.Add(new Role(roleA, "TxA", "First save inside transaction."));
                await ctx.SaveChangesAsync(ct);

                ctx.Roles.Add(new Role(roleB, "TxB", "Second save inside transaction."));
                await ctx.SaveChangesAsync(ct);

                throw new InvalidOperationException("rollback after two saves");
            }, Ct);

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        await using (var queryCtx = NewContext())
        {
            var existsA = await queryCtx.Roles.IgnoreQueryFilters().AnyAsync(r => r.Id == roleA, Ct);
            var existsB = await queryCtx.Roles.IgnoreQueryFilters().AnyAsync(r => r.Id == roleB, Ct);
            existsA.Should().BeFalse();
            existsB.Should().BeFalse();
        }
    }

    [Fact]
    public async Task TwoSaveChangesInOneTransaction_BothCommitAtomicallyAsync()
    {
        // The complement of the rollback test — if the work completes
        // without throwing, both saves are visible after commit.
        var roleA = Guid.NewGuid();
        var roleB = Guid.NewGuid();

        await using (var ctx = NewContext())
        {
            var uow = new UnitOfWork(ctx);
            await uow.ExecuteInTransactionAsync(async ct =>
            {
                ctx.Roles.Add(new Role(roleA, "CommitA", "First save."));
                await ctx.SaveChangesAsync(ct);

                ctx.Roles.Add(new Role(roleB, "CommitB", "Second save."));
                await ctx.SaveChangesAsync(ct);
            }, Ct);
        }

        await using (var queryCtx = NewContext())
        {
            (await queryCtx.Roles.AnyAsync(r => r.Id == roleA, Ct)).Should().BeTrue();
            (await queryCtx.Roles.AnyAsync(r => r.Id == roleB, Ct)).Should().BeTrue();
        }
    }
}
