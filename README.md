## picmag - the picture manager application

Helps to manage your picture collection.


### Check it out!

If you have some unordered borried collection of images located on your storage, this
tool can help you to give your images a tidy structure. Imagine you have a collection of pictures that is growing up year on year.
picmag analyses your collection and creates a clear chronological directory structure for all pictures in your collection.

```
# Import all JPG pictures from /home/user/some/borried/collection to /home/user/picture_album

./picmag -i /home/user/some/borried/collection /home/user/picture_album

# After import all files in source directory ramain untouched.
```

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

- Option to delete successfully imported file from the source directory.
- Import video files in mp4 format.
