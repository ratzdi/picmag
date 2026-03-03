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
public class ImagesTable
    {
        public class QualityImageEntry
        {
            public string Path { get; set; }
            public string QualityVerdict { get; set; }
            public string QualityReason { get; set; }
            public double? QualityContrast { get; set; }
            public double? QualitySharpness { get; set; }
            public double? QualityHighlights { get; set; }
            public double? QualityShadows { get; set; }
            public long? QualityAssessedAt { get; set; }
            public string QualityModelVersion { get; set; }
        }

        public static string TableName = "images";
        private SqliteConnection sqliteConnection;
        private ILog log;
        private const String tag = "DB";
        public ImagesTable(SqliteConnection connection, ILog log)
        {
            sqliteConnection = connection;
            this.log=log;
            EnsureQualityColumns();
        }
        public int Create()
        {
            using (var command = sqliteConnection.CreateCommand())
            {
                string createString = @"create table if not exists images(
                                path            TEXT        NOT NULL,
                                created         INTEGER     NOT NULL,
                                md5             TEXT        NOT NULL,
                                quality_verdict TEXT,
                                quality_reason TEXT,
                                quality_contrast REAL,
                                quality_sharpness REAL,
                                quality_highlights REAL,
                                quality_shadows REAL,
                                quality_assessed_at INTEGER,
                                quality_model_version TEXT
                                );";
                command.CommandText = createString;
                var created = command.ExecuteNonQuery();
                EnsureQualityColumns();
                return created;
            }
        }
        public void Insert(string path, DateTime created, string md5)
        {
            Insert(path, created, md5, null);
        }

        public void Insert(string path, DateTime created, string md5, QualityAssessmentResult qualityAssessment)
        {
            using (IDbCommand dbcmd = sqliteConnection.CreateCommand())
            {
                SqliteParameter param;
                var sql = "insert into images (path, created, md5, quality_verdict, quality_reason, quality_contrast, quality_sharpness, quality_highlights, quality_shadows, quality_assessed_at, quality_model_version) values (?,?,?,?,?,?,?,?,?,?,?);";
                dbcmd.CommandText = sql;
                param = new SqliteParameter("path", path);
                dbcmd.Parameters.Add(param);

                DateTimeOffset unix;
                unix = new DateTimeOffset(created);
                param = new SqliteParameter("created", unix.ToUnixTimeSeconds());
                dbcmd.Parameters.Add(param);
                param = new SqliteParameter("md5", md5);
                dbcmd.Parameters.Add(param);

                AddQualityParameters(dbcmd, qualityAssessment);
                if (dbcmd.ExecuteNonQuery() > 0)
                {
                    log.PrintDebug(tag, "Image inserted: " + path);
                }
            }
        }

        public List<QualityImageEntry> GetByQualityVerdict(QualityReviewVerdict verdict)
        {
            var result = new List<QualityImageEntry>();
            using (var dbcmd = sqliteConnection.CreateCommand())
            {
                dbcmd.CommandText = "select path, quality_verdict, quality_reason, quality_contrast, quality_sharpness, quality_highlights, quality_shadows, quality_assessed_at, quality_model_version from images where lower(quality_verdict) = ? order by path;";
                dbcmd.CommandType = CommandType.Text;
                dbcmd.Parameters.Add(new SqliteParameter("quality_verdict", verdict.ToString().ToLowerInvariant()));

                using (var reader = dbcmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(new QualityImageEntry
                        {
                            Path = reader["path"].ToString(),
                            QualityVerdict = reader["quality_verdict"].ToString(),
                            QualityReason = reader["quality_reason"] is DBNull ? null : reader["quality_reason"].ToString(),
                            QualityContrast = reader["quality_contrast"] is DBNull ? (double?)null : Convert.ToDouble(reader["quality_contrast"]),
                            QualitySharpness = reader["quality_sharpness"] is DBNull ? (double?)null : Convert.ToDouble(reader["quality_sharpness"]),
                            QualityHighlights = reader["quality_highlights"] is DBNull ? (double?)null : Convert.ToDouble(reader["quality_highlights"]),
                            QualityShadows = reader["quality_shadows"] is DBNull ? (double?)null : Convert.ToDouble(reader["quality_shadows"]),
                            QualityAssessedAt = reader["quality_assessed_at"] is DBNull ? (long?)null : Convert.ToInt64(reader["quality_assessed_at"]),
                            QualityModelVersion = reader["quality_model_version"] is DBNull ? null : reader["quality_model_version"].ToString()
                        });
                    }
                }
            }

            return result;
        }
        public void Update(string path, DateTime created, byte[] md5)
        {
            using (IDbCommand dbcmd = sqliteConnection.CreateCommand())
            {
                string sql;
                SqliteParameter param;
                sql = "update images set md5=?, created=? where path=?;";
                dbcmd.CommandText = sql;
                param = new SqliteParameter("md5", BitConverter.ToString(md5));
                dbcmd.Parameters.Add(param);
                DateTimeOffset unix;
                unix = new DateTimeOffset(created);
                param = new SqliteParameter("created", unix.ToUnixTimeSeconds());
                dbcmd.Parameters.Add(param);
                param = new SqliteParameter("path", path);
                dbcmd.Parameters.Add(param);
                if (dbcmd.ExecuteNonQuery() > 0)
                {
                    log.PrintDebug(tag, "Image updated: " + path);
                }
            }
        }
        public bool FindDuplicate(string imageFullPath5)
        {
            using (var dbcmd = sqliteConnection.CreateCommand())
            {
                dbcmd.CommandText = "select 1 from images where path = ? limit 1;";
                dbcmd.CommandType = CommandType.Text;
                dbcmd.Parameters.Add(new SqliteParameter("path", imageFullPath5));
                using (var reader = dbcmd.ExecuteReader())
                {
                    return reader.Read();
                }
            }
        }
        public int FindDuplicates()
        {
            using (var dbcmd = sqliteConnection.CreateCommand())
            {
                dbcmd.CommandText = "select md5, count(*) as duplicate_count from images group by md5 having count(*) > 1;";
                dbcmd.CommandType = CommandType.Text;

                int duplicateEntries = 0;
                using (var reader = dbcmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var md5 = reader["md5"].ToString();
                        var duplicateCount = Convert.ToInt32(reader["duplicate_count"]);
                        duplicateEntries += duplicateCount;
                        log.PrintDebug(tag, "Duplicate group found for md5 {0}: {1} files", md5, duplicateCount);
                    }
                }

                return duplicateEntries;
            }
        }

        public bool ImageExists(string path, string md5)
        {
            using (var dbcmd = sqliteConnection.CreateCommand())
            {
                dbcmd.CommandText = "select 1 from images where md5 = ? AND path = ? limit 1;";
                dbcmd.CommandType = CommandType.Text;
                dbcmd.Parameters.Add(new SqliteParameter("md5", md5));
                dbcmd.Parameters.Add(new SqliteParameter("path", path));
                using (var reader = dbcmd.ExecuteReader())
                {
                    return reader.Read();
                }
            }
        }

        public bool ImageExists(string path)
        {
            using (var dbcmd = sqliteConnection.CreateCommand())
            {
                dbcmd.CommandText = "select 1 from images where path = ? limit 1;";
                dbcmd.CommandType = CommandType.Text;
                dbcmd.Parameters.Add(new SqliteParameter("path", path));
                using (var reader = dbcmd.ExecuteReader())
                {
                    return reader.Read();
                }
            }
        }

        public List<string> GetAllPaths()
        {
            var result = new List<string>();
            using (var dbcmd = sqliteConnection.CreateCommand())
            {
                dbcmd.CommandText = "select path from images;";
                dbcmd.CommandType = CommandType.Text;
                using (var reader = dbcmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(reader["path"].ToString());
                    }
                }
            }
            return result;
        }

        public int RemoveByPath(string path)
        {
            using (IDbCommand dbcmd = sqliteConnection.CreateCommand())
            {
                var sql = "delete from images where path = ?;";
                dbcmd.CommandText = sql;
                var param = new SqliteParameter("path", path);
                dbcmd.Parameters.Add(param);
                return dbcmd.ExecuteNonQuery();
            }
        }

        private void EnsureQualityColumns()
        {
            if (!TableExists())
                return;

            var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var dbcmd = sqliteConnection.CreateCommand())
            {
                dbcmd.CommandText = "pragma table_info(images);";
                using (var reader = dbcmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        existingColumns.Add(reader["name"].ToString());
                    }
                }
            }

            AddColumnIfMissing(existingColumns, "quality_verdict", "TEXT");
            AddColumnIfMissing(existingColumns, "quality_reason", "TEXT");
            AddColumnIfMissing(existingColumns, "quality_contrast", "REAL");
            AddColumnIfMissing(existingColumns, "quality_sharpness", "REAL");
            AddColumnIfMissing(existingColumns, "quality_highlights", "REAL");
            AddColumnIfMissing(existingColumns, "quality_shadows", "REAL");
            AddColumnIfMissing(existingColumns, "quality_assessed_at", "INTEGER");
            AddColumnIfMissing(existingColumns, "quality_model_version", "TEXT");

            using (var dbcmd = sqliteConnection.CreateCommand())
            {
                dbcmd.CommandText = "create index if not exists idx_images_quality_verdict on images(quality_verdict);";
                dbcmd.ExecuteNonQuery();
            }
        }

        private bool TableExists()
        {
            using (var dbcmd = sqliteConnection.CreateCommand())
            {
                dbcmd.CommandText = "select 1 from sqlite_master where type = 'table' and name = 'images' limit 1;";
                using (var reader = dbcmd.ExecuteReader())
                {
                    return reader.Read();
                }
            }
        }

        private void AddColumnIfMissing(HashSet<string> existingColumns, string columnName, string columnType)
        {
            if (existingColumns.Contains(columnName))
                return;

            using (var dbcmd = sqliteConnection.CreateCommand())
            {
                dbcmd.CommandText = $"alter table images add column {columnName} {columnType};";
                dbcmd.ExecuteNonQuery();
            }
            existingColumns.Add(columnName);
        }

        private void AddQualityParameters(IDbCommand dbcmd, QualityAssessmentResult qualityAssessment)
        {
            if (qualityAssessment == null)
            {
                dbcmd.Parameters.Add(new SqliteParameter("quality_verdict", DBNull.Value));
                dbcmd.Parameters.Add(new SqliteParameter("quality_reason", DBNull.Value));
                dbcmd.Parameters.Add(new SqliteParameter("quality_contrast", DBNull.Value));
                dbcmd.Parameters.Add(new SqliteParameter("quality_sharpness", DBNull.Value));
                dbcmd.Parameters.Add(new SqliteParameter("quality_highlights", DBNull.Value));
                dbcmd.Parameters.Add(new SqliteParameter("quality_shadows", DBNull.Value));
                dbcmd.Parameters.Add(new SqliteParameter("quality_assessed_at", DBNull.Value));
                dbcmd.Parameters.Add(new SqliteParameter("quality_model_version", DBNull.Value));
                return;
            }

            dbcmd.Parameters.Add(new SqliteParameter("quality_verdict", qualityAssessment.Verdict.ToString().ToLowerInvariant()));
            dbcmd.Parameters.Add(new SqliteParameter("quality_reason", string.IsNullOrWhiteSpace(qualityAssessment.Reason) ? "none" : qualityAssessment.Reason));
            dbcmd.Parameters.Add(new SqliteParameter("quality_contrast", qualityAssessment.Contrast));
            dbcmd.Parameters.Add(new SqliteParameter("quality_sharpness", qualityAssessment.Sharpness));
            dbcmd.Parameters.Add(new SqliteParameter("quality_highlights", qualityAssessment.ClippedHighlightsRatio));
            dbcmd.Parameters.Add(new SqliteParameter("quality_shadows", qualityAssessment.ClippedShadowsRatio));
            dbcmd.Parameters.Add(new SqliteParameter("quality_assessed_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            dbcmd.Parameters.Add(new SqliteParameter("quality_model_version", "quality-v1"));
        }
    }
}