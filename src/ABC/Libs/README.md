# Opticon DLLs (64-bit)

Place the following proprietary Opticon 64-bit DLL files in this directory before building:

- `Csp2Ex.dll` (native 64-bit)
- `Opticon.csp2Ex.net.dll` (managed 64-bit .NET wrapper)

These files are required for real scanner connectivity. Without them, the app will fall back to mock scanner mode.

> **Note:** These files are excluded from source control via `.gitignore` as they are proprietary.
> **Note:** The 32-bit DLLs (`Csp2.dll`, `Opticon.csp2.net.dll`) are NOT used — this app targets x64 only.
