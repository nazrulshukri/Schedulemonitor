using Oracle.ManagedDataAccess.Client;

// One-off column/metadata probe. Never hard-code the OCAP password here - this
// file lives in a public repository. Pass the connection string in instead:
//
//   set OCAP_CONNECTION_STRING=<the connection string from appsettings.json>
//   dotnet run --project work/DbMetadataProbe
//
// or as the first argument:
//
//   dotnet run --project work/DbMetadataProbe -- "<connection string>"
var connectionString = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
    ? args[0]
    : Environment.GetEnvironmentVariable("OCAP_CONNECTION_STRING");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Set OCAP_CONNECTION_STRING, or pass the connection string as the first argument.");
    return 1;
}

await using var connection = new OracleConnection(connectionString);
await connection.OpenAsync();

await PrintColumns(connection, "AWACSWSTYPE");
await PrintColumns(connection, "TBLSAWING");
await PrintColumns(connection, "AWACSRECIPEBYWSTYPE");
await PrintColumns(connection, "AWACSLF");
await PrintTablesWithRecipeColumns(connection);
await SmokePagedQueries(connection);
return 0;

static async Task PrintColumns(OracleConnection connection, string tableName)
{
    Console.WriteLine($"-- {tableName}");
    await using var command = connection.CreateCommand();
    command.BindByName = true;
    command.CommandText = """
        SELECT owner, table_name, column_id, column_name, data_type, data_length, nullable
        FROM all_tab_columns
        WHERE table_name = :table_name
        ORDER BY owner, table_name, column_id
        """;
    command.Parameters.Add(new OracleParameter("table_name", tableName));

    await using var reader = await command.ExecuteReaderAsync();
    var found = false;
    while (await reader.ReadAsync())
    {
        found = true;
        Console.WriteLine($"{reader.GetString(0)}.{reader.GetString(1)} #{reader.GetDecimal(2)}: {reader.GetString(3)} {reader.GetString(4)}({reader.GetDecimal(5)}) nullable={reader.GetString(6)}");
    }

    if (!found)
    {
        Console.WriteLine("No columns found.");
    }
}

static async Task SmokePagedQueries(OracleConnection connection)
{
    Console.WriteLine("-- Smoke paged queries");
    await using var awacs = connection.CreateCommand();
    awacs.BindByName = true;
    awacs.CommandText = """
        SELECT tblrowid, lastupdate, lastupdatedby, wsid, wstype, wsdb
        FROM awacswstype
        WHERE wstype = 'SAWING'
          AND (:search IS NULL
               OR UPPER(tblrowid) LIKE :search
               OR UPPER(lastupdatedby) LIKE :search
               OR UPPER(wsid) LIKE :search
               OR UPPER(wsdb) LIKE :search)
        ORDER BY tblrowid
        OFFSET :offset ROWS FETCH NEXT :page_size ROWS ONLY
        """;
    awacs.Parameters.Add(new OracleParameter("search", OracleDbType.Varchar2) { Value = DBNull.Value });
    awacs.Parameters.Add(new OracleParameter("offset", OracleDbType.Int32) { Value = 0 });
    awacs.Parameters.Add(new OracleParameter("page_size", OracleDbType.Int32) { Value = 1 });
    await using var awacsReader = await awacs.ExecuteReaderAsync();
    Console.WriteLine($"AWACSWSTYPE page query OK: {await awacsReader.ReadAsync()}");

    await using var recipe = connection.CreateCommand();
    recipe.BindByName = true;
    recipe.CommandText = """
        SELECT wstype, "PACKAGE", product, leadframe12nc, recipe
        FROM awacsrecipebywstype
        WHERE wstype = :wstype
          AND (:search IS NULL
               OR UPPER(product) LIKE :search
               OR UPPER(leadframe12nc) LIKE :search
               OR UPPER(recipe) LIKE :search
               OR UPPER("PACKAGE") LIKE :search)
        ORDER BY lastupdate DESC NULLS LAST
        OFFSET :offset ROWS FETCH NEXT :page_size ROWS ONLY
        """;
    recipe.Parameters.Add(new OracleParameter("wstype", "SAWING"));
    recipe.Parameters.Add(new OracleParameter("search", OracleDbType.Varchar2) { Value = DBNull.Value });
    recipe.Parameters.Add(new OracleParameter("offset", OracleDbType.Int32) { Value = 0 });
    recipe.Parameters.Add(new OracleParameter("page_size", OracleDbType.Int32) { Value = 1 });
    await using var recipeReader = await recipe.ExecuteReaderAsync();
    Console.WriteLine($"AWACSRECIPEBYWSTYPE page query OK: {await recipeReader.ReadAsync()}");
}

static async Task PrintTablesWithRecipeColumns(OracleConnection connection)
{
    Console.WriteLine("-- Tables with PRODUCT/WSTYPE/LEADFRAME12NC/RECIPE columns");
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT owner, table_name, LISTAGG(column_name, ', ') WITHIN GROUP (ORDER BY column_id) AS columns_found
        FROM all_tab_columns
        WHERE column_name IN ('PRODUCT', 'WSTYPE', 'LEADFRAME12NC', 'RECIPE')
        GROUP BY owner, table_name
        HAVING COUNT(DISTINCT column_name) >= 2
        ORDER BY owner, table_name
        """;

    await using var reader = await command.ExecuteReaderAsync();
    var found = false;
    while (await reader.ReadAsync())
    {
        found = true;
        Console.WriteLine($"{reader.GetString(0)}.{reader.GetString(1)}: {reader.GetString(2)}");
    }

    if (!found)
    {
        Console.WriteLine("No matching tables found.");
    }
}
