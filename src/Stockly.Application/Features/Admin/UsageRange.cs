using FluentValidation;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Admin;

/// <summary>
/// The Africa/Cairo date range of AD14–AD16 (both days included): <c>to</c> defaults to today, <c>from</c> to the
/// <see cref="AdminLimits.DefaultUsageRangeDays"/> days ending on <c>to</c>. <c>to - from</c> is at most
/// <see cref="AdminLimits.MaxUsageRangeDays"/> days.
/// </summary>
public sealed record UsageRange(DateOnly From, DateOnly To)
{
    /// <summary>The first instant of <see cref="From"/> in UTC (inclusive).</summary>
    public DateTimeOffset StartUtc => BusinessCalendar.StartOfDayUtc(From);

    /// <summary>The first instant after <see cref="To"/> in UTC (exclusive).</summary>
    public DateTimeOffset EndUtc => BusinessCalendar.StartOfDayUtc(To.AddDays(1));

    public static UsageRange Resolve(DateOnly? from, DateOnly? to, IClock clock)
    {
        var end = to ?? clock.Today();
        return new UsageRange(from ?? end.AddDays(1 - AdminLimits.DefaultUsageRangeDays), end);
    }

    /// <summary>Validator rule for a query with <c>From</c> and <c>To</c>: the resolved range is ordered and not too long.</summary>
    public static void AddRules<T>(AbstractValidator<T> validator, Func<T, DateOnly?> from, Func<T, DateOnly?> to, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(validator);
        validator.RuleFor(query => Resolve(from(query), to(query), clock))
            .Must(range => range.To >= range.From && range.To.DayNumber - range.From.DayNumber <= AdminLimits.MaxUsageRangeDays)
            .WithName("to")
            .OverridePropertyName("to")
            .WithMessage(_ => ValidationMessages.UsageRangeInvalid);
    }
}
