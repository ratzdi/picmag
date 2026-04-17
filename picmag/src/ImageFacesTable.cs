using System;
using System.Collections.Generic;
using System.Data;
using Mono.Data.Sqlite;

namespace picmag
{
    public class ImageFacesTable
    {
        public class UnlabeledFaceEntry
        {
            public long FaceId { get; set; }
            public string ImagePath { get; set; } = string.Empty;
            public int FaceIndex { get; set; }
            public double DetectionConfidence { get; set; }
            public string EmbeddingModel { get; set; } = string.Empty;
        }

        private readonly SqliteConnection sqliteConnection;

        public ImageFacesTable(SqliteConnection connection)
        {
            sqliteConnection = connection;
            EnsureTables();
        }

        private void EnsureTables()
        {
            using (var cmd = sqliteConnection.CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = @"create table if not exists image_faces(
                                    id integer primary key autoincrement,
                                    image_path text not null,
                                    face_index integer not null,
                                    bbox_x real not null,
                                    bbox_y real not null,
                                    bbox_width real not null,
                                    bbox_height real not null,
                                    detection_confidence real not null,
                                    embedding blob not null,
                                    embedding_model text not null,
                                    detected_at integer not null,
                                    unique(image_path, face_index, embedding_model)
                                  );";
                cmd.ExecuteNonQuery();
            }

            using (var cmd = sqliteConnection.CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = @"create table if not exists image_face_labels(
                                    id integer primary key autoincrement,
                                    image_face_id integer not null unique,
                                    person_id integer,
                                    status text not null,
                                    labeled_at integer not null,
                                    foreign key(image_face_id) references image_faces(id),
                                    foreign key(person_id) references persons(id)
                                  );";
                cmd.ExecuteNonQuery();
            }
        }

        public List<string> GetJpegPathsForFaceScan(bool onlyMissingMetadata)
        {
            var result = new List<string>();
            using var cmd = sqliteConnection.CreateCommand();
            cmd.CommandType = CommandType.Text;

            if (onlyMissingMetadata)
            {
                cmd.CommandText = @"select i.path
                                    from images i
                                    where (lower(i.path) like '%.jpg' or lower(i.path) like '%.jpeg')
                                      and not exists (
                                          select 1 from image_faces f
                                          where f.image_path = i.path
                                      )
                                    order by i.path;";
            }
            else
            {
                cmd.CommandText = @"select i.path
                                    from images i
                                    where (lower(i.path) like '%.jpg' or lower(i.path) like '%.jpeg')
                                    order by i.path;";
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(reader["path"].ToString() ?? string.Empty);
            }

            return result;
        }

        public void UpsertFace(string imagePath, FaceDetectionResult face)
        {
            using var cmd = sqliteConnection.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = @"insert into image_faces(image_path, face_index, bbox_x, bbox_y, bbox_width, bbox_height, detection_confidence, embedding, embedding_model, detected_at)
                                values(?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                                on conflict(image_path, face_index, embedding_model)
                                do update set
                                    bbox_x = excluded.bbox_x,
                                    bbox_y = excluded.bbox_y,
                                    bbox_width = excluded.bbox_width,
                                    bbox_height = excluded.bbox_height,
                                    detection_confidence = excluded.detection_confidence,
                                    embedding = excluded.embedding,
                                    detected_at = excluded.detected_at;";
            cmd.Parameters.Add(new SqliteParameter("image_path", imagePath));
            cmd.Parameters.Add(new SqliteParameter("face_index", face.FaceIndex));
            cmd.Parameters.Add(new SqliteParameter("bbox_x", face.BBoxX));
            cmd.Parameters.Add(new SqliteParameter("bbox_y", face.BBoxY));
            cmd.Parameters.Add(new SqliteParameter("bbox_width", face.BBoxWidth));
            cmd.Parameters.Add(new SqliteParameter("bbox_height", face.BBoxHeight));
            cmd.Parameters.Add(new SqliteParameter("detection_confidence", face.DetectionConfidence));
            cmd.Parameters.Add(new SqliteParameter("embedding", face.Embedding));
            cmd.Parameters.Add(new SqliteParameter("embedding_model", face.EmbeddingModel));
            cmd.Parameters.Add(new SqliteParameter("detected_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            cmd.ExecuteNonQuery();
        }

        public List<UnlabeledFaceEntry> GetUnlabeledFaces(int limit)
        {
            var result = new List<UnlabeledFaceEntry>();
            using var cmd = sqliteConnection.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = @"select f.id, f.image_path, f.face_index, f.detection_confidence, f.embedding_model
                                from image_faces f
                                left join image_face_labels l on l.image_face_id = f.id
                                where l.id is null
                                order by f.image_path, f.face_index
                                limit ?;";
            cmd.Parameters.Add(new SqliteParameter("limit", Math.Max(limit, 1)));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new UnlabeledFaceEntry
                {
                    FaceId = Convert.ToInt64(reader["id"]),
                    ImagePath = reader["image_path"].ToString() ?? string.Empty,
                    FaceIndex = Convert.ToInt32(reader["face_index"]),
                    DetectionConfidence = Convert.ToDouble(reader["detection_confidence"]),
                    EmbeddingModel = reader["embedding_model"].ToString() ?? string.Empty
                });
            }

            return result;
        }

        public bool FaceExists(long faceId)
        {
            using var cmd = sqliteConnection.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = "select 1 from image_faces where id = ? limit 1;";
            cmd.Parameters.Add(new SqliteParameter("id", faceId));
            using var reader = cmd.ExecuteReader();
            return reader.Read();
        }

        public int UpsertLabel(long faceId, long? personId, FaceLabelStatus status)
        {
            using var cmd = sqliteConnection.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = @"insert into image_face_labels(image_face_id, person_id, status, labeled_at)
                                values(?, ?, ?, ?)
                                on conflict(image_face_id)
                                do update set
                                    person_id = excluded.person_id,
                                    status = excluded.status,
                                    labeled_at = excluded.labeled_at;";
            cmd.Parameters.Add(new SqliteParameter("image_face_id", faceId));
            if (personId.HasValue)
                cmd.Parameters.Add(new SqliteParameter("person_id", personId.Value));
            else
                cmd.Parameters.Add(new SqliteParameter("person_id", DBNull.Value));
            cmd.Parameters.Add(new SqliteParameter("status", status.ToString().ToLowerInvariant()));
            cmd.Parameters.Add(new SqliteParameter("labeled_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            return cmd.ExecuteNonQuery();
        }

        public List<string> GetConfirmedImagePathsByPerson(string personName)
        {
            var result = new List<string>();
            using var cmd = sqliteConnection.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = @"select distinct f.image_path
                                from image_face_labels l
                                join image_faces f on f.id = l.image_face_id
                                join persons p on p.id = l.person_id
                                where l.status = 'confirmed' and lower(p.name) = lower(?)
                                order by f.image_path;";
            cmd.Parameters.Add(new SqliteParameter("name", personName));
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(reader["image_path"].ToString() ?? string.Empty);
            }

            return result;
        }
    }
}
