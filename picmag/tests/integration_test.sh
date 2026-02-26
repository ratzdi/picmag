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

compare_db_with_ref()
{
	actual_db="$1"
	ref_sql="$2"
	work_dir="$3"

	ref_db="$work_dir/reference.sqlite"
	actual_rows="$work_dir/actual_rows.txt"
	expected_rows="$work_dir/expected_rows.txt"

	rm -f "$ref_db" "$actual_rows" "$expected_rows"
	sqlite3 "$ref_db" < "$ref_sql"
	sqlite3 "$actual_db" "select path, created, md5 from images order by path;" > "$actual_rows"
	sqlite3 "$ref_db" "select path, created, md5 from images order by path;" > "$expected_rows"
	diff "$expected_rows" "$actual_rows"
}

# Integration Test 1
rm -rf ./out/1
../bin/Debug/netcoreapp8.0/picmag -i ./in/1 ./out/1
compare_db_with_ref ./out/1/.picmag/database.sqlite ./in/1/database_ref.sql ./out/1/.picmag

# Ingegration Test 2

rm -rf ./out/2
../bin/Debug/netcoreapp8.0/picmag -i ./in/2 ./out/2
compare_db_with_ref ./out/2/.picmag/database.sqlite ./in/2/database_ref.sql ./out/2/.picmag

# Ingegration Test 3: delete imported source files

rm -rf ./out/3
mkdir -p ./out/3
cp -r ./in/1 ./out/3/source

../bin/Debug/netcoreapp8.0/picmag -i ./out/3/source ./out/3/target --delete-source
compare_db_with_ref ./out/3/target/.picmag/database.sqlite ./in/1/database_ref.sql ./out/3/target/.picmag

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
compare_db_with_ref ./out/4/target/.picmag/database.sqlite ./in/2/database_ref.sql ./out/4/target/.picmag

# source files should remain untouched because nothing was imported
test -f ./out/4/source/not_an_image
test -f ./out/4/source/Readme.md
test -f ./out/4/source/database_ref.sql

# Ingegration Test 5: import mp4 files

rm -rf ./out/5
mkdir -p ./out/5/source ./out/5/target
cp ./in/3/sample.mp4 ./out/5/source/sample.mp4

../bin/Debug/netcoreapp8.0/picmag -i ./out/5/source ./out/5/target

imported_count=$(sqlite3 ./out/5/target/.picmag/database.sqlite "select count(*) from images;")
test "$imported_count" -eq 1

target_path=$(sqlite3 ./out/5/target/.picmag/database.sqlite "select path from images limit 1;")
test -n "$target_path"
test "${target_path##*.}" = "mp4"
test -f "./out/5/target/$target_path"

# Ingegration Test 6: uppercase extension argument (MP4)

rm -rf ./out/6
mkdir -p ./out/6/source ./out/6/target
cp ./in/3/sample.mp4 ./out/6/source/sample.mp4

../bin/Debug/netcoreapp8.0/picmag -i ./out/6/source ./out/6/target MP4

imported_count_uppercase=$(sqlite3 ./out/6/target/.picmag/database.sqlite "select count(*) from images;")
test "$imported_count_uppercase" -eq 1

target_path_uppercase=$(sqlite3 ./out/6/target/.picmag/database.sqlite "select path from images limit 1;")
test -n "$target_path_uppercase"
test "${target_path_uppercase##*.}" = "mp4"
test -f "./out/6/target/$target_path_uppercase"

# Ingegration Test 7: metadata-based dating with ffprobe

if command -v ffprobe >/dev/null 2>&1; then
	rm -rf ./out/7
	mkdir -p ./out/7/source ./out/7/target
	cp ./in/4/metadata_creation_time.mp4 ./out/7/source/metadata_creation_time.mp4

	creation_time=$(ffprobe -v quiet -show_entries format_tags=creation_time -of default=noprint_wrappers=1:nokey=1 ./out/7/source/metadata_creation_time.mp4 | head -n1)
	test -n "$creation_time"

	expected_dir=$(date -d "$creation_time" +"%Y/%m/%d")
	../bin/Debug/netcoreapp8.0/picmag -i ./out/7/source ./out/7/target mp4

	metadata_target_path=$(sqlite3 ./out/7/target/.picmag/database.sqlite "select path from images limit 1;")
	test "$metadata_target_path" = "$expected_dir/metadata_creation_time.mp4"
	test -f "./out/7/target/$metadata_target_path"

	# second run should hit cache and not import again
	before_count=$(sqlite3 ./out/7/target/.picmag/database.sqlite "select count(*) from images;")
	../bin/Debug/netcoreapp8.0/picmag -i ./out/7/source ./out/7/target mp4
	after_count=$(sqlite3 ./out/7/target/.picmag/database.sqlite "select count(*) from images;")
	test "$before_count" -eq "$after_count"
else
	echo "Skip Integration Test 7: ffprobe not installed"
fi
