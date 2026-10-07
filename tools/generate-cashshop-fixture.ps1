<#
.SYNOPSIS
    Gera a fixture portavel tests/MUnique.OpenMU.Tests/CashShop/TestData/CashShopCatalogBuyableGroups.json
    a partir dos arquivos IBS reais. Nao grava no banco e nao altera os IBS.

.DESCRIPTION
    Reproduz a regra de "grupo compravel" que o CashShopSelectionResolver usa hoje, para que o
    teste de paridade rode sem os arquivos IBS e sem caminho local.

    Filtros de pacote (valores verificados nas 212 linhas de IBSPackage.txt):
      CashType           = 509 (W Coin(P), a carteira que este servidor cobra)
      DeleteFlag         = 181 (pacote ativo)
      SalesFlag          = 182 (a venda)
      PackageProductType = 170 (pacote normal, nao sorteio)
      EventFlag          = 200 (item normal)
      CashTypeFlag       = 669 (compra online)
      DeductMileageFlag  = 0   (nao e compra por milagem)

    Um grupo e (ProductSeq, PriceSeq, InGamePackageID): o produto cartesiano de ProductSeqList x
    PriceSeqList do pacote, limitado as linhas que existem de fato em IBSProduct.txt.

    A saida e deterministica: pacotes e grupos ordenados por chave, linhas por PropertySeq.

.PARAMETER Source
    Diretorio que contem IBSProduct.txt e IBSPackage.txt.

.PARAMETER Output
    Caminho do JSON a escrever.
#>
[CmdletBinding()]
param(
    [string]$Source = 'C:\Users\isaia\OneDrive\Desktop\Mu Offline\Data\InGameShopScript\512.2012.084',
    [string]$Output = "$PSScriptRoot\..\tests\MUnique.OpenMU.Tests\CashShop\TestData\CashShopCatalogBuyableGroups.json"
)

$ErrorActionPreference = 'Stop'
[System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)
$enc = [System.Text.Encoding]::GetEncoding(949)

$productPath = Join-Path $Source 'IBSProduct.txt'
$packagePath = Join-Path $Source 'IBSPackage.txt'

function Get-Sha256([string]$p) { (Get-FileHash $p -Algorithm SHA256).Hash }

function Read-IbsProduct {
    foreach ($line in [System.IO.File]::ReadAllLines($productPath, $enc)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $f = $line.Split('@')
        if ($f.Count -lt 17) { continue }
        [pscustomobject]@{
            ProductSeq = [int]$f[0]; ProductName = $f[1]; PropertyName = $f[2]; Value = $f[3]
            UnitName = $f[4]; Price = [int]$f[5]; PriceSeq = [int]$f[6]; PropertyType = [int]$f[7]
            MustFlag = [int]$f[8]; ViewOrder = [int]$f[9]; DeleteFlag = [int]$f[10]
            StorageGroup = [int]$f[11]; ShareFlag = [int]$f[12]; InGamePackageID = $f[13]
            PropertySeq = [int]$f[14]; ProductType = [int]$f[15]; UnitType = [int]$f[16]
        }
    }
}

function Read-IbsPackage {
    foreach ($line in [System.IO.File]::ReadAllLines($packagePath, $enc)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $f = $line.Split('@')
        if ($f.Count -lt 27) { continue }
        [pscustomobject]@{
            ProductDisplaySeq = [int]$f[0]; PackageProductSeq = [int]$f[2]; PackageProductType = [int]$f[4]
            Price = [int]$f[5]; SalesFlag = [int]$f[8]; GiftFlag = [int]$f[9]; StartDate = $f[10]
            EndDate = $f[11]; DeleteFlag = [int]$f[16]; EventFlag = [int]$f[17]
            ProductSeqList = $f[19]; InGamePackageID = $f[20]; PriceSeqList = $f[23]
            DeductMileageFlag = [int]$f[24]; CashType = [int]$f[25]; CashTypeFlag = [int]$f[26]
        }
    }
}

$productRows = @(Read-IbsProduct)
$packageRows = @(Read-IbsPackage)

$buyable = $packageRows | Where-Object {
    $_.CashType -eq 509 -and $_.DeleteFlag -eq 181 -and $_.SalesFlag -eq 182 -and
    $_.PackageProductType -eq 170 -and $_.EventFlag -eq 200 -and
    $_.CashTypeFlag -eq 669 -and $_.DeductMileageFlag -eq 0
} | Sort-Object PackageProductSeq

