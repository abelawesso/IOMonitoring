using FluentValidation;
using IOMonitoring.Application.Abstractions;
using IOMonitoring.Contracts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IOMonitoring.Application.Features.Auth;

/// <summary>Returns a JWT when the credentials are valid, otherwise null.</summary>
public sealed record LoginCommand(string Username, string Password) : IRequest<LoginResponse?>;

internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

internal sealed class LoginCommandHandler(IAppDbContext db, IPasswordHasher hasher, ITokenService tokens)
    : IRequestHandler<LoginCommand, LoginResponse?>
{
    public async Task<LoginResponse?> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim().ToLowerInvariant();
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

        if (user is null || !hasher.Verify(request.Password, user.PasswordHash))
        {
            return null;
        }

        var (token, expiresAt) = tokens.CreateToken(user);
        return new LoginResponse(token, expiresAt, user.Username, user.DisplayName, user.Role.ToString());
    }
}
