using System.Collections.Generic;
using System.Data;
using Dapper;

namespace NzbDrone.Core.Datastore.Migration.Setup.V2
{
    // Rows inserted with explicit Ids leave the identity sequences behind, so the next insert would collide.
    public static class PostgresSequences
    {
        public static void Reset(IDbConnection connection, IEnumerable<string> tables, IDbTransaction transaction)
        {
            foreach (var table in tables)
            {
                var quoted = Quote(table);
                var sequence = connection.ExecuteScalar<string>("SELECT pg_get_serial_sequence(@table, 'Id')", new { table = quoted }, transaction);

                if (sequence != null)
                {
                    connection.Execute(
                        $"SELECT setval(@sequence, COALESCE((SELECT MAX(\"Id\") FROM {quoted}), 1), (SELECT MAX(\"Id\") FROM {quoted}) IS NOT NULL)",
                        new { sequence },
                        transaction);
                }
            }
        }

        private static string Quote(string identifier)
        {
            return "\"" + identifier.Replace("\"", "\"\"") + "\"";
        }
    }
}
