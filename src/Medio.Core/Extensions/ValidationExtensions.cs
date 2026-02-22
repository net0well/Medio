using FluentValidation;
using Medio.Implementation;
using Medio.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Reflection;

namespace Medio.Extensions
{
    public static class ValidationExtensions
    {
        public static IServiceCollection AddMedioValidation(
            this IServiceCollection services,
            params Assembly[] assemblies)
        {
            foreach (var assembly in assemblies)
            {
                var validatorTypes = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract)
                    .SelectMany(t => t.GetInterfaces()
                        .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>))
                        .Select(i => new { Interface = i, Implementation = t }));

                foreach (var v in validatorTypes)
                    services.AddTransient(v.Interface, v.Implementation);
            }

            var requestHandlerTypes = assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract)
                .SelectMany(t => t.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
                .Select(i => new
                {
                    Request = i.GetGenericArguments()[0],
                    Response = i.GetGenericArguments()[1]
                })
                .Distinct();

            foreach (var rt in requestHandlerTypes)
            {
                var behaviorInterface = typeof(IPipelineBehavior<,>)
                    .MakeGenericType(rt.Request, rt.Response);
                var behaviorImpl = typeof(ValidationBehavior<,>)
                    .MakeGenericType(rt.Request, rt.Response);

                services.AddTransient(behaviorInterface, behaviorImpl);
            }

            return services;
        }
    }
}