using System;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Threading.Tasks;

#nullable disable

namespace MUnique.OpenMU.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddCashShopStorageGiftFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IsGift separa as linhas recebidas como presente das compradas pela própria conta.
            // O cliente escolhe a aba com o byte InventoryType do request D2 05 ('S' = compras,
            // 'G' = presentes) e o servidor filtra por esta coluna, então a aba pedida e as
            // linhas devolvidas passam a concordar. O default false mantém as linhas já existentes
            // como compras, que é a semântica anterior a esta coluna.
            migrationBuilder.AddColumn<bool>(
                name: "IsGift",
                schema: "data",
                table: "CashShopStorageItem",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // A unicidade passa a ser POR ABA. O StorageIndex é a posição da linha dentro da aba
            // que o cliente está vendo, então a conta pode ter a posição 0 na aba 'S' e na 'G'
            // ao mesmo tempo; com a unicidade global anterior, o primeiro presente recebido
            // colidiria com a primeira compra e a constraint derrubaria a entrega inteira.
            migrationBuilder.DropIndex(
                name: "IX_CashShopStorageItem_AccountId_StorageIndex",
                schema: "data",
                table: "CashShopStorageItem");

            migrationBuilder.CreateIndex(
                name: "IX_CashShopStorageItem_AccountId_IsGift_StorageIndex",
                schema: "data",
                table: "CashShopStorageItem",
                columns: new[] { "AccountId", "IsGift", "StorageIndex" },
                unique: true);

            // Cobre o filtro (conta, aba) que a listagem faz ao responder a aba pedida.
            migrationBuilder.CreateIndex(
                name: "IX_CashShopStorageItem_AccountId_IsGift",
                schema: "data",
                table: "CashShopStorageItem",
                columns: new[] { "AccountId", "IsGift" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // O downgrade NÃO é reversível depois que houver slots sobrepostos entre abas.
            //
            // A unicidade nova é (AccountId, IsGift, StorageIndex), então a mesma conta pode ter a
            // posição 0 na aba 'S' e na 'G'. Recriar a constraint global (AccountId, StorageIndex)
            // sobre esses dados violaria a unicidade e a migration falharia no meio, deixando o
            // banco sem a coluna IsGift e sem a unicidade por aba.
            //
            // Em vez de apagar, reindexar ou perder itens, o downgrade se recusa de forma explícita
            // quando há sobreposição, e apenas prossegue quando ela não existe. Reindexar
            // automaticamente reescreveria o StorageIndex que o cliente já conhece e poderia
            // apontar uma entrega para outra linha — pior que falhar.
            // migrationBuilder.Sql() only builds an operation, it does not execute it, so the
            // A guarda e a primeira operacao do script de downgrade: se houver sobreposicao, o
            // servidor aborta a transacao antes de qualquer comando destrutivo, e o esquema
            // fica intacto. Nada e apagado nem reindexado.
            GuardAgainstOverlappingSlots(migrationBuilder);
            migrationBuilder.DropIndex(
                name: "IX_CashShopStorageItem_AccountId_IsGift",
                schema: "data",
                table: "CashShopStorageItem");

            migrationBuilder.DropIndex(
                name: "IX_CashShopStorageItem_AccountId_IsGift_StorageIndex",
                schema: "data",
                table: "CashShopStorageItem");

            // Só agora, com a garantia de que não há sobreposição, a unicidade global pode ser
            // restaurada sem perder itens.
            migrationBuilder.CreateIndex(
                name: "IX_CashShopStorageItem_AccountId_StorageIndex",
                schema: "data",
                table: "CashShopStorageItem",
                columns: new[] { "AccountId", "StorageIndex" },
                unique: true);

            migrationBuilder.DropColumn(
                name: "IsGift",
                schema: "data",
                table: "CashShopStorageItem");
        }
        /// <summary>
        /// Counts the accounts that have more than one storage row with the same
        /// <c>(AccountId, StorageIndex)</c> across the two tabs, which is what would break the
        /// restored global uniqueness.
        /// </summary>
        /// <param name="migrationBuilder">The migration builder, whose connection is used.</param>
        /// <returns>The number of overlapping accounts.</returns>
        /// Guards the downgrade against accounts that have more than one storage row with the same
        /// <c>(AccountId, StorageIndex)</c> across the two tabs, which is what would break the
        /// restored global uniqueness.
        /// </summary>
        /// <remarks>
        /// <see cref="MigrationBuilder.Sql(string)"/> only appends an operation to the generated
        /// script, it does not run it, so the check cannot be read as a value here. Instead the
        /// guard is a statement that the server evaluates: <c>RAISE EXCEPTION</c> aborts the whole
        /// transaction before any destructive statement runs, so a refused downgrade leaves the
        /// schema exactly as it was. Reindexing automatically was rejected on purpose: it would
        /// rewrite <c>StorageIndex</c> values the client already knows and could point a delivery
        /// at the wrong row.
        /// </remarks>
        private static void GuardAgainstOverlappingSlots(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM data."CashShopStorageItem"
                        GROUP BY "AccountId", "StorageIndex"
                        HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION
                            'The cash shop gift storage cannot be downgraded: existing rows share the same (AccountId, StorageIndex) across the purchase and gift tabs. Reindexing them automatically would rewrite StorageIndex values the client already knows, so the downgrade is refused instead of losing or corrupting data. Reassign the StorageIndex of the affected rows by hand, or leave the gift column in place.';
                    END IF;
                END $$;
                """);
        }
    }
}
