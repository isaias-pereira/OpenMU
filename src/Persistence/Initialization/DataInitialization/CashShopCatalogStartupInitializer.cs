// <copyright file="CashShopCatalogStartupInitializer.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.DataInitialization;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Coordinates the optional CashShop catalog seed during server startup.
/// </summary>
public static class CashShopCatalogStartupInitializer
{
    /// <summary>
    /// Seeds the catalog when <see cref="CashShopCatalogInitializer.CatalogPathEnvironmentVariable"/> is configured.
    /// </summary>
    public static async Task InitializeIfConfiguredAsync(
        IPersistenceContextProvider persistenceContextProvider,
        ILoggerFactory loggerFactory,
        GameConfiguration? gameConfiguration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(persistenceContextProvider);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var catalogPath = Environment.GetEnvironmentVariable(CashShopCatalogInitializer.CatalogPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(catalogPath))
        {
            loggerFactory.CreateLogger<CashShopCatalogInitializer>().LogInformation(
                "CashShop catalog seed skipped because {EnvironmentVariable} is not set.",
                CashShopCatalogInitializer.CatalogPathEnvironmentVariable);
            return;
        }

        gameConfiguration ??= await GetDefaultGameConfigurationAsync(persistenceContextProvider, cancellationToken).ConfigureAwait(false);
        if (gameConfiguration is null)
        {
            throw new InvalidOperationException("CashShop catalog seed is configured, but no GameConfiguration exists.");
        }

        var parser = new CashShopCatalogParser(loggerFactory.CreateLogger<CashShopCatalogParser>());
        var catalogData = await parser.ParseCatalogAsync(
            Path.Combine(catalogPath, "IBSCategory.txt"),
            Path.Combine(catalogPath, "IBSPackage.txt"),
            Path.Combine(catalogPath, "IBSProduct.txt"),
            cancellationToken).ConfigureAwait(false);

        var initializer = new CashShopCatalogInitializer(
            persistenceContextProvider,
            loggerFactory.CreateLogger<CashShopCatalogInitializer>());
        await initializer.InitializeAsync(catalogData, gameConfiguration, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a schema update, either automatically or after manual confirmation, and runs the CashShop catalog seed only after a successful update.
    /// </summary>
    /// <param name="autoUpdateSchema">Whether the schema update is confirmed automatically.</param>
    /// <param name="applySchemaUpdate">Operation which applies the pending schema updates.</param>
    /// <param name="initializeCashShopCatalog">Operation which initializes the CashShop catalog.</param>
    /// <param name="readInput">Operation which reads the manual confirmation.</param>
    /// <param name="writeMessage">Operation which writes startup messages.</param>
    /// <returns><c>true</c> if the schema was updated; otherwise, <c>false</c> when the update was cancelled.</returns>
    public static async Task<bool> ApplySchemaUpdateAndInitializeAsync(
        bool autoUpdateSchema,
        Func<Task> applySchemaUpdate,
        Func<Task> initializeCashShopCatalog,
        Func<string?> readInput,
        Action<string> writeMessage)
    {
        if (autoUpdateSchema)
        {
            writeMessage("The database schema needs to be updated before the server can be started. Updating...");
            await applySchemaUpdate().ConfigureAwait(false);
            writeMessage("The database schema has been successfully updated.");
        }
        else
        {
            writeMessage("The database schema needs to be updated before the server can be started. Apply update? (y/n)");
            if (readInput()?.ToLowerInvariant() != "y")
            {
                writeMessage("Cancelled the schema update process, can't start the server.");
                return false;
            }

            await applySchemaUpdate().ConfigureAwait(false);
            writeMessage("The database schema has been successfully updated.");
        }

        await initializeCashShopCatalog().ConfigureAwait(false);
        return true;
    }

    private static async Task<GameConfiguration?> GetDefaultGameConfigurationAsync(
        IPersistenceContextProvider persistenceContextProvider,
        CancellationToken cancellationToken)
    {
        using var context = persistenceContextProvider.CreateNewConfigurationContext();
        return (await context.GetAsync<GameConfiguration>(cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }
}
