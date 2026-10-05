# PipeLayer dialog hotfix — 2026-10-02

## Report and root cause

User supplied a Revit 2024 screenshot: `Autodesk.Revit.Exceptions.ArgumentException: Corresponding button not found. Parameter name: defaultButton`, at `TaskDialog.set_DefaultButton` → `PipeLayerRunner.TryChooseMode` → `Run`.

Installed/source baseline was C57E695A630B. The dialog initializer set `DefaultButton = CommandLink1` before `AddCommandLink` registered BOP/COP/TOP. The failure occurs before height calculation or any PipeLayer transaction/movement, not because of model geometry or connections.

Fix scope: retain the already-approved mode UI and all geometry/safety/movement behavior; register links before setting BOP as default. [Autodesk's Revit 2024 example](https://help.autodesk.com/cloudhelp/2024/ENU/Revit-API/files/Revit_API_Developers_Guide/Introduction/Add_In_Integration/Revit_API_Revit_API_Developers_Guide_Introduction_Add_In_Integration_Revit_style_Task_Dialogs_html.html) uses registration before default assignment. Local RevitAPIUI.xml documents both CommonButton and CommandLink defaults.

## Completed changes

- Extracted unchanged mode UI/mapping into `src/TungMEP2/Core/Internal/Ctn/PipeLayerModeDialog.cs` to provide a test seam; moved the default assignment after all three `AddCommandLink` calls.
- `PipeLayerRunner` calls that helper; original inline UI method removed. Its math, transactions, movement and safety paths did not change.
- Added `tools/PipeLayerDialogConsoleTest.cs`, `PipeLayerTaskDialogContractDouble.cs`, and an isolated `.csproj` linking the real production mode-dialog code. Test-only doubles are outside the production compile root.
- Updated native smoke guide with the dialog regression and test limitations; copied guide into release ZIP.

## Verification and limitations

- RED: copied original builder sequence failed with the same `Corresponding button not found / defaultButton` exception under the narrow registration-order boundary double.
- GREEN: **10 assertions** passed for BOP/COP/TOP and Cancel/None mapping; the boundary also checks all links exist, BOP default and Cancel availability before Show.
- PipeLayerMath: 1,503 assertions passed. Old console suites: DirectY geometry 1,100; inventory 11; failure policy 17; FitSpace 32; PipeSpacing 86; AutoTagPipe 219; newly compiled ElbowRunSlopeMath 15. **Total: 2,993 assertions passed**.
- Fresh Test-FitRotate, Test-MepAlignment, Test-PipeSpacing, Test-FitSpace scripts passed.
- Revit 2024 net48/x64 full Rebuild and package build: 0 warnings/errors.
- Independent read-only reviewer found no Critical/Important/Minor issues in the targeted diff and approved packaging.

Native Revit dialog/model smoke testing remains outstanding. Two non-UI/non-model out-of-host probes could not load RevitAPIUI/native dependencies; no dialog was shown and no Revit model was touched. Those dependency failures are **not** the RED regression evidence; RED/GREEN was against the production builder with the explicitly documented contract double. The double does not prove native rendering, keyboard default activation, Esc/close handling, or model solver behavior. User must save, fully close/reopen Revit, then test the dialog and alignment on a model copy.

## Installed package

- Build ID: **DE59F1B4E205**.
- DLL SHA256: `DE59F1B4E2059836C0A91736AC64BC0BD2B2F5AA5C2CEAEC218A5C0E1F39F507`.
- ZIP SHA256: `F1A2099360AEC54821F826872F5B042868488BD80C5250AC859390B7068E420B`.
- `%APPDATA%/Autodesk/Revit/Addins/2024/TungMEP2.addin` now points to `%LOCALAPPDATA%/TungMEP2/Revit2024/DE59F1B4E205/net48/TungMEP2.dll`.
- Build, release, ZIP DLL entry and installed DLL hashes matched independently; installer/verification returned exit 0. No native Revit hot reload claimed.

Backup: `backups/PipeLayer-before-default-button-fix-20261002` contains pre-fix Runner, manifest and ZIP. Old C57E695A630B DLL is preserved with SHA256 `C57E695A630BF08246883C7D9220DD3684770D122E4182224D7565F3C5C8BC5D`; backup ZIP is unchanged with SHA256 `CC7FA3C4AFC02A7ACC88E4C1BA303B0EE307FE3510108CF502ADF06B019339DD`.

Do not repeat completed build/install. Next step is user native smoke feedback. Do not resume unrelated DirectY work or alter PipeLayer geometry/safety for this dialog-only issue.
