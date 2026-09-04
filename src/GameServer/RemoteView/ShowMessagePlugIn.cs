// <copyright file="ShowMessagePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// The default implementation of the <see cref="IShowMessagePlugIn"/> which is forwarding everything to the game client with specific data packets.
/// </summary>
[PlugIn]
[Display(Name = nameof(PlugInResources.ShowMessagePlugIn_Name), Description = nameof(PlugInResources.ShowMessagePlugIn_Description), ResourceType = typeof(PlugInResources))]
[Guid("e294f4ce-f2c6-4a92-8cd0-40d8d5afae66")]
public class ShowMessagePlugIn : IShowMessagePlugIn
{
    /// <summary>
    /// The prefix which is expected by the game clients of season 1 and later. These clients skip
    /// the first nine characters of the message text without rendering them, so the text has to be
    /// padded - otherwise the beginning of every message would be cut off.
    /// </summary>
    /// <remarks>
    /// This is the only place where the prefix is applied. If a modified client renders the prefix
    /// instead of skipping it (messages would show up as "000000000your text"), just set this
    /// constant to <see cref="string.Empty"/> - <see cref="MaximumMessageLength"/> adapts itself.
    /// </remarks>
    private const string MessagePrefix = "000000000";

    /// <summary>
    /// The maximum length of the whole packet, because the C1 header has a one byte length field.
    /// </summary>
    private const int MaximumPacketLength = 0xFF;

    /// <summary>
    /// The number of packet bytes which are not part of the message text: three header bytes,
    /// the message type and the string terminator. See <see cref="ServerMessageRef.GetRequiredSize(string)"/>.
    /// </summary>
    private const int PacketOverhead = 5;

    /// <summary>
    /// The maximum number of message bytes which fit into one packet, prefix included.
    /// </summary>
    private static readonly int MaximumMessageLength = MaximumPacketLength - PacketOverhead - MessagePrefix.Length;

    private readonly RemotePlayer _player;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShowMessagePlugIn"/> class.
    /// </summary>
    /// <param name="player">The player.</param>
    public ShowMessagePlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc/>
    public async ValueTask ShowMessageAsync(string message, OpenMU.Interfaces.MessageType messageType)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        if (Encoding.UTF8.GetByteCount(message) > MaximumMessageLength)
        {
            var rest = message;
            while (rest.Length > 0)
            {
                var partSize = Encoding.UTF8.GetCharacterCountOfMaxByteCount(rest, MaximumMessageLength);
                await this.ShowMessageAsync(rest.Substring(0, partSize), messageType).ConfigureAwait(false);
                rest = rest.Length > partSize ? rest.Substring(startIndex: partSize) : string.Empty;
            }

            return;
        }

        var content = this._player.ClientVersion.Season > 0 ? MessagePrefix + message : message;
        await this._player.Connection.SendServerMessageAsync(ConvertMessageType(messageType), content).ConfigureAwait(false);
    }

    private static ServerMessage.MessageType ConvertMessageType(OpenMU.Interfaces.MessageType messageType)
    {
        return messageType switch
        {
            Interfaces.MessageType.BlueNormal => ServerMessage.MessageType.BlueNormal,
            Interfaces.MessageType.GoldenCenter => ServerMessage.MessageType.GoldenCenter,
            Interfaces.MessageType.GuildNotice => ServerMessage.MessageType.GuildNotice,
            _ => throw new NotImplementedException($"Case for {messageType} is not implemented."),
        };
    }
}