using System;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Authentication;

namespace NzbDrone.Core.Test.Authentication
{
    [TestFixture]
    public class PasswordHasherFixture
    {
        private const string Password = "hunter2";

        // A copy of Whisparr v2's formula: PBKDF2, HMACSHA512, 256-bit output, per-user iterations.
        private static User V2User(int iterations)
        {
            var salt = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(Password), salt, iterations, HashAlgorithmName.SHA512, 32);

            return new User
            {
                Username = "user",
                Salt = Convert.ToBase64String(salt),
                Iterations = iterations,
                Password = Convert.ToBase64String(hash)
            };
        }

        [Test]
        public void should_verify_v2_hash_with_10000_iterations()
        {
            PasswordHasher.Verify(V2User(10000), Password).Should().BeTrue();
        }

        [Test]
        public void should_verify_unsalted_sha256()
        {
            var user = new User { Username = "user", Password = Password.SHA256Hash() };

            PasswordHasher.Verify(user, Password).Should().BeTrue();
        }

        [Test]
        public void should_reject_wrong_password()
        {
            PasswordHasher.Verify(V2User(10000), "wrong").Should().BeFalse();
            PasswordHasher.Verify(new User { Password = Password.SHA256Hash() }, "wrong").Should().BeFalse();
        }

        [Test]
        public void should_reject_blank_password_or_malformed_salt()
        {
            PasswordHasher.Verify(V2User(10000), " ").Should().BeFalse();

            var user = V2User(10000);
            user.Salt = "not base64!";

            PasswordHasher.Verify(user, Password).Should().BeFalse();
        }

        [Test]
        public void set_hash_should_produce_a_verifiable_salted_hash()
        {
            var user = new User { Username = "user" };

            PasswordHasher.SetHash(user, Password);

            user.Iterations.Should().Be(PasswordHasher.CurrentIterations);
            user.Salt.Should().NotBeNullOrEmpty();
            PasswordHasher.Verify(user, Password).Should().BeTrue();
            PasswordHasher.Verify(user, "wrong").Should().BeFalse();
        }
    }
}
