// <copyright file="CashShopCatalogStartupFlowTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.CashShop;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.Persistence.Initialization.DataInitialization;
using NUnit.Framework;

/// <summary>
/// Tests the CashShop catalog startup sequencing around database schema updates.
/// </summary>
[TestFixture]
public class CashShopCatalogStartupFlowTests
{
    [Test]
    public async Task AutomaticSchemaUpdate_SeedsOnceAfterSuccessfulUpdateAsync()
    {
        var calls = new List<string>();

        var updated = await CashShopCatalogStartupInitializer.ApplySchemaUpdateAndInitializeAsync(
            true,
            () =>
            {
                calls.Add("schema");
                return Task.CompletedTask;
            },
            () =>
            {
                calls.Add("cashshop");
                return Task.CompletedTask;
            },
            () => null,
            _ => { });

        Assert.That(updated, Is.True);
        Assert.That(calls, Is.EqualTo(new[] { "schema", "cashshop" }));
    }

    [Test]
    public async Task ConfirmedManualSchemaUpdate_SeedsOnceAfterSuccessfulUpdateAsync()
    {
        var calls = new List<string>();

        var updated = await CashShopCatalogStartupInitializer.ApplySchemaUpdateAndInitializeAsync(
            false,
            () =>
            {
                calls.Add("schema");
                return Task.CompletedTask;
            },
            () =>
            {
                calls.Add("cashshop");
                return Task.CompletedTask;
            },
            () => "y",
            _ => { });

        Assert.That(updated, Is.True);
        Assert.That(calls, Is.EqualTo(new[] { "schema", "cashshop" }));
    }

    [Test]
    public async Task CancelledSchemaUpdate_DoesNotApplyOrSeedAsync()
    {
        var schemaUpdateCalled = false;
        var cashShopSeedCalled = false;

        var updated = await CashShopCatalogStartupInitializer.ApplySchemaUpdateAndInitializeAsync(
            false,
            () =>
            {
                schemaUpdateCalled = true;
                return Task.CompletedTask;
            },
            () =>
            {
                cashShopSeedCalled = true;
                return Task.CompletedTask;
            },
            () => "n",
            _ => { });

        Assert.That(updated, Is.False);
        Assert.That(schemaUpdateCalled, Is.False);
        Assert.That(cashShopSeedCalled, Is.False);
    }

    [Test]
    public void FailedSchemaUpdate_DoesNotSeed()
    {
        var cashShopSeedCalled = false;

        Assert.ThrowsAsync<InvalidOperationException>(async () => await CashShopCatalogStartupInitializer.ApplySchemaUpdateAndInitializeAsync(
            true,
            () => Task.FromException(new InvalidOperationException("Schema update failed.")),
            () =>
            {
                cashShopSeedCalled = true;
                return Task.CompletedTask;
            },
            () => null,
            _ => { }));

        Assert.That(cashShopSeedCalled, Is.False);
    }

    [Test]
    public void MissingCatalogEnvironmentVariable_SkipsWithoutError()
    {
        var variableName = CashShopCatalogInitializer.CatalogPathEnvironmentVariable;
        var originalValue = Environment.GetEnvironmentVariable(variableName);

        try
        {
            Environment.SetEnvironmentVariable(variableName, null);

            Assert.DoesNotThrowAsync(async () => await CashShopCatalogStartupInitializer.InitializeIfConfiguredAsync(
                new InMemoryPersistenceContextProvider(),
                new NullLoggerFactory()));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, originalValue);
        }
    }
}
