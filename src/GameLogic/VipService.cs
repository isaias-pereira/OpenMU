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

    private static readonly ConcurrentDictionary<Guid, VipCacheEntry> _vipCache = new();

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
    /// </summary>
    public static float GetDropMultiplier(Guid accountId)
    {
        if (_vipCache.TryGetValue(accountId, out var entry))
        {
            if (entry.ExpiresAt > DateTime.UtcNow)
            {
                return entry.DropMultiplier;
            }

            // Expirou: remove do cache
            _vipCache.TryRemove(accountId, out _);
        }

        return 1.0f;
    }

    /// <summary>
    /// Retorna o status completo do VIP da conta (plano, expiração, dias restantes).
    /// </summary>
    public static async ValueTask<VipStatusInfo> GetVipStatusAsync(IContext context, Guid accountId)
    {
        // Primeiro tenta o cache
        if (_vipCache.TryGetValue(accountId, out var cached))
        {
            if (cached.ExpiresAt > DateTime.UtcNow)
            {
                var remaining = (cached.ExpiresAt - DateTime.UtcNow).TotalDays;
                return new VipStatusInfo(
                    IsActive: true,
                    PlanName: await ResolvePlanNameAsync(context, cached.PlanId).ConfigureAwait(false),
                    ExpiresAt: cached.ExpiresAt,
                    DaysRemaining: Math.Max(0, Math.Ceiling(remaining)),
                    DropMultiplier: cached.DropMultiplier);
            }

            // Expirou no cache
            _vipCache.TryRemove(accountId, out _);
        }

        // Fallback: busca no banco
        var accountVip = await context.GetByIdAsync<AccountVip>(accountId).ConfigureAwait(false);
        if (accountVip?.VipPlanId is null || accountVip.ExpiresAt is null)
        {
            return new VipStatusInfo(IsActive: false, PlanName: null, ExpiresAt: null, DaysRemaining: 0, DropMultiplier: 1.0f);
        }

        if (accountVip.ExpiresAt.Value <= DateTime.UtcNow)
        {
            return new VipStatusInfo(IsActive: false, PlanName: null, ExpiresAt: accountVip.ExpiresAt.Value, DaysRemaining: 0, DropMultiplier: 1.0f);
        }

        var multiplier = await ResolveMultiplierAsync(context, accountVip.VipPlanId.Value).ConfigureAwait(false);
        var remainingDb = (accountVip.ExpiresAt.Value - DateTime.UtcNow).TotalDays;

        // Atualiza o cache
        _vipCache[accountId] = new VipCacheEntry(accountVip.VipPlanId.Value, accountVip.ExpiresAt.Value, multiplier);

        return new VipStatusInfo(
            IsActive: true,
            PlanName: await ResolvePlanNameAsync(context, accountVip.VipPlanId.Value).ConfigureAwait(false),
            ExpiresAt: accountVip.ExpiresAt.Value,
            DaysRemaining: Math.Max(0, Math.Ceiling(remainingDb)),
            DropMultiplier: multiplier);
    }

    /// <summary>
    /// Ativa o VIP para a conta: garante o <see cref="VipPlan"/> no banco (auto-seed idempotente),
    /// faz o upsert do <see cref="AccountVip"/>, grava no histórico, persiste e atualiza o cache.
    /// </summary>
    public static async ValueTask ActivateVipAsync(
        IContext context,
        Guid accountId,
        Guid planId,
        int durationDays,
        string source)
    {
        // 1. Garante que o VipPlan exista no banco (auto-seed idempotente). Sem isso, a FK de
        //    AccountVip.VipPlanId seria violada. Só semeamos planos que conhecemos no catálogo;
        //    um planId desconhecido e ausente do banco é de fato inválido.
        var dbPlan = await context.GetByIdAsync<VipPlan>(planId).ConfigureAwait(false);
        if (dbPlan is null)
        {
            if (!KnownPlansById.TryGetValue(planId, out var known))
            {
                throw new InvalidOperationException("Plano VIP inválido ou inativo.");
            }

            dbPlan = context.CreateNew<VipPlan>();
            dbPlan.Id = known.Id;
            dbPlan.Name = known.Name;
            dbPlan.DurationDays = durationDays;
            dbPlan.DropChanceMultiplier = known.DropMultiplier;
            dbPlan.IsActive = true;
        }
        else if (!dbPlan.IsActive)
        {
            throw new InvalidOperationException("Plano VIP inválido ou inativo.");
        }

        var multiplier = dbPlan.DropChanceMultiplier;
        var now = DateTime.UtcNow;
        var expiresAt = now.AddDays(durationDays);

        // 2. Atualiza/Cria AccountVip (upsert via shared primary key).
        var accountVip = await context.GetByIdAsync<AccountVip>(accountId).ConfigureAwait(false)
                         ?? context.CreateNew<AccountVip>(accountId);
        accountVip.VipPlanId = planId;
        accountVip.ExpiresAt = expiresAt;

        // 3. Grava no histórico (append-only).
        var history = context.CreateNew<VipHistory>();
        history.Id = Guid.NewGuid();
        history.AccountId = accountId;
        history.VipPlanId = planId;
        history.StartedAt = now;
        history.ExpiresAt = expiresAt;
        history.Source = source;

        await context.SaveChangesAsync().ConfigureAwait(false);

        // 4. Atualiza o cache em memória só depois de persistir com sucesso.
        _vipCache[accountId] = new VipCacheEntry(planId, expiresAt, multiplier);
    }

    /// <summary>
    /// Carrega o VIP da conta do banco para o cache em memória (usado na entrada do jogador no mundo).
    /// </summary>
    public static async ValueTask LoadIntoCacheAsync(IContext context, Guid accountId)
    {
        var accountVip = await context.GetByIdAsync<AccountVip>(accountId).ConfigureAwait(false);
        if (accountVip?.VipPlanId is { } planId
            && accountVip.ExpiresAt is { } expiresAt
            && expiresAt > DateTime.UtcNow)
        {
            var multiplier = await ResolveMultiplierAsync(context, planId).ConfigureAwait(false);
            _vipCache[accountId] = new VipCacheEntry(planId, expiresAt, multiplier);
        }
        else
        {
            _vipCache.TryRemove(accountId, out _);
        }
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

    private static async ValueTask<string> ResolvePlanNameAsync(IContext context, Guid planId)
    {
        if (KnownPlansById.TryGetValue(planId, out var known))
        {
            return known.Name;
        }

        var dbPlan = await context.GetByIdAsync<VipPlan>(planId).ConfigureAwait(false);
        return dbPlan?.Name ?? "Desconhecido";
    }

    /// <summary>
    /// Status completo do VIP de uma conta.
    /// </summary>
    public record VipStatusInfo(
        bool IsActive,
        string? PlanName,
        DateTime? ExpiresAt,
        double DaysRemaining,
        float DropMultiplier);

    private record VipCacheEntry(Guid PlanId, DateTime ExpiresAt, float DropMultiplier);
}
