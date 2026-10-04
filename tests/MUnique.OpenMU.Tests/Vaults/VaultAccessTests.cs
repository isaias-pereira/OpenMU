// <copyright file="VaultAccessTests.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.Vaults;

using System;
using System.Threading.Tasks;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GoldFreeShop;
using MUnique.OpenMU.Persistence.InMemory;
using NUnit.Framework;

/// <summary>
/// Testes das regras de acesso aos báus numerados (<see cref="VaultAccess"/>) e da
/// resolução de preços da loja (<see cref="GoldFreeShopConfiguration.GetVaultPrice"/>).
/// </summary>
/// <remarks>
/// Regras cobertas: o baú 1 é sempre gratuito; os baús 2-7 dependem exclusivamente de
/// compra registrada em <c>Account.Vaults</c>; nada acima de 7 existe; e nenhuma regra
/// consulta VIP — o <see cref="InMemoryContext"/> usado aqui nem possui dados de VIP,
/// então qualquer dependência de VIP falharia em tempo de compilação ou de execução.
/// </remarks>
[TestFixture]
public class VaultAccessTests
{
    private InMemoryPersistenceContextProvider _contextProvider = null!;

    [SetUp]
    public void SetUp()
    {
        this._contextProvider = new InMemoryPersistenceContextProvider();
    }

    /// <summary>
    /// O limite absoluto do sistema é 7 baús.
    /// </summary>
    [Test]
    public void MaximumVaultCount_IsSeven()
    {
        Assert.That(VaultAccess.MaximumVaultCount, Is.EqualTo(7));
    }

    /// <summary>
    /// Uma conta recém-criada (sem nenhum baú persistido) tem acesso ao baú 1.
    /// </summary>
    [Test]
    public async Task GetMaxVaultsAsync_NewAccount_HasAccessToVault1()
    {
        var accountId = await this.CreateAccountAsync().ConfigureAwait(false);

        using var context = this._contextProvider.CreateNewContext();
        Assert.That(await VaultAccess.GetMaxVaultsAsync(context, accountId), Is.EqualTo(1));
        Assert.That(await VaultAccess.HasVaultAccessAsync(context, accountId, 1), Is.True);
    }

    /// <summary>
    /// Conta inexistente cai no piso de segurança: apenas o baú 1.
    /// </summary>
    [Test]
    public async Task GetMaxVaultsAsync_UnknownAccount_ReturnsFreeVaultCount()
    {
        using var context = this._contextProvider.CreateNewContext();
        Assert.That(await VaultAccess.GetMaxVaultsAsync(context, Guid.NewGuid()), Is.EqualTo(VaultAccess.FreeVaultCount));
    }

    /// <summary>
    /// Uma conta sem VIP pode possuir baús adicionais: o acesso vem só da compra.
    /// </summary>
    [Test]
    public async Task HasVaultAccessAsync_PurchasedVaultsWithoutVip_GrantsAccess()
    {
        var accountId = await this.CreateAccountAsync(1, 2, 7).ConfigureAwait(false);

        using var context = this._contextProvider.CreateNewContext();
        Assert.That(await VaultAccess.HasVaultAccessAsync(context, accountId, 2), Is.True);
        Assert.That(await VaultAccess.HasVaultAccessAsync(context, accountId, 7), Is.True);
        Assert.That(await VaultAccess.HasVaultAccessAsync(context, accountId, 3), Is.False);
        Assert.That(await VaultAccess.GetMaxVaultsAsync(context, accountId), Is.EqualTo(3));
    }

