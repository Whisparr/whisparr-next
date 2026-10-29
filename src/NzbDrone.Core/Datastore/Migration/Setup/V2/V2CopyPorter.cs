using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;

namespace NzbDrone.Core.Datastore.Migration.Setup.V2
{
    // Tables whose columns are identical in v2 and v5: v5 gets exactly the legacy rows, with their Ids.
    public class V2CopyPorter
    {
        private static readonly string[] Tables = { "Tags", "RootFolders", "Users", "DelayProfiles", "RemotePathMappings" };

        public void Port(V2PortContext context)
        {
            foreach (var table in Tables)
            {
                Copy(context, table);
            }
        }

        private static void Copy(V2PortContext context, string table)
        {
            var quoted = Quote(table);
            var booleans = BooleanColumns(context, table);

            // v5 PostgreSQL Ids are GENERATED ALWAYS, so explicit Ids need the override.
            var overriding = context.V5Type == DatabaseType.PostgreSQL ? " OVERRIDING SYSTEM VALUE" : string.Empty;

            context.V5.Execute($"DELETE FROM {quoted}", transaction: context.Transaction);

            foreach (var row in context.Legacy.Query($"SELECT * FROM {quoted} ORDER BY \"Id\"").Cast<IDictionary<string, object>>().ToList())
            {
                var parameters = new DynamicParameters();

                foreach (var (column, value) in row)
                {
                    parameters.Add(column, Normalize(value, booleans.Contains(column)));
                }

                var columns = string.Join(", ", row.Keys.Select(Quote));
                var names = string.Join(", ", row.Keys.Select(k => "@" + k));

                context.V5.Execute($"INSERT INTO {quoted} ({columns}){overriding} VALUES ({names})", parameters, context.Transaction);
            }

            context.InsertedTables.Add(table);
        }

        // SQLite stores booleans as integers and PostgreSQL has a real type, so the value follows the v5 column.
        private static object Normalize(object value, bool isBoolean)
        {
            if (value == null || value is DBNull)
            {
                return null;
            }

            return isBoolean ? Convert.ToBoolean(value) : value;
        }

        private static HashSet<string> BooleanColumns(V2PortContext context, string table)
        {
            if (context.V5Type != DatabaseType.PostgreSQL)
            {
                return new HashSet<string>();
            }

            return context.V5.Query<string>(
                "SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @table AND data_type = 'boolean'",
                new { table },
                context.Transaction).ToHashSet(StringComparer.Ordinal);
        }

        private static string Quote(string identifier)
        {
            return "\"" + identifier.Replace("\"", "\"\"") + "\"";
        }
    }
}
