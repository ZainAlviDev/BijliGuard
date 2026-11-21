using BijliGuard.Domain.Billing;
using FluentValidation;

namespace BijliGuard.Application.Billing;

public sealed class SaveHouseholdCommandValidator : AbstractValidator<SaveHouseholdCommand>
{
    public SaveHouseholdCommandValidator()
    {
        RuleFor(x => x.Provider).IsInEnum().WithMessage("Choose your electricity company.");

        // Blank means "keep the saved reference". The service rejects blank when no household exists yet.
        When(x => !string.IsNullOrWhiteSpace(x.ConsumerReference), () =>
        {
            RuleFor(x => x.ConsumerReference)
                .Length(6, 24).WithMessage("The consumer reference must be 6 to 24 characters.")
                .Matches("^[A-Za-z0-9-]+$").WithMessage("Use letters, digits and dashes only.");
        });

        RuleFor(x => x.TargetUnits)
            .InclusiveBetween(50, 1000).WithMessage("The target must be between 50 and 1000 units.");

        RuleFor(x => x.MeterNumber)
            .MaximumLength(30)
            .Matches("^[A-Za-z0-9 -]*$").When(x => !string.IsNullOrEmpty(x.MeterNumber))
            .WithMessage("The meter number can use letters, digits, spaces and dashes only.");
    }
}

public sealed class ConfirmBaselineCommandValidator : AbstractValidator<ConfirmBaselineCommand>
{
    public ConfirmBaselineCommandValidator()
    {
        RuleFor(x => x.PreviousReading).GreaterThanOrEqualTo(0).LessThanOrEqualTo(999_999_999)
            .WithMessage("Enter the previous meter reading.");

        RuleFor(x => x.CycleEnd)
            .GreaterThan(x => x.CycleStart).WithMessage("The cycle end must be after the cycle start.");

        RuleFor(x => x)
            .Must(x => x.CycleEnd.DayNumber - x.CycleStart.DayNumber + 1 is >= 25 and <= 40)
            .WithName("CycleEnd")
            .WithMessage("A billing cycle is usually 25 to 40 days long. Check the billing dates.");

        RuleFor(x => x.PreviousDate)
            .Must((x, d) => d >= x.CycleStart.AddDays(-7) && d <= x.CycleEnd)
            .WithMessage("The previous reading date should be within the billing period.");

        When(x => x.CurrentReading is not null, () =>
        {
            RuleFor(x => x.CurrentDate).NotNull().WithMessage("Enter the date of the current reading.");
            RuleFor(x => x.CurrentReading!.Value)
                .GreaterThanOrEqualTo(x => x.PreviousReading)
                .WithMessage("The current reading cannot be lower than the previous reading.");
            RuleFor(x => x.CurrentDate!.Value)
                .GreaterThan(x => x.PreviousDate).WithMessage("The current reading date must be after the previous one.");
        });
    }
}

public sealed class SubmitReadingCommandValidator : AbstractValidator<SubmitReadingCommand>
{
    public SubmitReadingCommandValidator()
    {
        RuleFor(x => x.Value).InclusiveBetween(0, 999_999_999).WithMessage("Enter the number shown on the meter.");
    }
}
