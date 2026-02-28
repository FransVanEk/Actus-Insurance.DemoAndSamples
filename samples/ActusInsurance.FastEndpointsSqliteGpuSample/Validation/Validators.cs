using ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Runs;
using ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Sinks;
using FastEndpoints;
using FluentValidation;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Validation;

public class CreateSinkValidator : Validator<CreateSinkRequest>
{
    public CreateSinkValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Sink name is required")
            .MaximumLength(200).WithMessage("Sink name must be 200 characters or fewer");

        RuleFor(x => x.Version)
            .NotEmpty().WithMessage("Version is required")
            .MaximumLength(50).WithMessage("Version must be 50 characters or fewer");

        RuleFor(x => x.JsonDefinition)
            .NotEmpty().WithMessage("JsonDefinition is required");
    }
}

public class UpdateSinkValidator : Validator<UpdateSinkRequest>
{
    public UpdateSinkValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Sink name is required")
            .MaximumLength(200).WithMessage("Sink name must be 200 characters or fewer");

        RuleFor(x => x.Version)
            .NotEmpty().WithMessage("Version is required")
            .MaximumLength(50).WithMessage("Version must be 50 characters or fewer");

        RuleFor(x => x.JsonDefinition)
            .NotEmpty().WithMessage("JsonDefinition is required");
    }
}
