using Npgsql;
var builder = new NpgsqlConnectionStringBuilder("Host=localhost;Port=5432;Database=test;Username=test;Password=test");
Console.WriteLine($"Pooling: {builder.Pooling}");
Console.WriteLine($"MaxPoolSize: {builder.MaxPoolSize}");
Console.WriteLine($"MinPoolSize: {builder.MinPoolSize}");
Console.WriteLine($"ConnectionIdleLifetime: {builder.ConnectionIdleLifetime}");
