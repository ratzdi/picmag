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

summary_file=$(find ./out/1/.picmag -maxdepth 1 -type f -name 'import-summary-*.log' | head -n1)
test -n "$summary_file"
grep -q "Number of imported files: 10" "$summary_file"
grep -q "Number of not imported files: 2" "$summary_file"
grep -q "46173826831_f8dddb93d6_o.jpg" "$summary_file"
grep -q "database_ref.sql" "$summary_file"

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

# Ingegration Test 8: sanity check sync database with filesystem

rm -rf ./out/8
../bin/Debug/netcoreapp8.0/picmag -i ./in/1 ./out/8

test -f ./out/8/2018/11/30/46173826831_f8dddb93d6_o.jpg
rm ./out/8/2018/11/30/46173826831_f8dddb93d6_o.jpg

mkdir -p ./out/8/2017/01/01
cp ./in/1/46233278832_080afdbd7d_o.jpg ./out/8/2017/01/01/manual_added.jpg

../bin/Debug/netcoreapp8.0/picmag --sanity-checks ./out/8

removed_path_count=$(sqlite3 ./out/8/.picmag/database.sqlite "select count(*) from images where path = '2018/11/30/46173826831_f8dddb93d6_o.jpg';")
test "$removed_path_count" -eq 1

added_path_count=$(sqlite3 ./out/8/.picmag/database.sqlite "select count(*) from images where path = '2017/01/01/manual_added.jpg';")
test "$added_path_count" -eq 0

sanity_report=$(find ./out/8/.picmag -maxdepth 1 -type f -name 'sanity-check-*.log' | head -n1)
test -n "$sanity_report"
grep -q "Mode: dry-run" "$sanity_report"
grep -q "missing_db_entries_count: 1" "$sanity_report"
grep -q "orphan_db_entries_count: 1" "$sanity_report"
grep -q "2017/01/01/manual_added.jpg" "$sanity_report"
grep -q "2018/11/30/46173826831_f8dddb93d6_o.jpg" "$sanity_report"

# Ingegration Test 9: sanity check apply changes

rm -rf ./out/9
../bin/Debug/netcoreapp8.0/picmag -i ./in/1 ./out/9

test -f ./out/9/2018/11/30/46173826831_f8dddb93d6_o.jpg
rm ./out/9/2018/11/30/46173826831_f8dddb93d6_o.jpg

mkdir -p ./out/9/2017/01/01
cp ./in/1/46233278832_080afdbd7d_o.jpg ./out/9/2017/01/01/manual_added.jpg

../bin/Debug/netcoreapp8.0/picmag --sanity-checks ./out/9 --apply-changes

removed_path_count_apply=$(sqlite3 ./out/9/.picmag/database.sqlite "select count(*) from images where path = '2018/11/30/46173826831_f8dddb93d6_o.jpg';")
test "$removed_path_count_apply" -eq 0

added_path_count_apply=$(sqlite3 ./out/9/.picmag/database.sqlite "select count(*) from images where path = '2017/01/01/manual_added.jpg';")
test "$added_path_count_apply" -eq 1

sanity_report_apply=$(find ./out/9/.picmag -maxdepth 1 -type f -name 'sanity-check-*.log' | head -n1)
test -n "$sanity_report_apply"
grep -q "Mode: apply-changes" "$sanity_report_apply"
grep -q "inserted_db_entries_count: 1" "$sanity_report_apply"
grep -q "removed_db_entries_count: 1" "$sanity_report_apply"

# Ingegration Test 10: migrate legacy cache format

rm -rf ./out/10
mkdir -p ./out/10/.picmag
printf "legacy/path.jpg legacy-md5\n" > ./out/10/.picmag/cache.txt

../bin/Debug/netcoreapp8.0/picmag --migrate-cache ./out/10

test -f ./out/10/.picmag/cache.txt
test -f ./out/10/.picmag/cache.txt.bak
grep -q "legacy/path.jpg legacy-md5" ./out/10/.picmag/cache.txt.bak
grep -q $'\t' ./out/10/.picmag/cache.txt
if grep -q "legacy/path.jpg legacy-md5" ./out/10/.picmag/cache.txt; then
	echo "Expected migrated cache to use current format"
	exit 1
fi

# Ingegration Test 11: quality filter warn mode with report

rm -rf ./out/11
../bin/Debug/netcoreapp8.0/picmag -i ./in/1 ./out/11 --quality-filter warn --quality-report

quality_summary=$(find ./out/11/.picmag -maxdepth 1 -type f -name 'import-summary-*.log' | head -n1)
test -n "$quality_summary"
grep -q "Quality filter mode: warn" "$quality_summary"

