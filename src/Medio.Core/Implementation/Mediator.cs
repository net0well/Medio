using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Medio.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Medio.Implementation
{
    public class Mediator : IMediator
    {
        private readonly IServiceProvider _provider;

        public Mediator(IServiceProvider provider)
        {
            _provider = provider;
        }

        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            var requestType = request.GetType();
            var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));

            var handler = _provider.GetService(handlerType)
                ?? throw new InvalidOperationException($"Handler not found for {requestType.Name}");

            // Handler final: chama o IRequestHandler real
            RequestHandlerDelegate<TResponse> handlerDelegate = () =>
                (Task<TResponse>)handlerType
                    .GetMethod("Handle")!
                    .Invoke(handler, new object[] { request, cancellationToken })!;

            // Busca behaviors registrados para TRequest/TResponse
            var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
            var behaviors = _provider.GetServices(behaviorType)
                                     .Cast<object>()
                                     .Reverse() // último registrado = mais externo
                                     .ToList();

            // Monta o pipeline encadeando os behaviors
            var pipeline = behaviors.Aggregate(
                handlerDelegate,
                (next, behavior) =>
                {
                    var capturedNext = next;
                    return () => (Task<TResponse>)behaviorType
                        .GetMethod("Handle")!
                        .Invoke(behavior, new object[] { request, cancellationToken, capturedNext })!;
                });

            return await pipeline();
        }

        public async Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            var handlerType = typeof(INotificationHandler<>).MakeGenericType(notification.GetType());
            var handlers = _provider.GetServices(handlerType);

            foreach (var handler in handlers)
            {
                await (Task)handlerType
                    .GetMethod("Handle")!
                    .Invoke(handler, new object[] { notification, cancellationToken })!;
            }
        }
    }
}