# Varias linhas (PropertySeq diferentes) compartilham a mesma chave de grupo, entao o indice
# acumula uma lista por chave em vez de sobrescrever: usar so a ultima linha perderia exatamente
# a informacao que o teste existe para provar.
$index = @{}
foreach ($p in $productRows) {
    $key = "$($p.ProductSeq)|$($p.PriceSeq)|$($p.InGamePackageID)"
    if ($index.ContainsKey($key)) { $index[$key] += $p } else { $index[$key] = @($p) }
}

$groupMap = [ordered]@{}
$packageOut = @()
foreach ($pkg in $buyable) {
    $productSeqs = @($pkg.ProductSeqList.Split('|') | Where-Object { $_ -ne '' })
    $priceSeqs   = @($pkg.PriceSeqList.Split('|')   | Where-Object { $_ -ne '' })
    $packageOut += [ordered]@{
        packageProductSeq = $pkg.PackageProductSeq; productDisplaySeq = $pkg.ProductDisplaySeq
        packageProductType = $pkg.PackageProductType; salesFlag = $pkg.SalesFlag
        giftFlag = $pkg.GiftFlag; deleteFlag = $pkg.DeleteFlag; eventFlag = $pkg.EventFlag
        cashTypeFlag = $pkg.CashTypeFlag; cashType = $pkg.CashType
        deductMileageFlag = $pkg.DeductMileageFlag; startDate = $pkg.StartDate
        endDate = $pkg.EndDate; price = $pkg.Price; inGamePackageID = $pkg.InGamePackageID
        productSeqList = @($productSeqs | ForEach-Object { [int]$_ })
        priceSeqList = @($priceSeqs | ForEach-Object { [int]$_ })
    }
    foreach ($ps in $productSeqs) {
        foreach ($xs in $priceSeqs) {
            $key = "$ps|$xs|$($pkg.InGamePackageID)"
            if ($index.ContainsKey($key) -and -not $groupMap.Contains($key)) { $groupMap[$key] = @($index[$key]) }
        }
    }
}

$groups = @()
foreach ($key in ($groupMap.Keys | Sort-Object)) {
    $rows = @($groupMap[$key] | Sort-Object PropertySeq)
    $head = $rows[0]
    $groups += [ordered]@{
        productSeq = $head.ProductSeq; priceSeq = $head.PriceSeq
        inGamePackageID = $head.InGamePackageID; price = $head.Price
        productType = $head.ProductType; storageGroup = $head.StorageGroup
        deleteFlag = $head.DeleteFlag; productName = $head.ProductName
        rows = @($rows | ForEach-Object {
            [ordered]@{
                propertySeq = $_.PropertySeq; propertyType = $_.PropertyType
                value = $_.Value; unitName = $_.UnitName; unitType = $_.UnitType
                mustFlag = $_.MustFlag; shareFlag = $_.ShareFlag
                viewOrder = $_.ViewOrder; propertyName = $_.PropertyName
            }
        })
    }
}

$document = [ordered]@{
    metadata = [ordered]@{
        description = 'Grupos de produto do catalogo IBS considerados compraveis por W Coin(P). Snapshot para teste de paridade; nao e copia dos IBS.'
        source = 'IBS 512.2012.084'
        productFile = 'IBSProduct.txt'
        productSha256 = Get-Sha256 $productPath
        packageFile = 'IBSPackage.txt'
        packageSha256 = Get-Sha256 $packagePath
        generator = 'tools/generate-cashshop-fixture.ps1'
        algorithm = 'Pacotes filtrados por CashType/DeleteFlag/SalesFlag/PackageProductType/EventFlag/CashTypeFlag/DeductMileageFlag; grupo = (ProductSeq, PriceSeq, InGamePackageID) do produto cartesiano ProductSeqList x PriceSeqList que exista em IBSProduct.txt. Saida ordenada por chave, linhas por PropertySeq.'
        packageFilter = [ordered]@{ cashType = 509; deleteFlag = 181; salesFlag = 182; packageProductType = 170; eventFlag = 200; cashTypeFlag = 669; deductMileageFlag = 0 }
        expectedGroupCount = 143
        expectedPackageCount = 113
    }
    packages = $packageOut
    groups = $groups
}

$outDir = Split-Path -Parent $Output
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
[System.IO.File]::WriteAllText($Output, ($document | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))

Write-Output ("pacotes = " + $packageOut.Count)
Write-Output ("grupos   = " + $groups.Count)
Write-Output ("linhas   = " + (($groups | ForEach-Object { $_.rows.Count } | Measure-Object -Sum).Sum))
Write-Output ("gravado  = " + $Output)