using System.Text.RegularExpressions;

namespace Finpulse.Application;

/// <summary>Reglas de entrada del caso de uso Registro/Acceso de estudiantes.</summary>
public sealed class AuthService
{
    private static readonly Regex CorreoRegex = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IUserRepository _repository;
    private readonly IPasswordHasher _hasher;

    public AuthService(IUserRepository repository, IPasswordHasher hasher)
    {
        _repository = repository;
        _hasher = hasher;
    }

    public async Task<RegistrationResult> RegisterAsync(string nombre, string correo,
        string password, string confirmacion, CancellationToken ct = default)
    {
        nombre = (nombre ?? "").Trim();
        var email = NormalizeEmail(correo);
        if (nombre.Length is < 3 or > 120)
            return new(false, "El nombre debe tener entre 3 y 120 caracteres.");
        if (!IsValidEmail(email))
            return new(false, "Introduce un correo electrónico válido (máximo 254 caracteres).");
        if (password is null || password.Length is < 12 or > 128)
            return new(false, "La contraseña debe tener entre 12 y 128 caracteres.");
        if (password != confirmacion)
            return new(false, "La confirmación no coincide con la contraseña.");

        var hash = _hasher.HashPassword(password);
        bool creado = await _repository.CreateAsync(nombre, email, hash, ct);
        return creado
            ? new(true, "Cuenta creada correctamente. Ya puedes iniciar sesión.")
            : new(false, "No fue posible registrar ese correo.");
    }

    public async Task<AuthResult> LoginAsync(string correo, string password, CancellationToken ct = default)
    {
        var email = NormalizeEmail(correo);
        if (!IsValidEmail(email) || string.IsNullOrEmpty(password))
            return new(false, "Credenciales incorrectas o acceso temporalmente restringido.");
        return await _repository.AuthenticateAsync(email, password, _hasher, ct);
    }

    public static string NormalizeEmail(string? value) => (value ?? "").Trim().ToLowerInvariant();

    public static bool IsValidEmail(string email) => email.Length is > 3 and <= 254
        && CorreoRegex.IsMatch(email);
}
