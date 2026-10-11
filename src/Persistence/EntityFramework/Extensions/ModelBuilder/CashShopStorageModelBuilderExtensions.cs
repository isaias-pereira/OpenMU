// <copyright file="CashShopStorageModelBuilderExtensions.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.EntityFramework.Extensions.ModelBuilder;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MUnique.OpenMU.Persistence.EntityFramework.Model;

internal static class CashShopStorageItemExtensions
{
    /// <summary>
    /// Aplica as configurações da entidade <see cref="CashShopStorageItem"/>.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static void Apply(this EntityTypeBuilder<CashShopStorageItem> builder)
    {
        // chItemType é um único caractere ('P' ou 'C'); o limite impede que uma
        // linha inválida seja gravada e descartada silenciosamente pelo cliente.
        builder.Property(i => i.ItemType).HasMaxLength(1).IsRequired();

        // Metadados de presente (Fase 10). Nullable: presentes antigos e compras não têm
        // remetente/mensagem, e nenhum registro existente é preenchido retroativamente.
        // GiftSender é o nome do personagem remetente (o campo do cliente aceita 11 bytes =
        // 10 caracteres + NUL, MAX_USERNAME_SIZE). GiftMessage é a mensagem; o limite da
        // coluna é em CARACTERES e independe do limite de bytes UTF-8 do fio (199 bytes úteis
        // + NUL), que é responsabilidade do serializer D2 0E, não do banco.
        builder.Property(i => i.GiftSender).HasMaxLength(10);
        builder.Property(i => i.GiftMessage).HasMaxLength(200);

        builder.HasIndex(i => i.AccountId);

        // A unicidade é POR ABA, e não por conta. O StorageIndex é a posição da linha dentro da
        // aba que o cliente está vendo (o cliente escolhe a aba pelo byte InventoryType do request
        // D2 05 e pagina a resposta), então a conta pode ter a posição 0 tanto na aba 'S' quanto
        // na 'G'. Com a unicidade global anterior, o primeiro presente recebido colidiria com a
        // primeira compra e derrubaria a entrega inteira pela constraint.
        builder.HasIndex(i => new { i.AccountId, i.IsGift, i.StorageIndex }).IsUnique();

        // Cobre o filtro (conta, aba) que a listagem faz ao responder a aba pedida.
        builder.HasIndex(i => new { i.AccountId, i.IsGift });


        // O storage é da conta (1:N), e não do personagem: o item comprado precisa
        // continuar acessível depois de trocar de personagem, assim como a carteira
        // de WCOIN que é debitada na mesma operação. A FK do Item é gerada pelo
        // MemberOfAggregate e usa Cascade, então apagar a linha apaga o item junto.
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(i => i.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.ItemId);
    }
}
