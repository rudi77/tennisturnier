namespace Matchday.Server.Api;

public sealed class NotFoundException(string message) : Exception(message);

public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>
/// Nicht angemeldet. Bewusst getrennt von <see cref="ForbiddenException"/>:
/// 401 heißt „sag mir, wer du bist", 403 heißt „ich weiß es, und du darfst
/// trotzdem nicht". Die Oberfläche macht daraus zweierlei — einmal die
/// Anmeldung, einmal eine Fehlermeldung.
/// </summary>
public sealed class UnauthorizedException(string message) : Exception(message);
