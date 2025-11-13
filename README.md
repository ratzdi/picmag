## picmag - the Picture manager application

Helps to manage your picture collection.

### Dependencies
- libsqlite3-dev
- dotnetcore 8.0
- sqlite3

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
