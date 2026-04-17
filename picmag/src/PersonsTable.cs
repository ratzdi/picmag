using System;
using System.Collections.Generic;
using System.Data;
using Mono.Data.Sqlite;

namespace picmag
{
    public class PersonsTable
    {
        public class PersonEntry
        {
            public long Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        private readonly SqliteConnection sqliteConnection;

        public PersonsTable(SqliteConnection connection)
        {
            sqliteConnection = connection;
            EnsureTable();
        }

        private void EnsureTable()
        {
            using var cmd = sqliteConnection.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = @"create table if not exists persons(
                                id integer primary key autoincrement,
                                name text not null unique,
                                created_at integer not null
                               );";
            cmd.ExecuteNonQuery();
        }

        public long AddOrGetId(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Person name is required", nameof(name));

            var normalized = name.Trim();

            using (var checkCmd = sqliteConnection.CreateCommand())
            {
                checkCmd.CommandType = CommandType.Text;
                checkCmd.CommandText = "select id from persons where lower(name) = lower(?) limit 1;";
                checkCmd.Parameters.Add(new SqliteParameter("name", normalized));
                var existing = checkCmd.ExecuteScalar();
                if (existing != null && existing != DBNull.Value)
                {
                    return Convert.ToInt64(existing);
                }
            }

            using var insertCmd = sqliteConnection.CreateCommand();
            insertCmd.CommandType = CommandType.Text;
            insertCmd.CommandText = "insert into persons(name, created_at) values(?, ?);";
            insertCmd.Parameters.Add(new SqliteParameter("name", normalized));
            insertCmd.Parameters.Add(new SqliteParameter("created_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            insertCmd.ExecuteNonQuery();

            using var idCmd = sqliteConnection.CreateCommand();
            idCmd.CommandType = CommandType.Text;
            idCmd.CommandText = "select id from persons where lower(name) = lower(?) limit 1;";
            idCmd.Parameters.Add(new SqliteParameter("name", normalized));
            var inserted = idCmd.ExecuteScalar();
            return Convert.ToInt64(inserted);
        }

        public List<PersonEntry> GetAll()
        {
            var result = new List<PersonEntry>();
            using var cmd = sqliteConnection.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = "select id, name from persons order by lower(name);";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new PersonEntry
                {
                    Id = Convert.ToInt64(reader["id"]),
                    Name = reader["name"].ToString() ?? string.Empty
                });
            }

            return result;
        }
    }
}
