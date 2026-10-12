using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Users;
using Stockly.Infrastructure.Persistence;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Persistence;

/// <summary>Rotation, family revocation and the row lock of refresh tokens on a real PostgreSQL.</summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class RefreshTokenRepositoryTests(PostgresFixture fixture)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    [Fact]
    public async Task Rotate_SavedThroughRepository_ChainsInTheSameFamily()
    {
        var user = await fixture.CreateUserAsync();
        var first = RefreshToken.StartFamily(user, Hash(), "pixel-7", TestTime.T0, Lifetime);
        await SaveAsync(first);

        RefreshToken second;
        await using (var scope = fixture.CreateScope(shopId: null))
        {
            var repository = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            second = await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    var loaded = await repository.FindByHashForUpdateAsync(first.TokenHash, token);
                    var successor = loaded!.Rotate(Hash(), TestTime.T1, Lifetime);
                    repository.Add(successor);
                    await unitOfWork.SaveChangesAsync(token);
                    return successor;
                },
                _ => true,
                CancellationToken.None);
        }

        await using var reader = fixture.CreateScope(shopId: null);
        var tokens = reader.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
        var storedFirst = await tokens.FindByHashAsync(first.TokenHash, CancellationToken.None);
        var storedSecond = await tokens.FindByHashAsync(second.TokenHash, CancellationToken.None);
        storedFirst!.RevokedAt.Should().Be(TestTime.T1);
        storedFirst.ReplacedById.Should().Be(second.Id);
        storedSecond!.FamilyId.Should().Be(first.Id);
        storedSecond.DeviceId.Should().Be("pixel-7");
        storedSecond.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeFamily_RevokesEveryActiveTokenOfThatFamilyOnly()
    {
        var user = await fixture.CreateUserAsync();
        var deviceA1 = RefreshToken.StartFamily(user, Hash(), "a", TestTime.T0, Lifetime);
        var deviceA2 = deviceA1.Rotate(Hash(), TestTime.T1, Lifetime);
        var deviceB = RefreshToken.StartFamily(user, Hash(), "b", TestTime.T0, Lifetime);
        await SaveAsync(deviceA1, deviceA2, deviceB);

        await using (var scope = fixture.CreateScope(shopId: null))
        {
            await scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>()
                .RevokeFamilyAsync(deviceA1.FamilyId, TestTime.T2, CancellationToken.None);
        }

        await using var reader = fixture.CreateScope(shopId: null);
        var tokens = reader.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
        (await tokens.FindByHashAsync(deviceA1.TokenHash, CancellationToken.None))!.RevokedAt.Should().Be(TestTime.T1, "already revoked rows keep their time");
        (await tokens.FindByHashAsync(deviceA2.TokenHash, CancellationToken.None))!.RevokedAt.Should().Be(TestTime.T2);
        (await tokens.FindByHashAsync(deviceB.TokenHash, CancellationToken.None))!.IsRevoked.Should().BeFalse("other families are untouched");
    }

    [Fact]
    public async Task FindByHash_Unknown_ReturnsNull()
    {
        await using var scope = fixture.CreateScope(shopId: null);
        var tokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();

        (await tokens.FindByHashAsync(Hash(), CancellationToken.None)).Should().BeNull();
        (await tokens.FindByHashForUpdateAsync(Hash(), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task FindByHashForUpdate_LocksTheRowUntilTheTransactionEnds()
    {
        var user = await fixture.CreateUserAsync();
        var token = RefreshToken.StartFamily(user, Hash(), null, TestTime.T0, Lifetime);
        await SaveAsync(token);

        await using var holder = fixture.CreateScope(shopId: null);
        var db = holder.ServiceProvider.GetRequiredService<StocklyDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        (await holder.ServiceProvider.GetRequiredService<IRefreshTokenRepository>()
            .FindByHashForUpdateAsync(token.TokenHash, CancellationToken.None)).Should().NotBeNull();

        var competitor = () => fixture.ExecuteAsync(
            "SET lock_timeout = '200ms'; UPDATE refresh_tokens SET device_id = 'x' WHERE token_hash = @p0", token.TokenHash);

        var error = (await competitor.Should().ThrowAsync<PostgresException>()).Which;
        error.SqlState.Should().Be(PostgresErrorCodes.LockNotAvailable);
        await transaction.RollbackAsync();
    }

    private static string Hash() => Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + Convert.ToHexStringLower(Guid.NewGuid().ToByteArray());

    private async Task SaveAsync(params RefreshToken[] tokens)
    {
        await using var scope = fixture.CreateScope(shopId: null);
        var repository = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
        foreach (var token in tokens)
        {
            repository.Add(token);
        }

        await scope.ServiceProvider.GetRequiredService<StocklyDbContext>().SaveChangesAsync();
    }
}
