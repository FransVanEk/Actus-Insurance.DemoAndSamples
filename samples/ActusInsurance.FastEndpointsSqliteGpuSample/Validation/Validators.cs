using ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Files;
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

public class UploadFileValidator : Validator<UploadFileRequest>
{
    public UploadFileValidator()
    {
        RuleFor(x => x.File)
            .NotNull().WithMessage("A file is required");

        When(x => x.File is not null, () =>
        {
            RuleFor(x => x.File.Length)
                .GreaterThan(0).WithMessage("Uploaded file must not be empty");

            RuleFor(x => x.File.FileName)
                .NotEmpty().WithMessage("File must have a name");
        });
    }
}

public class StartRunValidator : Validator<StartRunRequest>
{
    public StartRunValidator()
    {
        RuleFor(x => x)
            .Must(r => r.ScenarioArtifactId.HasValue
                    || r.RiskArtifactId.HasValue
                    || r.PortfolioArtifactId.HasValue)
            .WithMessage("At least one of ScenarioArtifactId, RiskArtifactId, or PortfolioArtifactId must be provided");
    }
}

