# Medio

Lightweight mediator for .NET with built-in pipeline behavior support, enabling clean separation of concerns via request/response and notification patterns.

---

## Installation

```bash
dotnet add package Medio
```

---

## Getting Started

### 1. Register Medio

```csharp
// Program.cs
builder.Services.AddMedio(typeof(Program).Assembly);
```

You can also pass multiple assemblies or namespace prefixes:

```csharp
builder.Services.AddMedio(typeof(Program).Assembly, typeof(OtherClass).Assembly);
builder.Services.AddMedio("MyApp.Features", "MyApp.Domain");
```

---

### 2. Create a Request and Handler

```csharp
// Request
public record CreateOrder(string Product, int Quantity) : IRequest<Guid>;

// Handler
public class CreateOrderHandler : IRequestHandler<CreateOrder, Guid>
{
    public Task<Guid> Handle(CreateOrder request, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        // business logic...
        return Task.FromResult(id);
    }
}
```

---

### 3. Send a Request

```csharp
app.MapPost("/orders", async (IMediator mediator, CreateOrder command) =>
{
    var id = await mediator.Send(command);
    return Results.Ok(id);
});
```

---

### 4. Notifications (Pub/Sub)

```csharp
// Notification
public record OrderCreated(Guid OrderId) : INotification;

// Handler 1
public class SendEmailOnOrderCreated : INotificationHandler<OrderCreated>
{
    public Task Handle(OrderCreated notification, CancellationToken cancellationToken)
    {
        // send email...
        return Task.CompletedTask;
    }
}

// Handler 2
public class LogOrderCreated : INotificationHandler<OrderCreated>
{
    public Task Handle(OrderCreated notification, CancellationToken cancellationToken)
    {
        // log event...
        return Task.CompletedTask;
    }
}

// Publish
await mediator.Publish(new OrderCreated(id));
```

All handlers are invoked sequentially in registration order.

---

### 5. Pipeline Behaviors

Pipeline behaviors wrap request handling, enabling cross-cutting concerns like logging, validation, and caching.

```csharp
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        CancellationToken cancellationToken,
        RequestHandlerDelegate<TResponse> next)
    {
        // logic before handler
        var response = await next();
        // logic after handler
        return response;
    }
}

// Register
builder.Services.AddTransient<
    IPipelineBehavior<CreateOrder, Guid>,
    ValidationBehavior<CreateOrder, Guid>>();
```

#### Built-in: LoggingBehavior

Medio ships with a ready-to-use `LoggingBehavior` that logs request name, elapsed time, and errors:

```csharp
builder.Services.AddTransient<
    IPipelineBehavior<CreateOrder, Guid>,
    LoggingBehavior<CreateOrder, Guid>>();
```

Output:
```
[Medio] Handling CreateOrder { Product = "Book", Quantity = 2 }
[Medio] Handled CreateOrder in 12ms "3f2a1..."
```

#### Pipeline execution order

With multiple behaviors registered, execution follows a Russian-doll model:

```
→ LoggingBehavior
    → ValidationBehavior
        → CreateOrderHandler
        ← returns Guid
    ← ValidationBehavior
← LoggingBehavior
```

---

## Interfaces Reference

| Interface | Purpose |
|---|---|
| `IRequest<TResponse>` | Marks a request that returns `TResponse` |
| `IRequestHandler<TRequest, TResponse>` | Handles a specific request |
| `INotification` | Marks a notification (no return value) |
| `INotificationHandler<TNotification>` | Handles a specific notification |
| `IPipelineBehavior<TRequest, TResponse>` | Wraps request handling (middleware) |
| `IMediator` | Dispatches requests and notifications |

---

## License

MIT
