using DryIoc;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Datastore.Migration.Setup;

namespace NzbDrone.Core.Datastore.Extensions
{
    public static class CompositionExtensions
    {
        public static IContainer AddDatabase(this IContainer container)
        {
            container.RegisterDelegate<IDbFactory, IMainDatabase>(f => new MainDatabase(f.Create()), Reuse.Singleton);

            // Auto-registration also finds LegacyUserService for IUserService, and the last one registered wins.
            container.Register<IUserService, UserService>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            container.RegisterInstance(SetupModeState.Inactive, ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            return container;
        }

        public static IContainer AddLogDatabase(this IContainer container)
        {
            container.RegisterDelegate<IDbFactory, ILogDatabase>(f => new LogDatabase(f.Create(MigrationType.Log)), Reuse.Singleton);

            return container;
        }

        public static IContainer AddDummyDatabase(this IContainer container)
        {
            container.RegisterInstance<IMainDatabase>(new MainDatabase(null));

            return container;
        }

        // Setup mode: the state instance, and a read-only user service in place of the one that needs the v5 database.
        public static IContainer AddSetupMode(this IContainer container, ISetupModeState state)
        {
            container.RegisterInstance(state, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            container.Register<IUserService, LegacyUserService>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            return container;
        }

        public static IContainer AddDummyLogDatabase(this IContainer container)
        {
            container.RegisterInstance<ILogDatabase>(new LogDatabase(null));

            return container;
        }
    }
}
