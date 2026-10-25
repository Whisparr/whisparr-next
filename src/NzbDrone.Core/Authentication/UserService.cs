using System;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Authentication
{
    public interface IUserService
    {
        User Add(string username, string password);
        User Update(User user);
        User Upsert(string username, string password);
        User FindUser();
        User FindUser(string username, string password);
        User FindUser(Guid identifier);
    }

    public class UserService : IUserService
    {
        private readonly IUserRepository _repo;

        public UserService(IUserRepository repo)
        {
            _repo = repo;
        }

        public User Add(string username, string password)
        {
            var user = new User
            {
                Identifier = Guid.NewGuid(),
                Username = username.ToLowerInvariant()
            };

            SetUserHashedPassword(user, password);

            return _repo.Insert(user);
        }

        public User Update(User user)
        {
            return _repo.Update(user);
        }

        public User Upsert(string username, string password)
        {
            var user = FindUser();

            if (user == null)
            {
                if (password == null)
                {
                    return null;
                }

                return Add(username, password);
            }

            if (password != null)
            {
                SetUserHashedPassword(user, password);
            }

            user.Username = username.ToLowerInvariant();

            return Update(user);
        }

        public User FindUser()
        {
            return _repo.SingleOrDefault();
        }

        public User FindUser(string username, string password)
        {
            if (username.IsNullOrWhiteSpace() || password.IsNullOrWhiteSpace())
            {
                return null;
            }

            var user = _repo.FindUser(username.ToLowerInvariant());

            if (user == null)
            {
                return null;
            }

            if (!PasswordHasher.Verify(user, password))
            {
                return null;
            }

            // Upgrade an unsalted SHA-256 hash, or a salted one with fewer iterations than we use now.
            if (user.Salt.IsNullOrWhiteSpace() || user.Iterations < PasswordHasher.CurrentIterations)
            {
                return Update(SetUserHashedPassword(user, password));
            }

            return user;
        }

        public User FindUser(Guid identifier)
        {
            return _repo.FindUser(identifier);
        }

        private static User SetUserHashedPassword(User user, string password)
        {
            PasswordHasher.SetHash(user, password);

            return user;
        }
    }
}
