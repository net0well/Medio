using Medio.Interfaces;

namespace SampleApi
{
    public class PingHandler : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request, CancellationToken cancellationToken)
        {
            return Task.FromResult($"Pong: {request.Message}");
        }
    }
}
