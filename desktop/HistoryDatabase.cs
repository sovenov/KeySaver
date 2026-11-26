using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace KeySaver
{
    internal sealed class AppSummary
    {
        public string App { get; set; }
        public int Count { get; set; }
        public string Path { get; set; }
    }

    internal sealed class HistoryDatabase : IDisposable
    {
        private static readonly byte[] AssociatedData = Encoding.ASCII.GetBytes("keysaver-row:1");
        private readonly SqliteConnection connection;
        private byte[] key;
        private SqliteTransaction transaction;

        public HistoryDatabase(string path, byte[] key)
        {
            SQLitePCL.Batteries_V2.Init();
            this.key = key;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            connection.Open();
            Execute("PRAGMA journal_mode=WAL;");
            Execute(@"CREATE TABLE IF NOT EXISTS sessions (
                id INTEGER PRIMARY KEY,
                app TEXT NOT NULL,
                started_at INTEGER NOT NULL,
                ended_at INTEGER NOT NULL,
                content_hash BLOB NOT NULL UNIQUE,
                nonce BLOB NOT NULL,
                content BLOB NOT NULL)");
            Execute("CREATE INDEX IF NOT EXISTS ix_sessions_started ON sessions(started_at);");
            Execute("CREATE INDEX IF NOT EXISTS ix_sessions_app ON sessions(app);");
        }

        public int Count()
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM sessions";
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public IReadOnlyList<AppSummary> Apps()
        {
            var result = new List<AppSummary>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT s.app, c.cnt, s.nonce, s.content
                    FROM sessions s
                    JOIN (SELECT app, COUNT(*) AS cnt, MAX(id) AS id FROM sessions GROUP BY app) c ON s.id = c.id";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string text;
                        string path;
                        Unpack(Open((byte[])reader["nonce"], (byte[])reader["content"]), out text, out path);
                        result.Add(new AppSummary { App = reader.GetString(0), Count = reader.GetInt32(1), Path = path });
                    }
                }
            }
            return result;
        }

        public IReadOnlyList<SessionRecord> Query(string app, DateTime? from, DateTime? to, string text)
        {
            var result = new List<SessionRecord>();
            using (var command = connection.CreateCommand())
            {
                var sql = new StringBuilder("SELECT id, app, started_at, ended_at, nonce, content FROM sessions WHERE 1 = 1");
                if (!string.IsNullOrEmpty(app) && app != "all")
                {
                    sql.Append(" AND app = @app COLLATE NOCASE");
                    command.Parameters.AddWithValue("@app", app);
                }
                if (from.HasValue)
                {
                    sql.Append(" AND started_at >= @from");
                    command.Parameters.AddWithValue("@from", ToUnix(from.Value.Date));
                }
                if (to.HasValue)
                {
                    sql.Append(" AND started_at < @to");
                    command.Parameters.AddWithValue("@to", ToUnix(to.Value.Date.AddDays(1)));
                }
                command.CommandText = sql.ToString();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var record = ReadRecord(reader);
                        if (text.Length == 0 || (record.Text != null && record.Text.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0))
                            result.Add(record);
                    }
                }
            }
            return result;
        }

        public IReadOnlyList<SessionRecord> All()
        {
            return Query("all", null, null, string.Empty);
        }

        public bool Add(SessionRecord record)
        {
            var hash = Fingerprint(record);
            var plain = Pack(record.Text, record.Path);
            byte[] nonce;
            var content = HistoryTransfer.Seal(key, plain, AssociatedData, out nonce);
            Array.Clear(plain, 0, plain.Length);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"INSERT OR IGNORE INTO sessions(app, started_at, ended_at, content_hash, nonce, content)
                    VALUES (@app, @started, @ended, @hash, @nonce, @content)";
                command.Parameters.AddWithValue("@app", record.App);
                command.Parameters.AddWithValue("@started", ToUnix(record.StartedAt));
                command.Parameters.AddWithValue("@ended", ToUnix(record.EndedAt));
                command.Parameters.AddWithValue("@hash", hash);
                command.Parameters.AddWithValue("@nonce", nonce);
                command.Parameters.AddWithValue("@content", content);
                command.Transaction = transaction;
                return command.ExecuteNonQuery() > 0;
            }
        }

        public int Import(IReadOnlyList<SessionRecord> records)
        {
            int added = 0;
            transaction = connection.BeginTransaction();
            try
            {
                foreach (var record in records)
                    if (Add(record)) added++;
                transaction.Commit();
                return added;
            }
            finally
            {
                transaction.Dispose();
                transaction = null;
            }
        }

        public void Delete(IEnumerable<long> ids)
        {
            using (var transaction = connection.BeginTransaction())
            {
                foreach (var id in ids)
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = "DELETE FROM sessions WHERE id = @id";
                        command.Parameters.AddWithValue("@id", id);
                        command.ExecuteNonQuery();
                    }
                }
                transaction.Commit();
            }
        }

        public void Rekey(byte[] nextKey)
        {
            using (var transaction = connection.BeginTransaction())
            using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT id, nonce, content FROM sessions";
                var rows = new List<Tuple<long, byte[], byte[]>>();
                using (var reader = read.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var plain = Open((byte[])reader["nonce"], (byte[])reader["content"]);
                        byte[] nonce;
                        var content = HistoryTransfer.Seal(nextKey, plain, AssociatedData, out nonce);
                        Array.Clear(plain, 0, plain.Length);
                        rows.Add(Tuple.Create(reader.GetInt64(0), nonce, content));
                    }
                }
                foreach (var row in rows)
                {
                    using (var update = connection.CreateCommand())
                    {
                        update.Transaction = transaction;
                        update.CommandText = "UPDATE sessions SET nonce = @nonce, content = @content WHERE id = @id";
                        update.Parameters.AddWithValue("@nonce", row.Item2);
                        update.Parameters.AddWithValue("@content", row.Item3);
                        update.Parameters.AddWithValue("@id", row.Item1);
                        update.ExecuteNonQuery();
                    }
                }
                transaction.Commit();
            }
            var replacement = nextKey;
            key = replacement;
        }

        public void Dispose()
        {
            connection.Dispose();
        }

        private SessionRecord ReadRecord(SqliteDataReader reader)
        {
            string text;
            string path;
            Unpack(Open((byte[])reader["nonce"], (byte[])reader["content"]), out text, out path);
            return new SessionRecord(reader.GetString(1), FromUnix(reader.GetInt64(2)), FromUnix(reader.GetInt64(3)), text, path, reader.GetInt64(0));
        }

        private byte[] Open(byte[] nonce, byte[] content)
        {
            return HistoryTransfer.Open(key, nonce, content, AssociatedData);
        }

        private void Execute(string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        private static byte[] Fingerprint(SessionRecord record)
        {
            var text = Encoding.UTF8.GetBytes(record.Text ?? string.Empty);
            var app = Encoding.UTF8.GetBytes(record.App ?? string.Empty);
            var data = new byte[app.Length + 16 + text.Length];
            Buffer.BlockCopy(app, 0, data, 0, app.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(ToUnix(record.StartedAt)), 0, data, app.Length, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(ToUnix(record.EndedAt)), 0, data, app.Length + 8, 8);
            Buffer.BlockCopy(text, 0, data, app.Length + 16, text.Length);
            using (var sha = SHA256.Create())
                return sha.ComputeHash(data);
        }

        private static byte[] Pack(string text, string path)
        {
            var textBytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            var pathBytes = Encoding.UTF8.GetBytes(path ?? string.Empty);
            var result = new byte[4 + pathBytes.Length + textBytes.Length];
            Buffer.BlockCopy(BitConverter.GetBytes(pathBytes.Length), 0, result, 0, 4);
            Buffer.BlockCopy(pathBytes, 0, result, 4, pathBytes.Length);
            Buffer.BlockCopy(textBytes, 0, result, 4 + pathBytes.Length, textBytes.Length);
            return result;
        }

        private static void Unpack(byte[] plain, out string text, out string path)
        {
            if (plain == null || plain.Length < 4) throw new InvalidDataException("Файл истории повреждён.");
            int pathLength = BitConverter.ToInt32(plain, 0);
            if (pathLength < 0 || 4 + pathLength > plain.Length) throw new InvalidDataException("Файл истории повреждён.");
            path = pathLength == 0 ? null : Encoding.UTF8.GetString(plain, 4, pathLength);
            text = Encoding.UTF8.GetString(plain, 4 + pathLength, plain.Length - 4 - pathLength);
            Array.Clear(plain, 0, plain.Length);
        }

        private static long ToUnix(DateTimeOffset value)
        {
            return value.ToUniversalTime().ToUnixTimeMilliseconds();
        }

        private static long ToUnix(DateTime localDate)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(localDate, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        }

        private static DateTimeOffset FromUnix(long value)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(value);
        }
    }
}
