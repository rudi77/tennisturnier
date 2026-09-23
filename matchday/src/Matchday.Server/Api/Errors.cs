namespace Matchday.Server.Api;

public sealed class NotFoundException(string message) : Exception(message);

public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>
/// Der Stand hat sich geändert, seit der Aufrufer ihn gesehen hat — ein
/// anderes Handy war schneller, oder ein nachgeschickter Schritt kam schon an.
/// </summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>
/// Nicht angemeldet. Bewusst getrennt von <see cref="ForbiddenException"/>:
/// 401 heißt „sag mir, wer du bist", 403 heißt „ich weiß es, und du darfst
/// trotzdem nicht". Die Oberfläche macht daraus zweierlei — einmal die
/// Anmeldung, einmal eine Fehlermeldung.
/// </summary>
public sealed class UnauthorizedException(string message) : Exception(message);
