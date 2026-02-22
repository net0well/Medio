using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Medio.Interfaces;
using Microsoft.Extensions.Logging;

namespace Medio.Implementation
{
    public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

        public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
        {
            _logger = logger;
        }

        public async Task<TResponse> Handle(
            TRequest request,
            CancellationToken cancellationToken,
            RequestHandlerDelegate<TResponse> next)
        {
            var requestName = typeof(TRequest).Name;

            _logger.LogInformation("[Medio] Handling {RequestName} {@Request}", requestName, request);

            var sw = Stopwatch.StartNew();
            try
            {
                var response = await next();
                sw.Stop();

                _logger.LogInformation(
                    "[Medio] Handled {RequestName} in {ElapsedMs}ms {@Response}",
                    requestName, sw.ElapsedMilliseconds, response);

                return response;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex,
                    "[Medio] Error handling {RequestName} after {ElapsedMs}ms",
                    requestName, sw.ElapsedMilliseconds);
                throw;
            }
        }
    }
}