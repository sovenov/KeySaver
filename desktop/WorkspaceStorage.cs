using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace KeySaver
{
    [DataContract]
    public sealed class Preferences
    {
        [DataMember(Name = "theme")]
        public string Theme { get; set; }

        [DataMember(Name = "mode")]
        public string Mode { get; set; }

        [DataMember(Name = "recordingEnabled")]
        public bool RecordingEnabled { get; set; }

        [DataMember(Name = "exceptPaths")]
        public List<string> ExceptPaths { get; set; }

        [DataMember(Name = "onlyPaths")]
        public List<string> OnlyPaths { get; set; }

        [DataMember(Name = "mouseRule")]
        public bool MouseRule { get; set; }

        [DataMember(Name = "tabRule")]
        public bool TabRule { get; set; }

        [DataMember(Name = "enterRule")]
        public bool EnterRule { get; set; }

        [DataMember(Name = "idleRule")]
        public bool IdleRule { get; set; }

        [DataMember(Name = "idleSeconds")]
        public int IdleSeconds { get; set; }

        public Preferences()
        {
            SetDefaults();
        }

        [OnDeserializing]
        private void SetMissingMemberDefaults(StreamingContext context)
        {
            SetDefaults();
        }

        private void SetDefaults()
        {
            Theme = "system";
            Mode = "all";
            RecordingEnabled = true;
            ExceptPaths = new List<string>();
            OnlyPaths = new List<string>();
            MouseRule = true;
            TabRule = true;
            EnterRule = false;
            IdleRule = true;
            IdleSeconds = 10;
        }
    }

    public sealed class WorkspaceStorage
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly string historyPath;
        private readonly string preferencesPath;

        public WorkspaceStorage()
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeySaver"))
        {
        }

        public WorkspaceStorage(string directory)
        {
            var fullDirectory = Path.GetFullPath(directory);
            historyPath = Path.Combine(fullDirectory, "history.json");
            preferencesPath = Path.Combine(fullDirectory, "preferences.json");
        }

        public IReadOnlyList<SessionRecord> LoadHistory()
        {
            using (var stream = OpenIfPresent(historyPath))
            {
                if (stream == null)
                    return null;
                if (stream.Length > HistoryTransfer.MaxFileBytes)
                    throw new InvalidDataException("Файл истории слишком большой.");

                using (var reader = new StreamReader(stream, Utf8))
                    return HistoryTransfer.Read(reader.ReadToEnd()).Decode(string.Empty);
            }
        }

        public void SaveHistory(IReadOnlyList<SessionRecord> records)
        {
            WriteText(historyPath, HistoryTransfer.ExportJson(records));
        }

        internal static void WriteText(string path, string content)
        {
            WriteAtomically(Path.GetFullPath(path), stream =>
            {
                var bytes = Utf8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            });
        }

        public Preferences LoadPreferences()
        {
            using (var stream = OpenIfPresent(preferencesPath))
            {
                if (stream == null)
                    return new Preferences();

                var serializer = new DataContractJsonSerializer(typeof(Preferences));
                var preferences = (Preferences)serializer.ReadObject(stream);
                ValidatePreferences(preferences);
                return preferences;
            }
        }

        public void SavePreferences(Preferences preferences)
        {
            ValidatePreferences(preferences);
            var serializer = new DataContractJsonSerializer(typeof(Preferences));
            WriteAtomically(preferencesPath, stream => serializer.WriteObject(stream, preferences));
        }

        private static void ValidatePreferences(Preferences preferences)
        {
            if (preferences == null)
                throw new InvalidDataException("Файл настроек не содержит параметров.");
            if (preferences.Theme != "system" && preferences.Theme != "light" && preferences.Theme != "dark")
                throw new InvalidDataException("Не удалось прочитать выбранную тему.");
            if (preferences.Mode != "all" && preferences.Mode != "except" && preferences.Mode != "only")
                throw new InvalidDataException("Не удалось прочитать режим области записи.");
            if (preferences.IdleSeconds < 1)
                throw new InvalidDataException("Время простоя должно быть целым числом секунд от 1.");
            if (preferences.ExceptPaths == null || preferences.OnlyPaths == null ||
                preferences.ExceptPaths.Exists(string.IsNullOrWhiteSpace) ||
                preferences.OnlyPaths.Exists(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Файл настроек содержит неверный список приложений.");
        }

        private static FileStream OpenIfPresent(string path)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }

        private static void WriteAtomically(string path, Action<Stream> write)
        {
            var directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            var ownsTemporaryFile = false;
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    ownsTemporaryFile = true;
                    write(stream);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                {
                    try
                    {
                        File.Move(temporaryPath, path);
                    }
                    catch (IOException) when (File.Exists(path))
                    {
                        File.Replace(temporaryPath, path, null);
                    }
                }
                ownsTemporaryFile = false;
            }
            finally
            {
                if (ownsTemporaryFile)
                    DeleteTemporaryFile(temporaryPath);
            }
        }

        private static void DeleteTemporaryFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Keep the original write error when a temporary file is locked.
            }
            catch (UnauthorizedAccessException)
            {
                // Keep the original write error when cleanup is denied.
            }
        }
    }
}
