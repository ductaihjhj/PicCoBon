using System;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using PickleballClub.Models;
using PickleballClub.Utilities;

namespace PickleballClub.Services;

public class PasswordService
{
    private static readonly Regex LegacyMd5Regex = new("^[a-fA-F0-9]{32}$", RegexOptions.Compiled);
    private readonly PasswordHasher<TbAccount> _hasher = new();

    public string HashPassword(TbAccount account, string plainPassword)
    {
        return _hasher.HashPassword(account, plainPassword);
    }

    public PasswordVerificationResult VerifyPassword(TbAccount account, string? storedHash, string providedPassword, out string? upgradedHash)
    {
        upgradedHash = null;
        if (string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(providedPassword))
        {
            return PasswordVerificationResult.Failed;
        }

        // Check if legacy 32-char MD5 hex hash
        if (LegacyMd5Regex.IsMatch(storedHash))
        {
            string md5 = HashMD5.GetMD5(providedPassword);
            if (string.Equals(storedHash, md5, StringComparison.OrdinalIgnoreCase))
            {
                upgradedHash = _hasher.HashPassword(account, providedPassword);
                return PasswordVerificationResult.SuccessRehashNeeded;
            }
            return PasswordVerificationResult.Failed;
        }

        try
        {
            var result = _hasher.VerifyHashedPassword(account, storedHash, providedPassword);
            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                upgradedHash = _hasher.HashPassword(account, providedPassword);
            }
            return result;
        }
        catch
        {
            // Fallback if stored value was plain text or malformed
            return PasswordVerificationResult.Failed;
        }
    }
}
