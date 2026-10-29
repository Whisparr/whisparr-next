using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using NLog;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Authentication;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    // Signs in against the legacy install's Users while v5 has no database of its own. It is read-only: it
    // never rehashes, updates or adds, so the legacy database is not changed by a login.
    public class LegacyUserService : IUserService
    {
        private readonly ISetupModeState _setupModeState;
        private readonly Logger _logger;

        public LegacyUserService(ISetupModeState setupModeState)
        {
            _setupModeState = setupModeState;
            _logger = NzbDroneLogger.GetLogger(this);
        }

        public User Add(string username, string password)
        {
            throw ReadOnly();
        }

        public User Update(User user)
        {
            throw ReadOnly();
        }

        public User Upsert(string username, string password)
        {
            throw ReadOnly();
        }

        public User FindUser()
        {
            return ReadUsers().FirstOrDefault();
        }

        public User FindUser(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return null;
            }

            var user = ReadUsers().FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

            return PasswordHasher.Verify(user, password) ? user : null;
        }

        public User FindUser(Guid identifier)
        {
            return ReadUsers().FirstOrDefault(u => u.Identifier == identifier);
        }

        private static InvalidOperationException ReadOnly()
        {
            return new InvalidOperationException("Users cannot be changed while Whisparr is in setup mode");
        }

        // Eros wins when both installs exist, as it does for config5.xml. Setup mode can be required with no
        // install at all (an unreadable legacy database), which leaves nobody to sign in.
        private LegacyDatabaseLocation ChooseSource()
        {
            var detection = _setupModeState.Detection;

            // A finished migration waiting for an env switch lists no install, its Users are in the v5 database.
            if (detection.PendingEnvSwitch)
            {
                return detection.UserSource;
            }

            var installs = detection.Installs;

            return (installs.FirstOrDefault(i => i.Kind == LegacyKind.Eros) ?? installs.FirstOrDefault())?.Database;
        }

        private User[] ReadUsers()
        {
            var source = ChooseSource();

            if (source == null)
            {
                return Array.Empty<User>();
            }

            try
            {
                using var connection = source.Type == DatabaseType.SQLite
                    ? LegacyConnections.OpenSqliteReadOnly(source.SqlitePath)
                    : LegacyConnections.OpenPostgres(source.PostgresConnectionString);

                return ReadUsers(connection);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not read users from the legacy database");

                return Array.Empty<User>();
            }
        }

        private static User[] ReadUsers(IDbConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM \"Users\"";

            using var reader = command.ExecuteReader();

            var users = new List<User>();

            while (reader.Read())
            {
                users.Add(new User
                {
                    Id = Get<int>(reader, "Id"),
                    Identifier = ParseGuid(Value(reader, "Identifier")),
                    Username = Value(reader, "Username")?.ToString(),
                    Password = Value(reader, "Password")?.ToString(),
                    Salt = Value(reader, "Salt")?.ToString(),
                    Iterations = Get<int>(reader, "Iterations")
                });
            }

            return users.ToArray();
        }

        // Older legacy schemas lack some columns, which then read as null.
        private static object Value(IDataRecord reader, string column)
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), column, StringComparison.OrdinalIgnoreCase))
                {
                    return reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
            }

            return null;
        }

        private static T Get<T>(IDataRecord reader, string column)
        {
            var value = Value(reader, column);

            return value == null ? default : (T)Convert.ChangeType(value, typeof(T));
        }

        private static Guid ParseGuid(object value)
        {
            return value switch
            {
                Guid guid => guid,
                string text when Guid.TryParse(text, out var parsed) => parsed,
                _ => Guid.Empty
            };
        }
    }
}
