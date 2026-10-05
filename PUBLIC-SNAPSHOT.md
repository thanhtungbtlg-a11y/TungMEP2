# Public source snapshot — 2026-10-05

Repository: TungMEP2.

This snapshot contains the current source, UI resources, build/installer scripts and relevant documentation. It intentionally starts a new Git history rather than publishing local development history or backups.

Not published: local SDKs, build/cache/output directories, private local configuration, credentials, model/project files, workbooks, PDFs, manufacturer family libraries and third-party library DLLs. The original working folders and existing installation are unchanged.

Building requires Autodesk Revit 2024 and the appropriate .NET SDK/framework tooling. Restore NuGet dependencies where the projects use PackageReference; projects with local lib/ references need those libraries from their official distributions. Revit API DLLs are not redistributed. Existing dependency licenses still apply; public visibility is not a new license grant.

The current Auto90 measured-Tee-clearance update is included. The lib/TungMEP2.PrintSheets.dll dependency is the verified 2026.10.03.1 engine (SHA256 899CFE674CE15794AB28CD28BDB3D3B41AD9477652F1A7A91C976D60EEC768B7). Portable installer source lives in release/TungMEP2; rebuild before installation.

