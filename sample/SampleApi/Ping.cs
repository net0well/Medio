using Medio.Interfaces;

namespace SampleApi
{
    public class Ping : IRequest<string>
    {
        public string Message { get; set; } = "Ping!";
    }
}
