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
using System.Text;
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
            var command = sqliteConnection.CreateCommand();
            string createString = @"create table if not exists images(
                                path            TEXT        NOT NULL,
                                created         INTEGER     NOT NULL,
                                md5             TEXT        NOT NULL
                                );";
            command.CommandText = createString;
            return command.ExecuteNonQuery();
        }
        public void Insert(string path, DateTime created, string md5)
        {
            IDbCommand dbcmd = sqliteConnection.CreateCommand();
            // todo: try insert or ignore into images ...
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
        public void Update(string path, DateTime created, byte[] md5)
        {
            IDbCommand dbcmd = sqliteConnection.CreateCommand();
            // todo: try insert or ignore into images ...
            string sql;
            SqliteParameter param;
            sql = "update images set md5='?', created=? where path='?';";
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
        public bool FindDuplicate(string imageFullPath5)
        {
            var dbcmd = sqliteConnection.CreateCommand();
            var sql = new StringBuilder();
            sql.Append("select * from images where path == " + imageFullPath5 + ";");
            dbcmd.CommandText = sql.ToString();
            dbcmd.CommandType = CommandType.Text;
            var reader = dbcmd.ExecuteReader();

            if (reader.Read())
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        public int FindDuplicates()
        {
            var dbcmd = sqliteConnection.CreateCommand();
            var rows = new Dictionary<string, string>();
            dbcmd.CommandText = "select * from images;";
            dbcmd.CommandType = CommandType.Text;
            var reader = dbcmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(reader["path"].ToString(), reader["md5"].ToString());
            }
            var duplicateList = new List<string>();
            foreach (var row in rows)
            {
                foreach (var tmp in rows)
                {
                    if (row.Key != tmp.Key && !duplicateList.Contains(row.Key))
                    {
                        if (row.Value == tmp.Value)
                        {
                            log.PrintDebug(tag, "Duplicate found for " + row.Key + " in " + tmp.Key);
                            duplicateList.Add(tmp.Key);
                        }
                    }
                }
            }
            return duplicateList.Count;
        }

        public bool ImageExists(string path, string md5)
        {
            bool result = false;
            var dbcmd = sqliteConnection.CreateCommand();
            dbcmd.CommandText = "select * from images where md5 = '" + md5 + "' AND path ='" + path + "';";
            dbcmd.CommandType = CommandType.Text;
            var reader = dbcmd.ExecuteReader();
            if (reader.Read())
            {
                result = true;
            }
            return result;
        }

        public bool ImageExists(string path)
        {
            bool result = false;
            var dbcmd = sqliteConnection.CreateCommand();
            dbcmd.CommandText = "select * from images where path = '" + path + "';";
            dbcmd.CommandType = CommandType.Text;
            var reader = dbcmd.ExecuteReader();
            if (reader.Read())
            {
                result = true;
            }
            return result;
        }
    }
}