// <copyright file="RecordingViewPlugInContainer.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.CustomMove;

using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A view plugin container which records all shown messages, and provides loose mocks for all other views.
/// </summary>
internal sealed class RecordingViewPlugInContainer : ICustomPlugInContainer<IViewPlugIn>
{
    private readonly MockViewPlugInContainer fallbackContainer = new();

    /// <summary>
    /// Gets the message plugin which records every shown message.
    /// </summary>
    public RecordingMessagePlugIn MessagePlugIn { get; } = new();

    /// <inheritdoc />
    public T? GetPlugIn<T>()
        where T : class, IViewPlugIn
    {
        if (typeof(T) == typeof(IShowMessagePlugIn))
        {
            return (T)(object)this.MessagePlugIn;
        }

        return this.fallbackContainer.GetPlugIn<T>();
    }
}

/// <summary>
/// A message plugin which records every shown message.
/// </summary>
internal sealed class RecordingMessagePlugIn : IShowMessagePlugIn
{
    /// <summary>
    /// Gets the list of messages which were shown.
    /// </summary>
    public List<(string Message, MessageType MessageType)> Messages { get; } = new();

    /// <inheritdoc />
    public ValueTask ShowMessageAsync(string message, MessageType messageType)
    {
        this.Messages.Add((message, messageType));
        return ValueTask.CompletedTask;
    }
}
