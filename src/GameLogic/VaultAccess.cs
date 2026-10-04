// <copyright file="VaultAccess.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic;

using System;
using System.Linq;
using System.Threading.Tasks;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Centralizes access to the numbered vaults (báus) of an account:
/// how many vaults are allowed based on purchased vaults, and resolving/creating the
/// <see cref="ItemStorage"/> of the vault a player currently has selected.
/// Vault 1 is always free. Vaults 2-7 must be purchased via shop.
/// VIP status does not influence the vault count in any way.
/// </summary>
public static class VaultAccess
{
    /// <summary>The number of vaults an account always has access to (vault 1).</summary>
    public const int FreeVaultCount = 1;

    /// <summary>The maximum number of vaults an account can have.</summary>
    public const int MaximumVaultCount = 7;

    /// <summary>
    /// Gets the number of vaults the account may access, based on purchased vaults.
    /// </summary>
    /// <param name="context">The persistence context.</param>
    /// <param name="accountId">The identifier of the account.</param>
    /// <returns>The accessible vault count (at least 1, at most <see cref="MaximumVaultCount"/>).</returns>
    public static async ValueTask<int> GetMaxVaultsAsync(IContext context, Guid accountId)
    {
        // Get the account to check purchased vaults
        var account = await context.GetByIdAsync<Account>(accountId).ConfigureAwait(false);
        if (account is null)
        {
            return FreeVaultCount;
        }

        // Count how many vaults the account has purchased (including vault 1 which is always free)
        var purchasedVaultCount = account.Vaults?.Count ?? 0;

        // Ensure at least 1 (vault 1 is always free) and at most MaximumVaultCount
        return Math.Clamp(purchasedVaultCount, FreeVaultCount, MaximumVaultCount);
    }

    /// <summary>
    /// Gets the number of vaults the account has purchased.
    /// </summary>
    /// <param name="context">The persistence context.</param>
    /// <param name="accountId">The identifier of the account.</param>
    /// <returns>The number of purchased vaults.</returns>
    public static async ValueTask<int> GetPurchasedVaultCountAsync(IContext context, Guid accountId)
    {
        var account = await context.GetByIdAsync<Account>(accountId).ConfigureAwait(false);
        if (account is null)
        {
            return 0;
        }

        return account.Vaults?.Count ?? 0;
    }

    /// <summary>
    /// Checks if the account has access to a specific vault number.
    /// </summary>
    /// <param name="context">The persistence context.</param>
    /// <param name="accountId">The identifier of the account.</param>
    /// <param name="vaultNumber">The vault number to check (1-based).</param>
    /// <returns>True if the account has access to the vault, false otherwise.</returns>
    public static async ValueTask<bool> HasVaultAccessAsync(IContext context, Guid accountId, int vaultNumber)
    {
        if (vaultNumber < 1 || vaultNumber > MaximumVaultCount)
        {
            return false;
        }

        // Vault 1 is always free
        if (vaultNumber == 1)
        {
            return true;
        }

        // For vaults 2-7, check if they have been purchased
        var account = await context.GetByIdAsync<Account>(accountId).ConfigureAwait(false);
        if (account is null)
        {
            return false;
        }

        return account.Vaults?.Any(v => v.Number == vaultNumber) ?? false;
    }

    /// <summary>
    /// Clamps the requested vault number to the effectively granted access.
    /// Values below 1 are raised to 1; values above the maximum fall back to the maximum.
    /// </summary>
    /// <param name="requestedNumber">The requested vault number.</param>
    /// <param name="maxVaults">The effective maximum, see <see cref="GetMaxVaultsAsync"/>.</param>
    /// <returns>The vault number to use.</returns>
    public static int ClampVaultNumber(int requestedNumber, int maxVaults) =>
        Math.Clamp(requestedNumber, 1, maxVaults);

    /// <summary>
    /// Resolves the <see cref="ItemStorage"/> of the vault which the player currently has selected
    /// (<see cref="Player.CurrentVaultNumber"/>), creating it lazily if it does not exist yet.
    /// The selected number is clamped to the purchased vault-based access as anti-bypass measure.
    /// </summary>
    /// <param name="player">The player whose vault should be resolved.</param>
    /// <returns>The storage of the selected vault.</returns>
    public static async ValueTask<ItemStorage> GetOrCreateVaultStorageAsync(Player player)
    {
        var account = player.Account!;
        var maxVaults = await GetMaxVaultsAsync(player.PersistenceContext, account.Id).ConfigureAwait(false);
        player.CurrentVaultNumber = ClampVaultNumber(player.CurrentVaultNumber, maxVaults);

        var vault = account.Vaults?.FirstOrDefault(v => v.Number == player.CurrentVaultNumber);
        if (vault is null)
        {
            // Only allow creating vaults that have been purchased (or vault 1 which is free)
            if (!await HasVaultAccessAsync(player.PersistenceContext, account.Id, player.CurrentVaultNumber))
            {
                // If they don't have access to this vault, default to vault 1
                player.CurrentVaultNumber = 1;
                vault = account.Vaults?.FirstOrDefault(v => v.Number == 1);

                // Ensure vault 1 exists (it should from migration)
                if (vault is null)
                {
                    vault = player.PersistenceContext.CreateNew<AccountVault>();
                    vault.Number = 1;
                    vault.Storage = player.PersistenceContext.CreateNew<ItemStorage>();
                    account.Vaults!.Add(vault);
                }
            }
            else
            {
                // They have access to this vault, create it if it doesn't exist
                vault = player.PersistenceContext.CreateNew<AccountVault>();
                vault.Number = player.CurrentVaultNumber;
                vault.Storage = player.PersistenceContext.CreateNew<ItemStorage>();
                account.Vaults!.Add(vault);
            }
        }

        vault.Storage ??= player.PersistenceContext.CreateNew<ItemStorage>();
        return vault.Storage;
    }

    /// <summary>
    /// Creates the runtime <see cref="Storage"/> wrapper for the given vault storage.
    /// The legacy extended-vault flag is honored for vault number 1 only, so existing
    /// extended vaults keep their items; additional vaults always use the standard size.
    /// </summary>
    /// <param name="player">The player who opens the vault (used for size calculation).</param>
    /// <param name="storage">The persistent storage of the vault.</param>
    /// <returns>The runtime storage wrapper.</returns>
    public static Storage CreateRuntimeStorage(Player player, ItemStorage storage)
    {
        var warehouseSize = player.CurrentVaultNumber == 1 && player.Account?.IsVaultExtended is true
            ? InventoryConstants.WarehouseSize * 2
            : InventoryConstants.WarehouseSize;
        return new Storage(warehouseSize, storage);
    }
}
