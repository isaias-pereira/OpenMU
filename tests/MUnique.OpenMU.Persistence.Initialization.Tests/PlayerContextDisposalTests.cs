// <copyright file="PlayerContextDisposalTests.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Tests;

using System.Data.Common;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.Persistence.EntityFramework;

/// <summary>
/// Verifies that the real player context owns its EF context and prevents later saves after
/// retirement. Connection interception prevents this fixture from accessing any database.
/// </summary>
[TestFixture]
public class PlayerContextDisposalTests
{
    /// <summary>
    /// Disposing the player context blocks a pending save, including after repeated disposal.
    /// </summary>
    /// <param name="disposeCount">The number of disposal calls before the save attempt.</param>
    [TestCase(1)]
    [TestCase(2)]
    public void Dispose_WithPendingChanges_PreventsSaveWithoutOpeningConnection(int disposeCount)
    {
        var connectionGuard = new RejectConnectionInterceptor();
        var options = new DbContextOptionsBuilder<PendingChangesContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=disposal_test;Username=disposal_test;Pooling=false")
            .AddInterceptors(connectionGuard)
            .Options;
        using var dbContext = new PendingChangesContext(options);
        var repositories = new CacheAwareRepositoryProvider(NullLoggerFactory.Instance, null);
        using var playerContext = new PlayerContext(dbContext, repositories, NullLogger<PlayerContext>.Instance);

        dbContext.Set<PendingChange>().Add(new PendingChange { Id = Guid.NewGuid() });
        Assert.That(playerContext.HasChanges, Is.True, "The test must begin with actual EF changes pending.");

        for (var i = 0; i < disposeCount; i++)
        {
            playerContext.Dispose();
        }

        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await playerContext.SaveChangesAsync().ConfigureAwait(false));
        Assert.Throws<ObjectDisposedException>(() => dbContext.ChangeTracker.HasChanges());
        Assert.That(connectionGuard.OpenAttempts, Is.Zero, "No connection should be needed to reject a save on a disposed context.");
    }

    private sealed class PendingChangesContext : DbContext
    {
        public PendingChangesContext(DbContextOptions<PendingChangesContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PendingChange>().HasKey(change => change.Id);
        }
    }

    private sealed class PendingChange
    {
        public Guid Id { get; set; }
    }

    private sealed class RejectConnectionInterceptor : DbConnectionInterceptor
    {
        public int OpenAttempts { get; private set; }

        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            this.OpenAttempts++;
            throw new InvalidOperationException("Database access is forbidden in the context disposal fixture.");
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            this.OpenAttempts++;
            throw new InvalidOperationException("Database access is forbidden in the context disposal fixture.");
        }
    }
}
