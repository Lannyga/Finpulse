using Finpulse.Domain;

namespace Finpulse.Application;

public sealed record AuthResult(bool Exitoso, string Mensaje, UserSession? Usuario = null);
public sealed record RegistrationResult(bool Exitoso, string Mensaje);

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string storedHash);
}

/// <summary>Contrato de persistencia y autenticación transaccional.</summary>
public interface IUserRepository
{
    Task<bool> CreateAsync(string nombre, string correoNormalizado, string hash, CancellationToken ct = default);
    Task<AuthResult> AuthenticateAsync(string correoNormalizado, string password, IPasswordHasher hasher,
        CancellationToken ct = default);
}
