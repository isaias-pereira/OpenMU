// <copyright file="ModalCreateNewRenderTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.CustomMove;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Web.Shared.Components.Form;
using MUnique.OpenMU.Web.Shared.Components.Form.Modal;
using MUnique.OpenMU.Web.Shared.Services;
using NUnit.Framework;

/// <summary>
/// Render tests for the custom move configuration modal. Regression test for the
/// "An unhandled error has occurred" crash: the standard Blazor <c>InputNumber<byte></c>
/// does not support the byte type ("The type 'System.Byte' is not a supported numeric
/// type"), so the move editor must use the custom <c>InputByte</c> component, and the
/// form components require the <c>EditForm</c> wrapper in <see cref="ModalCreateNew{TItem}" />.
/// </summary>
[TestFixture]
public class ModalCreateNewRenderTests
{
    /// <summary>
    /// Tests that the custom move modal renders without exceptions when a move
    /// exists, i.e. all form components find their <c>EditContext</c>.
    /// </summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task CustomMoveModalRendersMoveRowWithoutException()
    {
        var configuration = new CustomMoveConfiguration
        {
            Enabled = true,
            Moves = new List<CustomMoveEntry> { new(Guid.NewGuid()) { Name = "bar" } },
        };

        var html = await this.RenderModalAsync(configuration).ConfigureAwait(false);

        Assert.That(html, Does.Contain("customMoveEnabled"), "the enabled checkbox must be rendered");
        Assert.That(html, Does.Contain("<form"), "the EditForm wrapper must be rendered");
    }

    /// <summary>
    /// Tests that an empty custom move modal (no moves yet) renders without exceptions.
    /// </summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task CustomMoveModalRendersWithoutMoves()
    {
        var configuration = new CustomMoveConfiguration { Enabled = true, Moves = new List<CustomMoveEntry>() };

        var html = await this.RenderModalAsync(configuration).ConfigureAwait(false);

        Assert.That(html, Does.Contain("customMoveEnabled"));
    }

    /// <summary>
    /// Tests that the editor shows a warning when the plugin itself is deactivated
    /// (<c>PlugInConfiguration.IsActive</c> = false), which is independent of the
    /// <c>Enabled</c> flag of the configuration. A deactivated plugin was the root cause
    /// of the incident where moves were correctly configured but had no effect in the game.
    /// </summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task EditorShowsWarningWhenPluginIsDeactivated()
    {
        var configuration = new CustomMoveConfiguration { Enabled = true, Moves = new List<CustomMoveEntry>() };

        var html = await this.RenderModalAsync(configuration, pluginActive: false).ConfigureAwait(false);

        Assert.That(html, Does.Contain("The custom move plugin is deactivated."), "the activation warning must be rendered");
    }

    /// <summary>
    /// Tests that no activation warning is shown when the plugin is activated.
    /// </summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task EditorShowsNoWarningWhenPluginIsActivated()
    {
        var configuration = new CustomMoveConfiguration { Enabled = true, Moves = new List<CustomMoveEntry>() };

        var html = await this.RenderModalAsync(configuration, pluginActive: true).ConfigureAwait(false);

        Assert.That(html, Does.Not.Contain("The custom move plugin is deactivated."));
    }

    /// <summary>
    /// Tests that the editor itself renders without exceptions (outside the modal),
    /// which guards the usage of the custom <c>InputByte</c> component for the byte
    /// coordinates instead of the unsupported <c>InputNumber<byte></c>.
    /// </summary>
    /// <returns>A task.</returns>
    [Test]
    public void EditorStandaloneRendersWithoutException()
    {
        var configuration = new CustomMoveConfiguration
        {
            Enabled = true,
            Moves = new List<CustomMoveEntry> { new(Guid.NewGuid()) { Name = "bar" } },
        };

        Assert.DoesNotThrowAsync(async () =>
        {
            using var renderer = this.CreateRenderer(out _);
            var parameters = ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(CustomMoveConfigurationEditor.Configuration)] = configuration });
            await renderer.Dispatcher.InvokeAsync(
                async () => await renderer.RenderComponentAsync<CustomMoveConfigurationEditor>(parameters).ConfigureAwait(false)).ConfigureAwait(false);
        });
    }

    private HtmlRenderer CreateRenderer(out ServiceProvider serviceProvider)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<ILookupController>().Object);
        services.AddSingleton(new Mock<IContext>().Object);
        services.AddSingleton(new Mock<IJSRuntime>().Object);
        serviceProvider = services.BuildServiceProvider();
        return new HtmlRenderer(serviceProvider, NullLoggerFactory.Instance);
    }

    private async Task<string> RenderModalAsync(CustomMoveConfiguration configuration, bool? pluginActive = null)
    {
        using var renderer = this.CreateRenderer(out var serviceProvider);
        var context = serviceProvider.GetRequiredService<IContext>();
        var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(ModalCreateNew<CustomMoveConfiguration>.Item)] = configuration,
            [nameof(ModalCreateNew<CustomMoveConfiguration>.PersistenceContext)] = context,
            [nameof(ModalCreateNew<CustomMoveConfiguration>.PluginActive)] = pluginActive,
        });

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ModalCreateNew<CustomMoveConfiguration>>(parameters).ConfigureAwait(false);
            return component.ToHtmlString();
        }).ConfigureAwait(false);
        return html;
    }
}
