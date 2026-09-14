// <copyright file="CashShopPacketTests.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Network.Packets.Tests.ServerToClient;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net;
using System.Threading.Tasks;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using MUnique.OpenMU.PlugIns;
using Nito.AsyncEx;

/// <summary>
/// Tests for the hand-written cash shop server-to-client packets (packet group 0xD2) which are
/// defined in <see cref="CashShopConnectionExtensions"/>. The expected layouts are taken 1:1
/// from the client structs (WSclient.h, pack(1)): PMSG_CASHSHOP_CASHPOINT_ANS (45 bytes),
/// PMSG_CASHSHOP_SHOPOPEN_ANS (5), PMSG_CASHSHOP_STORAGECOUNT (12),
/// PMSG_CASHSHOP_VERSION_UPDATE (10), PMSG_CASHSHOP_EVENTITEM_COUNT (6) and
/// PMSG_CASHSHOP_BANNER_UPDATE (10).
/// </summary>
[TestFixture]
public class CashShopPacketTests
{
    /// <summary>
    /// Tests the point info response (C1 D2 01): 45 bytes, byte at offset 4, five little-endian
    /// doubles at byte offsets 5, 13, 21, 29 and 37 (unaligned on purpose — the client struct
    /// lives inside #pragma pack(push, 1)).
    /// </summary>
    [Test]
    public async Task CashShopPointInfo_HasCorrectLayout()
    {
        using var connection = new SpyConnection();
        await connection.SendCashShopPointInfoAsync(2, 1.5, 2.5, 4711.25, 3.5, 4.5);

        var bytes = await connection.GetSinglePacket();
        Assert.Multiple(() =>
        {
            Assert.That(bytes.Length, Is.EqualTo(45));
            Assert.That(bytes[0], Is.EqualTo(0xC1));
            Assert.That(bytes[1], Is.EqualTo(45));
            Assert.That(bytes[2], Is.EqualTo(0xD2));
            Assert.That(bytes[3], Is.EqualTo(0x01));
            Assert.That(bytes[4], Is.EqualTo(2));
            Assert.That(BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(5)), Is.EqualTo(1.5));
            Assert.That(BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(13)), Is.EqualTo(2.5));
            Assert.That(BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(21)), Is.EqualTo(4711.25));
            Assert.That(BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(29)), Is.EqualTo(3.5));
            Assert.That(BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(37)), Is.EqualTo(4.5));
        });
    }

    /// <summary>
    /// Tests the open state result (C1 D2 02): 5 bytes with a non-zero result byte on success
    /// and 0 on failure — the client compares the result against 0.
    /// </summary>
    [Test]
    public async Task CashShopOpenResult_HasCorrectLayout()
    {
        using var successConnection = new SpyConnection();
        await successConnection.SendCashShopOpenResultAsync(true);
        var successBytes = await successConnection.GetSinglePacket();

        using var failureConnection = new SpyConnection();
        await failureConnection.SendCashShopOpenResultAsync(false);
        var failureBytes = await failureConnection.GetSinglePacket();

        Assert.Multiple(() =>
        {
            Assert.That(successBytes.Length, Is.EqualTo(5));
            Assert.That(successBytes[0], Is.EqualTo(0xC1));
            Assert.That(successBytes[1], Is.EqualTo(5));
            Assert.That(successBytes[2], Is.EqualTo(0xD2));
            Assert.That(successBytes[3], Is.EqualTo(0x02));
            Assert.That(successBytes[4], Is.Not.EqualTo(0));
            Assert.That(failureBytes.Length, Is.EqualTo(5));
            Assert.That(failureBytes[4], Is.EqualTo(0));
        });
    }

    /// <summary>
    /// Tests the storage item count (C1 D2 06): 12 bytes with four little-endian words
    /// (total, current page items, page index, total pages) starting at offset 4.
    /// </summary>
    [Test]
    public async Task CashShopStorageCount_HasCorrectLayout()
    {
        using var connection = new SpyConnection();
        await connection.SendCashShopStorageCountAsync(120, 8, 1, 15);

        var bytes = await connection.GetSinglePacket();
        Assert.Multiple(() =>
        {
            Assert.That(bytes.Length, Is.EqualTo(12));
            Assert.That(bytes[2], Is.EqualTo(0xD2));
            Assert.That(bytes[3], Is.EqualTo(0x06));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)), Is.EqualTo(120));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6)), Is.EqualTo(8));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8)), Is.EqualTo(1));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10)), Is.EqualTo(15));
        });
    }

    /// <summary>
    /// Tests the script version message (C1 D2 0C): 10 bytes with three little-endian words
    /// (zone, year, year identifier). This message is the only client-side unlock trigger with
    /// KJH_MOD_SHOP_SCRIPT_DOWNLOAD, so the client-side folder name must be reproducible with
    /// the L"%03d.%04d.%03d" format.
    /// </summary>
    [Test]
    public async Task CashShopScriptVersion_HasCorrectLayout()
    {
        using var connection = new SpyConnection();
        await connection.SendCashShopScriptVersionAsync(512, 2012, 76);

        var bytes = await connection.GetSinglePacket();
        Assert.Multiple(() =>
        {
            Assert.That(bytes.Length, Is.EqualTo(10));
            Assert.That(bytes[2], Is.EqualTo(0xD2));
            Assert.That(bytes[3], Is.EqualTo(0x0C));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)), Is.EqualTo(512));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6)), Is.EqualTo(2012));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8)), Is.EqualTo(76));
        });

        // Reproduces the client folder naming from ListManager.cpp (StringCchPrintf "%03d.%04d.%03d").
        var zone = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
        var year = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6));
        var yearId = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));
        Assert.That($"{zone:D3}.{year:D4}.{yearId:D3}", Is.EqualTo("512.2012.076"));
    }

    /// <summary>
    /// Tests the event item count message (C1 D2 13): 6 bytes with one little-endian word.
    /// </summary>
    [Test]
    public async Task CashShopEventItemCount_HasCorrectLayout()
    {
        using var connection = new SpyConnection();
        await connection.SendCashShopEventItemCountAsync(3);

        var bytes = await connection.GetSinglePacket();
        Assert.Multiple(() =>
        {
            Assert.That(bytes.Length, Is.EqualTo(6));
            Assert.That(bytes[2], Is.EqualTo(0xD2));
            Assert.That(bytes[3], Is.EqualTo(0x13));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)), Is.EqualTo(3));
        });
    }

    /// <summary>
    /// Tests the banner version message (C1 D2 15): 10 bytes, same layout as the script version.
    /// </summary>
    [Test]
    public async Task CashShopBannerVersion_HasCorrectLayout()
    {
        using var connection = new SpyConnection();
        await connection.SendCashShopBannerVersionAsync(583, 2011, 1);

        var bytes = await connection.GetSinglePacket();
        Assert.Multiple(() =>
        {
            Assert.That(bytes.Length, Is.EqualTo(10));
            Assert.That(bytes[2], Is.EqualTo(0xD2));
            Assert.That(bytes[3], Is.EqualTo(0x15));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)), Is.EqualTo(583));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6)), Is.EqualTo(2011));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8)), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// A minimal in-memory <see cref="IConnection"/> which captures everything written to its
    /// output pipe, so the packet layout can be asserted byte by byte.
    /// </summary>
    private sealed class SpyConnection : IConnection
    {
        private readonly Pipe _pipe = new ();

#pragma warning disable CS0067 // Not used — required by the interface.
        public event AsyncEventHandler<ReadOnlySequence<byte>>? PacketReceived;

        public event AsyncEventHandler? Disconnected;
#pragma warning restore CS0067

        public bool Connected => true;

        public EndPoint? EndPoint => null;

        public EndPoint? LocalEndPoint => null;

        public PipeWriter Output => this._pipe.Writer;

        public AsyncLock OutputLock { get; } = new ();

        public Task BeginReceiveAsync() => Task.CompletedTask;

        public ValueTask DisconnectAsync() => default;

        public void Dispose() => this._pipe.Reader.Complete();

        /// <summary>
        /// Reads the single packet which was written to the connection so far.
        /// </summary>
        public async Task<byte[]> GetSinglePacket()
        {
            var result = await this._pipe.Reader.ReadAsync();
            var bytes = result.Buffer.ToArray();
            this._pipe.Reader.AdvanceTo(result.Buffer.End);
            return bytes;
        }
    }
}
