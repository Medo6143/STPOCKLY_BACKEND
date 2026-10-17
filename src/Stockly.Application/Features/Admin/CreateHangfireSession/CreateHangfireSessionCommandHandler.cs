using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Features.Auth;
using Stockly.Contracts.Admin;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Admin.CreateHangfireSession;

public sealed class CreateHangfireSessionCommandHandler(ICurrentUser currentUser, IHangfireSessionCodes codes)
    : IRequestHandler<CreateHangfireSessionCommand, Result<HangfireSessionResponse>>
{
    public Task<Result<HangfireSessionResponse>> Handle(CreateHangfireSessionCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } adminId)
        {
            return Task.FromResult<Result<HangfireSessionResponse>>(AuthErrors.Unauthenticated);
        }

        var code = codes.Issue(adminId);
        var url = $"{HangfireSession.Path}?{HangfireSession.CodeParameter}={Uri.EscapeDataString(code)}";
        return Task.FromResult<Result<HangfireSessionResponse>>(new HangfireSessionResponse(url));
    }
}
