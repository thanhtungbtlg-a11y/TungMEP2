# TungMEP2 dependency audit

## Independence checks

- Installed assembly: `TungMEP2.dll`
- Assembly identity: `TungMEP2`
- Revit entry point: `TungMEP2.Application`
- Add-in ID: `66CC37B1-88C0-4BCB-85CB-D0F5B4C5B3A3`
- Reference to another add-in assembly: **none**
- Reference to `VP_RevitAPI.dll`: **none**
- Reference to an earlier release folder: **none**
- License or activation module in final assembly: **none**
- Write access to global WPF `Application.ResourceAssembly`: **none**

## Runtime dependencies

TungMEP2 only references:

- Autodesk Revit 2024 APIs from the installed Revit application;
- standard .NET Framework/WPF assemblies;
- third-party libraries copied locally into the TungMEP2 installation directory, including Nice3point, HandyControl, EPPlus, MathNet, PdfSharpCore and PdfPig.

All non-Revit DLLs required at runtime are packaged next to `TungMEP2.dll`. They are third-party libraries, not add-in binaries, and no dependency is loaded from another add-in folder.

Some implementation namespaces retain compatibility-oriented type identifiers used by recovered WPF BAML. Those types are compiled inside `TungMEP2.dll`; they are not an assembly reference, file link, shared installation or runtime dependency on another add-in.
