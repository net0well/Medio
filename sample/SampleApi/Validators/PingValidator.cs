using FluentValidation;
using SampleApi;

public class PingValidator : AbstractValidator<Ping>
{
    public PingValidator()
    {
        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Message é obrigatório")
            .MaximumLength(100).WithMessage("Message deve ter no máximo 100 caracteres");
    }
}