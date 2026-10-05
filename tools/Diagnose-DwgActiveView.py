# -*- coding: utf-8 -*-
"""Compare DWG export with the target sheet active versus a safe view active."""

from __future__ import print_function

import codecs
import os
import traceback

from pyrevit import DB, HOST_APP
from System.Collections.Generic import List


OUTPUT_ROOT = os.environ.get(
    "TM2_DWG_ACTIVE_DIAG_OUTPUT",
    os.path.join(os.environ.get("TEMP", "."), "TungMEP2-DwgActiveViewDiagnosis"),
)
SHEET_NUMBER = os.environ.get("TM2_DWG_DIAG_SHEET", "FF-01-A")
SETUP_NAME = os.environ.get("TM2_DWG_DIAG_SETUP", "XUATCAD-MEP Tung")


def write_report(lines):
    if not os.path.isdir(OUTPUT_ROOT):
        os.makedirs(OUTPUT_ROOT)
    with codecs.open(
        os.path.join(OUTPUT_ROOT, "active-view-diagnosis.txt"),
        "w",
        "utf-8",
    ) as stream:
        stream.write("\n".join(lines))
        stream.write("\n")


def export_case(document, sheet_id, options, case_name, lines):
    folder = os.path.join(OUTPUT_ROOT, case_name)
    if not os.path.isdir(folder):
        os.makedirs(folder)
    ids = List[DB.ElementId]()
    ids.Add(sheet_id)
    try:
        result = document.Export(folder, case_name, ids, options)
        files = [
            name
            for name in os.listdir(folder)
            if os.path.isfile(os.path.join(folder, name))
        ]
        lines.append(
            "{0}: result={1}; files={2}".format(
                case_name,
                result,
                " | ".join(sorted(files)),
            )
        )
    except Exception:
        lines.append("{0}: exception".format(case_name))
        lines.extend(
            "  " + line
            for line in traceback.format_exc().strip().splitlines()
        )


def main():
    if not __models__:
        raise RuntimeError("No Revit model was supplied to pyrevit run.")
    if not os.path.isdir(OUTPUT_ROOT):
        os.makedirs(OUTPUT_ROOT)

    ui_document = HOST_APP.uiapp.OpenAndActivateDocument(__models__[0])
    document = ui_document.Document
    sheets = [
        item
        for item in DB.FilteredElementCollector(document).OfClass(DB.ViewSheet)
        if item.SheetNumber == SHEET_NUMBER
    ]
    if not sheets:
        raise RuntimeError("Sheet not found: {0}".format(SHEET_NUMBER))
    sheet = sheets[0]

    settings = [
        item
        for item in DB.FilteredElementCollector(document).OfClass(
            DB.ExportDWGSettings
        )
        if item.Name == SETUP_NAME
    ]
    if not settings:
        raise RuntimeError("DWG setup not found: {0}".format(SETUP_NAME))

    lines = [
        "Document={0}".format(document.PathName),
        "Sheet={0} - {1}".format(sheet.SheetNumber, sheet.Name),
        "Setup={0}".format(SETUP_NAME),
    ]

    with settings[0].GetDWGExportOptions() as options:
        ui_document.ActiveView = sheet
        ui_document.RefreshActiveView()
        lines.append("ActiveViewBeforeFirstExport={0}".format(
            ui_document.ActiveView.Name
        ))
        export_case(
            document,
            sheet.Id,
            options,
            "01-target-sheet-active",
            lines,
        )

        safe_views = [
            view
            for view in DB.FilteredElementCollector(document).OfClass(DB.View3D)
            if not view.IsTemplate
        ]
        if not safe_views:
            safe_views = [
                view
                for view in DB.FilteredElementCollector(document).OfClass(DB.View)
                if not view.IsTemplate
                and view.Id != sheet.Id
                and view.ViewType
                in (
                    DB.ViewType.DraftingView,
                    DB.ViewType.Legend,
                    DB.ViewType.FloorPlan,
                )
            ]
        if not safe_views:
            raise RuntimeError("No safe non-sheet view was found.")

        safe_view = sorted(safe_views, key=lambda view: view.Name.lower())[0]
        ui_document.ActiveView = safe_view
        ui_document.RefreshActiveView()
        lines.append("ActiveViewBeforeSecondExport={0}".format(
            ui_document.ActiveView.Name
        ))
        export_case(
            document,
            sheet.Id,
            options,
            "02-safe-view-active",
            lines,
        )

    write_report(lines)
    print("TM2_DWG_ACTIVE_DIAG_OUTPUT={0}".format(OUTPUT_ROOT))


main()
