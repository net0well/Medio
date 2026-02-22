using System.Collections.Generic;

namespace Medio.Models
{
    public class ValidationErrorResponse
    {
        public string Type { get; } = "https://tools.ietf.org/html/rfc7231#section-6.5.1";
        public string Title { get; } = "One or more validation errors occurred.";
        public int Status { get; } = 400;
        public Dictionary<string, string[]> Errors { get; }

        public ValidationErrorResponse(Dictionary<string, string[]> errors)
        {
            Errors = errors;
        }
    }
}