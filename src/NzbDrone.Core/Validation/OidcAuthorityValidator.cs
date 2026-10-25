using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Core.Authentication;

namespace NzbDrone.Core.Validation
{
    public class OidcAuthorityValidator<T> : PropertyValidator<T, string>
    {
        private readonly IOidcDiscoveryService _oidcDiscoveryService;

        public OidcAuthorityValidator(IOidcDiscoveryService oidcDiscoveryService)
        {
            _oidcDiscoveryService = oidcDiscoveryService;
        }

        public override string Name => "OidcAuthorityValidator";

        protected override string GetDefaultMessageTemplate(string errorCode) => "Unable to retrieve the OIDC configuration from the Authority";

        public override bool IsValid(ValidationContext<T> context, string value)
        {
            return _oidcDiscoveryService.IsDiscoverable(value);
        }
    }
}
