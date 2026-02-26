## picmag - the picture manager application

Helps to manage your picture collection.


### Check it out!

If you have some unordered borried collection of images located on your storage, this
tool can help you to give your images a tidy structure. Imagine you have a collection of pictures that is growing up year on year.
picmag analyses your collection and creates a clear chronological directory structure for all pictures in your collection.

```
# Import all JPG pictures from /home/user/some/borried/collection to /home/user/picture_album

./picmag -i /home/user/some/borried/collection /home/user/picture_album

# Import and delete only successfully imported source files
./picmag -i /home/user/some/borried/collection /home/user/picture_album --delete-source

# After import all files in source directory remain untouched by default.
```

### Safety behavior of --delete-source

- Deletion is opt-in only. Without --delete-source, source files are never deleted.
- A source file is deleted only after successful copy to destination and successful database insert.
- Files that are not imported (e.g. unsupported extension, duplicate, existing target) are never deleted.
- If deletion fails, the import remains successful and the deletion failure is logged.
- If no files are importable, no source files are deleted.

### Dependencies
- libsqlite3-dev
- dotnetcore 8.0

### Build
```
dotnet build
```

### Run
```
./picmag -h
```

### Test
```
./picmag/tests/integration_test.sh
```

### Debian Package

Install initially the dotnet-deb tool.
```
cd picmag
dotnet tool install --global dotnet-deb
dotnet deb install
```

Build the package
```
dotnet deb
```

### Next Features

- [x] Option to delete successfully imported file from the source directory (`--delete-source`).
- Import video files in mp4 format.
- Summerizes result of the import as log file:
  - Number of imported files
  - List of imported files
  - Number of not imported files
  - List of not imported files
- Sanity checks
  - sync the database with filesystem, i.e. if an DB entry does not exist on filesystem, the entry must be removed and vice-versa.
