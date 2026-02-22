# \# Medio

# 

# Lightweight mediator for .NET with built-in pipeline behavior support, enabling clean separation of concerns via request/response and notification patterns.

# 

# ---

# 

# \## Installation

# 

# ```bash

# dotnet add package MedioPkg

# ```

# 

# ---

# 

# \## Quick Start

# 

# ```csharp

# // Program.cs

# builder.Services.AddMedio(typeof(Program).Assembly);

# builder.Services.AddMedioValidation(typeof(Program).Assembly);

# 

# var app = builder.Build();

# app.UseMedio(); // handles ValidationException → 400

# ```

# 

# ---

# 

# \## Registering Assemblies

# 

# `AddMedio` supports three ways to specify which assemblies to scan:

# 

# ```csharp

# // Scan all loaded assemblies

# builder.Services.AddMedio();

# 

# // Scan specific assemblies

# builder.Services.AddMedio(typeof(Program).Assembly, typeof(OtherClass).Assembly);

# 

# // Filter by namespace prefix (most performant)

# builder.Services.AddMedio("MyApp.Features", "MyApp.Domain");

# ```

# 

# ---

# 

# \## Request / Response

# 

# Define a request and its handler:

# 

# ```csharp

# // Request

# public record CreateOrder(string Product, int Quantity) : IRequest<Guid>;

# 

# // Handler

# public class CreateOrderHandler : IRequestHandler<CreateOrder, Guid>

# {

# &nbsp;   public Task<Guid> Handle(CreateOrder request, CancellationToken cancellationToken)

# &nbsp;   {

# &nbsp;       var id = Guid.NewGuid();

# &nbsp;       // business logic...

# &nbsp;       return Task.FromResult(id);

# &nbsp;   }

# }

# 

# // Usage

# app.MapPost("/orders", async (IMediator mediator, CreateOrder command) =>

# {

# &nbsp;   var id = await mediator.Send(command);

# &nbsp;   return Results.Ok(id);

# });

# ```

# 

# ---

# 

# \## Notifications (Pub/Sub)

# 

# Broadcast an event to multiple handlers:

# 

# ```csharp

# // Notification

# public record OrderCreated(Guid OrderId) : INotification;

# 

# // Handler 1

# public class SendEmailOnOrderCreated : INotificationHandler<OrderCreated>

# {

# &nbsp;   public Task Handle(OrderCreated notification, CancellationToken cancellationToken)

# &nbsp;   {

# &nbsp;       // send email...

# &nbsp;       return Task.CompletedTask;

# &nbsp;   }

# }

# 

# // Handler 2

# public class LogOrderCreated : INotificationHandler<OrderCreated>

# {

# &nbsp;   public Task Handle(OrderCreated notification, CancellationToken cancellationToken)

# &nbsp;   {

# &nbsp;       // log event...

# &nbsp;       return Task.CompletedTask;

# &nbsp;   }

# }

# 

# // Publish — all handlers are invoked sequentially

# await mediator.Publish(new OrderCreated(id));

# ```

# 

# ---

# 

# \## Pipeline Behaviors

# 

# Behaviors wrap request handling in a Russian-doll model, enabling cross-cutting concerns without touching handlers.

# 

# ```

# → LoggingBehavior

# &nbsp;   → ValidationBehavior

# &nbsp;       → YourHandler

# &nbsp;       ← returns result

# &nbsp;   ← ValidationBehavior

# ← LoggingBehavior

# ```

# 

# \### Built-in: LoggingBehavior

# 

# Logs request name, payload, elapsed time, and errors automatically.

# 

# ```csharp

# // Register for a specific request

# builder.Services.AddTransient<

# &nbsp;   IPipelineBehavior<CreateOrder, Guid>,

# &nbsp;   LoggingBehavior<CreateOrder, Guid>>();

# ```

# 

# Console output:

# ```

# \[Medio] Handling CreateOrder { Product = "Book", Quantity = 2 }

# \[Medio] Handled CreateOrder in 12ms

# ```

# 

# On error:

# ```

# \[Medio] Error handling CreateOrder after 3ms

# ```

# 

# \### Built-in: ValidationBehavior

# 

# Automatically validates requests before they reach the handler. Throws `ValidationException` if validation fails — the handler is never called.

# 

# \#### 1. Install FluentValidation

# 

# ```bash

# dotnet add package FluentValidation

# ```

# 

# \#### 2. Create a validator

# 

# ```csharp

# public class CreateOrderValidator : AbstractValidator<CreateOrder>

# {

# &nbsp;   public CreateOrderValidator()

# &nbsp;   {

# &nbsp;       RuleFor(x => x.Product)

