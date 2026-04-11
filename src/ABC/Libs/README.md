# Opticon DLLs

Place the following proprietary Opticon DLL files in this directory before building:

- `Csp2.dll`
- `Csp2Ex.dll`
- `Opticon.csp2.net.dll`
- `Opticon.csp2Ex.net.dll`

These files are required for real scanner connectivity. Without them, the app will fall back to mock scanner mode.

> **Note:** These files are excluded from source control via `.gitignore` as they are proprietary.
