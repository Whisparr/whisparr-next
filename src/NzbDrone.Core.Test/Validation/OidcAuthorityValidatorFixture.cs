using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Test.Validation
{
    [TestFixture]
    public class OidcAuthorityValidatorFixture : CoreTest
    {
        private class Model
        {
            public string Authority { get; set; }
        }

        private AbstractValidator<Model> GivenValidator(bool discoverable)
        {
            var discovery = new Mock<IOidcDiscoveryService>();
            discovery.Setup(d => d.IsDiscoverable(It.IsAny<string>())).Returns(discoverable);

            var validator = new InlineValidator<Model>();
            validator.RuleFor(m => m.Authority).SetValidator(new OidcAuthorityValidator<Model>(discovery.Object));

            return validator;
        }

        [Test]
        public void should_be_valid_when_discoverable()
        {
            GivenValidator(true).Validate(new Model { Authority = "https://id.example" }).IsValid.Should().BeTrue();
        }

        [Test]
        public void should_be_invalid_when_not_discoverable()
        {
            var result = GivenValidator(false).Validate(new Model { Authority = "https://id.example" });

            result.IsValid.Should().BeFalse();
            result.Errors[0].ErrorMessage.Should().Contain("OIDC");
        }
    }
}
