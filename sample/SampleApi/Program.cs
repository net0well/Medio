using Medio.Interfaces;
using Medio.Extensions;
using Medio.Implementation;
using SampleApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMedio(typeof(Program).Assembly);
builder.Services.AddTransient<IPipelineBehavior<Ping, string>, LoggingBehavior<Ping, string>>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/ping", async (IMediator mediator) =>
{
    var result = await mediator.Send(new Ping { Message = "Hello Mediator" });
    return Results.Ok(result);
})
.WithName("Ping")
.WithTags("Mediator");

app.Run();