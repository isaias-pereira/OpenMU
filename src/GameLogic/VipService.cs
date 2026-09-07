// <copyright file="VipService.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace MUnique.OpenMU.GameLogic;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Definição canônica de um plano VIP. É a fonte única de verdade para Id, nome e
/// multiplicador de drop — usada tanto na ativação quanto na hidratação do cache.
/// </summary>
public readonly record struct VipPlanDefinition(Guid Id, string Name, float DropMultiplier);

/// <summary>
/// Serviço central de VIP. Mantém um cache em memória O(1) para o <see cref="DefaultDropGenerator"/>
/// e gerencia a ativação/expiração via banco de dados. Segue o mesmo padrão estático do
/// <see cref="WCoinService"/>: recebe o <c>player.PersistenceContext</c> por parâmetro.
/// </summary>
public static class VipService
{
    /// <summary>O plano Bronze (+10% de chance de drop).</summary>
    public static readonly VipPlanDefinition Bronze = new(Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"), "Bronze", 1.1f);

    /// <summary>O plano Silver (+25% de chance de drop).</summary>
    public static readonly VipPlanDefinition Silver = new(Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901"), "Silver", 1.25f);

    /// <summary>O plano Gold (+50% de chance de drop).</summary>
    public static readonly VipPlanDefinition Gold = new(Guid.Parse("c3d4e5f6-a7b8-9012-cdef-123456789012"), "Gold", 1.5f);

    /// <summary>O plano Platinum (dobro da chance de drop).</summary>
    public static readonly VipPlanDefinition Platinum = new(Guid.Parse("d4e5f6a7-b8c9-0123-def1-234567890123"), "Platinum", 2.0f);

    private static readonly IReadOnlyDictionary<Guid, VipPlanDefinition> KnownPlansById = new Dictionary<Guid, VipPlanDefinition>
    {
        [Bronze.Id] = Bronze,
        [Silver.Id] = Silver,
        [Gold.Id] = Gold,
        [Platinum.Id] = Platinum,
    };

    private static readonly IReadOnlyDictionary<string, VipPlanDefinition> KnownPlansByName = new Dictionary<string, VipPlanDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        [Bronze.Name] = Bronze,
        [Silver.Name] = Silver,
        [Gold.Name] = Gold,
        [Platinum.Name] = Platinum,
    };

    private static readonly ConcurrentDictionary<Guid, VipCacheEntry> VipCache = new();

    /// <summary>
    /// Tenta resolver um plano conhecido pelo nome (case-insensitive).
    /// </summary>
    /// <param name="name">O nome do plano (Bronze, Silver, Gold, Platinum).</param>
    /// <param name="plan">O plano resolvido, se encontrado.</param>
    /// <returns><c>true</c> se o plano foi encontrado.</returns>
    public static bool TryGetPlanByName(string? name, out VipPlanDefinition plan)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return KnownPlansByName.TryGetValue(name.Trim(), out plan);
        }

