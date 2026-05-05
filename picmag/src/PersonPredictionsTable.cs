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
    public enum PredictionStatus
    {
        Suggested,
        Confirmed,
        Rejected
    }

    public class PersonPredictionsTable
    {
        public class PersonPredictionEntry
        {
            public long PredictionId { get; set; }
            public long FaceId { get; set; }
            public string ImagePath { get; set; } = string.Empty;
            public long PersonId { get; set; }
            public string PersonName { get; set; } = string.Empty;
            public double ConfidenceScore { get; set; }
            public string Status { get; set; } = string.Empty;
            public long CreatedAt { get; set; }
        }

        private readonly SqliteConnection sqliteConnection;

        public PersonPredictionsTable(SqliteConnection connection)
        {
            sqliteConnection = connection;
            EnsureTable();
        }

        private void EnsureTable()
        {
            using var cmd = sqliteConnection.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = @"create table if not exists person_predictions(
                                id integer primary key autoincrement,
                                face_id integer not null unique,
                                person_id integer not null,
                                confidence_score real not null,
                                status text not null,
                                created_at integer not null,
                                foreign key(face_id) references image_faces(id),
                                foreign key(person_id) references persons(id)
                               );";
            cmd.ExecuteNonQuery();

            using var indexCmd = sqliteConnection.CreateCommand();
            indexCmd.CommandType = CommandType.Text;
            indexCmd.CommandText = "create index if not exists idx_person_predictions_status on person_predictions(status);";
            indexCmd.ExecuteNonQuery();
        }

        public void ClearSuggested()
        {
            using var cmd = sqliteConnection.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = "delete from person_predictions where status = 'suggested';";
            cmd.ExecuteNonQuery();
        }

        public int UpsertPrediction(long faceId, long personId, double confidenceScore, PredictionStatus status)
        {
            if (faceId <= 0)
                throw new ArgumentOutOfRangeException(nameof(faceId));
            if (personId <= 0)
                throw new ArgumentOutOfRangeException(nameof(personId));
            if (confidenceScore < 0d || confidenceScore > 1d)
                throw new ArgumentOutOfRangeException(nameof(confidenceScore), "Must be between 0 and 1");

            using var cmd = sqliteConnection.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = @"insert into person_predictions(face_id, person_id, confidence_score, status, created_at)
                                values(?, ?, ?, ?, ?)
                                on conflict(face_id)
                                do update set
                                    person_id = excluded.person_id,
                                    confidence_score = excluded.confidence_score,
                                    status = excluded.status,
                                    created_at = excluded.created_at;";
            cmd.Parameters.Add(new SqliteParameter("face_id", faceId));
            cmd.Parameters.Add(new SqliteParameter("person_id", personId));
            cmd.Parameters.Add(new SqliteParameter("confidence_score", confidenceScore));
            cmd.Parameters.Add(new SqliteParameter("status", status.ToString().ToLowerInvariant()));
            cmd.Parameters.Add(new SqliteParameter("created_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            return cmd.ExecuteNonQuery();
        }

        public List<PersonPredictionEntry> GetSuggestedPredictions(int limit = 50)
        {
            var result = new List<PersonPredictionEntry>();
            using var cmd = sqliteConnection.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = @"select pp.id, pp.face_id, f.image_path, pp.person_id, p.name, pp.confidence_score, pp.status, pp.created_at
                                from person_predictions pp
                                join image_faces f on f.id = pp.face_id
                                join persons p on p.id = pp.person_id
                                where pp.status = 'suggested'
                                order by pp.confidence_score desc, f.image_path, f.face_index
                                limit ?;";
            cmd.Parameters.Add(new SqliteParameter("limit", Math.Max(limit, 1)));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new PersonPredictionEntry
                {
                    PredictionId = Convert.ToInt64(reader["id"]),
                    FaceId = Convert.ToInt64(reader["face_id"]),
                    ImagePath = reader["image_path"].ToString() ?? string.Empty,
                    PersonId = Convert.ToInt64(reader["person_id"]),
                    PersonName = reader["name"].ToString() ?? string.Empty,
                    ConfidenceScore = Convert.ToDouble(reader["confidence_score"]),
                    Status = reader["status"].ToString() ?? string.Empty,
                    CreatedAt = Convert.ToInt64(reader["created_at"])
                });
            }

            return result;
        }
    }
}
