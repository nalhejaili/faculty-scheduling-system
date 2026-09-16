using System;
using System.Security.Cryptography;

namespace TrainerScheduler.Security
{
    /// <summary>
    /// PBKDF2 password hashing (salt + hash), suitable for local auth.
    /// </summary>
    public static class PasswordHasher
    {
        private const int SaltSize = 16;      // 128-bit
        private const int KeySize = 32;       // 256-bit
        private const int Iterations = 100_000;

        public static (string hashBase64, string saltBase64) Hash(string password)
        {
            if (password is null) throw new ArgumentNullException(nameof(password));

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                KeySize);

            return (Convert.ToBase64String(key), Convert.ToBase64String(salt));
        }

        public static bool Verify(string password, string expectedHashBase64, string expectedSaltBase64)
        {
            if (password is null) return false;
            if (string.IsNullOrWhiteSpace(expectedHashBase64)) return false;
            if (string.IsNullOrWhiteSpace(expectedSaltBase64)) return false;

            byte[] salt;
            byte[] expectedHash;
            try
            {
                salt = Convert.FromBase64String(expectedSaltBase64);
                expectedHash = Convert.FromBase64String(expectedHashBase64);
            }
            catch
            {
                return false;
            }

            byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
    }
}
