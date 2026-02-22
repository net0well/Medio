using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Medio.Implementation;
using Medio.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Medio.Tests
{
    // ─── Contratos de teste ──────────────────────────────────────────────────────

    public record SampleRequest(string Payload) : IRequest<string>;
    public record AnotherRequest() : IRequest<int>;
    public record SampleNotification(string Message) : INotification;

    // ─── Helper ──────────────────────────────────────────────────────────────────

    internal static class ServiceProviderBuilder
    {
        public static IServiceProvider With(Action<IServiceCollection> configure)
        {
            var services = new ServiceCollection();
            configure(services);
            return services.BuildServiceProvider();
        }
    }

    // ─── Behavior de teste (rastreia chamadas) ────────────────────────────────────

    public class TrackingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public static readonly List<string> Log = new();

        private readonly string _name;
        public TrackingBehavior(string name) => _name = name;

        public async Task<TResponse> Handle(
            TRequest request,
            CancellationToken cancellationToken,
            RequestHandlerDelegate<TResponse> next)
        {
            Log.Add($"{_name}:before");
            var result = await next();
            Log.Add($"{_name}:after");
            return result;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Testes do Mediator — Send
    // ════════════════════════════════════════════════════════════════════════════

    public class MediatorSendTests
    {
        [Fact]
        public async Task Send_ComHandlerRegistrado_RetornaRespostaDoHandler()
        {
            var handlerMock = new Mock<IRequestHandler<SampleRequest, string>>();
            handlerMock
                .Setup(h => h.Handle(It.IsAny<SampleRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("ok");

            var provider = ServiceProviderBuilder.With(s =>
                s.AddTransient<IRequestHandler<SampleRequest, string>>(_ => handlerMock.Object));

            var mediator = new Mediator(provider);

            var result = await mediator.Send(new SampleRequest("teste"));

            Assert.Equal("ok", result);
        }

        [Fact]
        public async Task Send_PassaORequestCorretoParaOHandler()
        {
            SampleRequest? captured = null;
            var handlerMock = new Mock<IRequestHandler<SampleRequest, string>>();
            handlerMock
                .Setup(h => h.Handle(It.IsAny<SampleRequest>(), It.IsAny<CancellationToken>()))
                .Callback<SampleRequest, CancellationToken>((req, _) => captured = req)
                .ReturnsAsync("ok");

            var provider = ServiceProviderBuilder.With(s =>
                s.AddTransient<IRequestHandler<SampleRequest, string>>(_ => handlerMock.Object));

            await new Mediator(provider).Send(new SampleRequest("payload-esperado"));

            Assert.NotNull(captured);
            Assert.Equal("payload-esperado", captured!.Payload);
        }

        [Fact]
        public async Task Send_PassaOCancellationTokenCorretamente()
        {
            CancellationToken capturedToken = default;
            using var cts = new CancellationTokenSource();

            var handlerMock = new Mock<IRequestHandler<SampleRequest, string>>();
            handlerMock
                .Setup(h => h.Handle(It.IsAny<SampleRequest>(), It.IsAny<CancellationToken>()))
                .Callback<SampleRequest, CancellationToken>((_, ct) => capturedToken = ct)
                .ReturnsAsync("ok");

            var provider = ServiceProviderBuilder.With(s =>
                s.AddTransient<IRequestHandler<SampleRequest, string>>(_ => handlerMock.Object));

            await new Mediator(provider).Send(new SampleRequest("x"), cts.Token);

            Assert.Equal(cts.Token, capturedToken);
        }

        [Fact]
        public async Task Send_SemHandlerRegistrado_LancaInvalidOperationException()
        {
            var provider = ServiceProviderBuilder.With(_ => { });
            var mediator = new Mediator(provider);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => mediator.Send(new SampleRequest("teste")));

            Assert.Contains("SampleRequest", ex.Message);
        }

        [Fact]
        public async Task Send_ComTipoDeRetornoValor_RetornaCorretamente()
        {
            var handlerMock = new Mock<IRequestHandler<AnotherRequest, int>>();
            handlerMock
                .Setup(h => h.Handle(It.IsAny<AnotherRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(42);

            var provider = ServiceProviderBuilder.With(s =>
                s.AddTransient<IRequestHandler<AnotherRequest, int>>(_ => handlerMock.Object));

            var result = await new Mediator(provider).Send(new AnotherRequest());

            Assert.Equal(42, result);
        }

        [Fact]
        public async Task Send_HandlerEChamadoExatamenteUmaVez()
        {
            var handlerMock = new Mock<IRequestHandler<SampleRequest, string>>();
            handlerMock
                .Setup(h => h.Handle(It.IsAny<SampleRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("ok");

            var provider = ServiceProviderBuilder.With(s =>
                s.AddTransient<IRequestHandler<SampleRequest, string>>(_ => handlerMock.Object));

            await new Mediator(provider).Send(new SampleRequest("x"));

            handlerMock.Verify(
                h => h.Handle(It.IsAny<SampleRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Send_QuandoHandlerLancaExcecao_PropagaExcecao()
        {
            var handlerMock = new Mock<IRequestHandler<SampleRequest, string>>();
            handlerMock
                .Setup(h => h.Handle(It.IsAny<SampleRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ApplicationException("erro interno"));

            var provider = ServiceProviderBuilder.With(s =>
                s.AddTransient<IRequestHandler<SampleRequest, string>>(_ => handlerMock.Object));

            await Assert.ThrowsAsync<ApplicationException>(
                () => new Mediator(provider).Send(new SampleRequest("x")));
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Testes do Mediator — Publish
    // ════════════════════════════════════════════════════════════════════════════

    public class MediatorPublishTests
    {
        [Fact]
        public async Task Publish_ComUmHandler_InvocaHandlerUmaVez()
        {
            var handlerMock = new Mock<INotificationHandler<SampleNotification>>();
            handlerMock
                .Setup(h => h.Handle(It.IsAny<SampleNotification>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var provider = ServiceProviderBuilder.With(s =>
                s.AddTransient<INotificationHandler<SampleNotification>>(_ => handlerMock.Object));

            await new Mediator(provider).Publish(new SampleNotification("evento"));

            handlerMock.Verify(
                h => h.Handle(It.IsAny<SampleNotification>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Publish_ComMultiplosHandlers_InvocaTodosOsHandlers()
        {
            var handler1 = new Mock<INotificationHandler<SampleNotification>>();
            var handler2 = new Mock<INotificationHandler<SampleNotification>>();
            var handler3 = new Mock<INotificationHandler<SampleNotification>>();

            foreach (var h in new[] { handler1, handler2, handler3 })
                h.Setup(x => x.Handle(It.IsAny<SampleNotification>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

            var provider = ServiceProviderBuilder.With(s =>
            {
                s.AddTransient<INotificationHandler<SampleNotification>>(_ => handler1.Object);
                s.AddTransient<INotificationHandler<SampleNotification>>(_ => handler2.Object);
                s.AddTransient<INotificationHandler<SampleNotification>>(_ => handler3.Object);
            });

            await new Mediator(provider).Publish(new SampleNotification("broadcast"));

            foreach (var h in new[] { handler1, handler2, handler3 })
                h.Verify(
                    x => x.Handle(It.IsAny<SampleNotification>(), It.IsAny<CancellationToken>()),
                    Times.Once);
        }

        [Fact]
        public async Task Publish_SemHandlersRegistrados_NaoLancaExcecao()
        {
            var provider = ServiceProviderBuilder.With(_ => { });
            await new Mediator(provider).Publish(new SampleNotification("silencio"));
        }

        [Fact]
        public async Task Publish_PassaANotificacaoCorretaParaCadaHandler()
        {
            SampleNotification? captured = null;
            var handlerMock = new Mock<INotificationHandler<SampleNotification>>();
            handlerMock
                .Setup(h => h.Handle(It.IsAny<SampleNotification>(), It.IsAny<CancellationToken>()))
                .Callback<SampleNotification, CancellationToken>((n, _) => captured = n)
                .Returns(Task.CompletedTask);

            var provider = ServiceProviderBuilder.With(s =>
                s.AddTransient<INotificationHandler<SampleNotification>>(_ => handlerMock.Object));

            await new Mediator(provider).Publish(new SampleNotification("mensagem-especifica"));

            Assert.NotNull(captured);
            Assert.Equal("mensagem-especifica", captured!.Message);
        }

        [Fact]
        public async Task Publish_HandlersExecutadosEmOrdemSequencial()
        {
            var ordem = new List<int>();
            var handler1 = new Mock<INotificationHandler<SampleNotification>>();
            handler1.Setup(h => h.Handle(It.IsAny<SampleNotification>(), It.IsAny<CancellationToken>()))
                    .Callback(() => ordem.Add(1))
                    .Returns(Task.CompletedTask);

            var handler2 = new Mock<INotificationHandler<SampleNotification>>();
            handler2.Setup(h => h.Handle(It.IsAny<SampleNotification>(), It.IsAny<CancellationToken>()))
                    .Callback(() => ordem.Add(2))
                    .Returns(Task.CompletedTask);

            var provider = ServiceProviderBuilder.With(s =>
            {
                s.AddTransient<INotificationHandler<SampleNotification>>(_ => handler1.Object);
                s.AddTransient<INotificationHandler<SampleNotification>>(_ => handler2.Object);
            });

            await new Mediator(provider).Publish(new SampleNotification("ordem"));

            Assert.Equal(new[] { 1, 2 }, ordem);
        }

        [Fact]
        public async Task Publish_QuandoHandlerLancaExcecao_PropagaExcecao()
        {
            var handlerMock = new Mock<INotificationHandler<SampleNotification>>();
            handlerMock
                .Setup(h => h.Handle(It.IsAny<SampleNotification>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("handler quebrado"));

            var provider = ServiceProviderBuilder.With(s =>
                s.AddTransient<INotificationHandler<SampleNotification>>(_ => handlerMock.Object));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new Mediator(provider).Publish(new SampleNotification("x")));
        }

        [Fact]
        public async Task Publish_QuandoPrimeiroHandlerLancaExcecao_SegundoNaoEInvocado()
        {
            var handler1 = new Mock<INotificationHandler<SampleNotification>>();
            handler1.Setup(h => h.Handle(It.IsAny<SampleNotification>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new Exception("falha"));

            var handler2 = new Mock<INotificationHandler<SampleNotification>>();
            handler2.Setup(h => h.Handle(It.IsAny<SampleNotification>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

            var provider = ServiceProviderBuilder.With(s =>
            {
                s.AddTransient<INotificationHandler<SampleNotification>>(_ => handler1.Object);
                s.AddTransient<INotificationHandler<SampleNotification>>(_ => handler2.Object);
            });

            await Assert.ThrowsAsync<Exception>(
                () => new Mediator(provider).Publish(new SampleNotification("x")));

            handler2.Verify(
                h => h.Handle(It.IsAny<SampleNotification>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Testes do Pipeline Behavior
    // ════════════════════════════════════════════════════════════════════════════

    public class PipelineBehaviorTests
    {
        private static IRequestHandler<SampleRequest, string> HandlerReturning(string value)
        {
            var mock = new Mock<IRequestHandler<SampleRequest, string>>();
            mock.Setup(h => h.Handle(It.IsAny<SampleRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(value);
            return mock.Object;
        }

        [Fact]
        public async Task Send_ComUmBehavior_BehaviorEChamadoAntesDoHandler()
        {
            var callOrder = new List<string>();

            var behaviorMock = new Mock<IPipelineBehavior<SampleRequest, string>>();
            behaviorMock
                .Setup(b => b.Handle(
                    It.IsAny<SampleRequest>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<RequestHandlerDelegate<string>>()))
                .Returns<SampleRequest, CancellationToken, RequestHandlerDelegate<string>>(
                    async (_, _, next) =>
                    {
                        callOrder.Add("behavior");
                        var result = await next();
                        return result;
                    });

            var handlerMock = new Mock<IRequestHandler<SampleRequest, string>>();
            handlerMock
                .Setup(h => h.Handle(It.IsAny<SampleRequest>(), It.IsAny<CancellationToken>()))
                .Callback(() => callOrder.Add("handler"))
                .ReturnsAsync("ok");

            var provider = ServiceProviderBuilder.With(s =>
            {
                s.AddTransient<IRequestHandler<SampleRequest, string>>(_ => handlerMock.Object);
                s.AddTransient<IPipelineBehavior<SampleRequest, string>>(_ => behaviorMock.Object);
            });

            await new Mediator(provider).Send(new SampleRequest("x"));

            Assert.Equal(new[] { "behavior", "handler" }, callOrder);
        }

        [Fact]
        public async Task Send_ComDoisBehaviors_ExecutadosNaOrdemCorreta()
        {
            // Registrados: A, B → pipeline esperado: A(before) → B(before) → handler → B(after) → A(after)
            var callOrder = new List<string>();

            IPipelineBehavior<SampleRequest, string> MakeBehavior(string name) =>
                new LambdaBehavior<SampleRequest, string>(async (_, _, next) =>
                {
                    callOrder.Add($"{name}:before");
                    var r = await next();
                    callOrder.Add($"{name}:after");
                    return r;
                });

            var handlerMock = new Mock<IRequestHandler<SampleRequest, string>>();
            handlerMock
                .Setup(h => h.Handle(It.IsAny<SampleRequest>(), It.IsAny<CancellationToken>()))
                .Callback(() => callOrder.Add("handler"))
                .ReturnsAsync("ok");

            var provider = ServiceProviderBuilder.With(s =>
            {
                s.AddTransient<IRequestHandler<SampleRequest, string>>(_ => handlerMock.Object);
                s.AddTransient<IPipelineBehavior<SampleRequest, string>>(_ => MakeBehavior("A"));
                s.AddTransient<IPipelineBehavior<SampleRequest, string>>(_ => MakeBehavior("B"));
            });

            await new Mediator(provider).Send(new SampleRequest("x"));

            Assert.Equal(new[] { "A:before", "B:before", "handler", "B:after", "A:after" }, callOrder);
        }

        [Fact]
        public async Task Send_BehaviorPodeModificarResposta()
        {
            var behaviorMock = new Mock<IPipelineBehavior<SampleRequest, string>>();
            behaviorMock
                .Setup(b => b.Handle(
                    It.IsAny<SampleRequest>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<RequestHandlerDelegate<string>>()))
                .Returns<SampleRequest, CancellationToken, RequestHandlerDelegate<string>>(
                    async (_, _, next) =>
                    {
                        var result = await next();
                        return result + "-modificado";
                    });

            var provider = ServiceProviderBuilder.With(s =>
            {
                s.AddTransient<IRequestHandler<SampleRequest, string>>(_ => HandlerReturning("original"));
                s.AddTransient<IPipelineBehavior<SampleRequest, string>>(_ => behaviorMock.Object);
            });

            var result = await new Mediator(provider).Send(new SampleRequest("x"));

            Assert.Equal("original-modificado", result);
        }

        [Fact]
        public async Task Send_BehaviorPodeInterceptarExcecaoDoHandler()
        {
            var exceptionCaught = false;

            var behaviorMock = new Mock<IPipelineBehavior<SampleRequest, string>>();
            behaviorMock
                .Setup(b => b.Handle(
                    It.IsAny<SampleRequest>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<RequestHandlerDelegate<string>>()))
                .Returns<SampleRequest, CancellationToken, RequestHandlerDelegate<string>>(
                    async (_, _, next) =>
                    {
                        try { return await next(); }
                        catch { exceptionCaught = true; throw; }
                    });

            var handlerMock = new Mock<IRequestHandler<SampleRequest, string>>();
            handlerMock
                .Setup(h => h.Handle(It.IsAny<SampleRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ApplicationException("boom"));

            var provider = ServiceProviderBuilder.With(s =>
            {
                s.AddTransient<IRequestHandler<SampleRequest, string>>(_ => handlerMock.Object);
                s.AddTransient<IPipelineBehavior<SampleRequest, string>>(_ => behaviorMock.Object);
            });

            await Assert.ThrowsAsync<ApplicationException>(
                () => new Mediator(provider).Send(new SampleRequest("x")));

            Assert.True(exceptionCaught);
        }

        [Fact]
        public async Task Send_BehaviorPodeCurtocircuitarSemChamarHandler()
        {
            var behaviorMock = new Mock<IPipelineBehavior<SampleRequest, string>>();
            behaviorMock
                .Setup(b => b.Handle(
                    It.IsAny<SampleRequest>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<RequestHandlerDelegate<string>>()))
                .ReturnsAsync("curtocircuito"); // não chama next()

            var handlerMock = new Mock<IRequestHandler<SampleRequest, string>>();
            handlerMock
                .Setup(h => h.Handle(It.IsAny<SampleRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("handler");

            var provider = ServiceProviderBuilder.With(s =>
            {
                s.AddTransient<IRequestHandler<SampleRequest, string>>(_ => handlerMock.Object);
                s.AddTransient<IPipelineBehavior<SampleRequest, string>>(_ => behaviorMock.Object);
            });

            var result = await new Mediator(provider).Send(new SampleRequest("x"));

            Assert.Equal("curtocircuito", result);
            handlerMock.Verify(
                h => h.Handle(It.IsAny<SampleRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Send_SemBehaviors_ExecutaHandlerDiretamente()
        {
            var provider = ServiceProviderBuilder.With(s =>
                s.AddTransient<IRequestHandler<SampleRequest, string>>(_ => HandlerReturning("direto")));

            var result = await new Mediator(provider).Send(new SampleRequest("x"));

            Assert.Equal("direto", result);
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Testes do LoggingBehavior
    // ════════════════════════════════════════════════════════════════════════════

    public class LoggingBehaviorTests
    {
        private static (LoggingBehavior<SampleRequest, string> behavior,
                        Mock<ILogger<LoggingBehavior<SampleRequest, string>>> loggerMock)
            CreateBehavior()
        {
            var loggerMock = new Mock<ILogger<LoggingBehavior<SampleRequest, string>>>();
            var behavior = new LoggingBehavior<SampleRequest, string>(loggerMock.Object);
            return (behavior, loggerMock);
        }

        [Fact]
        public async Task Handle_LogaInformacaoAntesEDepoisDoHandler()
        {
            var (behavior, loggerMock) = CreateBehavior();
            var callCount = 0;
            RequestHandlerDelegate<string> next = () => { callCount++; return Task.FromResult("ok"); };

            await behavior.Handle(new SampleRequest("x"), CancellationToken.None, next);

            // Deve ter chamado LogInformation pelo menos 2x (antes e depois)
            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Exactly(2));

            Assert.Equal(1, callCount);
        }

        [Fact]
        public async Task Handle_RetornaRespostaDoHandler()
        {
            var (behavior, _) = CreateBehavior();
            RequestHandlerDelegate<string> next = () => Task.FromResult("resposta");

            var result = await behavior.Handle(new SampleRequest("x"), CancellationToken.None, next);

            Assert.Equal("resposta", result);
        }

        [Fact]
        public async Task Handle_QuandoHandlerLancaExcecao_LogaErroEPropaga()
        {
            var (behavior, loggerMock) = CreateBehavior();
            RequestHandlerDelegate<string> next = () => throw new InvalidOperationException("falha");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => behavior.Handle(new SampleRequest("x"), CancellationToken.None, next));

            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_LoggingBehaviorIntegradoComMediator_ExecutaEGeraNaoExcecao()
        {
            var loggerMock = new Mock<ILogger<LoggingBehavior<SampleRequest, string>>>();

            var provider = ServiceProviderBuilder.With(s =>
            {
                s.AddSingleton(loggerMock.Object);
                s.AddTransient<IRequestHandler<SampleRequest, string>>(_ =>
                {
                    var m = new Mock<IRequestHandler<SampleRequest, string>>();
                    m.Setup(h => h.Handle(It.IsAny<SampleRequest>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync("integrado");
                    return m.Object;
                });
                s.AddTransient<IPipelineBehavior<SampleRequest, string>>(sp =>
                    new LoggingBehavior<SampleRequest, string>(loggerMock.Object));
            });

            var result = await new Mediator(provider).Send(new SampleRequest("integração"));

            Assert.Equal("integrado", result);
        }
    }

    // ─── Utilitário: behavior com lambda para testes ──────────────────────────

    internal class LambdaBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly Func<TRequest, CancellationToken, RequestHandlerDelegate<TResponse>, Task<TResponse>> _fn;

        public LambdaBehavior(
            Func<TRequest, CancellationToken, RequestHandlerDelegate<TResponse>, Task<TResponse>> fn)
            => _fn = fn;

        public Task<TResponse> Handle(
            TRequest request,
            CancellationToken cancellationToken,
            RequestHandlerDelegate<TResponse> next)
            => _fn(request, cancellationToken, next);
    }
}