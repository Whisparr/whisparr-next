using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Authentication
{
    // Salted PBKDF2-HMACSHA512 with per-user iterations (the format v2 and v5 share), plus the unsalted
    // SHA-256 format older installs still hold.
    public static class PasswordHasher
    {
        public const int CurrentIterations = 220000;

        // What v2 stores for users created before it recorded iterations.
        private const int DefaultIterations = 10000;
        private const int SaltSize = 128 / 8;
        private const int NumberOfBytes = 256 / 8;

        public static bool Verify(User user, string password)
        {
            if (user == null || user.Password.IsNullOrWhiteSpace() || password.IsNullOrWhiteSpace())
            {
                return false;
            }

            if (user.Salt.IsNullOrWhiteSpace())
            {
                return IsMatchingHash(user.Password, password.SHA256Hash());
            }

            try
            {
                var salt = Convert.FromBase64String(user.Salt);
                var iterations = user.Iterations > 0 ? user.Iterations : DefaultIterations;

                return IsMatchingHash(user.Password, GetHashedPassword(password, salt, iterations));
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static void SetHash(User user, string password)
        {
            var salt = new byte[SaltSize];
            RandomNumberGenerator.Fill(salt);

            user.Iterations = CurrentIterations;
            user.Salt = Convert.ToBase64String(salt);
            user.Password = GetHashedPassword(password, salt, CurrentIterations);
        }

        private static string GetHashedPassword(string password, byte[] salt, int iterations)
        {
            return Convert.ToBase64String(KeyDerivation.Pbkdf2(
                password: password,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA512,
                iterationCount: iterations,
                numBytesRequested: NumberOfBytes));
        }

        private static bool IsMatchingHash(string storedHash, string hash)
        {
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(storedHash), Encoding.UTF8.GetBytes(hash));
        }
    }
}