quality_report=$(find ./out/11/.picmag -maxdepth 1 -type f -name 'quality-report-*.log' | head -n1)
test -n "$quality_report"
grep -q "Mode: warn" "$quality_report"
grep -q "Assessed files:" "$quality_report"

# Ingegration Test 12: quality review list action from latest report

../bin/Debug/netcoreapp8.0/picmag --quality-review ./out/11 --verdict reject --action list

db_reject_count=$(sqlite3 ./out/11/.picmag/database.sqlite "select count(*) from images where quality_verdict = 'reject';")
test "$db_reject_count" -gt 0

review_report=$(find ./out/11/.picmag -maxdepth 1 -type f -name 'quality-review-*.log' | head -n1)
test -n "$review_report"
grep -q "action: list" "$review_report"
grep -q "verdict: reject" "$review_report"

# Ingegration Test 13: quality review delete action apply changes

rm -rf ./out/12
../bin/Debug/netcoreapp8.0/picmag -i ./in/1 ./out/12 --quality-filter warn

count_before_delete=$(sqlite3 ./out/12/.picmag/database.sqlite "select count(*) from images;")
../bin/Debug/netcoreapp8.0/picmag --quality-review ./out/12 --verdict reject --action delete --apply-changes
count_after_delete=$(sqlite3 ./out/12/.picmag/database.sqlite "select count(*) from images;")

test "$count_after_delete" -lt "$count_before_delete"

# Ingegration Test 14: quality scan existing dry-run does not mutate DB

rm -rf ./out/13
../bin/Debug/netcoreapp8.0/picmag -i ./in/1 ./out/13

quality_count_before_scan=$(sqlite3 ./out/13/.picmag/database.sqlite "select count(*) from images where quality_verdict is not null and trim(quality_verdict) != '';" )
test "$quality_count_before_scan" -eq 0

../bin/Debug/netcoreapp8.0/picmag --quality-scan-existing ./out/13

quality_count_after_dry_scan=$(sqlite3 ./out/13/.picmag/database.sqlite "select count(*) from images where quality_verdict is not null and trim(quality_verdict) != '';" )
test "$quality_count_after_dry_scan" -eq 0

scan_report_dry=$(find ./out/13/.picmag -maxdepth 1 -type f -name 'quality-scan-existing-*.log' | head -n1)
test -n "$scan_report_dry"
grep -q "mode: dry-run" "$scan_report_dry"
grep -q "scan_scope: only-missing" "$scan_report_dry"

# Ingegration Test 15: quality scan existing apply writes quality metadata

../bin/Debug/netcoreapp8.0/picmag --quality-scan-existing ./out/13 --apply-changes

quality_count_after_apply_scan=$(sqlite3 ./out/13/.picmag/database.sqlite "select count(*) from images where quality_verdict is not null and trim(quality_verdict) != '';" )
test "$quality_count_after_apply_scan" -gt 0

scan_report_apply=$(ls -1t ./out/13/.picmag/quality-scan-existing-*.log | head -n1)
test -n "$scan_report_apply"
grep -q "mode: apply-changes" "$scan_report_apply"

# Integration Test 16: person recognition with real person photos (5 train + 5 probe)

person_dataset_root="${PICMAG_PERSON_ITEST_DATASET:-./in/5-person}"
train_dataset_dir="$person_dataset_root/train"
probe_dataset_dir="$person_dataset_root/probe"
negative_probe_dataset_dir="$person_dataset_root/probe-negative"
embedding_model_path="../../arcfaceresnet100-8.onnx"

if [ ! -d "$train_dataset_dir" ] || [ ! -d "$probe_dataset_dir" ] || [ ! -d "$negative_probe_dataset_dir" ]; then
	echo "Skip Integration Test 16: dataset missing. Provide train/probe/probe-negative folders in $person_dataset_root or set PICMAG_PERSON_ITEST_DATASET"
