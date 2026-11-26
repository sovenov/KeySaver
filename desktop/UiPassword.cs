using System;
using System.Security.Cryptography;

namespace KeySaver
{
    internal static class UiPassword
    {
        public static bool IsSet(Preferences preferences)
        {
            return preferences != null
                && !string.IsNullOrEmpty(preferences.PasswordSalt)
                && !string.IsNullOrEmpty(preferences.PasswordHash);
        }

        public static void Set(Preferences preferences, string password)
        {
            var salt = new byte[16];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(salt);
            preferences.PasswordSalt = Convert.ToBase64String(salt);
            preferences.PasswordHash = Hash(password, salt);
        }

        public static bool Matches(Preferences preferences, string password)
        {
            if (!IsSet(preferences) || password == null) return false;
            byte[] salt;
            try { salt = Convert.FromBase64String(preferences.PasswordSalt); }
            catch (FormatException) { return false; }
            return FixedEquals(Hash(password, salt), preferences.PasswordHash);
        }

        private static string Hash(string password, byte[] salt)
        {
            using (var derive = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256))
                return Convert.ToBase64String(derive.GetBytes(32));
        }

        private static bool FixedEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }
}
