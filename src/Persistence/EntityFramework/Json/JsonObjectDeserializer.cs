// <copyright file="JsonObjectDeserializer.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.EntityFramework.Json;

using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.PlugIns;
using System.Text.Json;

/// <summary>
/// A deserializer which parses the json retrieved from the postgres database by using a query built by the <see cref="JsonQueryBuilder"/>.
/// We need to register a special binary converter, because postgres provides binary data in a non-standard format.
/// </summary>
/// <remarks>
/// We additionally register the <see cref="LocalizedStringJsonConverter"/> here, because the query
/// built by the <see cref="JsonQueryBuilder"/> is composed with postgres' <c>row_to_json</c>, which
/// serializes every column by its native database type. A <see cref="LocalizedString"/> is stored in
/// a <c>varchar</c> column, so it arrives as a plain json string (for example <c>"Name": "Dark Wizard"</c>).
/// Without the converter, <c>System.Text.Json</c> tries to read it as an object and fails with
/// "The JSON value could not be converted to MUnique.OpenMU.Interfaces.LocalizedString".
/// Registering it in the persistence layer keeps every host working, instead of relying on the
/// application entry points (<c>Startup.Program</c> and <c>DaprService</c>) having registered it.
/// </remarks>
public class JsonObjectDeserializer : MUnique.OpenMU.Persistence.Json.JsonObjectDeserializer
{
    /// <inheritdoc/>
    protected override void BeforeDeserialize(JsonSerializerOptions options)
    {
        base.BeforeDeserialize(options);
        foreach (var converter in JsonConverterRegistry.Converters)
        {
            options.Converters.Add(converter);
        }

        if (!options.Converters.Any(converter => converter is LocalizedStringJsonConverter))
        {
            options.Converters.Add(new LocalizedStringJsonConverter());
        }

        // Same reasoning as above: postgres returns bytea as a hex string, which the default
        // converter tries to read as base64. It was only registered by the application entry
        // points before, so any other host reading binary columns failed here.
        if (!options.Converters.Any(converter => converter is BinaryAsHexJsonConverter))
        {
            options.Converters.Add(new BinaryAsHexJsonConverter());
        }
    }
}