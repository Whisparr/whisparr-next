using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using Dapper;
using Npgsql;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    public class PostgresTableCopier
    {
        private static readonly UTF8Encoding Utf8 = new(false);

        public IEnumerable<string> ListTables(IDbConnection connection)
        {
            return connection.Query<string>(
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' ORDER BY table_name").ToList();
        }

        // COPY text format, one table per stream.
        public void CopyOut(NpgsqlConnection connection, string table, Stream output)
        {
            using var reader = connection.BeginTextExport($"COPY {Quote(table)} TO STDOUT");
            using var writer = new StreamWriter(output, Utf8, 8192, true);

            var buffer = new char[8192];
            int read;

            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                writer.Write(buffer, 0, read);
            }
        }

        public void CopyIn(NpgsqlConnection connection, string table, Stream input)
        {
            using var reader = new StreamReader(input, Utf8, false, 8192, true);
            using var writer = connection.BeginTextImport($"COPY {Quote(table)} FROM STDIN");

            var buffer = new char[8192];
            int read;

            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                writer.Write(buffer, 0, read);
            }
        }

        public string DescribeSchema(NpgsqlConnection connection)
        {
            var columns = connection.Query(
                @"SELECT c.table_name, c.column_name, c.data_type, c.udt_name, c.character_maximum_length, c.is_nullable, c.column_default
                  FROM information_schema.columns c
                  JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name
                  WHERE c.table_schema = 'public' AND t.table_type = 'BASE TABLE'
                  ORDER BY c.table_name, c.ordinal_position").ToList();

            var builder = new StringBuilder();

            foreach (var table in columns.GroupBy(c => (string)c.table_name))
            {
                builder.Append("CREATE TABLE ").Append(Quote(table.Key)).AppendLine(" (");

                var lines = table.Select(c =>
                {
                    var type = (string)c.data_type == "USER-DEFINED" || (string)c.data_type == "ARRAY"
                        ? (string)c.udt_name
                        : (string)c.data_type;

                    if (c.character_maximum_length != null)
                    {
                        type += $"({c.character_maximum_length})";
                    }

                    var line = $"    {Quote((string)c.column_name)} {type}";

                    if ((string)c.is_nullable == "NO")
                    {
                        line += " NOT NULL";
                    }

                    if (c.column_default != null)
                    {
                        line += $" DEFAULT {c.column_default}";
                    }

                    return line;
                });

                builder.AppendLine(string.Join(",\n", lines));
                builder.AppendLine(");");
                builder.AppendLine();
            }

            var indexes = connection.Query<string>(
                "SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' ORDER BY tablename, indexname");

            foreach (var index in indexes)
            {
                builder.Append(index).AppendLine(";");
            }

            return builder.ToString();
        }

        private static string Quote(string identifier)
        {
            return "\"" + identifier.Replace("\"", "\"\"") + "\"";
        }
    }
}
