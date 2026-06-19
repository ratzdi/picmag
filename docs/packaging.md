# Debian and Raspberry Pi Packaging

## Build Debian package

```bash
cd picmag
dotnet tool install --global dotnet-deb
dotnet deb
```

## Runtime-specific packages

```bash
# Raspberry Pi OS 64-bit
dotnet deb -r linux-arm64

# Raspberry Pi OS 32-bit
dotnet deb -r linux-arm
```

## Install package

```bash
sudo apt update
sudo apt install ./path/to/picmag_*_arm64.deb   # or *_armhf.deb
```

Alternative:

```bash
sudo dpkg -i ./path/to/picmag_*_arm64.deb   # or *_armhf.deb
sudo apt -f install
```

If using an older non-self-contained package and the system reports missing .NET runtime:

```bash
sudo apt update
sudo apt install dotnet-runtime-8.0
```
