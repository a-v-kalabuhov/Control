using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Wintime.Control.Core.Interfaces;

namespace Wintime.Control.Infrastructure.Auth;

/// <summary>
/// Потокобезопасное in-memory хранилище refresh-токенов.
/// </summary>
/// <remarks>
/// Ключом является SHA-256 хеш токена, а не само значение: даже при дампе памяти
/// процессы утечёт только хеш, из которого невозможно восстановить исходный токен.
/// Записи с истёкшим сроком действия удаляются лениво — при каждом вызове
/// <see cref="Store"/> проходит по словарю и вычищает просроченные.
/// </remarks>
public sealed class MemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly ConcurrentDictionary<string, RefreshTokenInfo> _tokensByHash = new();
    private readonly TimeProvider _timeProvider;

    public MemoryRefreshTokenStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public RefreshTokenInfo Store(string token, string userId, DateTime expiresAtUtc)
    {
        var hash = HashToken(token);
        var info = new RefreshTokenInfo(userId, expiresAtUtc, RevokedAtUtc: null);
        _tokensByHash[hash] = info;

        EvictExpired();
        return info;
    }

    public RefreshTokenInfo? GetActive(string token)
    {
        var hash = HashToken(token);
        return _tokensByHash.TryGetValue(hash, out var info) && info.IsActive(_timeProvider.GetUtcNow().DateTime)
            ? info
            : null;
    }

    public void Revoke(string token)
    {
        var hash = HashToken(token);
        var nowUtc = _timeProvider.GetUtcNow().DateTime;
        _tokensByHash.AddOrUpdate(
            hash,
            _ => new RefreshTokenInfo(string.Empty, DateTime.MinValue, nowUtc),
            (_, existing) => existing with { RevokedAtUtc = existing.RevokedAtUtc ?? nowUtc });
    }

    public void RevokeAllForUser(string userId)
    {
        var nowUtc = _timeProvider.GetUtcNow().DateTime;
        foreach (var kv in _tokensByHash)
        {
            if (kv.Value.UserId == userId && kv.Value.RevokedAtUtc is null)
                _tokensByHash.TryUpdate(kv.Key, kv.Value with { RevokedAtUtc = nowUtc }, kv.Value);
        }
    }

    private void EvictExpired()
    {
        var nowUtc = _timeProvider.GetUtcNow().DateTime;
        foreach (var kv in _tokensByHash)
        {
            // Удаляем и истёкшие, и отозванные — они больше не нужны
            if (!kv.Value.IsActive(nowUtc))
                _tokensByHash.TryRemove(kv.Key, out _);
        }
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
