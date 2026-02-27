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
        public static string TableName = "images";
        private SqliteConnection sqliteConnection;
        private ILog log;
        private const String tag = "DB";
        public ImagesTable(SqliteConnection connection, ILog log)
        {
            sqliteConnection = connection;
            this.log=log;
        }
        public int Create()
        {
            using (var command = sqliteConnection.CreateCommand())
            {
                string createString = @"create table if not exists images(
                                path            TEXT        NOT NULL,
                                created         INTEGER     NOT NULL,
                                md5             TEXT        NOT NULL
                                );";
                command.CommandText = createString;
                return command.ExecuteNonQuery();
            }
        }
        public void Insert(string path, DateTime created, string md5)
        {
            using (IDbCommand dbcmd = sqliteConnection.CreateCommand())
            {
                string sql;
                SqliteParameter param;
                sql = "insert into images (path, created, md5) values (?,?,?);";
                dbcmd.CommandText = sql;
                param = new SqliteParameter("path", path);
                dbcmd.Parameters.Add(param);

                DateTimeOffset unix;
                unix = new DateTimeOffset(created);
                param = new SqliteParameter("created", unix.ToUnixTimeSeconds());
                dbcmd.Parameters.Add(param);
                param = new SqliteParameter("md5", md5);
                dbcmd.Parameters.Add(param);
                if (dbcmd.ExecuteNonQuery() > 0)
                {
                    log.PrintDebug(tag, "Image inserted: " + path);
                }
            }
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
    }
}