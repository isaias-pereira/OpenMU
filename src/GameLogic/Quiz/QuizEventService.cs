// <copyright file="QuizEventService.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Quiz;

using System.Collections.Concurrent;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Estado en el que se encuentra una ronda del Quiz del servidor.
/// </summary>
public enum QuizState
{
    /// <summary>
    /// Sin evento en curso.
    /// </summary>
    Idle,

    /// <summary>
    /// El evento se está preparando (selección de preguntas).
    /// </summary>
    Starting,

    /// <summary>
    /// Hay una pregunta activa y se aceptan respuestas.
    /// </summary>
    QuestionActive,

    /// <summary>
    /// La pregunta actual ya fue respondida correctamente (se está procesando el premio).
    /// </summary>
    QuestionAnswered,

    /// <summary>
    /// La ronda está finalizando.
    /// </summary>
    Finishing,
}

/// <summary>
/// Servicio central del evento Quiz. Mantiene una <see cref="QuizSession"/> por
/// <see cref="IGameContext"/>. Flujo: Idle → Starting → QuestionActive → (primer acierto)
/// → QuestionAnswered → siguiente pregunta → ... → Finishing → Idle.
/// Sólo el servidor decide el ganador y el premio; el cliente nunca define moneda ni monto.
/// </summary>
public static class QuizEventService
{
    private static readonly ConcurrentDictionary<IGameContext, QuizSession> Sessions = new();

    /// <summary>
    /// Obtiene (creándola si hace falta) la sesión de Quiz del contexto.
    /// </summary>
    public static QuizSession GetOrCreateSession(IGameContext gameContext)
    {
        return Sessions.GetOrAdd(gameContext, _ => new QuizSession(gameContext));
    }

    /// <summary>
    /// Carga configuración y preguntas y arranca el scheduler automático (si está habilitado).
    /// Debe llamarse una vez al iniciar el servidor.
    /// </summary>
    public static async ValueTask EnsureInitializedAsync(IGameContext gameContext)
    {
        var session = GetOrCreateSession(gameContext);
        await session.LoadDataAsync().ConfigureAwait(false);
        session.StartSchedulerIfNeeded();
    }

    /// <summary>
    /// Recarga configuración y preguntas desde la base de datos.
    /// </summary>
    public static async ValueTask ReloadAsync(IGameContext gameContext)
    {
        await GetOrCreateSession(gameContext).LoadDataAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Inicia una ronda manualmente (uso GM: /quiz start). No hace nada si ya hay una en curso.
    /// </summary>
    public static async ValueTask StartRoundAsync(IGameContext gameContext)
    {
        await GetOrCreateSession(gameContext).StartRoundAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Detiene el evento actual y el scheduler (uso GM: /quiz stop).
    /// </summary>
    public static async ValueTask StopAsync(IGameContext gameContext)
    {
        await GetOrCreateSession(gameContext).StopAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Procesa el comando <c>/resp &lt;respuesta&gt;</c> de un jugador.
    /// </summary>
    /// <returns><c>true</c>, si el jugador fue declarado ganador (primera respuesta correcta).</returns>
    public static async ValueTask<bool> SubmitAnswerAsync(Player player, string answer)
    {
        var session = GetOrCreateSession(player.GameContext);
        return await session.SubmitAnswerAsync(player, answer).ConfigureAwait(false);
    }

    /// <summary>
    /// Devuelve un resumen del estado actual (para /quiz status).
    /// </summary>
    public static QuizStatus GetStatus(IGameContext gameContext)
    {
        return GetOrCreateSession(gameContext).GetStatus();
    }

    /// <summary>
    /// Resumen del estado del quiz.
    /// </summary>
    public record QuizStatus(QuizState State, bool Enabled, int QuestionCount, int CurrentIndex, string? CurrentQuestion, string? WinnerName, long RewardAmount, QuizRewardCurrency RewardCurrency);
}
