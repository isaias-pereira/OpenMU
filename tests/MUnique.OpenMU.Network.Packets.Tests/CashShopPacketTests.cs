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
    /// <summary>
    /// Tests the storage count (C1 D2 06) and one storage item (C1 D2 0D).
    /// </summary>
    /// <remarks>
    /// D2 06 is 12 bytes with four WORDs. D2 0D is 33 bytes: five little-endian ints at
    /// offsets 4/8/12/16/20, an unaligned double at 24 and the item type byte at 32, matching
    /// <c>PMSG_CASHSHOP_STORAGELIST</c> in the client. The client resolves the displayed item
    /// from the product and price sequences, so those offsets carry the catalog identity.
    /// </remarks>
    [Test]
    public async Task CashShopStorageItem_HasCorrectLayout()
    {
        using var connection = new SpyConnection();
        await connection.SendCashShopStorageItemAsync(
            storageIndex: 7,
            itemSeq: 42,
            storageGroupCode: 3,
            productSeq: 528,
            priceSeq: 607,
            cashPoint: 1234.5,
            itemType: 'P');

        var bytes = await connection.GetSinglePacket();
        Assert.Multiple(() =>
        {
            Assert.That(bytes.Length, Is.EqualTo(33));
            Assert.That(bytes[0], Is.EqualTo(0xC1));
            Assert.That(bytes[1], Is.EqualTo(33));
            Assert.That(bytes[2], Is.EqualTo(0xD2));
            Assert.That(bytes[3], Is.EqualTo(0x0D));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)), Is.EqualTo(7));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)), Is.EqualTo(42));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12)), Is.EqualTo(3));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16)), Is.EqualTo(528));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(20)), Is.EqualTo(607));
            Assert.That(BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(24)), Is.EqualTo(1234.5));
            Assert.That(bytes[32], Is.EqualTo((byte)'P'));
        });
    }

    /// <summary>
    /// Tests the buy result (C1 D2 03): 9 bytes with the result code at offset 4 and the
    /// remaining item count at offset 5. The client only reads the result code.
    /// </summary>
    [Test]
    public async Task CashShopBuyResult_HasCorrectLayout()
    {
        using var connection = new SpyConnection();
        await connection.SendCashShopBuyResultAsync(0, 12345);

        var bytes = await connection.GetSinglePacket();
        Assert.Multiple(() =>
        {
            Assert.That(bytes.Length, Is.EqualTo(9));
            Assert.That(bytes[0], Is.EqualTo(0xC1));
            Assert.That(bytes[1], Is.EqualTo(9));
            Assert.That(bytes[2], Is.EqualTo(0xD2));
            Assert.That(bytes[3], Is.EqualTo(0x03));
            Assert.That(bytes[4], Is.EqualTo(0));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(5)), Is.EqualTo(12345));
        });
    }

    /// <summary>
    /// Tests that the negative result codes reach the client as the two's-complement bytes the
    /// client compares against (<c>static_cast&lt;BYTE&gt;(-1)</c> = 255 and
    /// <c>static_cast&lt;BYTE&gt;(-2)</c> = 254), instead of being clamped to zero.
    /// </summary>
    [Test]
    [TestCase(-1, 255)]
    [TestCase(-2, 254)]
    public async Task CashShopBuyResult_SendsNegativeCodesAsTwoComplementBytes(int errorCode, byte expected)
    {
        using var connection = new SpyConnection();
        await connection.SendCashShopBuyResultAsync(unchecked((byte)errorCode), 0);

        var bytes = await connection.GetSinglePacket();
        Assert.Multiple(() =>
        {
            Assert.That(bytes.Length, Is.EqualTo(9));
            Assert.That(bytes[4], Is.EqualTo(expected));
        });
    }

    /// <summary>
    /// FASE 10 (RED): o D2 0E tem exatamente 244 bytes com o cabeçalho C1/D2/0x0E e os campos
    /// base do D2 0D nos mesmos offsets — espelha <c>PMSG_CASHSHOP_GIFTSTORAGELIST</c>
    /// (WSclient.h:3325-3339, pack(1)).
    /// </summary>
    [Test]
    public async Task CashShopGiftStorageItem_HasCorrectLayout()
    {
        using var connection = new SpyConnection();
        await connection.SendCashShopGiftStorageItemAsync(
            storageIndex: 7,
            itemSeq: 42,
            storageGroupCode: 3,
            productSeq: 528,
            priceSeq: 607,
            cashPoint: 1234.5,
            itemType: 'P',
            giftSender: "Ana",
            giftMessage: "Oi");

        var bytes = await connection.GetSinglePacket();
        Assert.Multiple(() =>
        {
            Assert.That(bytes.Length, Is.EqualTo(244), "O D2 0E tem exatamente 244 bytes no fio.");
            Assert.That(bytes[0], Is.EqualTo(0xC1));
            Assert.That(bytes[1], Is.EqualTo(244));
            Assert.That(bytes[2], Is.EqualTo(0xD2));
            Assert.That(bytes[3], Is.EqualTo(0x0E));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)), Is.EqualTo(7));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)), Is.EqualTo(42));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12)), Is.EqualTo(3));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16)), Is.EqualTo(528));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(20)), Is.EqualTo(607));
            Assert.That(BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(24)), Is.EqualTo(1234.5));
            Assert.That(bytes[32], Is.EqualTo((byte)'P'));
        });
    }

    /// <summary>
    /// FASE 10 (RED): o remetente ocupa o campo fixo de 11 bytes no offset 33, em UTF-8,
    /// terminado em NUL e com o restante do campo preenchido com zero.
    /// </summary>
    [Test]
    public async Task CashShopGiftStorageItem_Sender_IsNulTerminatedAndZeroPadded()
    {
        using var connection = new SpyConnection();
        await connection.SendCashShopGiftStorageItemAsync(0, 0, 0, 0, 0, 0, 'P', "Ana", null);

        var bytes = await connection.GetSinglePacket();
        Assert.Multiple(() =>
        {
            Assert.That(bytes[33], Is.EqualTo((byte)'A'));
            Assert.That(bytes[34], Is.EqualTo((byte)'n'));
            Assert.That(bytes[35], Is.EqualTo((byte)'a'));
            Assert.That(bytes[36], Is.EqualTo(0), "O NUL termina o nome logo após o conteúdo.");
            Assert.That(bytes.AsSpan(37, 7).ToArray().All(b => b == 0), Is.True, "O restante do campo de 11 bytes é preenchido com zero.");
        });
    }

    /// <summary>
    /// FASE 10 (RED): a mensagem ocupa o campo fixo de 200 bytes no offset 44, em UTF-8,
    /// terminado em NUL e com o restante preenchido com zero.
    /// </summary>
    [Test]
    public async Task CashShopGiftStorageItem_Message_IsNulTerminatedAndZeroPadded()
    {
        using var connection = new SpyConnection();
        await connection.SendCashShopGiftStorageItemAsync(0, 0, 0, 0, 0, 0, 'P', null, "Oi");

        var bytes = await connection.GetSinglePacket();
        Assert.Multiple(() =>
        {
            Assert.That(bytes[44], Is.EqualTo((byte)'O'));
            Assert.That(bytes[45], Is.EqualTo((byte)'i'));
            Assert.That(bytes[46], Is.EqualTo(0), "O NUL termina a mensagem logo após o conteúdo.");
            Assert.That(bytes.AsSpan(47, 197).ToArray().All(b => b == 0), Is.True, "O restante do campo de 200 bytes é preenchido com zero.");
        });
    }

    /// <summary>
    /// FASE 10 (RED): o remetente nunca passa de 10 bytes úteis — o cliente converte no máximo
    /// 10 bytes (<c>ConvertFromUtf8(..., MAX_USERNAME_SIZE)</c>) e o <c>wcscpy</c> exige um NUL
    /// interno no campo de 11.
    /// </summary>
    [TestCase(10)]
    [TestCase(11)]
    [TestCase(25)]
    public async Task CashShopGiftStorageItem_Sender_NeverExceedsTenUsefulBytes(int senderLength)
    {
        var sender = new string('n', senderLength);
        using var connection = new SpyConnection();
        await connection.SendCashShopGiftStorageItemAsync(0, 0, 0, 0, 0, 0, 'P', sender, null);

        var bytes = await connection.GetSinglePacket();
        var senderField = bytes.AsSpan(33, 11).ToArray();
        var used = Array.IndexOf(senderField, (byte)0);
        Assert.Multiple(() =>
        {
            Assert.That(used, Is.GreaterThanOrEqualTo(1), "O campo sempre contém um NUL interno.");
            Assert.That(used, Is.LessThanOrEqualTo(10), "No máximo 10 bytes úteis antes do NUL.");
            Assert.That(senderField[10], Is.EqualTo(0), "O último byte do campo é NUL (ou padding).");
        });
    }

    /// <summary>
    /// FASE 10 (RED): a mensagem nunca passa de 199 bytes úteis — o campo de 200 bytes precisa
    /// de um NUL interno para o <c>wcscpy</c> do cliente (MultiLanguage.cpp:58 + NewUIInGameShop.cpp:786).
    /// </summary>
    [TestCase(199)]
    [TestCase(200)]
    [TestCase(250)]
    public async Task CashShopGiftStorageItem_Message_NeverExceeds199UsefulBytes(int messageLength)
    {
        var message = new string('m', messageLength);
        using var connection = new SpyConnection();
        await connection.SendCashShopGiftStorageItemAsync(0, 0, 0, 0, 0, 0, 'P', null, message);

        var bytes = await connection.GetSinglePacket();
        var messageField = bytes.AsSpan(44, 200).ToArray();
        var used = Array.IndexOf(messageField, (byte)0);
        Assert.Multiple(() =>
        {
            Assert.That(used, Is.GreaterThanOrEqualTo(1), "O campo sempre contém um NUL interno.");
            Assert.That(used, Is.LessThanOrEqualTo(199), "No máximo 199 bytes úteis antes do NUL.");
            Assert.That(messageField[199], Is.EqualTo(0), "O último byte do campo é NUL (ou padding).");
        });
    }

    /// <summary>
    /// FASE 10 (RED): o truncamento nunca divide uma sequência UTF-8 — uma mensagem de 120 'á'
    /// (2 bytes cada = 240 bytes) é cortada em 99 caracteres (198 bytes) com NUL no byte 199 do
    /// campo, e os bytes usados decodificam sem caractere de substituição.
    /// </summary>
    [Test]
    public async Task CashShopGiftStorageItem_Message_TruncationKeepsUtf8Boundaries()
    {
        var message = new string('á', 120); // 240 bytes UTF-8 — acima dos 199 úteis.
        using var connection = new SpyConnection();
        await connection.SendCashShopGiftStorageItemAsync(0, 0, 0, 0, 0, 0, 'P', null, message);

        var bytes = await connection.GetSinglePacket();
        var messageField = bytes.AsSpan(44, 200).ToArray();
        var used = Array.IndexOf(messageField, (byte)0);
        Assert.Multiple(() =>
        {
            Assert.That(used, Is.EqualTo(198), "99 caracteres × 2 bytes = 198 bytes úteis — o 199º byte cortaria o 'á' ao meio.");
            Assert.That(messageField[197], Is.Not.EqualTo(0), "O último byte útil pertence ao 99º 'á'.");
            var decoded = System.Text.Encoding.UTF8.GetString(messageField, 0, used);
            Assert.That(decoded, Is.EqualTo(new string('á', 99)), "Os bytes usados decodificam exatamente 99 'á', sem sequência parcial.");
        });
    }

    /// <summary>
    /// FASE 10: emoji (4 bytes UTF-8) e multibyte seguem a mesma regra de fronteira — o corte
    /// recua para o início do caractere que não couber.
    /// </summary>
    [Test]
    public async Task CashShopGiftStorageItem_Message_EmojiTruncationKeepsUtf8Boundaries()
    {
        var emoji = "\U0001F389"; // 🎉 — 4 bytes UTF-8 (par surrogate).
        var message = string.Concat(System.Linq.Enumerable.Repeat(emoji, 60)); // 240 bytes UTF-8.
        using var connection = new SpyConnection();
        await connection.SendCashShopGiftStorageItemAsync(0, 0, 0, 0, 0, 0, 'P', null, message);

        var bytes = await connection.GetSinglePacket();
        var messageField = bytes.AsSpan(44, 200).ToArray();
        var used = Array.IndexOf(messageField, (byte)0);
        var decoded = System.Text.Encoding.UTF8.GetString(messageField, 0, used);
        var expected = string.Concat(System.Linq.Enumerable.Repeat(emoji, 49));
        Assert.Multiple(() =>
        {
            Assert.That(used, Is.EqualTo(196), "49 emoji × 4 bytes = 196 bytes úteis — o 50º não cabe antes do NUL obrigatório.");
            Assert.That(decoded, Is.EqualTo(expected), "Os bytes usados decodificam 49 emoji inteiros.");
        });
    }

    /// <summary>
    /// FASE 10: NUL embutido e caracteres de controle têm política determinística — o conteúdo
    /// no fio termina no primeiro NUL (o cliente não apresentaria nada além dele) e o restante
    /// do campo é zero. Caracteres de controle passam sem transformação (são UTF-8 válidos).
    /// </summary>
    [Test]
    public async Task CashShopGiftStorageItem_Message_EmbeddedNul_StopsWireContentAtFirstNul()
    {
        using var connection = new SpyConnection();
        await connection.SendCashShopGiftStorageItemAsync(0, 0, 0, 0, 0, 0, 'P', null, "A\0B\tC");

        var bytes = await connection.GetSinglePacket();
        Assert.Multiple(() =>
        {
            Assert.That(bytes[44], Is.EqualTo((byte)'A'));
            Assert.That(bytes[45], Is.EqualTo(0), "O primeiro NUL embutido termina o conteúdo no fio.");
            Assert.That(bytes.AsSpan(46, 198).ToArray().All(b => b == 0), Is.True, "Nada além do primeiro NUL é escrito no fio — sem divergência com o que o cliente apresenta.");
        });
    }

    /// <summary>
    /// FASE 10: mensagem com acentos do português brasileiro serializa integralmente em UTF-8.
    /// </summary>
    [Test]
    public async Task CashShopGiftStorageItem_Message_AccentedBrazilianText_IsSerializable()
    {
        var message = "Olá! Presente de Natal ção ãéíóú";
        using var connection = new SpyConnection();
        await connection.SendCashShopGiftStorageItemAsync(0, 0, 0, 0, 0, 0, 'P', null, message);

        var bytes = await connection.GetSinglePacket();
        var messageField = bytes.AsSpan(44, 200).ToArray();
        var used = Array.IndexOf(messageField, (byte)0);
        var expectedBytes = System.Text.Encoding.UTF8.GetByteCount(message);
        Assert.Multiple(() =>
        {
            Assert.That(used, Is.EqualTo(expectedBytes), "A mensagem inteira cabe: 32 caracteres × até 2 bytes < 199.");
            var decoded = System.Text.Encoding.UTF8.GetString(messageField, 0, used);
            Assert.That(decoded, Is.EqualTo(message), "Os acentos sobrevivem ao fio sem substituição.");
        });
    }

    /// <summary>
    /// FASE 10: metadados nulos (presentes antigos) produzem campos vazios e seguros — 11 e 200
    /// bytes zerados, pacote íntegro de 244 bytes; o cliente exibe remetente/mensagem vazios
    /// (caminho <c>pszUserName == NULL</c> do <c>AddStorageItem</c>).
    /// </summary>
    [Test]
    public async Task CashShopGiftStorageItem_NullMetadata_ProducesEmptySafeFields()
    {
        using var connection = new SpyConnection();
        await connection.SendCashShopGiftStorageItemAsync(9, 8, 7, 6, 5, 4, 'P', null, null);

        var bytes = await connection.GetSinglePacket();
        Assert.Multiple(() =>
        {
            Assert.That(bytes.Length, Is.EqualTo(244));
            Assert.That(bytes.AsSpan(33, 11).ToArray().All(b => b == 0), Is.True, "Remetente nulo = campo de 11 bytes zerado, NUL garantido.");
            Assert.That(bytes.AsSpan(44, 200).ToArray().All(b => b == 0), Is.True, "Mensagem nula = campo de 200 bytes zerado, NUL garantido.");
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)), Is.EqualTo(9), "Os campos base continuam corretos.");
        });
    }

    /// <summary>
    /// FASE 10 Rodada 2C — Caso 1: mensagem de 198 ASCII + 'é' (200 bytes UTF-8) contra os
    /// 199 bytes úteis do campo. O 'é' NÃO entra parcialmente: o corte recua para a fronteira
    /// e o campo fica com os 198 ASCII + NUL. Decodificação UTF-8 estrita obrigatória.
    /// </summary>
    [Test]
    public async Task CashShopGiftStorageItem_Message_198AsciiPlusAccented_TruncatesAtCharacterBoundary()
    {
        var message = new string('a', 198) + 'é'; // 198 + 2 = 200 bytes UTF-8.
        using var connection = new SpyConnection();
        await connection.SendCashShopGiftStorageItemAsync(0, 0, 0, 0, 0, 0, 'P', null, message);

        var bytes = await connection.GetSinglePacket();
        var messageField = bytes.AsSpan(44, 200).ToArray();
        var used = Array.IndexOf(messageField, (byte)0);
        Assert.Multiple(() =>
        {
            Assert.That(used, Is.EqualTo(198), "Os 198 bytes ASCII cabem; o 'é' (2 bytes) não cabe antes do NUL.");
            Assert.That(messageField[197], Is.EqualTo((byte)'a'), "O último byte útil é ASCII — nenhum C3 isolado.");
            Assert.That(messageField[198], Is.EqualTo(0), "NUL terminador dentro do campo.");
            Assert.That(messageField.AsSpan(198, 2).ToArray().All(b => b == 0), Is.True, "Nenhum byte do 'é' vaza após o corte.");
            Assert.That(() => StrictUtf8.GetString(messageField, 0, used), Throws.Nothing, "Payload decodifica em UTF-8 estrito.");
            Assert.That(StrictUtf8.GetString(messageField, 0, used), Is.EqualTo(new string('a', 198)));
        });
    }

    /// <summary>
    /// FASE 10 Rodada 2C — Caso 2: 197 ASCII + '😀' (4 bytes = 201 bytes totais) contra os
    /// 199 úteis. O emoji não cabe inteiro e não pode sair parcial (sequência F0 9F 98 80).
    /// </summary>
    [Test]
    public async Task CashShopGiftStorageItem_Message_197AsciiPlusEmoji_TruncatesAtCharacterBoundary()
    {
        var message = new string('a', 197) + "\U0001F600"; // 197 + 4 = 201 bytes UTF-8.
        using var connection = new SpyConnection();
        await connection.SendCashShopGiftStorageItemAsync(0, 0, 0, 0, 0, 0, 'P', null, message);

        var bytes = await connection.GetSinglePacket();
        var messageField = bytes.AsSpan(44, 200).ToArray();
        var used = Array.IndexOf(messageField, (byte)0);
        Assert.Multiple(() =>
        {
            Assert.That(used, Is.EqualTo(197), "O emoji não cabe nos 2 bytes restantes: o corte recua para o fim do ASCII.");
            Assert.That(messageField[197], Is.EqualTo(0), "NUL terminador dentro do campo.");
            Assert.That(messageField.AsSpan(197, 3).ToArray().All(b => b == 0), Is.True, "Nenhum byte do emoji (F0/9F/98/80) vaza após o corte.");
            Assert.That(() => StrictUtf8.GetString(messageField, 0, used), Throws.Nothing, "Payload decodifica em UTF-8 estrito.");
            Assert.That(StrictUtf8.GetString(messageField, 0, used), Is.EqualTo(new string('a', 197)));
        });
    }

    /// <summary>
    /// FASE 10 Rodada 2C — Caso 3: remetente multibyte (2 e 3 bytes por caractere) próximo ao
    /// limite de 10 bytes úteis do campo de 11. Nunca divide caractere, nunca passa de 10
    /// bytes úteis, sempre com NUL terminador.
    /// </summary>
    [TestCase("€€€a", "€€€a", 10)]      // 3×3 + 1 = 10 bytes: cabe exato.
    [TestCase("€€€aa", "€€€a", 10)]     // 11 bytes: o 2º 'a' é cortado inteiro.
    [TestCase("ááááá", "ááááá", 10)]    // 5×2 = 10 bytes: cabe exato.
    [TestCase("áááááá", "ááááá", 10)]   // 12 bytes: o 6º 'á' (2 bytes) não cabe — cortado inteiro.
    public async Task CashShopGiftStorageItem_Sender_MultibyteNearLimit_NeverSplitsOrExceedsTenBytes(
        string sender,
        string expectedContent,
        int expectedUsed)
    {
        using var connection = new SpyConnection();
        await connection.SendCashShopGiftStorageItemAsync(0, 0, 0, 0, 0, 0, 'P', sender, null);

        var bytes = await connection.GetSinglePacket();
        var senderField = bytes.AsSpan(33, 11).ToArray();
        var used = Array.IndexOf(senderField, (byte)0);
        Assert.Multiple(() =>
        {
            Assert.That(used, Is.EqualTo(expectedUsed), "Bytes úteis antes do NUL — sem caractere dividido.");
            Assert.That(used, Is.LessThanOrEqualTo(10));
            Assert.That(senderField[10], Is.EqualTo(0), "O último byte do campo é NUL (terminador ou padding).");
            Assert.That(() => StrictUtf8.GetString(senderField, 0, used), Throws.Nothing, "Payload decodifica em UTF-8 estrito.");
            Assert.That(StrictUtf8.GetString(senderField, 0, used), Is.EqualTo(expectedContent));
        });
    }

    /// <summary>
    /// FASE 10 Rodada 2C — Caso 4: string vazia deixa o campo inteiro em zero, igual a NULL
    /// (contrato determinístico; o campo zerado já contém o NUL exigido pelo cliente).
    /// </summary>
    [Test]
    public async Task CashShopGiftStorageItem_EmptyString_LeavesFieldAllZero()
    {
        using var connection = new SpyConnection();
        await connection.SendCashShopGiftStorageItemAsync(0, 0, 0, 0, 0, 0, 'P', string.Empty, string.Empty);

        var bytes = await connection.GetSinglePacket();
        Assert.Multiple(() =>
        {
            Assert.That(bytes.AsSpan(33, 11).ToArray().All(b => b == 0), Is.True, "Remetente vazio = campo de 11 bytes zerado.");
            Assert.That(bytes.AsSpan(44, 200).ToArray().All(b => b == 0), Is.True, "Mensagem vazia = campo de 200 bytes zerado.");
        });
    }

    /// <summary>
    /// FASE 10 Rodada 2C — Caso 6: surrogate órfão em string .NET não pode virar UTF-8
    /// malformado. Política determinística: o encoder .NET substitui por U+FFFD
    /// (EF BF BD) e a saída permanece UTF-8 bem-formado, decodificável em modo estrito.
    /// </summary>
    [Test]
    public async Task CashShopGiftStorageItem_LoneSurrogate_IsReplacedWithWellFormedUtf8()
    {
        var message = "A\uD83DB"; // Surrogate alto órfão entre dois ASCII.
        using var connection = new SpyConnection();
        await connection.SendCashShopGiftStorageItemAsync(0, 0, 0, 0, 0, 0, 'P', null, message);

        var bytes = await connection.GetSinglePacket();
        var messageField = bytes.AsSpan(44, 200).ToArray();
        var used = Array.IndexOf(messageField, (byte)0);
        var decoded = StrictUtf8.GetString(messageField, 0, used); // Lançaria se houvesse sequência inválida.
        Assert.Multiple(() =>
        {
            Assert.That(used, Is.EqualTo(5), "'A' + U+FFFD (3 bytes EF BF BD) + 'B'.");
            Assert.That(decoded, Is.EqualTo("A\uFFFDB"), "O surrogate órfão vira U+FFFD — nunca bytes malformados no fio.");
        });
    }

    /// <summary>
    /// Decodificador UTF-8 estrito (lança em sequência inválida) usado pelos testes críticos
    /// da Rodada 2C — não basta comparar comprimentos.
    /// </summary>
    private static readonly System.Text.UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

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
