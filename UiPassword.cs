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

        public static void Set(Preferences preferences, string password, out byte[] key)
        {
            var salt = new byte[16];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(salt);
            preferences.PasswordSalt = Convert.ToBase64String(salt);
            preferences.PasswordVersion = 2;
            preferences.PasswordHash = Split(password, salt, out key);
        }

        public static bool Open(Preferences preferences, string password, out byte[] key, out bool upgraded)
        {
            key = null;
            upgraded = false;
            if (!IsSet(preferences) || password == null) return false;
            byte[] salt;
            try { salt = Convert.FromBase64String(preferences.PasswordSalt); }
            catch (FormatException) { return false; }

            if (preferences.PasswordVersion >= 2)
            {
                var verifier = Split(password, salt, out key);
                if (FixedEquals(verifier, preferences.PasswordHash)) return true;
                Clear(key);
                key = null;
                return false;
            }

            if (!FixedEquals(LegacyHash(password, salt), preferences.PasswordHash)) return false;
            upgraded = true;
            Set(preferences, password, out key);
            return true;
        }

        public static bool Matches(Preferences preferences, string password)
        {
            if (!IsSet(preferences) || password == null) return false;
            byte[] salt;
            try { salt = Convert.FromBase64String(preferences.PasswordSalt); }
            catch (FormatException) { return false; }
            if (preferences.PasswordVersion >= 2)
            {
                var verifier = Split(password, salt, out var key);
                Clear(key);
                return FixedEquals(verifier, preferences.PasswordHash);
            }
            return FixedEquals(LegacyHash(password, salt), preferences.PasswordHash);
        }

        private static string Split(string password, byte[] salt, out byte[] key)
        {
            using (var derive = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256))
            {
                var material = derive.GetBytes(64);
                key = new byte[32];
                Buffer.BlockCopy(material, 0, key, 0, key.Length);
                var verifier = Convert.ToBase64String(material, 32, 32);
                Array.Clear(material, 0, material.Length);
                return verifier;
            }
        }

        private static string LegacyHash(string password, byte[] salt)
        {
            using (var derive = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256))
                return Convert.ToBase64String(derive.GetBytes(32));
        }

        private static void Clear(byte[] key)
        {
            if (key != null) Array.Clear(key, 0, key.Length);
        }

        private static bool FixedEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }

    internal static class HistorySecrets
    {
        private static byte[] key;

        public static bool IsReady => key != null;
        public static byte[] Key => key;

        public static void Install(byte[] value)
        {
            if (key != null && !ReferenceEquals(key, value)) Array.Clear(key, 0, key.Length);
            key = value;
        }
    }
}
