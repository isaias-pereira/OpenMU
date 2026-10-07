namespace MUnique.OpenMU.Tests.CashShop;

using System.Collections.Generic;

/// <summary>
///     Documento da fixture <c>CashShopCatalogBuyableGroups.json</c>, gerada por
///     <c>tools/generate-cashshop-fixture.ps1</c> a partir dos IBS 512.2012.084.
/// </summary>
/// <remarks>
///     O snapshot existe para que o teste de paridade rode sem os arquivos IBS e sem caminho de
///     maquina. Ele nao e copia dos IBS: contem apenas os campos necessarios para reproduzir a
///     selecao que o <see cref="CashShopSelectionResolver"/> faz.
/// </remarks>
public sealed class BuyableGroupsDocument
{
    /// <summary>Gets or sets the provenance of the snapshot.</summary>
    public BuyableGroupsMetadata? Metadata { get; set; }

    /// <summary>Gets or sets the buyable packages.</summary>
    public List<BuyablePackage>? Packages { get; set; }

    /// <summary>Gets or sets the buyable product groups.</summary>
    public List<BuyableGroup>? Groups { get; set; }
}

/// <summary>
///     Proveniencia e algoritmo usados para gerar o snapshot.
/// </summary>
public sealed class BuyableGroupsMetadata
{
    /// <summary>Gets or sets the IBS version the snapshot was derived from.</summary>
    public string? Source { get; set; }

    /// <summary>Gets or sets the SHA-256 of the IBS product file.</summary>
    public string? ProductSha256 { get; set; }

    /// <summary>Gets or sets the SHA-256 of the IBS package file.</summary>
    public string? PackageSha256 { get; set; }

    /// <summary>Gets or sets the generator that produced the snapshot.</summary>
    public string? Generator { get; set; }

    /// <summary>Gets or sets the number of groups the snapshot is expected to contain.</summary>
    public int ExpectedGroupCount { get; set; }
}

/// <summary>
///     Um pacote do catalogo, com os campos que o resolver le.
/// </summary>
public sealed class BuyablePackage
{
    /// <summary>Gets or sets the package sequence.</summary>
    public int PackageProductSeq { get; set; }

    /// <summary>Gets or sets the display sequence (category) of the package.</summary>
    public int ProductDisplaySeq { get; set; }

    /// <summary>Gets or sets the package type (170 is a normal, non-random package).</summary>
    public int PackageProductType { get; set; }

    /// <summary>Gets or sets the sales flag (182 is on sale).</summary>
    public int SalesFlag { get; set; }

    /// <summary>Gets or sets the gift flag; 184 enables the present button in the client.</summary>
    public int GiftFlag { get; set; }

    /// <summary>Gets or sets the delete flag (181 is active).</summary>
    public int DeleteFlag { get; set; }

    /// <summary>Gets or sets the event flag (200 is a normal item).</summary>
    public int EventFlag { get; set; }

    /// <summary>Gets or sets the cash type flag (669 is an online purchase).</summary>
    public int CashTypeFlag { get; set; }

    /// <summary>Gets or sets the cash type, which is the currency (509 is W Coin(P)).</summary>
    public int CashType { get; set; }

    /// <summary>Gets or sets a value indicating whether the package is bought with mileage.</summary>
    public int DeductMileageFlag { get; set; }

    /// <summary>Gets or sets the package price.</summary>
    public int Price { get; set; }

    /// <summary>Gets or sets the in-game item code of the package.</summary>
    public string? InGamePackageID { get; set; }

    /// <summary>Gets or sets the declared product sequences.</summary>
    public List<int> ProductSeqList { get; set; } = new();

    /// <summary>Gets or sets the declared price sequences.</summary>
    public List<int> PriceSeqList { get; set; } = new();
}
/// <summary>
///     Um grupo de produto, isto e, as linhas que descrevem uma unica oferta.
/// </summary>
/// <remarks>
///     Um grupo e identificado por <c>(ProductSeq, PriceSeq, InGamePackageID)</c>. As linhas dentro
///     dele sao atributos complementares da mesma oferta — o cliente percorre todas em
///     <c>GetPriceSeqNext</c> e monta um unico pedido —, e nao variantes concorrentes.
/// </remarks>
public sealed class BuyableGroup
{
    /// <summary>Gets or sets the product sequence.</summary>
    public int ProductSeq { get; set; }

    /// <summary>Gets or sets the price sequence that selects the variant.</summary>
    public int PriceSeq { get; set; }

    /// <summary>Gets or sets the in-game item code, which defines the delivered item.</summary>
    public string? InGamePackageID { get; set; }

    /// <summary>Gets or sets the price charged.</summary>
    public int Price { get; set; }

    /// <summary>Gets or sets the product type.</summary>
    public int ProductType { get; set; }

    /// <summary>Gets or sets the storage group, which is transported metadata and not logical identity.</summary>
    public int StorageGroup { get; set; }

    /// <summary>Gets or sets the delete flag, which is the activity filter.</summary>
    public int DeleteFlag { get; set; }

    /// <summary>Gets or sets the catalog name.</summary>
    public string? ProductName { get; set; }

    /// <summary>Gets or sets the complementary attribute rows of the offer.</summary>
    public List<BuyableGroupRow> Rows { get; set; } = new();
}

/// <summary>
///     Uma linha de atributo dentro de um grupo.
/// </summary>
public sealed class BuyableGroupRow
{
    /// <summary>Gets or sets the property sequence, which is the attribute tag and not a variant key.</summary>
    public int PropertySeq { get; set; }

    /// <summary>Gets or sets the property type.</summary>
    public int PropertyType { get; set; }

    /// <summary>Gets or sets the attribute value.</summary>
    public string? Value { get; set; }

    /// <summary>Gets or sets the unit name.</summary>
    public string? UnitName { get; set; }

    /// <summary>Gets or sets the unit type, which is a measure and never a currency.</summary>
    public int UnitType { get; set; }

    /// <summary>Gets or sets the must flag (145 required, 146 optional).</summary>
    public int MustFlag { get; set; }

    /// <summary>Gets or sets the share flag.</summary>
    public int ShareFlag { get; set; }

    /// <summary>Gets or sets the presentation order.</summary>
    public int ViewOrder { get; set; }

    /// <summary>Gets or sets the attribute name.</summary>
    public string? PropertyName { get; set; }
}

/// <summary>
///     O catalogo carregado da fixture, ja validado.
/// </summary>
public sealed class BuyableGroupsCatalog
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="BuyableGroupsCatalog"/> class.
    /// </summary>
    /// <param name="groups">The buyable groups.</param>
    /// <param name="packages">The packages that reference them.</param>
    /// <param name="expectedGroupCount">The expected group count from the metadata.</param>
    public BuyableGroupsCatalog(List<BuyableGroup> groups, List<BuyablePackage> packages, int expectedGroupCount)
    {
        this.Groups = groups;
        this.Packages = packages;
        this.ExpectedGroupCount = expectedGroupCount;
    }

    /// <summary>Gets the buyable groups.</summary>
    public List<BuyableGroup> Groups { get; }

    /// <summary>Gets the packages.</summary>
    public List<BuyablePackage> Packages { get; }

    /// <summary>Gets the expected group count.</summary>
    public int ExpectedGroupCount { get; }
}