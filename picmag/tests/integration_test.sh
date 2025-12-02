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
