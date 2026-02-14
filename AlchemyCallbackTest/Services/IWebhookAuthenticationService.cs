using System.Threading;
using System.Threading.Tasks;
using AlchemyCallbackTest.Domain;

namespace AlchemyCallbackTest.Services
{
    public interface IWebhookAuthenticationService
    {
        Task<AuthenticationResult> AuthenticateAsync(IncomingWebhookEvent incomingEvent, CancellationToken cancellationToken = default);
    }
}
