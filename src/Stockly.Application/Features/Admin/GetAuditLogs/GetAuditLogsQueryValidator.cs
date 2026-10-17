using FluentValidation;
using Stockly.Application.Validation;

namespace Stockly.Application.Features.Admin.GetAuditLogs;

public sealed class GetAuditLogsQueryValidator : AbstractValidator<GetAuditLogsQuery>
{
    public GetAuditLogsQueryValidator()
    {
        RuleFor(q => q.TargetType).MaximumLength(AdminLimits.TargetTypeMaxLength);
        RuleFor(q => q.Page).GreaterThanOrEqualTo(Paging.DefaultPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}
