#!/bin/bash

set -ex

# Integration Test 1
rm -rf ./out/1
../bin/Debug/netcoreapp8.0/picmag -i ./in/1 ./out/1
sqlite3 ./out/1/.picmag/database.sqlite .dump > ./out/1/.picmag/database.sql
diff ./in/1/database_ref.sql ./out/1/.picmag/database.sql

# Ingegration Test 2

rm -rf ./out/2
../bin/Debug/netcoreapp8.0/picmag -i ./in/2 ./out/2
sqlite3 ./out/2/.picmag/database.sqlite .dump > ./out/2/.picmag/database.sql
diff ./in/2/database_ref.sql ./out/2/.picmag/database.sql