        plan = default;
        return false;
    }

    /// <summary>
    /// Retorna o multiplicador de drop para a conta. O(1) via cache.
    /// Se não tiver VIP ou estiver expirado, retorna 1.0f.
    /// </summary>
    /// <param name="accountId">O Id da conta.</param>
    /// <returns>O multiplicador de drop.</returns>
    public static float GetDropMultiplier(Guid accountId)
    {
        if (VipCache.TryGetValue(accountId, out var entry))
        {
            if (entry.ExpiresAt > DateTime.UtcNow)
            {
                return entry.DropMultiplier;
            }

            // Expirou: remove do cache.
            VipCache.TryRemove(accountId, out _);
        }

        return 1.0f;
    }

    /// <summary>
    /// Carrega o status VIP persistido da conta para o cache em memória. Deve ser chamado
    /// quando o jogador entra no mundo, pois o cache é volátil (perdido ao reiniciar o servidor).
    /// </summary>
    /// <param name="context">O contexto de persistência do jogador.</param>
    /// <param name="accountId">O Id da conta.</param>
    public static async ValueTask LoadIntoCacheAsync(IContext context, Guid accountId)
    {
        var accountVip = await context.GetByIdAsync<AccountVip>(accountId).ConfigureAwait(false);
        if (accountVip?.VipPlanId is { } planId
            && accountVip.ExpiresAt is { } expiresAt
            && expiresAt > DateTime.UtcNow)
        {
            var multiplier = await ResolveMultiplierAsync(context, planId).ConfigureAwait(false);
            VipCache[accountId] = new VipCacheEntry(planId, expiresAt, multiplier);
        }
        else
        {
            VipCache.TryRemove(accountId, out _);
        }
    }

    /// <summary>
    /// Ativa o VIP para a conta: garante o <see cref="VipPlan"/> no banco (auto-seed idempotente),
    /// faz o upsert do <see cref="AccountVip"/>, grava no histórico, persiste e atualiza o cache.
    /// </summary>
    /// <param name="context">O contexto de persistência do jogador.</param>
    /// <param name="accountId">O Id da conta.</param>
    /// <param name="plan">A definição do plano a ativar.</param>
    /// <param name="durationDays">A duração da ativação, em dias.</param>
    /// <param name="source">A origem da ativação (ex.: "ChatCommand").</param>
    /// <returns>A data de expiração calculada.</returns>
    public static async ValueTask<DateTime> ActivateVipAsync(
        IContext context,
        Guid accountId,
        VipPlanDefinition plan,
        int durationDays,
        string source)
    {
        // 1. Garante que o VipPlan exista no banco. Sem isso, a FK de AccountVip.VipPlanId
        //    seria violada no INSERT/UPDATE. É idempotente: cria só na primeira vez.
        var dbPlan = await context.GetByIdAsync<VipPlan>(plan.Id).ConfigureAwait(false);
        if (dbPlan is null)
        {
            dbPlan = context.CreateNew<VipPlan>();
            dbPlan.Id = plan.Id;
            dbPlan.Name = plan.Name;
            dbPlan.DurationDays = durationDays;
            dbPlan.DropChanceMultiplier = plan.DropMultiplier;
            dbPlan.IsActive = true;
        }

        var now = DateTime.UtcNow;
        var expiresAt = now.AddDays(durationDays);

        // 2. Atualiza/Cria AccountVip (upsert via shared primary key).
        //    Usa o construtor sem parâmetros + atribuição do Id em vez de CreateNew(accountId):
        //    o modelo EF gerado nem sempre expõe o ctor (Guid), e um .Generated.cs
        //    desatualizado faria CreateNew(accountId) lançar MissingMethodException.
        var accountVip = await context.GetByIdAsync<AccountVip>(accountId).ConfigureAwait(false);
        if (accountVip is null)
        {
            accountVip = context.CreateNew<AccountVip>();
            accountVip.Id = accountId;
        }

        accountVip.VipPlanId = plan.Id;
        accountVip.ExpiresAt = expiresAt;

        // 3. Grava no histórico (append-only).
        var history = context.CreateNew<VipHistory>();
        history.Id = Guid.NewGuid();
        history.AccountId = accountId;
        history.VipPlanId = plan.Id;
        history.StartedAt = now;
        history.ExpiresAt = expiresAt;
        history.Source = source;

        await context.SaveChangesAsync().ConfigureAwait(false);

        // 4. Atualiza o cache em memória só depois de persistir com sucesso.
        VipCache[accountId] = new VipCacheEntry(plan.Id, expiresAt, plan.DropMultiplier);

        return expiresAt;
    }

    private static async ValueTask<float> ResolveMultiplierAsync(IContext context, Guid planId)
    {
        if (KnownPlansById.TryGetValue(planId, out var known))
        {
            return known.DropMultiplier;
        }

        // Plano customizado (criado fora do catálogo): usa o valor persistido.
        var dbPlan = await context.GetByIdAsync<VipPlan>(planId).ConfigureAwait(false);
        return dbPlan?.DropChanceMultiplier ?? 1.0f;
    }

    private record VipCacheEntry(Guid PlanId, DateTime ExpiresAt, float DropMultiplier);
}
