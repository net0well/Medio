using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using Medio.Implementation;
using Medio.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Medio.Tests.Validation
{
    // ─── Contratos de teste ──────────────────────────────────────────────────────

    public record CreateUserRequest(string Name, string Email) : IRequest<Guid>;

    public class CreateUserValidator : AbstractValidator<CreateUserRequest>
    {
        public CreateUserValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name é obrigatório")
                .MinimumLength(3).WithMessage("Name deve ter ao menos 3 caracteres");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email é obrigatório")
                .EmailAddress().WithMessage("Email inválido");
        }
    }

    // ─── Helper ──────────────────────────────────────────────────────────────────

    internal static class ValidationTestServiceProvider
    {
        public static IServiceProvider With(Action<IServiceCollection> configure)
        {
            var services = new ServiceCollection();
            configure(services);
            return services.BuildServiceProvider();
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Testes do ValidationBehavior
    // ════════════════════════════════════════════════════════════════════════════

    public class ValidationBehaviorTests
    {
        private static RequestHandlerDelegate<Guid> NextReturning(Guid value)
            => () => Task.FromResult(value);

        private static RequestHandlerDelegate<Guid> NextThatShouldNotBeCalled()
            => () => throw new Exception("Handler não deveria ter sido chamado");

        // ── Sem validators ───────────────────────────────────────────────────────

        [Fact]
        public async Task Handle_SemValidators_ChamaNextERetornaResultado()
        {
            var behavior = new ValidationBehavior<CreateUserRequest, Guid>(
                new List<IValidator<CreateUserRequest>>());

            var expected = Guid.NewGuid();
            var result = await behavior.Handle(
                new CreateUserRequest("Wellington", "w@email.com"),
                CancellationToken.None,
                NextReturning(expected));

            Assert.Equal(expected, result);
        }

        // ── Request válido ───────────────────────────────────────────────────────

        [Fact]
        public async Task Handle_RequestValido_ChamaNextERetornaResultado()
        {
            var behavior = new ValidationBehavior<CreateUserRequest, Guid>(
                new List<IValidator<CreateUserRequest>> { new CreateUserValidator() });

            var expected = Guid.NewGuid();
            var result = await behavior.Handle(
                new CreateUserRequest("Wellington", "wellington@email.com"),
                CancellationToken.None,
                NextReturning(expected));

            Assert.Equal(expected, result);
        }

        [Fact]
        public async Task Handle_RequestValido_HandlerEChamadoExatamenteUmaVez()
        {
            var callCount = 0;
            var behavior = new ValidationBehavior<CreateUserRequest, Guid>(
                new List<IValidator<CreateUserRequest>> { new CreateUserValidator() });

            await behavior.Handle(
                new CreateUserRequest("Wellington", "w@email.com"),
                CancellationToken.None,
                () => { callCount++; return Task.FromResult(Guid.NewGuid()); });

            Assert.Equal(1, callCount);
        }

        // ── Request inválido ─────────────────────────────────────────────────────

        [Fact]
        public async Task Handle_NameVazio_LancaValidationException()
        {
            var behavior = new ValidationBehavior<CreateUserRequest, Guid>(
                new List<IValidator<CreateUserRequest>> { new CreateUserValidator() });

            await Assert.ThrowsAsync<ValidationException>(() =>
                behavior.Handle(
                    new CreateUserRequest("", "w@email.com"),
                    CancellationToken.None,
                    NextThatShouldNotBeCalled()));
        }

        [Fact]
        public async Task Handle_EmailInvalido_LancaValidationException()
        {
            var behavior = new ValidationBehavior<CreateUserRequest, Guid>(
                new List<IValidator<CreateUserRequest>> { new CreateUserValidator() });

            await Assert.ThrowsAsync<ValidationException>(() =>
                behavior.Handle(
                    new CreateUserRequest("Wellington", "nao-e-email"),
                    CancellationToken.None,
                    NextThatShouldNotBeCalled()));
        }

        [Fact]
        public async Task Handle_CamposInvalidos_ExcecaoContemErrosDeCadaCampo()
        {
            var behavior = new ValidationBehavior<CreateUserRequest, Guid>(
                new List<IValidator<CreateUserRequest>> { new CreateUserValidator() });

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                behavior.Handle(
                    new CreateUserRequest("", "nao-e-email"),
                    CancellationToken.None,
                    NextThatShouldNotBeCalled()));

            Assert.Contains(ex.Errors, e => e.PropertyName == "Name");
            Assert.Contains(ex.Errors, e => e.PropertyName == "Email");
        }

        [Fact]
        public async Task Handle_NameMenorQue3Caracteres_ExcecaoContemMensagemCorreta()
        {
            var behavior = new ValidationBehavior<CreateUserRequest, Guid>(
                new List<IValidator<CreateUserRequest>> { new CreateUserValidator() });

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                behavior.Handle(
                    new CreateUserRequest("Ab", "w@email.com"),
                    CancellationToken.None,
                    NextThatShouldNotBeCalled()));

            Assert.Contains(ex.Errors, e =>
                e.PropertyName == "Name" &&
                e.ErrorMessage == "Name deve ter ao menos 3 caracteres");
        }

        [Fact]
        public async Task Handle_RequestInvalido_HandlerNaoEChamado()
        {
            var handlerCalled = false;
            var behavior = new ValidationBehavior<CreateUserRequest, Guid>(
                new List<IValidator<CreateUserRequest>> { new CreateUserValidator() });

            await Assert.ThrowsAsync<ValidationException>(() =>
                behavior.Handle(
                    new CreateUserRequest("", ""),
                    CancellationToken.None,
                    () => { handlerCalled = true; return Task.FromResult(Guid.NewGuid()); }));

            Assert.False(handlerCalled);
        }

        [Fact]
        public async Task Handle_TodosOsCamposInvalidos_ExcecaoContemTodosOsErros()
        {
            var behavior = new ValidationBehavior<CreateUserRequest, Guid>(
                new List<IValidator<CreateUserRequest>> { new CreateUserValidator() });

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                behavior.Handle(
                    new CreateUserRequest("", ""),
                    CancellationToken.None,
                    NextThatShouldNotBeCalled()));

            Assert.True(ex.Errors.Count() >= 2);
        }

        // ── Múltiplos validators ─────────────────────────────────────────────────

        [Fact]
        public async Task Handle_MultiploValidators_TodosSaoExecutados()
        {
            var validator1 = new Mock<IValidator<CreateUserRequest>>();
            validator1
                .Setup(v => v.ValidateAsync(
                    It.IsAny<ValidationContext<CreateUserRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[]
                {
                    new ValidationFailure("Name", "Erro do validator 1")
                }));

            var validator2 = new Mock<IValidator<CreateUserRequest>>();
            validator2
                .Setup(v => v.ValidateAsync(
                    It.IsAny<ValidationContext<CreateUserRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[]
                {
                    new ValidationFailure("Email", "Erro do validator 2")
                }));

            var behavior = new ValidationBehavior<CreateUserRequest, Guid>(
                new List<IValidator<CreateUserRequest>> { validator1.Object, validator2.Object });

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                behavior.Handle(
                    new CreateUserRequest("", ""),
                    CancellationToken.None,
                    NextThatShouldNotBeCalled()));

            Assert.Contains(ex.Errors, e => e.PropertyName == "Name");
            Assert.Contains(ex.Errors, e => e.PropertyName == "Email");
        }

        [Fact]
        public async Task Handle_MultiploValidatorsTodosValidos_ChamaNext()
        {
            var validator1 = new Mock<IValidator<CreateUserRequest>>();
            validator1
                .Setup(v => v.ValidateAsync(
                    It.IsAny<ValidationContext<CreateUserRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());

            var validator2 = new Mock<IValidator<CreateUserRequest>>();
            validator2
                .Setup(v => v.ValidateAsync(
                    It.IsAny<ValidationContext<CreateUserRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());

            var behavior = new ValidationBehavior<CreateUserRequest, Guid>(
                new List<IValidator<CreateUserRequest>> { validator1.Object, validator2.Object });

            var expected = Guid.NewGuid();
            var result = await behavior.Handle(
                new CreateUserRequest("Wellington", "w@email.com"),
                CancellationToken.None,
                NextReturning(expected));

            Assert.Equal(expected, result);
        }

        [Fact]
        public async Task Handle_MultiploValidatorsUmFalha_LancaValidationException()
        {
            var validatorOk = new Mock<IValidator<CreateUserRequest>>();
            validatorOk
                .Setup(v => v.ValidateAsync(
                    It.IsAny<ValidationContext<CreateUserRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());

            var validatorFail = new Mock<IValidator<CreateUserRequest>>();
            validatorFail
                .Setup(v => v.ValidateAsync(
                    It.IsAny<ValidationContext<CreateUserRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[]
                {
                    new ValidationFailure("Name", "Falhou")
                }));

            var behavior = new ValidationBehavior<CreateUserRequest, Guid>(
                new List<IValidator<CreateUserRequest>> { validatorOk.Object, validatorFail.Object });

            await Assert.ThrowsAsync<ValidationException>(() =>
                behavior.Handle(
                    new CreateUserRequest("Wellington", "w@email.com"),
                    CancellationToken.None,
                    NextThatShouldNotBeCalled()));
        }

        // ── CancellationToken ────────────────────────────────────────────────────

        [Fact]
        public async Task Handle_PassaCancellationTokenParaValidators()
        {
            CancellationToken capturedToken = default;
            using var cts = new CancellationTokenSource();

            var validatorMock = new Mock<IValidator<CreateUserRequest>>();
            validatorMock
                .Setup(v => v.ValidateAsync(
                    It.IsAny<ValidationContext<CreateUserRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<IValidationContext, CancellationToken>((_, ct) =>
                {
                    capturedToken = ct;
                    return Task.FromResult(new ValidationResult());
                });

            var behavior = new ValidationBehavior<CreateUserRequest, Guid>(
                new List<IValidator<CreateUserRequest>> { validatorMock.Object });

            await behavior.Handle(
                new CreateUserRequest("Wellington", "w@email.com"),
                cts.Token,
                NextReturning(Guid.NewGuid()));

            Assert.Equal(cts.Token, capturedToken);
        }

        // ── Integração com Mediator ──────────────────────────────────────────────

        [Fact]
        public async Task Mediator_ComValidationBehavior_RequestValido_RetornaResposta()
        {
            var expectedId = Guid.NewGuid();

            var handlerMock = new Mock<IRequestHandler<CreateUserRequest, Guid>>();
            handlerMock
                .Setup(h => h.Handle(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedId);

            var provider = ValidationTestServiceProvider.With(s =>
            {
                s.AddTransient<IRequestHandler<CreateUserRequest, Guid>>(_ => handlerMock.Object);
                s.AddTransient<IValidator<CreateUserRequest>, CreateUserValidator>();
                s.AddTransient<IPipelineBehavior<CreateUserRequest, Guid>,
                    ValidationBehavior<CreateUserRequest, Guid>>();
            });

            var result = await new Mediator(provider).Send(
                new CreateUserRequest("Wellington", "w@email.com"));

            Assert.Equal(expectedId, result);
        }

        [Fact]
        public async Task Mediator_ComValidationBehavior_RequestInvalido_LancaValidationException()
        {
            var handlerMock = new Mock<IRequestHandler<CreateUserRequest, Guid>>();
            handlerMock
                .Setup(h => h.Handle(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Guid.NewGuid());

            var provider = ValidationTestServiceProvider.With(s =>
            {
                s.AddTransient<IRequestHandler<CreateUserRequest, Guid>>(_ => handlerMock.Object);
                s.AddTransient<IValidator<CreateUserRequest>, CreateUserValidator>();
                s.AddTransient<IPipelineBehavior<CreateUserRequest, Guid>,
                    ValidationBehavior<CreateUserRequest, Guid>>();
            });

            await Assert.ThrowsAsync<ValidationException>(() =>
                new Mediator(provider).Send(new CreateUserRequest("", "invalido")));

            handlerMock.Verify(
                h => h.Handle(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Mediator_ComValidationELoggingBehavior_OrdemCorreta()
        {
            var callOrder = new List<string>();

            var loggingBehavior = new Mock<IPipelineBehavior<CreateUserRequest, Guid>>();
            loggingBehavior
                .Setup(b => b.Handle(
                    It.IsAny<CreateUserRequest>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<RequestHandlerDelegate<Guid>>()))
                .Returns<CreateUserRequest, CancellationToken, RequestHandlerDelegate<Guid>>(
                    async (_, _, next) =>
                    {
                        callOrder.Add("logging:before");
                        var r = await next();
                        callOrder.Add("logging:after");
                        return r;
                    });

            var handlerMock = new Mock<IRequestHandler<CreateUserRequest, Guid>>();
            handlerMock
                .Setup(h => h.Handle(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()))
                .Callback(() => callOrder.Add("handler"))
                .ReturnsAsync(Guid.NewGuid());

            var provider = ValidationTestServiceProvider.With(s =>
            {
                s.AddTransient<IRequestHandler<CreateUserRequest, Guid>>(_ => handlerMock.Object);
                s.AddTransient<IValidator<CreateUserRequest>, CreateUserValidator>();
                s.AddTransient<IPipelineBehavior<CreateUserRequest, Guid>>(_ => loggingBehavior.Object);
                s.AddTransient<IPipelineBehavior<CreateUserRequest, Guid>,
                    ValidationBehavior<CreateUserRequest, Guid>>();
            });

            await new Mediator(provider).Send(new CreateUserRequest("Wellington", "w@email.com"));

            Assert.Equal(new[] { "logging:before", "handler", "logging:after" }, callOrder);
        }
    }
}