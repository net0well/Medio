using Medio.Middleware;
using Microsoft.AspNetCore.Builder;

namespace Medio.Extensions
{
    public static class ApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseMedio(this IApplicationBuilder app)
        {
            app.UseMiddleware<MedioExceptionMiddleware>();
            return app;
        }
    }
}