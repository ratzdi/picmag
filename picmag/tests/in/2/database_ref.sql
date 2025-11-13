PRAGMA foreign_keys=OFF;
BEGIN TRANSACTION;
CREATE TABLE images(
                                path            TEXT        NOT NULL,
                                created         INTEGER     NOT NULL,
                                md5             TEXT        NOT NULL
                                );
COMMIT;
