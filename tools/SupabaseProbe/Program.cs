using Npgsql;

const string sql = """
SELECT current_database() AS database_name,
       current_user AS database_user,
       current_setting('server_version') AS postgres_version,
       EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'spike_os') AS has_spike_schema
""";

var connectionString = Environment.GetEnvironmentVariable("SPIKE_SUPABASE_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Missing SPIKE_SUPABASE_CONNECTION. See docs/SUPABASE_EXPERIMENT_PLAN.md.");
    return 2;
}

try
{
    // Do not print the connection string, host, username, or password.
    var builder = new NpgsqlConnectionStringBuilder(connectionString)
    {
        Timeout = 10,
        CommandTimeout = 10
    };

    if (builder.SslMode == SslMode.Disable)
    {
        Console.Error.WriteLine("Refusing a PostgreSQL connection without TLS.");
        return 2;
    }

    await using var db = new NpgsqlConnection(builder.ConnectionString);
    await db.OpenAsync();
    await using var command = new NpgsqlCommand(sql, db);
    await using var reader = await command.ExecuteReaderAsync();

    if (!await reader.ReadAsync())
    {
        Console.Error.WriteLine("Connected, but diagnostic query returned no result.");
        return 1;
    }

    Console.WriteLine("Supabase PostgreSQL connection: OK");
    Console.WriteLine($"Database: {reader.GetString(0)}");
    Console.WriteLine($"Server version: {reader.GetString(2)}");
    Console.WriteLine($"Private schema spike_os exists: {reader.GetBoolean(3)}");
    Console.WriteLine("Probe completed. No database changes were made.");
    return 0;
}
catch (Exception ex)
{
    // Keep diagnostics sanitized: no connection strings or server responses in console.
    Console.Error.WriteLine($"Connection probe failed ({ex.GetType().Name}). Check hostname, port, credentials, SSL and firewall.");
    return 1;
}
