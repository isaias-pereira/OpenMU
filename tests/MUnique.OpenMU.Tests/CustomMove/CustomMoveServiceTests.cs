// <copyright file="CustomMoveServiceTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.CustomMove;

using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using NUnit.Framework;

/// <summary>
/// Tests for the <see cref="CustomMoveService"/>.
/// </summary>
[TestFixture]
public class CustomMoveServiceTests
{
    /// <summary>
    /// Tests that a move is found by its configured command alias.
    /// </summary>
    [Test]
    public void GetMoveByCommandAlias()
    {
        var move = CreateMove(name: "lorencia_bar", command: "bar");
        var configuration = CreateConfiguration(move);

        var result = CustomMoveService.GetMove("/bar", configuration);

        Assert.That(result, Is.SameAs(move));
    }

    /// <summary>
    /// Tests that the command lookup is case-insensitive.
    /// </summary>
    [Test]
    public void GetMoveIsCaseInsensitive()
    {
        var move = CreateMove(name: "lorencia_bar", command: "bar");
        var configuration = CreateConfiguration(move);

        var result = CustomMoveService.GetMove("/BAR", configuration);

        Assert.That(result, Is.SameAs(move));
    }

    /// <summary>
    /// Tests that a move without a configured command is found by its name.
    /// </summary>
    [Test]
    public void GetMoveByNameWhenCommandIsNull()
    {
        var move = CreateMove(name: "bar");
        var configuration = CreateConfiguration(move);

        var result = CustomMoveService.GetMove("/bar", configuration);

        Assert.That(result, Is.SameAs(move));
    }

    /// <summary>
    /// Tests that a command which was configured with a leading slash is tolerated
    /// (the effective command is normalized to a single leading slash).
    /// </summary>
    [Test]
    public void GetMoveToleratesLeadingSlashInCommand()
    {
        var move = CreateMove(name: "lorencia_bar", command: "/bar");
        var configuration = CreateConfiguration(move);

        Assert.That(move.EffectiveCommand, Is.EqualTo("/bar"));
        Assert.That(CustomMoveService.GetMove("/bar", configuration), Is.SameAs(move));
    }

    /// <summary>
    /// Tests that a command which was configured with surrounding whitespace is tolerated.
    /// </summary>
    [Test]
    public void GetMoveToleratesSurroundingWhitespaceInCommand()
    {
        var move = CreateMove(name: "lorencia_bar", command: " /bar ");
        var configuration = CreateConfiguration(move);

        Assert.That(move.EffectiveCommand, Is.EqualTo("/bar"));
        Assert.That(CustomMoveService.GetMove("/bar", configuration), Is.SameAs(move));
    }

    /// <summary>
    /// Tests that an empty command falls back to the move name.
    /// </summary>
    [Test]
    public void GetMoveFallsBackToNameWhenCommandIsEmpty()
    {
        var move = CreateMove(name: "foo", command: string.Empty);
        var configuration = CreateConfiguration(move);

        var result = CustomMoveService.GetMove("/foo", configuration);

        Assert.That(result, Is.SameAs(move));
    }

    /// <summary>
    /// Tests that no move is returned when nothing matches.
    /// </summary>
    [Test]
    public void GetMoveReturnsNullWhenNoMoveMatches()
    {
        var configuration = CreateConfiguration(CreateMove(name: "bar", command: "bar"));

        var result = CustomMoveService.GetMove("/unknown", configuration);

        Assert.That(result, Is.Null);
    }

    /// <summary>
    /// Tests that no move is returned when the moves list is null.
    /// </summary>
    [Test]
    public void GetMoveReturnsNullWhenMovesListIsNull()
    {
        var configuration = new CustomMoveConfiguration { Moves = null! };

        var result = CustomMoveService.GetMove("/bar", configuration);

        Assert.That(result, Is.Null);
    }

    /// <summary>
    /// Tests that no move is returned when the configuration is null.
    /// </summary>
    [Test]
    public void GetMoveReturnsNullWhenConfigurationIsNull()
    {
        var result = CustomMoveService.GetMove("/bar", null!);

        Assert.That(result, Is.Null);
    }

    private static CustomMoveEntry CreateMove(string name, string? command = null)
    {
        return new CustomMoveEntry(Guid.NewGuid()) { Name = name, Command = command };
    }

    private static CustomMoveConfiguration CreateConfiguration(params CustomMoveEntry[] moves)
    {
        return new CustomMoveConfiguration { Enabled = true, Moves = moves.ToList() };
    }
}
