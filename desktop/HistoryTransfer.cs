using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace KeySaver
{
    public sealed class SessionRecord
    {
        public SessionRecord(string app, DateTimeOffset startedAt, DateTimeOffset endedAt, string text, string path = null)
        {
            App = app;
            StartedAt = startedAt.ToUniversalTime();
            EndedAt = endedAt.ToUniversalTime();
            Text = text;
            Path = string.IsNullOrWhiteSpace(path) ? null : path;
        }

        public string App { get; }
        public DateTimeOffset StartedAt { get; }
        public DateTimeOffset EndedAt { get; }
        public string Text { get; }
        public string Path { get; }
    }

    public sealed class TransferDocument
    {
        private readonly IReadOnlyList<SessionRecord> sessions;
        private readonly byte[] salt;
        private readonly byte[] nonce;
        private readonly byte[] content;

        internal TransferDocument(string kind, IReadOnlyList<SessionRecord> sessions)
        {
            Kind = kind;
            this.sessions = sessions;
        }

        internal TransferDocument(byte[] salt, byte[] nonce, byte[] content)
        {
            Kind = "archive";
            this.salt = salt;
            this.nonce = nonce;
            this.content = content;
        }

        public string Kind { get; }
        public bool RequiresPassword => content != null;

        public IReadOnlyList<SessionRecord> Decode(string password)
        {
            return RequiresPassword ? HistoryTransfer.Decrypt(salt, nonce, content, password) : sessions;
        }
    }

    public static class HistoryTransfer
    {
        public const int MaxFileBytes = 16 * 1024 * 1024;

        private const int MaxDataBytes = 10 * 1024 * 1024;
        private const int MaxSessions = 5000;
        private const int MaxPasswordBytes = 1024;
        private const int Version = 1;
        private const int Iterations = 600000;
        private const int SaltSize = 16;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int KeySize = 32;
        private const string JsonFormat = "keysaver-json";
        private const string ArchiveFormat = "keysaver-archive";
        private const string TimestampOutput = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
        private const string BrokenFile = "Файл повреждён. Проверьте, что выбрали файл экспорта KeySaver.";
        private const string BrokenArchive = "Архив повреждён. Выберите другой файл.";
        private const string BrokenPayload = "Данные внутри архива повреждены.";
        private const string DataTooLarge = "Объём истории превышает 10 МБ.";
        private static readonly byte[] AssociatedData = Encoding.ASCII.GetBytes("keysaver-archive:1:encrypted");
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly Regex TimestampPattern = new Regex(
            @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,3})?(?:Z|[+-][0-9]{2}:[0-9]{2})\z",
            RegexOptions.CultureInvariant);
        private static readonly string[] TimestampFormats =
        {
            "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFK"
        };

        public static string ExportJson(IReadOnlyList<SessionRecord> records)
        {
            var data = BuildData(records);
            ValidatePayloadSize(Serialize(data, DataTooLarge));
            return Serialize(new { format = JsonFormat, version = Version, data }, "Размер файла превышает 16 МБ.");
        }

        public static string ExportArchive(IReadOnlyList<SessionRecord> records, string password)
        {
            var data = BuildData(records);
            var payload = Serialize(data, DataTooLarge);
            ValidatePayloadSize(payload);
            if (string.IsNullOrEmpty(password))
            {
                return Serialize(new { format = ArchiveFormat, version = Version, mode = "plain", data }, "Размер файла превышает 16 МБ.");
            }

            var salt = new byte[SaltSize];
            var nonce = new byte[NonceSize];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(salt);
                random.GetBytes(nonce);
            }

            var plaintext = Encoding.UTF8.GetBytes(payload);
            var tag = new byte[TagSize];
            byte[] ciphertext;
            try
            {
                ciphertext = Transform(plaintext, password, salt, nonce, tag, true);
            }
            finally
            {
                Array.Clear(plaintext, 0, plaintext.Length);
            }

            // Web Crypto appends the authentication tag to the ciphertext.
            var content = new byte[ciphertext.Length + tag.Length];
            Buffer.BlockCopy(ciphertext, 0, content, 0, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, content, ciphertext.Length, tag.Length);
            return Serialize(new
            {
                format = ArchiveFormat,
                version = Version,
                mode = "encrypted",
                salt = Convert.ToBase64String(salt),
                iv = Convert.ToBase64String(nonce),
                content = Convert.ToBase64String(content)
            }, "Размер файла превышает 16 МБ.");
        }

        public static TransferDocument Read(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) throw new InvalidDataException(BrokenFile);
            if (source.Length > MaxFileBytes || Encoding.UTF8.GetByteCount(source) > MaxFileBytes)
                throw new InvalidDataException("Размер файла превышает 16 МБ.");
            if (source[0] == '\uFEFF') source = source.Substring(1);

            var file = Deserialize(source, BrokenFile);
            var format = Property(file, "format") as string;
            if (format != JsonFormat && format != ArchiveFormat)
                throw new InvalidDataException("Выберите JSON или архив, экспортированный из KeySaver.");

            var version = Property(file, "version");
            if (!IsCurrentVersion(version))
                throw new InvalidDataException("Эта версия формата KeySaver пока не поддерживается.");

            var mode = Property(file, "mode") as string;
            if (format == JsonFormat || mode == "plain")
            {
                var records = ParseData(Property(file, "data"));
                ValidatePayloadSize(Serialize(BuildData(records), DataTooLarge));
                return new TransferDocument(format == JsonFormat ? "json" : "archive", records);
            }

            if (mode != "encrypted") throw new InvalidDataException(BrokenArchive);
            return new TransferDocument(
                DecodeBase64(Property(file, "salt"), SaltSize, SaltSize),
                DecodeBase64(Property(file, "iv"), NonceSize, NonceSize),
                DecodeBase64(Property(file, "content"), TagSize, MaxDataBytes + TagSize));
        }

        internal static IReadOnlyList<SessionRecord> Decrypt(byte[] salt, byte[] nonce, byte[] content, string password)
        {
            var ciphertext = new byte[content.Length - TagSize];
            var tag = new byte[TagSize];
            Buffer.BlockCopy(content, 0, ciphertext, 0, ciphertext.Length);
            Buffer.BlockCopy(content, ciphertext.Length, tag, 0, tag.Length);
            var plaintext = Transform(ciphertext, password, salt, nonce, tag, false);
            try
            {
                return ParseData(Deserialize(StrictUtf8.GetString(plaintext), BrokenPayload));
            }
            catch (DecoderFallbackException error)
            {
                throw new InvalidDataException(BrokenPayload, error);
            }
            finally
            {
                Array.Clear(plaintext, 0, plaintext.Length);
            }
        }

        private static bool IsCurrentVersion(object value)
        {
            return value is int integer && integer == Version
                || value is long wideInteger && wideInteger == Version
                || value is decimal number && number == Version
                || value is double floatingPoint && floatingPoint == Version;
        }

        private static object BuildData(IReadOnlyList<SessionRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            ValidateCount(records.Count);
            var rows = new object[records.Count];
            for (var index = 0; index < rows.Length; index++)
            {
                var record = records[index];
                if (record == null || !IsValidApp(record.App))
                    throw new InvalidDataException($"Укажите приложение для сессии №{index + 1}.");
                if (record.EndedAt < record.StartedAt)
                    throw new InvalidDataException($"Проверьте время начала и окончания сессии №{index + 1}.");
                if (record.Text == null)
                    throw new InvalidDataException($"Проверьте текст сессии №{index + 1}.");
                rows[index] = new
                {
                    app = record.App,
                    path = record.Path,
                    startedAt = record.StartedAt.ToString(TimestampOutput, CultureInfo.InvariantCulture),
                    endedAt = record.EndedAt.ToString(TimestampOutput, CultureInfo.InvariantCulture),
                    text = record.Text
                };
            }

            return new { sessions = rows };
        }

        private static IReadOnlyList<SessionRecord> ParseData(object data)
        {
            var rows = Property(data, "sessions") as IList;
            if (rows == null) throw new InvalidDataException("В файле отсутствует список сессий.");
            ValidateCount(rows.Count);
            var records = new List<SessionRecord>(rows.Count);
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                var app = Property(row, "app") as string;
                if (!IsValidApp(app))
                    throw new InvalidDataException($"Укажите приложение для сессии №{index + 1}.");
                if (!TryReadTimestamp(Property(row, "startedAt") as string, out var startedAt)
                    || !TryReadTimestamp(Property(row, "endedAt") as string, out var endedAt)
                    || endedAt < startedAt)
                    throw new InvalidDataException($"Проверьте время начала и окончания сессии №{index + 1}.");
                if (!(Property(row, "text") is string text))
                    throw new InvalidDataException($"Проверьте текст сессии №{index + 1}.");
                records.Add(new SessionRecord(app, startedAt, endedAt, text, ReadPath(row, index + 1)));
            }

            return records.AsReadOnly();
        }

        private static object Property(object instance, string name)
        {
            return instance is IDictionary<string, object> fields && fields.TryGetValue(name, out var value) ? value : null;
        }

        private static string ReadPath(object row, int number)
        {
            if (!(row is IDictionary<string, object> fields) || !fields.TryGetValue("path", out var value) || value == null)
                return null;
            var path = value as string;
            if (path == null || path.Length > 2048 || path.IndexOf('\0') >= 0 || path.IndexOf('\r') >= 0 || path.IndexOf('\n') >= 0)
                throw new InvalidDataException($"Проверьте путь к приложению для сессии №{number}.");
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }

        private static bool IsValidApp(string app)
        {
            return !string.IsNullOrWhiteSpace(app) && app.Length <= 128;
        }

        private static void ValidateCount(int count)
        {
            if (count > MaxSessions) throw new InvalidDataException("Файл содержит больше 5 000 сессий.");
        }

        private static bool TryReadTimestamp(string text, out DateTimeOffset value)
        {
            value = default;
            return text != null && TimestampPattern.IsMatch(text)
                && DateTimeOffset.TryParseExact(text, TimestampFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = MaxFileBytes, RecursionLimit = 32 };
        }

        private static string Serialize(object value, string sizeError)
        {
            string source;
            try
            {
                source = CreateSerializer().Serialize(value);
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidDataException(sizeError, error);
            }

            if (Encoding.UTF8.GetByteCount(source) > MaxFileBytes) throw new InvalidDataException(sizeError);
            return source;
        }

        private static object Deserialize(string source, string parseError)
        {
            try
            {
                return CreateSerializer().DeserializeObject(source);
            }
            catch (ArgumentException error)
            {
                throw new InvalidDataException(parseError, error);
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidDataException(parseError, error);
            }
        }

        private static void ValidatePayloadSize(string payload)
        {
            if (Encoding.UTF8.GetByteCount(payload) > MaxDataBytes) throw new InvalidDataException(DataTooLarge);
        }

        private static byte[] DecodeBase64(object value, int minSize, int maxSize)
        {
            if (!(value is string encoded) || encoded.Length > (maxSize + 2) / 3 * 4 || encoded.Length % 4 != 0)
                throw new InvalidDataException(BrokenArchive);
            foreach (var character in encoded)
            {
                if (!(character >= 'A' && character <= 'Z' || character >= 'a' && character <= 'z'
                    || character >= '0' && character <= '9' || character == '+' || character == '/' || character == '='))
                    throw new InvalidDataException(BrokenArchive);
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(encoded);
            }
            catch (FormatException error)
            {
                throw new InvalidDataException(BrokenArchive, error);
            }

            if (bytes.Length < minSize || bytes.Length > maxSize) throw new InvalidDataException(BrokenArchive);
            return bytes;
        }

        private static byte[] Transform(byte[] input, string password, byte[] salt, byte[] nonce, byte[] tag, bool encrypt)
        {
            if (string.IsNullOrEmpty(password)) throw new InvalidDataException("Введите пароль архива.");
            if (password.Length > MaxPasswordBytes || Encoding.UTF8.GetByteCount(password) > MaxPasswordBytes)
                throw new InvalidDataException("Пароль слишком длинный.");

            var passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[] secret;
            try
            {
                secret = CngArchiveCrypto.DeriveKey(passwordBytes, salt);
            }
            finally
            {
                Array.Clear(passwordBytes, 0, passwordBytes.Length);
            }

            try
            {
                return CngArchiveCrypto.Transform(input, secret, nonce, AssociatedData, tag, encrypt);
            }
            finally
            {
                Array.Clear(secret, 0, secret.Length);
            }
        }

        private static class CngArchiveCrypto
        {
            private const int HmacAlgorithm = 0x00000008;
            private const int AuthTagMismatch = unchecked((int)0xC000A002);
            private static readonly byte[] GcmMode = Encoding.Unicode.GetBytes("ChainingModeGCM\0");

            public static byte[] DeriveKey(byte[] password, byte[] salt)
            {
                var status = BCryptOpenAlgorithmProvider(out var provider, "SHA256", null, HmacAlgorithm);
                using (provider)
                {
                    Check(status);
                    var key = new byte[KeySize];
                    status = BCryptDeriveKeyPBKDF2(provider, password, password.Length, salt, salt.Length, Iterations, key, key.Length, 0);
                    if (status != 0) Array.Clear(key, 0, key.Length);
                    Check(status);
                    return key;
                }
            }

            public static byte[] Transform(byte[] input, byte[] secret, byte[] nonce, byte[] aad, byte[] tag, bool encrypt)
            {
                var status = BCryptOpenAlgorithmProvider(out var provider, "AES", null, 0);
                using (provider)
                {
                    Check(status);
                    Check(BCryptSetProperty(provider, "ChainingMode", GcmMode, GcmMode.Length, 0));
                    status = BCryptGenerateSymmetricKey(provider, out var key, IntPtr.Zero, 0, secret, secret.Length, 0);
                    using (key)
                    {
                        Check(status);
                        return TransformWithKey(key, input, nonce, aad, tag, encrypt);
                    }
                }
            }

            private static byte[] TransformWithKey(KeyHandle key, byte[] input, byte[] nonce, byte[] aad, byte[] tag, bool encrypt)
            {
                var buffers = new GCHandle[3];
                // A non-null buffer also allows CNG to authenticate a zero-length payload.
                var output = new byte[Math.Max(1, input.Length)];
                try
                {
                    buffers[0] = GCHandle.Alloc(nonce, GCHandleType.Pinned);
                    buffers[1] = GCHandle.Alloc(aad, GCHandleType.Pinned);
                    buffers[2] = GCHandle.Alloc(tag, GCHandleType.Pinned);
                    var info = new AuthenticatedCipherModeInfo
                    {
                        Size = Marshal.SizeOf<AuthenticatedCipherModeInfo>(),
                        Version = 1,
                        Nonce = buffers[0].AddrOfPinnedObject(),
                        NonceSize = nonce.Length,
                        AuthData = buffers[1].AddrOfPinnedObject(),
                        AuthDataSize = aad.Length,
                        Tag = buffers[2].AddrOfPinnedObject(),
                        TagSize = tag.Length
                    };

                    int written;
                    var status = encrypt
                        ? BCryptEncrypt(key, input, input.Length, ref info, IntPtr.Zero, 0, output, output.Length, out written, 0)
                        : BCryptDecrypt(key, input, input.Length, ref info, IntPtr.Zero, 0, output, output.Length, out written, 0);
                    if (status == AuthTagMismatch)
                        throw new InvalidDataException("Не удалось открыть архив. Проверьте пароль и целостность файла.");
                    Check(status);
                    if (written != input.Length) throw new CryptographicException("Windows вернула неполные данные архива.");
                    return written == 0 ? Array.Empty<byte>() : output;
                }
                catch
                {
                    Array.Clear(output, 0, output.Length);
                    throw;
                }
                finally
                {
                    foreach (var buffer in buffers)
                        if (buffer.IsAllocated) buffer.Free();
                }
            }

            private static void Check(int status)
            {
                if (status != 0)
                    throw new CryptographicException($"Windows не удалось обработать защищённый архив (0x{status:X8}).");
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct AuthenticatedCipherModeInfo
            {
                public int Size;
                public int Version;
                public IntPtr Nonce;
                public int NonceSize;
                public IntPtr AuthData;
                public int AuthDataSize;
                public IntPtr Tag;
                public int TagSize;
                public IntPtr MacContext;
                public int MacContextSize;
                public int AadSize;
                public ulong DataSize;
                public int Flags;
            }

            private sealed class AlgorithmHandle : SafeHandleZeroOrMinusOneIsInvalid
            {
                public AlgorithmHandle() : base(true) { }
                protected override bool ReleaseHandle() => BCryptCloseAlgorithmProvider(handle, 0) == 0;
            }

            private sealed class KeyHandle : SafeHandleZeroOrMinusOneIsInvalid
            {
                public KeyHandle() : base(true) { }
                protected override bool ReleaseHandle() => BCryptDestroyKey(handle) == 0;
            }

            [DllImport("bcrypt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
            private static extern int BCryptOpenAlgorithmProvider(out AlgorithmHandle provider, string algorithm, string implementation, int flags);

            [DllImport("bcrypt.dll", ExactSpelling = true)]
            private static extern int BCryptCloseAlgorithmProvider(IntPtr provider, int flags);

            [DllImport("bcrypt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
            private static extern int BCryptSetProperty(AlgorithmHandle provider, string property, byte[] value, int length, int flags);

            [DllImport("bcrypt.dll", ExactSpelling = true)]
            private static extern int BCryptGenerateSymmetricKey(AlgorithmHandle provider, out KeyHandle key, IntPtr keyObject, int keyObjectSize,
                byte[] secret, int secretSize, int flags);

            [DllImport("bcrypt.dll", ExactSpelling = true)]
            private static extern int BCryptDestroyKey(IntPtr key);

            [DllImport("bcrypt.dll", ExactSpelling = true)]
            private static extern int BCryptDeriveKeyPBKDF2(AlgorithmHandle provider, byte[] password, int passwordSize,
                byte[] salt, int saltSize, ulong iterations, [Out] byte[] key, int keySize, int flags);

            [DllImport("bcrypt.dll", ExactSpelling = true)]
            private static extern int BCryptEncrypt(KeyHandle key, byte[] input, int inputSize, ref AuthenticatedCipherModeInfo info,
                IntPtr iv, int ivSize, [Out] byte[] output, int outputSize, out int written, int flags);

            [DllImport("bcrypt.dll", ExactSpelling = true)]
            private static extern int BCryptDecrypt(KeyHandle key, byte[] input, int inputSize, ref AuthenticatedCipherModeInfo info,
                IntPtr iv, int ivSize, [Out] byte[] output, int outputSize, out int written, int flags);
        }
    }
}
