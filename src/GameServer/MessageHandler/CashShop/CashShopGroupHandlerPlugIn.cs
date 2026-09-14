// <copyright file="CashShopGroupHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler.CashShop;

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Packet handler for cash shop packets (0xD2 identifier).
/// </summary>
[PlugIn]
[Display(Name = "Cash Shop Packet Handler", Description = "Packet handler for the cash shop / XShop (0xD2).")]
[Guid("B712A1C7-0E85-4CBF-9924-AA3ABD6F61E9")]
internal class CashShopGroupHandlerPlugIn : GroupPacketHandlerPlugIn
{
    /// <summary>
    /// The group key (packet code 0xD2).
    /// </summary>
    internal const byte GroupKey = 0xD2;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopGroupHandlerPlugIn" /> class.
    /// </summary>
    /// <param name="clientVersionProvider">The client version provider.</param>
    /// <param name="manager">The manager.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public CashShopGroupHandlerPlugIn(IClientVersionProvider clientVersionProvider, PlugInManager manager, ILoggerFactory loggerFactory)
        : base(clientVersionProvider, manager, loggerFactory)
    {
    }

    /// <inheritdoc/>
    public override bool IsEncryptionExpected => false;

    /// <inheritdoc/>
    public override byte Key => GroupKey;
}
