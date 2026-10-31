// <copyright file="CustomMoveConfigurationPersistenceTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.CustomMove;

using System.Collections;
using System.Threading;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.PlugIns;
using NUnit.Framework;

/// <summary>
/// Tests for the persistence of the <see cref="CustomMoveConfiguration" /> via
/// <see cref="PlugInConfigurationExtensions" /> and <see cref="ByDataSourceReferenceHandler" />,
/// which is the exact save/load path used by the admin panel
/// (<see cref="MUnique.OpenMU.Web.Shared.Services.PlugInController" />).
/// Entries are plain data model instances (as created by the editor and the plugin),
/// while referenced maps are <see cref="IIdentifiable" /> objects of the data source.
/// </summary>
[TestFixture]
public class CustomMoveConfigurationPersistenceTests
{
    /// <summary>
    /// Tests that a configuration with moves survives a save/load round-trip,
    /// including the map reference which is resolved by the data source.
    /// </summary>
    [Test]
    public void ConfigurationSurvivesRoundTripWithMapReference()
    {
        var map = new MUnique.OpenMU.Persistence.BasicModel.GameMapDefinition { Id = Guid.NewGuid(), Name = "Lorencia" };
        using var dataSource = new FakeDataSource();
        dataSource.Add(map);
        var referenceHandler = new ByDataSourceReferenceHandler(dataSource);

        var configuration = new CustomMoveConfiguration
        {
            Enabled = false,
            Moves = new List<CustomMoveEntry>
            {
                new(Guid.NewGuid()) { Name = "bar", Command = "/bar", Map = map, X = 100, Y = 50, Cost = 100, MinLevel = 10 },
                new(Guid.NewGuid()) { Name = "free", Command = null, Map = null, X = 5, Y = 6, Cost = 0, MinLevel = 0 },
            },
        };

        var plugInConfiguration = new PlugInConfiguration();
        plugInConfiguration.SetConfiguration(configuration, referenceHandler);

        Assert.That(plugInConfiguration.CustomConfiguration, Is.Not.Null.Or.Empty);
        Assert.That(plugInConfiguration.CustomConfiguration, Does.Contain(map.Id.ToString()), "the map must be stored as reference");

        var restored = plugInConfiguration.GetConfiguration(typeof(CustomMoveConfiguration), referenceHandler);
        Assert.That(restored, Is.InstanceOf<CustomMoveConfiguration>());
        var restoredConfiguration = (CustomMoveConfiguration)restored!;

        Assert.That(restoredConfiguration.Enabled, Is.False);
        Assert.That(restoredConfiguration.Moves, Has.Count.EqualTo(2));

        var firstMove = restoredConfiguration.Moves[0];
        Assert.That(firstMove.Name, Is.EqualTo("bar"));
        Assert.That(firstMove.Command, Is.EqualTo("/bar"));
        Assert.That(firstMove.EffectiveCommand, Is.EqualTo("/bar"));
        Assert.That(firstMove.X, Is.EqualTo((byte)100));
        Assert.That(firstMove.Y, Is.EqualTo((byte)50));
        Assert.That(firstMove.Cost, Is.EqualTo(100));
        Assert.That(firstMove.MinLevel, Is.EqualTo(10));

        // The map reference must be resolved to the same instance from the data source,
        // so that the running game server works with its cached map definition.
        Assert.That(firstMove.Map, Is.SameAs(map));

        var secondMove = restoredConfiguration.Moves[1];
        Assert.That(secondMove.Name, Is.EqualTo("free"));
        Assert.That(secondMove.Command, Is.Null);
        Assert.That(secondMove.EffectiveCommand, Is.EqualTo("/free"));
        Assert.That(secondMove.Map, Is.Null);
    }

    /// <summary>
    /// Tests that an empty configuration produces a configuration without moves
    /// after a round-trip, instead of failing.
    /// </summary>
    [Test]
    public void EmptyConfigurationSurvivesRoundTrip()
    {
        using var dataSource = new FakeDataSource();
        var referenceHandler = new ByDataSourceReferenceHandler(dataSource);

        var configuration = new CustomMoveConfiguration { Enabled = true, Moves = new List<CustomMoveEntry>() };
        var plugInConfiguration = new PlugInConfiguration();
        plugInConfiguration.SetConfiguration(configuration, referenceHandler);

        var restored = plugInConfiguration.GetConfiguration(typeof(CustomMoveConfiguration), referenceHandler);
        Assert.That(restored, Is.InstanceOf<CustomMoveConfiguration>());
        Assert.That(((CustomMoveConfiguration)restored!).Moves, Is.Empty);
    }

    /// <summary>
    /// Tests that a stored configuration without JSON returns null,
    /// so that plugins fall back to their default configuration.
    /// </summary>
    [Test]
    public void MissingCustomConfigurationReturnsNull()
    {
        var plugInConfiguration = new PlugInConfiguration();
        using var dataSource = new FakeDataSource();
        var restored = plugInConfiguration.GetConfiguration(typeof(CustomMoveConfiguration), new ByDataSourceReferenceHandler(dataSource));
        Assert.That(restored, Is.Null);
    }

    /// <summary>
    /// A minimal <see cref="IDataSource{TOwner}" /> which resolves objects by their id
    /// from an in-memory dictionary, as the real data source of the admin panel does.
    /// </summary>
    private sealed class FakeDataSource : IDataSource<GameConfiguration>
    {
        private readonly Dictionary<Guid, IIdentifiable> objects = new();

        /// <summary>
        /// Adds an object to the data source, so that references to it can be resolved.
        /// </summary>
        /// <param name="identifiable">The object.</param>
        public void Add(IIdentifiable identifiable) => this.objects.Add(identifiable.Id, identifiable);

        /// <inheritdoc />
        public IIdentifiable? Get(Guid id) => this.objects.TryGetValue(id, out var result) ? result : null;

        /// <inheritdoc />
        public ValueTask<IContext> GetContextAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <inheritdoc />
        public ValueTask<GameConfiguration> GetOwnerAsync(Guid ownerId = default, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <inheritdoc />
        ValueTask<object> IDataSource.GetOwnerAsync(Guid ownerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        /// <inheritdoc />
        public bool IsSupporting(Type type) => true;

        /// <inheritdoc />
        public ValueTask DiscardChangesAsync() => throw new NotSupportedException();

        /// <inheritdoc />
        public ValueTask ForceDiscardChangesAsync() => throw new NotSupportedException();

        /// <inheritdoc />
        public IEnumerable<T> GetAll<T>() => throw new NotSupportedException();

        /// <inheritdoc />
        public IEnumerable GetAll(Type type) => throw new NotSupportedException();

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to dispose.
        }
    }
}
