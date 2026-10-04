// <copyright file="LocalizedStringPostgresJsonTests.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Tests;

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using ReferenceHandler = MUnique.OpenMU.Persistence.Json.IdReferenceHandler;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.Persistence.EntityFramework.Json;
using NUnit.Framework;

/// <summary>
/// Reproduz a incompatibilidade entre o JSON produzido pelo PostgreSQL e o tipo
/// <see cref="LocalizedString"/> no caminho Entity Framework.
/// </summary>
/// <remarks>
/// <para>
/// O <c>JsonQueryBuilder</c> monta o JSON da leitura com <c>row_to_json</c>, que serializa cada
/// coluna pelo seu tipo nativo no PostgreSQL. Uma coluna <c>varchar</c> — como a que guarda uma
/// <see cref="LocalizedString"/> — chega como uma string JSON simples, por exemplo
/// <c>"Name": "Dark Wizard"</c>. O <see cref="LocalizedString"/> é um <c>struct</c> com uma única
/// propriedade, então o <c>System.Text.Json</c> tentaria lê-lo como um objeto e falharia.
/// </para>
/// <para>
/// O projeto já tem o <see cref="LocalizedStringJsonConverter"/>, que faz exatamente a conversão
/// correta nos dois sentidos. O defeito é que ele só era registrado pelos pontos de entrada da
/// aplicação (<c>Startup.Program</c> e <c>DaprService</c>), e não pela camada de persistência: um
/// host que usasse o caminho EF/PostgreSQL sem passar por eles — testes, ferramentas — não o tinha.
/// </para>
/// <para>
/// O <c>InMemoryPersistenceContextProvider</c> não é afetado porque mantém objetos vivos em memória,
/// sem passar por JSON.
/// </para>
/// </remarks>
[TestFixture]
public class LocalizedStringPostgresJsonTests
{
    /// <summary>
    /// Reproduz o formato que o PostgreSQL devolve e exige que a leitura funcione sem que o
    /// chamador registre conversores manualmente.
    /// </summary>
    [Test]
    public void Deserialize_PostgresRowToJsonShape_ReadsLocalizedString()
    {
        // Exatamente o que row_to_json produz para uma linha cuja coluna Name é varchar.
        const string postgresJson = """{"Id":"11111111-1111-1111-1111-111111111111","Name":"Dark Wizard"}""";

        var deserializer = new JsonObjectDeserializer();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(postgresJson));

        var result = deserializer.Deserialize<AccountLike>(stream, new ReferenceHandler());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null, "The object must be readable from the postgres json.");
            Assert.That(result!.Name.Value, Is.EqualTo("Dark Wizard"), "The localized string must keep its value.");
        });
    }

    /// <summary>
    /// O valor <c>null</c> de uma coluna nullable precisa continuar virando um
    /// <see cref="LocalizedString"/> vazio, e não uma exceção.
    /// </summary>
    [Test]
    public void Deserialize_NullColumn_YieldsEmptyLocalizedString()
    {
        const string postgresJson = """{"Id":"11111111-1111-1111-1111-111111111111","Name":null}""";

        var deserializer = new JsonObjectDeserializer();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(postgresJson));

        var result = deserializer.Deserialize<AccountLike>(stream, new ReferenceHandler());

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name.Value, Is.Null, "A null column must deserialize into a default LocalizedString.");
    }

    /// <summary>
    /// A escrita precisa produzir o mesmo formato que a leitura aceita, para que ida e volta não
    /// quebrem: string simples, nunca um objeto com a propriedade <c>Value</c>.
    /// </summary>
    [Test]
    public async Task Serialize_ProducesStringShape_ThatRoundTrips()
    {
        var serializer = new MUnique.OpenMU.Persistence.Json.JsonObjectSerializer();
        using var cancellation = new System.Threading.CancellationTokenSource();
        var json = await serializer.SerializeAsync(new AccountLike { Name = new LocalizedString("Blade Master") }, cancellation.Token).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"Name\": \"Blade Master\""), "The name must be written as a plain json string.");
            Assert.That(json, Does.Not.Contain("\"Value\""), "The struct must not be written as an object.");
        });
    }

    /// <summary>
    /// O conversor do próprio tipo continua sendo o usado, sem depender do registro global.
    /// </summary>
    [Test]
    public void Converter_HandlesStringAndNullTokens()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new LocalizedStringJsonConverter());

        Assert.Multiple(() =>
        {
            Assert.That(
                JsonSerializer.Deserialize<LocalizedString>("\"Value\"", options).Value,
                Is.EqualTo("Value"),
                "A json string must deserialize into the localized value.");
            Assert.That(
                JsonSerializer.Deserialize<LocalizedString>("null", options).Value,
                Is.Null,
                "A json null must deserialize into a default value.");
        });
    }

    /// <summary>
    /// Um objeto mínimo com uma propriedade <see cref="LocalizedString"/>, no formato usado pelos
    /// testes para exercitar apenas a serialização.
    /// </summary>
    public sealed class AccountLike
    {
        /// <summary>
        /// Gets or sets the identifier.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets the localized name.
        /// </summary>
        public LocalizedString Name { get; set; }
    }
}

