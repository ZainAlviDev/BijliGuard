using BijliGuard.Application.Common;
using BijliGuard.Domain.Consumption;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace BijliGuard.Application.Estimate;

/// <summary>Anonymous quick check: no account, nothing is stored.</summary>
public sealed record QuickEstimateCommand(
    int PreviousReading,
    DateOnly PreviousDate,
    int CurrentReading,
    DateOnly CurrentDate,
    DateOnly CycleEnd,
    int? TargetUnits);

public sealed record QuickEstimateResult(
    int UnitsUsed,
    int DaysElapsed,
    int DaysRemaining,
    int RemainingUnits,
    decimal PercentUsed,
    decimal? AveragePerDay,
    decimal? AllowedPerDay,
    decimal? ProjectedUnits,
    int TargetUnits,
    string Status);

public interface IQuickEstimateService
{
    Result<QuickEstimateResult> Calculate(QuickEstimateCommand command);
}

public sealed class QuickEstimateValidator : AbstractValidator<QuickEstimateCommand>
{
    public QuickEstimateValidator()
    {
        RuleFor(x => x.PreviousReading).InclusiveBetween(0, 999_999_999)
            .WithMessage("Enter the previous meter reading as a whole number.");
        RuleFor(x => x.CurrentReading).InclusiveBetween(0, 999_999_999)
            .WithMessage("Enter the current meter reading as a whole number.");
        RuleFor(x => x.CurrentReading)
            .GreaterThanOrEqualTo(x => x.PreviousReading)
            .WithMessage("The current reading cannot be lower than the previous reading. Check the meter, or ask us about a meter rollover.");
        RuleFor(x => x.CurrentDate)
            .GreaterThanOrEqualTo(x => x.PreviousDate)
            .WithMessage("The current date cannot be before the previous reading date.");
        RuleFor(x => x.CycleEnd)
            .GreaterThanOrEqualTo(x => x.CurrentDate)
            .WithMessage("The billing cycle end date has already passed.");
        RuleFor(x => x.CycleEnd)
            .LessThanOrEqualTo(x => x.PreviousDate.AddDays(62))
            .WithMessage("A billing cycle cannot be longer than about two months.");
        RuleFor(x => x.TargetUnits)
            .InclusiveBetween(50, 1000)
            .When(x => x.TargetUnits is not null)
            .WithMessage("The target must be between 50 and 1000 units.");
    }
}

public sealed class QuickEstimateService(
    IValidator<QuickEstimateCommand> validator,
    ILogger<QuickEstimateService> logger) : IQuickEstimateService
{
    public Result<QuickEstimateResult> Calculate(QuickEstimateCommand command)
    {
        var validation = validator.Validate(command);
        if (!validation.IsValid)
        {
            var fields = validation.Errors.Select(e => new FieldError(e.PropertyName, e.ErrorMessage)).ToList();
            logger.LogInformation("Estimate.Rejected {Reason}", "validation");
            return Result<QuickEstimateResult>.Failure(ErrorCodes.ValidationFailed, "Some fields are invalid.", fields);
        }

        var target = command.TargetUnits ?? CycleCalculator.DefaultTargetUnits;
        var snapshot = CycleCalculator.Calculate(
            command.PreviousReading, command.PreviousDate,
            command.CurrentReading, command.CurrentDate,
            command.CycleEnd, target);

        // Log the outcome only. Readings and dates are personal data and are never logged.
        logger.LogInformation("Estimate.Calculated {Status} {DaysElapsed} {TargetUnits}",
            snapshot.Status.ToString(), snapshot.DaysElapsed, target);

        return Result<QuickEstimateResult>.Success(new QuickEstimateResult(
            snapshot.UnitsUsed,
            snapshot.DaysElapsed,
            snapshot.DaysRemaining,
            snapshot.RemainingUnits,
            snapshot.PercentUsed,
            snapshot.AveragePerDay,
            snapshot.AllowedPerDay,
            snapshot.ProjectedUnits,
            target,
            snapshot.Status.ToString()));
    }
}
