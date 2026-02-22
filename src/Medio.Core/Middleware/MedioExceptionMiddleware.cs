using FluentValidation;
using Medio.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Medio.Middleware
{
    public class MedioExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<MedioExceptionMiddleware> _logger;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public MedioExceptionMiddleware(
            RequestDelegate next,
            ILogger<MedioExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (ValidationException ex)
            {
                _logger.LogWarning("[Medio] Validation failed for {Path}: {Errors}",
                    context.Request.Path,
                    string.Join(", ", ex.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")));

                var errors = ex.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray());

                var response = new ValidationErrorResponse(errors);

                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsync(
                    JsonSerializer.Serialize(response, _jsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Medio] Unhandled exception for {Path}", context.Request.Path);
                throw;
            }
        }
    }
}