# &nbsp;           .NotEmpty().WithMessage("Product is required");

# 

# &nbsp;       RuleFor(x => x.Quantity)

# &nbsp;           .GreaterThan(0).WithMessage("Quantity must be greater than 0");

# &nbsp;   }

# }

# ```

# 

# \#### 3. Register with AddMedioValidation

# 

# ```csharp

# // Scans the assembly, registers all validators and ValidationBehavior automatically

# builder.Services.AddMedioValidation(typeof(Program).Assembly);

# ```

# 

# \#### 4. Handle validation errors

# 

# Add `UseMedio()` to return structured `400` responses instead of `500`:

# 

# ```csharp

# app.UseMedio();

# ```

# 

# Response when validation fails:

# ```json

# {

# &nbsp; "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",

# &nbsp; "title": "One or more validation errors occurred.",

# &nbsp; "status": 400,

# &nbsp; "errors": {

# &nbsp;   "Product": \["Product is required"],

# &nbsp;   "Quantity": \["Quantity must be greater than 0"]

# &nbsp; }

# }

# ```

# 

# \### Custom Behaviors

# 

# Create your own behavior by implementing `IPipelineBehavior<TRequest, TResponse>`:

# 

# ```csharp

# public class CacheBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>

# &nbsp;   where TRequest : IRequest<TResponse>

# {

# &nbsp;   public async Task<TResponse> Handle(

# &nbsp;       TRequest request,

# &nbsp;       CancellationToken cancellationToken,

# &nbsp;       RequestHandlerDelegate<TResponse> next)

# &nbsp;   {

# &nbsp;       // logic before handler

# &nbsp;       var response = await next();

# &nbsp;       // logic after handler

# &nbsp;       return response;

# &nbsp;   }

# }

# 

# // Register

# builder.Services.AddTransient<

# &nbsp;   IPipelineBehavior<CreateOrder, Guid>,

# &nbsp;   CacheBehavior<CreateOrder, Guid>>();

# ```

# 

# > \*\*Registration order matters.\*\* The first registered behavior is the outermost wrapper in the pipeline.

# 

# ---

# 

# \## Exception Middleware

# 

# `UseMedio()` registers `MedioExceptionMiddleware`, which:

# 

# \- Catches `ValidationException` → returns `400` with structured errors

# \- Logs warnings for validation failures

# \- Logs errors for unhandled exceptions and re-throws them

# \- Follows the same error format as ASP.NET Core `ValidationProblemDetails`

# 

# ```csharp

# var app = builder.Build();

# 

# app.UseSwagger();

# app.UseSwaggerUI();

# app.UseMedio(); // ← add before mapping endpoints

# 

# app.MapPost("/orders", ...);

# ```

# 

# ---

# 

# \## Interfaces Reference

# 

# | Interface | Description |

# |---|---|

# | `IRequest<TResponse>` | Marks a request that returns `TResponse` |

# | `IRequestHandler<TRequest, TResponse>` | Handles a specific request |

# | `INotification` | Marks a notification (no return value) |

# | `INotificationHandler<TNotification>` | Handles a specific notification |

# | `IPipelineBehavior<TRequest, TResponse>` | Wraps request handling (middleware) |

# | `RequestHandlerDelegate<TResponse>` | Delegate representing the next step in the pipeline |

# | `IMediator` | Dispatches requests and publishes notifications |

# 

# ---

# 

# \## Full Program.cs Example

# 

# ```csharp

# using Medio.Interfaces;

# using Medio.Extensions;

# using Medio.Implementation;

# using FluentValidation;

# 

# var builder = WebApplication.CreateBuilder(args);

# 

# builder.Services.AddMedio(typeof(Program).Assembly);

# builder.Services.AddMedioValidation(typeof(Program).Assembly);

# 

# builder.Services.AddTransient<

# &nbsp;   IPipelineBehavior<CreateOrder, Guid>,

# &nbsp;   LoggingBehavior<CreateOrder, Guid>>();

# 

# builder.Services.AddEndpointsApiExplorer();

# builder.Services.AddSwaggerGen();

# 

# var app = builder.Build();

# 

# app.UseSwagger();

# app.UseSwaggerUI();

# app.UseMedio();

# 

# app.MapPost("/orders", async (IMediator mediator, CreateOrder command) =>

# {

# &nbsp;   var id = await mediator.Send(command);

# &nbsp;   return Results.Created($"/orders/{id}", new { id });

# })

# .WithName("CreateOrder")

# .WithTags("Orders")

# .WithOpenApi();

# 

# app.Run();

# ```

# 

# ---

# 

# \## License

# 

# MIT © Wellington Neto

