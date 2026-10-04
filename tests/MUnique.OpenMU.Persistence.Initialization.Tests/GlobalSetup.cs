// <copyright file="GlobalSetup.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Tests;

using MUnique.OpenMU.Persistence.EntityFramework;
using NUnit.Framework;

/// <summary>
/// Initialises the process-wide <see cref="ConnectionConfigurator"/> exactly once, before any
/// fixture in this assembly runs.
/// </summary>
/// <remarks>
/// <see cref="ConnectionConfigurator.Initialize"/> throws when called twice, and the fixtures
/// that need it call it from their own <c>OneTimeSetUp</c>. Whichever runs first wins and the
/// others then fail with "provider is initialized already". Doing it here — at assembly level,
/// before any fixture — makes the initialisation order irrelevant, so a new fixture that builds
/// a context cannot break unrelated ones.
/// </remarks>
[SetUpFixture]
public class GlobalSetup
{
    /// <summary>
    /// Runs once before the first test of this assembly.
    /// </summary>
    [OneTimeSetUp]
    public void InitializeConnectionConfigurator()
    {
        if (!ConnectionConfigurator.IsInitialized)
        {
            ConnectionConfigurator.Initialize(new ConfigFileDatabaseConnectionStringProvider());
        }
    }
}