else
	train_available=$(find "$train_dataset_dir" -type f \( -iname '*.jpg' -o -iname '*.jpeg' \) | wc -l)
	probe_available=$(find "$probe_dataset_dir" -type f \( -iname '*.jpg' -o -iname '*.jpeg' \) | wc -l)
	negative_probe_available=$(find "$negative_probe_dataset_dir" -type f \( -iname '*.jpg' -o -iname '*.jpeg' \) | wc -l)

	if [ "$train_available" -lt 5 ] || [ "$probe_available" -lt 5 ] || [ "$negative_probe_available" -lt 5 ]; then
		echo "Skip Integration Test 16: need at least 5 JPG/JPEG train, 5 JPG/JPEG probe images of same person, and 5 JPG/JPEG probe-negative images of other persons"
	elif [ ! -f "$embedding_model_path" ]; then
		echo "Skip Integration Test 16: embedding model not found at $embedding_model_path"
	else
		rm -rf ./out/14
		mkdir -p ./out/14/train_source ./out/14/probe_source ./out/14/target

		find "$train_dataset_dir" -type f \( -iname '*.jpg' -o -iname '*.jpeg' \) | sort | head -n 5 | while read -r file; do
			cp "$file" ./out/14/train_source/
		done
		find "$probe_dataset_dir" -type f \( -iname '*.jpg' -o -iname '*.jpeg' \) | sort | head -n 5 | while read -r file; do
			cp "$file" ./out/14/probe_source/
		done
		find "$negative_probe_dataset_dir" -type f \( -iname '*.jpg' -o -iname '*.jpeg' \) | sort | head -n 5 | while read -r file; do
			cp "$file" ./out/14/probe_source/
		done

		../bin/Debug/netcoreapp8.0/picmag -i ./out/14/train_source ./out/14/target

		train_imported_count=$(sqlite3 ./out/14/target/.picmag/database.sqlite "select count(*) from images;")
		test "$train_imported_count" -eq 5

		PICMAG_FACE_EMBEDDING_MODEL="$embedding_model_path" ../bin/Debug/netcoreapp8.0/picmag --person-scan-existing ./out/14/target --all

		train_faces_count=$(sqlite3 ./out/14/target/.picmag/database.sqlite "select count(*) from image_faces;")
		test "$train_faces_count" -ge 5

		../bin/Debug/netcoreapp8.0/picmag --person-add ./out/14/target "Integration Person"

		for face_id in $(sqlite3 ./out/14/target/.picmag/database.sqlite "select f.id from image_faces f join (select image_path, max(detection_confidence) as max_conf from image_faces group by image_path) best on best.image_path = f.image_path and best.max_conf = f.detection_confidence order by f.image_path limit 5;"); do
			../bin/Debug/netcoreapp8.0/picmag --person-label ./out/14/target --face-id "$face_id" --person "Integration Person"
		done

		confirmed_labels_count=$(sqlite3 ./out/14/target/.picmag/database.sqlite "select count(*) from image_face_labels where status = 'confirmed';")
		test "$confirmed_labels_count" -eq 5

		../bin/Debug/netcoreapp8.0/picmag --person-train ./out/14/target

		profiles_count=$(sqlite3 ./out/14/target/.picmag/database.sqlite "select count(*) from person_profiles;")
		test "$profiles_count" -ge 1

		../bin/Debug/netcoreapp8.0/picmag -i ./out/14/probe_source ./out/14/target

		total_imported_count=$(sqlite3 ./out/14/target/.picmag/database.sqlite "select count(*) from images;")
		test "$total_imported_count" -eq 15

		PICMAG_FACE_EMBEDDING_MODEL="$embedding_model_path" ../bin/Debug/netcoreapp8.0/picmag --person-scan-existing ./out/14/target --only-missing

		total_faces_count=$(sqlite3 ./out/14/target/.picmag/database.sqlite "select count(*) from image_faces;")
		test "$total_faces_count" -ge 10

		../bin/Debug/netcoreapp8.0/picmag --person-predict ./out/14/target --limit 10 --min-confidence 0.60

		recognized_probe_count=$(sqlite3 ./out/14/target/.picmag/database.sqlite "select count(distinct f.image_path) from person_predictions pp join image_faces f on f.id = pp.face_id join persons p on p.id = pp.person_id where pp.status = 'suggested' and p.name = 'Integration Person' and f.image_path like '%/obama_probe_%';")
		test "$recognized_probe_count" -eq 5

		# Current predictor uses closed-set matching against known profiles only.
		# For negative probe images we therefore assert review behavior, not unknown rejection.
		negative_probe_suggestions=$(sqlite3 ./out/14/target/.picmag/database.sqlite "select count(distinct f.image_path) from person_predictions pp join image_faces f on f.id = pp.face_id where pp.status = 'suggested' and f.image_path like '%/other_probe_%';")
		test "$negative_probe_suggestions" -ge 1

		negative_probe_confirmed_labels=$(sqlite3 ./out/14/target/.picmag/database.sqlite "select count(*) from image_face_labels l join image_faces f on f.id = l.image_face_id where l.status = 'confirmed' and f.image_path like '%/other_probe_%';")
		test "$negative_probe_confirmed_labels" -eq 0
	fi
fi
