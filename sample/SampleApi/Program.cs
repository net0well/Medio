using Medio.Interfaces;
using Medio.Extensions;
using Medio.Implementation;
using SampleApi;

var builder = WebApplication.CreateBuilder(args);

//Adicione Medio 
builder.Services.AddMedio(typeof(Program).Assembly);

//Adicione Medio Validation (opcional, mas recomendado para validação automática usando FluentValidation)
builder.Services.AddMedioValidation(typeof(Program).Assembly);


//Registre um comportamento de pipeline personalizado (opcional)
builder.Services.AddTransient<IPipelineBehavior<Ping, string>, LoggingBehavior<Ping, string>>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

//Substitua o middleware de tratamento de exceções padrão pelo middleware de tratamento de exceções do Medio
app.UseMedio();



//Exemplo de endpoint usando Mediator
app.MapGet("/ping", async (IMediator mediator) =>
{
    var result = await mediator.Send(new Ping { Message = "Hello Mediator" });
    return Results.Ok(result);
})
.WithName("PingGet")
.WithTags("Mediator");

app.MapPost("/ping", async (IMediator mediator, Ping request) =>
{
    var result = await mediator.Send(request);
    return Results.Ok(result);
})
.WithName("PingPost")
.WithTags("Mediator");

app.Run();