    /// <summary>
    /// Baús fora da faixa 1-7 são sempre rejeitados, mesmo que exista lixo persistido.
    /// </summary>
    [Test]
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(8)]
    [TestCase(999)]
    public async Task HasVaultAccessAsync_OutOfRange_AlwaysRejected(int vaultNumber)
    {
        var accountId = await this.CreateAccountAsync(1, 2, 3, 4, 5, 6, 7).ConfigureAwait(false);

        using var context = this._contextProvider.CreateNewContext();
        Assert.That(await VaultAccess.HasVaultAccessAsync(context, accountId, vaultNumber), Is.False);
    }

    /// <summary>
    /// Mesmo com dados corrompidos (mais de 7 baús persistidos), o máximo efetivo é 7.
    /// </summary>
    [Test]
    public async Task GetMaxVaultsAsync_MoreThanSevenPersisted_ClampedToSeven()
    {
        var accountId = await this.CreateAccountAsync(1, 2, 3, 4, 5, 6, 7, 8, 9).ConfigureAwait(false);

        using var context = this._contextProvider.CreateNewContext();
        Assert.That(await VaultAccess.GetMaxVaultsAsync(context, accountId), Is.EqualTo(VaultAccess.MaximumVaultCount));
    }

    /// <summary>
    /// O clamp nunca devolve menos que 1 nem mais que o máximo efetivo da conta.
    /// </summary>
    [Test]
    [TestCase(0, 3, 1)]
    [TestCase(-5, 3, 1)]
    [TestCase(2, 3, 2)]
    [TestCase(8, 3, 3)]
    [TestCase(999, 7, 7)]
    public void ClampVaultNumber_StaysWithinAccess(int requested, int maxVaults, int expected)
    {
        Assert.That(VaultAccess.ClampVaultNumber(requested, maxVaults), Is.EqualTo(expected));
    }

    /// <summary>
    /// O preço de cada baú adicional vem da configuração do administrador, não de fórmula.
    /// </summary>
    [Test]
    public void GetVaultPrice_ReturnsConfiguredPricePerVault()
    {
        var config = new GoldFreeShopConfiguration
        {
            Vault2Price = 500,
            Vault3Price = 800,
            Vault4Price = 1200,
            Vault5Price = 1600,
            Vault6Price = 2000,
            Vault7Price = 2500,
        };

        Assert.That(config.GetVaultPrice(2), Is.EqualTo(500));
        Assert.That(config.GetVaultPrice(3), Is.EqualTo(800));
        Assert.That(config.GetVaultPrice(4), Is.EqualTo(1200));
        Assert.That(config.GetVaultPrice(5), Is.EqualTo(1600));
        Assert.That(config.GetVaultPrice(6), Is.EqualTo(2000));
        Assert.That(config.GetVaultPrice(7), Is.EqualTo(2500));
    }

    /// <summary>
    /// Baú 1 e qualquer número fora de 2-7 não têm preço: não são vendáveis.
    /// </summary>
    [Test]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(8)]
    [TestCase(-1)]
    [TestCase(999)]
    public void GetVaultPrice_OutOfPurchasableRange_ReturnsZero(int vaultNumber)
    {
        var config = new GoldFreeShopConfiguration();
        Assert.That(config.GetVaultPrice(vaultNumber), Is.EqualTo(0));
    }

    /// <summary>
    /// Um baú com preço 0 não está à venda; <see cref="GoldFreeShopConfiguration.SellsVaults"/>
    /// só é verdadeiro quando pelo menos um baú adicional tem preço positivo.
    /// </summary>
    [Test]
    public void SellsVaults_OnlyWhenAtLeastOnePriceIsPositive()
    {
        var disabled = new GoldFreeShopConfiguration
        {
            Vault2Price = 0,
            Vault3Price = 0,
            Vault4Price = 0,
            Vault5Price = 0,
            Vault6Price = 0,
            Vault7Price = 0,
        };
        Assert.That(disabled.SellsVaults, Is.False);

        var enabled = new GoldFreeShopConfiguration { Vault5Price = 100 };
        Assert.That(enabled.SellsVaults, Is.True);
    }

    private async Task<Guid> CreateAccountAsync(params int[] vaultNumbers)
    {
        using var context = this._contextProvider.CreateNewContext();
        var account = context.CreateNew<Account>();
        foreach (var number in vaultNumbers)
        {
            var vault = context.CreateNew<AccountVault>();
            vault.Number = number;
            vault.Storage = context.CreateNew<ItemStorage>();
            account.Vaults.Add(vault);
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
        return account.Id;
    }
}
