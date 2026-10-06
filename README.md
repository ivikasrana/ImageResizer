# Image Resizer

WPF (.NET 10) app that batch-resizes images to **JPG** or **WebP**. Drop the single `.exe` into any folder of
images and run it: the source and output folders default to the folder the exe is in. Results are written to a
`New_<timestamp>` sub-folder, keeping the original folder structure.

- Size (1-100%) and quality sliders, optional EXIF retention, optional sub-folders
- Light / dark theme (remembered in `%LocalAppData%\ImageResizer\theme.txt`)
- Inputs: jpg, jpeg, png, bmp, gif, tif, tiff, webp

## Bundle into a single .exe

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

The project is configured (see `src/ImageResizer.csproj`) for a self-contained, compressed, single-file x64 publish.
The .NET runtime is inside the exe, so the target PC needs nothing installed.

```powershell
dotnet publish src -c Release -o publish
```

The result is `publish\ImageResizer.exe` (~62 MB). Copy it anywhere and run it.

How it is bundled:

- **.NET runtime + WPF**: `SelfContained`, `PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract` and
  `EnableCompressionInSingleFile` in the csproj.
- **libwebp**: `src/native/libwebp.dll` and `src/native/libsharpyuv.dll` are `EmbeddedResource`s. On first WebP use
  `WebpWriter` unpacks them to `%TEMP%\ImageResizer\<version>\` and loads them from there.

Optional: for a ~2 MB exe that needs the .NET 10 Desktop Runtime installed on the PC, publish with
`dotnet publish src -c Release -o publish --self-contained false -p:EnableCompressionInSingleFile=false`.

### Rebuilding the native libwebp (optional)

The committed DLLs are libwebp v1.6.0 built from source with a static CRT (no VC++ redistributable needed).
To rebuild them (needs Visual Studio C++ tools and CMake):

```powershell
git clone --depth 1 --branch v1.6.0 https://github.com/webmproject/libwebp
cmake -S libwebp -B build -G "Visual Studio 17 2022" -A x64 -DBUILD_SHARED_LIBS=ON -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded `
  -DWEBP_BUILD_CWEBP=OFF -DWEBP_BUILD_DWEBP=OFF -DWEBP_BUILD_GIF2WEBP=OFF -DWEBP_BUILD_IMG2WEBP=OFF `
  -DWEBP_BUILD_VWEBP=OFF -DWEBP_BUILD_WEBPINFO=OFF -DWEBP_BUILD_WEBPMUX=OFF -DWEBP_BUILD_EXTRAS=OFF `
  -DWEBP_BUILD_ANIM_UTILS=OFF -DWEBP_BUILD_LIBWEBPMUX=OFF
cmake --build build --config Release --target webp
copy build\Release\libwebp.dll, build\Release\libsharpyuv.dll -> src\native\
```

## License

MIT License — free for personal, educational, and non-commercial use.
Commercial / business use requires a paid license. Contact [vikasrulez@gmail.com](mailto:vikasrulez@gmail.com).
See [LICENSE](LICENSE).

## Third-party

WebP encoding uses [libwebp](https://github.com/webmproject/libwebp) v1.6.0 (BSD-3-Clause).
Its license is in `src/native/libwebp-LICENSE.txt`.
