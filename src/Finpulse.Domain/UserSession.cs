namespace Finpulse.Domain;

/// <summary>Información mínima del usuario autenticado; nunca incluye credenciales.</summary>
public sealed record UserSession(long Id, string NombreCompleto, string Correo);
