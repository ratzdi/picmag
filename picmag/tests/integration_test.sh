# MIT License
#
# Copyright (c) 2025 Dimitri Ratz
#
# Permission is hereby granted, free of charge, to any person obtaining a copy
# of this software and associated documentation files (the "Software"), to deal
# in the Software without restriction, including without limitation the rights
# to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
# copies of the Software, and to permit persons to whom the Software is
# furnished to do so, subject to the following conditions:
#
# The above copyright notice and this permission notice shall be included in all
# copies or substantial portions of the Software.
#
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
# IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
# FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
# AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
# LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
# OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
# SOFTWARE.

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

# Ingegration Test 3: delete imported source files

rm -rf ./out/3
mkdir -p ./out/3
cp -r ./in/1 ./out/3/source

../bin/Debug/netcoreapp8.0/picmag -i ./out/3/source ./out/3/target --delete-source
sqlite3 ./out/3/target/.picmag/database.sqlite .dump > ./out/3/target/.picmag/database.sql
diff ./in/1/database_ref.sql ./out/3/target/.picmag/database.sql

# all imported jpg files should be deleted from source directory
if find ./out/3/source -type f -name '*.jpg' | grep -q .; then
	echo "Expected no jpg files left in source when --delete-source is used"
	exit 1
fi

# non-imported source files should remain untouched
test -f ./out/3/source/Readme.md
test -f ./out/3/source/database_ref.sql

# Ingegration Test 4: --delete-source with no importable files

rm -rf ./out/4
mkdir -p ./out/4
cp -r ./in/2 ./out/4/source

../bin/Debug/netcoreapp8.0/picmag -i ./out/4/source ./out/4/target --delete-source
sqlite3 ./out/4/target/.picmag/database.sqlite .dump > ./out/4/target/.picmag/database.sql
diff ./in/2/database_ref.sql ./out/4/target/.picmag/database.sql

# source files should remain untouched because nothing was imported
test -f ./out/4/source/not_an_image
test -f ./out/4/source/Readme.md
test -f ./out/4/source/database_ref.sql
