// <copyright file="CashShopStorageItemMigrationTests.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Tests;

using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.Persistence.EntityFramework;
using NUnit.Framework;

/// <summary>
/// Verifica que o Entity Framework descobre a migration do storage de presentes e que o modelo
/// mapeado usa a unicidade por aba.
/// </summary>
/// <remarks>
/// Uma migration sem o arquivo <c>.Designer.cs</c> (com os atributos <c>[DbContext]</c> e
/// <c>[Migration]</c>) compila e roda, mas o EF Core não a enxerga: ela nunca entra em
/// <c>GetPendingMigrations</c> e portanto nunca é aplicada. Estes testes exercitam a mesma
/// descoberta que <c>dotnet ef migrations list</c> usa, sem depender do banco.
/// </remarks>
[TestFixture]
[Order(1000)]
public class CashShopStorageItemMigrationTests
{
    /// <summary>
    /// O id da migration do storage de presentes, como o EF a ordena e a persiste.
    /// </summary>
    private const string GiftFlagMigrationId = "20260927120000_AddCashShopStorageGiftFlag";

    /// <summary>
    /// Gets the migrations the Entity Framework knows about, which is the same set that
    /// <c>dotnet ef migrations list</c> reports and that <c>Database.MigrateAsync()</c> applies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Building a context runs <see cref="EntityDataContext.OnConfiguring"/>, which initialises
    /// <see cref="ConnectionConfigurator"/> exactly once per process. The fixture is therefore
    /// ordered last (<c>[Order(1000)]</c>) so the other fixtures of this assembly — which expect to
    /// be the ones to initialise it — keep working. A design-time factory used here would make
    /// them fail with "provider is initialized already".
    /// </para>
    /// <para>
    /// The model and the migration assembly are built entirely in memory: nothing is written to
    /// any database.
    /// </para>
    /// </remarks>
    private static IMigrationsAssembly MigrationsAssembly => new EntityDataContextFactory().CreateDbContext(Array.Empty<string>()).Database.GetService<IMigrationsAssembly>();

    /// <summary>
    /// A migration precisa existir para o EF, o que só acontece com os atributos
    /// <c>[DbContext]</c> e <c>[Migration]</c> no arquivo parcial gerado.
    /// </summary>
    [Test]
    public void GiftFlagMigration_IsDiscoveredByEntityFramework()
    {
        var migrations = MigrationsAssembly.Migrations.Keys.ToList();

        Assert.That(
            migrations,
            Does.Contain(GiftFlagMigrationId),
            "Without the generated designer file the migration is invisible to EF and never runs.");
    }

    /// <summary>
    /// A migration do gift precisa vir depois da que cria a tabela, senão tentaria alterar uma
    /// tabela que ainda não existe.
    /// </summary>
    [Test]
    public void GiftFlagMigration_ComesAfterTheStorageTableMigration()
    {
        var migrations = MigrationsAssembly.Migrations.Keys.ToList();
        var storageIndex = migrations.ToList().IndexOf("20260926174815_AddCashShopStorage");

        Assert.That(storageIndex, Is.GreaterThanOrEqualTo(0), "The storage table migration must be known too.");
        Assert.That(migrations.ToList().IndexOf(GiftFlagMigrationId), Is.GreaterThan(storageIndex));
    }

    /// <summary>
    /// O modelo mapeado precisa da coluna IsGift e dos dois índices: o único por aba e o de
    /// filtro. Sem o único por aba, um presente colidiria com a compra de mesmo índice.
    /// </summary>
    [Test]
    public void CashShopStorageItemModel_HasPerTabUniquenessAndGiftFlagColumn()
    {
        using var context = new EntityDataContextFactory().CreateDbContext(Array.Empty<string>());
        var entity = context.Model.FindEntityType("MUnique.OpenMU.Persistence.EntityFramework.Model.CashShopStorageItem");

        Assert.That(entity, Is.Not.Null);

        var propertyNames = entity!.GetProperties().Select(p => p.Name).ToList();
        var uniqueIndexes = entity.GetIndexes()
            .Where(i => i.IsUnique)
            .Select(i => string.Join(", ", i.Properties.Select(p => p.Name)))
            .ToList();
        var allIndexes = entity.GetIndexes()
            .Select(i => string.Join(", ", i.Properties.Select(p => p.Name)))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(propertyNames, Does.Contain(nameof(CashShopStorageItem.IsGift)));
            Assert.That(
                uniqueIndexes,
                Does.Contain("AccountId, IsGift, StorageIndex"),
                "Uniqueness must be per tab so a gift can reuse slot 0 of a purchase.");
            Assert.That(
                allIndexes,
                Does.Contain("AccountId, IsGift"),
                "The tab filter needs its own index.");
            Assert.That(
                uniqueIndexes,
                Does.Not.Contain("AccountId, StorageIndex"),
                "The old global uniqueness would reject a gift colliding with a purchase.");
        });
    }
}
