// <copyright file="ConfigFileDatabaseConnectionStringProviderTests.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Tests;

using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using MUnique.OpenMU.Persistence.EntityFramework;
using NUnit.Framework;

/// <summary>
/// Verifies that the connection settings provider honours its environment variables.
/// </summary>
/// <remarks>
/// <para>
/// This is a pure unit test: it writes a temporary <c>ConnectionSettings.xml</c> and reads the
/// settings back. No database is opened, and the file that ships with the project is never modified.
/// </para>
/// <para>
/// The cases cover the two defects being fixed: the database name could not be overridden at all,
/// and the admin password variable was silently ignored because the replacement matched a
/// <c>Password=admin;</c> literal that the shipped file does not contain.
/// </para>
/// </remarks>
[TestFixture]
public class ConfigFileDatabaseConnectionStringProviderTests
{
    private string _directory = null!;
    private string _fileName = null!;
    private string? _originalDatabase;
    private string? _originalHost;
    private string? _originalAdminUser;
    private string? _originalAdminPassword;

    [SetUp]
    public void SetUp()
    {
        this._directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "openmu-conn-" + Guid.NewGuid().ToString("N"))).FullName;
        this._fileName = Path.Combine(this._directory, "ConnectionSettings.xml");

        // Mirrors the shape of the file the project ships: an admin connection and an account
        // connection, both on the same database.
        File.WriteAllText(this._fileName, """
            <?xml version="1.0" encoding="utf-8" ?>
            <ConnectionSettings xmlns="http://www.munique.net/ConnectionSettings">
              <Connections>
                <Connection>
                  <ContextTypeName>MUnique.OpenMU.Persistence.EntityFramework.EntityDataContext</ContextTypeName>
                  <ConnectionString>Server=localhost;Port=5432;User Id=postgres;Password=postgres;Database=openmu;Command Timeout=120;</ConnectionString>
                  <DatabaseEngine>Npgsql</DatabaseEngine>
                </Connection>
                <Connection>
                  <ContextTypeName>MUnique.OpenMU.Persistence.EntityFramework.AccountContext</ContextTypeName>
                  <ConnectionString>Server=localhost;Port=5432;User Id=account;Password=account;Database=openmu;Command Timeout=120;</ConnectionString>
                  <DatabaseEngine>Npgsql</DatabaseEngine>
                </Connection>
              </Connections>
            </ConnectionSettings>
            """);

        this._originalDatabase = Environment.GetEnvironmentVariable("DB_NAME");
        this._originalHost = Environment.GetEnvironmentVariable("DB_HOST");
        this._originalAdminUser = Environment.GetEnvironmentVariable("DB_ADMIN_USER");
        this._originalAdminPassword = Environment.GetEnvironmentVariable("DB_ADMIN_PW");
        ClearVariables();
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("DB_NAME", this._originalDatabase);
        Environment.SetEnvironmentVariable("DB_HOST", this._originalHost);
        Environment.SetEnvironmentVariable("DB_ADMIN_USER", this._originalAdminUser);
        Environment.SetEnvironmentVariable("DB_ADMIN_PW", this._originalAdminPassword);
        try
        {
            Directory.Delete(this._directory, true);
        }
        catch (IOException)
        {
            // A leftover temp directory must not fail the test.
        }
    }

    /// <summary>
    /// Sem nenhuma variável, as connection strings permanecem exatamente como estão no arquivo.
    /// </summary>
    [Test]
    public void GetConnectionSetting_WithoutVariables_KeepsConfiguredValues()
    {
        var setting = this.GetSetting<EntityDataContext>();

        Assert.Multiple(() =>
        {
            Assert.That(setting.ConnectionString, Does.Contain("Database=openmu"), "The configured database must be kept.");
            Assert.That(setting.ConnectionString, Does.Contain("Server=localhost"), "The configured host must be kept.");
            Assert.That(setting.ConnectionString, Does.Contain("Password=postgres"), "The configured password must be kept.");
        });
    }

    /// <summary>
    /// <c>DB_NAME</c> redireciona todas as conexoes, que e o que permite usar um banco isolado sem
    /// editar o arquivo de configuracao distribuido com o projeto.
    /// </summary>
    [Test]
    public void GetConnectionSetting_WithDatabaseName_RetargetsAllConnections()
    {
        Environment.SetEnvironmentVariable("DB_NAME", "openmu_cashshop_validation");

        var admin = this.GetSetting<EntityDataContext>();
        var account = this.GetSetting<AccountContext>();

        Assert.Multiple(() =>
        {
            Assert.That(admin.ConnectionString, Does.Contain("Database=openmu_cashshop_validation"), "The admin connection must use the configured database.");
            Assert.That(account.ConnectionString, Does.Contain("Database=openmu_cashshop_validation"), "The account connection must use the configured database.");
            Assert.That(admin.ConnectionString, Does.Not.Contain("Database=openmu;"), "The previous database name must not survive.");
        });
    }

    /// <summary>
    /// Uma variavel vazia e ignorada, em vez de produzir uma connection string com valor vazio.
    /// </summary>
    [Test]
    public void GetConnectionSetting_WithEmptyDatabaseName_IsIgnored()
    {
        Environment.SetEnvironmentVariable("DB_NAME", string.Empty);

        var setting = this.GetSetting<EntityDataContext>();

        Assert.That(setting.ConnectionString, Does.Contain("Database=openmu"), "An empty variable must not clear the database name.");
    }

    /// <summary>
    /// A variavel de senha administrativa e aplicada a conexao que usa o usuario <c>postgres</c>.
    /// Este e o defeito corrigido: antes a substituicao procurava um literal que o arquivo
    /// distribuido nunca contem, entao a variavel era ignorada em silencio.
    /// </summary>
    [Test]
    public void GetConnectionSetting_WithAdminPassword_OverridesTheConfiguredPassword()
    {
        Environment.SetEnvironmentVariable("DB_ADMIN_PW", "s3cret");

        var admin = this.GetSetting<EntityDataContext>();
        var account = this.GetSetting<AccountContext>();

        Assert.Multiple(() =>
        {
            Assert.That(admin.ConnectionString, Does.Contain("Password=s3cret"), "The admin password must come from the environment.");
            Assert.That(admin.ConnectionString, Does.Not.Contain("Password=postgres;"), "The password from the file must be replaced.");
            Assert.That(account.ConnectionString, Does.Contain("Password=account"), "A non-admin role must keep its own configured password.");
        });
    }

    /// <summary>
    /// A variavel de host continua funcionando junto com a nova variavel de banco.
    /// </summary>
    [Test]
    public void GetConnectionSetting_WithHostAndDatabase_AppliesBoth()
    {
        Environment.SetEnvironmentVariable("DB_HOST", "database");
        Environment.SetEnvironmentVariable("DB_NAME", "openmu_homologacao");

        var setting = this.GetSetting<EntityDataContext>();

        Assert.Multiple(() =>
        {
            Assert.That(setting.ConnectionString, Does.Contain("Server=database"), "The host must come from the environment.");
            Assert.That(setting.ConnectionString, Does.Contain("Database=openmu_homologacao"), "The database must come from the environment.");
        });
    }

    private static void ClearVariables()
    {
        Environment.SetEnvironmentVariable("DB_NAME", null);
        Environment.SetEnvironmentVariable("DB_HOST", null);
        Environment.SetEnvironmentVariable("DB_ADMIN_USER", null);
        Environment.SetEnvironmentVariable("DB_ADMIN_PW", null);
    }

    private ConnectionSetting GetSetting<TContextType>()
        where TContextType : DbContext =>
        new ConfigFileDatabaseConnectionStringProvider(this._fileName).GetConnectionSetting<TContextType>();
}
