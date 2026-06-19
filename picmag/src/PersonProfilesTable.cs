// MIT License
//
// Copyright (c) 2025 Dimitri Ratz
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Data;
using Mono.Data.Sqlite;

namespace picmag
{
    public class PersonProfilesTable
    {
        public class PersonProfileEntry
        {
            public long PersonId { get; set; }
            public string PersonName { get; set; } = string.Empty;
            public string EmbeddingModel { get; set; } = string.Empty;
            public int SampleCount { get; set; }
            public long UpdatedAt { get; set; }
        }

        private readonly SqliteConnection sqliteConnection;

        public PersonProfilesTable(SqliteConnection connection)
        {
            sqliteConnection = connection;
            EnsureTable();
        }

        private void EnsureTable()
        {
            using var cmd = sqliteConnection.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = @"create table if not exists person_profiles(
                                id integer primary key autoincrement,
                                person_id integer not null,
                                embedding_model text not null,
                                embedding blob not null,
                                sample_count integer not null,
                                updated_at integer not null,
                                unique(person_id, embedding_model),
                                foreign key(person_id) references persons(id)
                               );";
            cmd.ExecuteNonQuery();

            using var indexCmd = sqliteConnection.CreateCommand();
            indexCmd.CommandType = CommandType.Text;
            indexCmd.CommandText = "create index if not exists idx_person_profiles_person on person_profiles(person_id);";
            indexCmd.ExecuteNonQuery();
        }

        public void ClearAll()
        {
            using var cmd = sqliteConnection.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = "delete from person_profiles;";
            cmd.ExecuteNonQuery();
        }

        public int UpsertProfile(long personId, string embeddingModel, byte[] embedding, int sampleCount)
        {
            if (personId <= 0)
                throw new ArgumentOutOfRangeException(nameof(personId));
            if (string.IsNullOrWhiteSpace(embeddingModel))
                throw new ArgumentException("Embedding model is required", nameof(embeddingModel));
            if (embedding == null || embedding.Length == 0)
                throw new ArgumentException("Embedding must not be empty", nameof(embedding));

            using var cmd = sqliteConnection.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = @"insert into person_profiles(person_id, embedding_model, embedding, sample_count, updated_at)
                                values(?, ?, ?, ?, ?)
                                on conflict(person_id, embedding_model)
                                do update set
                                    embedding = excluded.embedding,
                                    sample_count = excluded.sample_count,
                                    updated_at = excluded.updated_at;";
            cmd.Parameters.Add(new SqliteParameter("person_id", personId));
            cmd.Parameters.Add(new SqliteParameter("embedding_model", embeddingModel));
            cmd.Parameters.Add(new SqliteParameter("embedding", embedding));
            cmd.Parameters.Add(new SqliteParameter("sample_count", Math.Max(1, sampleCount)));
            cmd.Parameters.Add(new SqliteParameter("updated_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            return cmd.ExecuteNonQuery();
        }

        public List<PersonProfileEntry> GetAllMetadata()
        {
            var result = new List<PersonProfileEntry>();
            using var cmd = sqliteConnection.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = @"select pp.person_id, p.name, pp.embedding_model, pp.sample_count, pp.updated_at
                                from person_profiles pp
                                join persons p on p.id = pp.person_id
                                order by lower(p.name), pp.embedding_model;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new PersonProfileEntry
                {
                    PersonId = Convert.ToInt64(reader["person_id"]),
                    PersonName = reader["name"].ToString() ?? string.Empty,
                    EmbeddingModel = reader["embedding_model"].ToString() ?? string.Empty,
                    SampleCount = Convert.ToInt32(reader["sample_count"]),
                    UpdatedAt = Convert.ToInt64(reader["updated_at"])
                });
            }

            return result;
        }

        public class PersonProfileEmbeddingEntry
        {
            public long PersonId { get; set; }
            public string EmbeddingModel { get; set; } = string.Empty;
            public byte[] Embedding { get; set; } = Array.Empty<byte>();
        }

        public List<PersonProfileEmbeddingEntry> GetAllEmbeddings()
        {
            var result = new List<PersonProfileEmbeddingEntry>();
            using var cmd = sqliteConnection.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = @"select person_id, embedding_model, embedding
                                from person_profiles
                                order by person_id, embedding_model;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new PersonProfileEmbeddingEntry
                {
                    PersonId = Convert.ToInt64(reader["person_id"]),
                    EmbeddingModel = reader["embedding_model"].ToString() ?? string.Empty,
                    Embedding = reader["embedding"] as byte[] ?? Array.Empty<byte>()
                });
            }

            return result;
        }
    }
}
