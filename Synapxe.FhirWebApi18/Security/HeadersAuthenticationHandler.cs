using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Synapxe.FhirWebApi18.Security
{
    public class HeadersAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SecurityHeaderName = "x-api-key";

        public HeadersAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var request = Context.Request;
            if (request.Headers.TryGetValue(SecurityHeaderName, out var securityHeader))
            {
                var identity = new ClaimsIdentity(securityHeader);
                identity.AddClaim(new Claim(ClaimTypes.Name, securityHeader));
                var principal = new ClaimsPrincipal(identity);
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "HeaderIdentity")));
            }
            else
            {
                return Task.FromResult(AuthenticateResult.Fail("Missing header"));
            }
        }
    }
}
