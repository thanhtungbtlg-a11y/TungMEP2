# -*- coding: utf-8 -*-
"""Read-only DWG export diagnosis for use with ``pyrevit run``.

The script opens a disposable model copy, exports one diagnostic sheet with
each predefined DWG setup plus Revit's default options, and writes a plain-text
inventory of the sheet contents and export options.
"""

from __future__ import print_function

import os
import re
import traceback
import codecs

from pyrevit import DB, HOST_APP
from System.Collections.Generic import List


OUTPUT_ROOT = os.environ.get(
    "TM2_DWG_DIAG_OUTPUT",
    os.path.join(os.environ.get("TEMP", "."), "TungMEP2-DwgDiagnosis"),
)
SHEET_NUMBER = os.environ.get("TM2_DWG_DIAG_SHEET", "FF-01-A")


def safe_name(value):
    value = re.sub(r'[<>:"/\\|?*]+', "_", value or "unnamed")
    return value.strip(" .") or "unnamed"


def describe(value):
    if value is None:
        return "<null>"
    return str(value)


def write_lines(path, lines):
    with codecs.open(path, "w", "utf-8") as stream:
        stream.write("\n".join(lines))
        stream.write("\n")


def export_options_lines(name, options):
    lines = ["", "SETUP: {0}".format(name)]
    property_names = [
        "FileVersion",
        "ExportOfSolids",
        "ACAPreference",
        "Colors",
        "TargetUnit",
        "SharedCoords",
        "MergedViews",
        "LayerMapping",
        "PropOverrides",
        "HideScopeBox",
        "HideReferencePlane",
        "HideUnreferenceViewTags",
        "PreserveCoincidentLines",
        "MarkNonplotLayers",
        "NonplotSuffix",
        "ExportingAreas",
    ]
    for property_name in property_names:
        try:
            value = getattr(options, property_name)
            lines.append("  {0}={1}".format(property_name, describe(value)))
        except Exception as error:
            lines.append("  {0}=<error: {1}>".format(property_name, error))

    try:
        layer_table = options.GetExportLayerTable()
        lines.append("  LayerCount={0}".format(layer_table.Count))
        relevant = []
        for pair in layer_table:
            key = pair.Key
            info = pair.Value
            category = describe(key.CategoryName)
            subcategory = describe(key.SubCategoryName)
            searchable = (category + " " + subcategory).lower()
            if any(
                token in searchable
                for token in [
                    "title",
                    "schedule",
                    "annotation",
                    "viewport",
                    "sheet",
                    "khung",
                    "bảng",
                ]
            ):
                relevant.append(
                    "    {0} | {1} => {2} / {3}".format(
                        category,
                        subcategory,
                        describe(info.LayerName),
                        describe(info.CutLayerName),
                    )
                )
        if relevant:
            lines.append("  RelevantLayers:")
            lines.extend(relevant)
        else:
            lines.append("  RelevantLayers=<none found>")
    except Exception as error:
        lines.append("  LayerTable=<error: {0}>".format(error))

    return lines


def main():
    if not __models__:
        raise RuntimeError("No Revit model was supplied to pyrevit run.")
    document = HOST_APP.app.OpenDocumentFile(__models__[0])

    if not os.path.isdir(OUTPUT_ROOT):
        os.makedirs(OUTPUT_ROOT)

    sheets = [
        sheet
        for sheet in DB.FilteredElementCollector(document).OfClass(DB.ViewSheet)
        if sheet.SheetNumber == SHEET_NUMBER
    ]
    if not sheets:
        raise RuntimeError("Sheet not found: {0}".format(SHEET_NUMBER))
    sheet = sheets[0]

    report = [
        "Document={0}".format(document.PathName),
        "Sheet={0} - {1}".format(sheet.SheetNumber, sheet.Name),
        "SheetId={0}".format(sheet.Id.IntegerValue),
    ]

    title_blocks = list(
        DB.FilteredElementCollector(document, sheet.Id)
        .OfCategory(DB.BuiltInCategory.OST_TitleBlocks)
        .WhereElementIsNotElementType()
    )
    report.append("TitleBlockInstances={0}".format(len(title_blocks)))
    for item in title_blocks:
        symbol = document.GetElement(item.GetTypeId())
        family_name = "<unknown>"
        type_name = "<unknown>"
        try:
            family_name = symbol.FamilyName
            type_name = symbol.Name
        except Exception:
            pass
        report.append(
            "  TitleBlock id={0}; family={1}; type={2}".format(
                item.Id.IntegerValue, family_name, type_name
            )
        )

    schedules = list(
        DB.FilteredElementCollector(document, sheet.Id)
        .OfClass(DB.ScheduleSheetInstance)
        .WhereElementIsNotElementType()
    )
    report.append("ScheduleInstances={0}".format(len(schedules)))
    for item in schedules:
        schedule_view = document.GetElement(item.ScheduleId)
        schedule_name = schedule_view.Name if schedule_view else "<missing>"
        report.append(
            "  Schedule id={0}; view={1}; name={2}".format(
                item.Id.IntegerValue, item.ScheduleId.IntegerValue, schedule_name
            )
        )

    viewports = list(
        DB.FilteredElementCollector(document, sheet.Id)
        .OfClass(DB.Viewport)
        .WhereElementIsNotElementType()
    )
    report.append("Viewports={0}".format(len(viewports)))

    configurations = [("<default-options>", DB.DWGExportOptions())]
    settings = sorted(
        list(DB.FilteredElementCollector(document).OfClass(DB.ExportDWGSettings)),
        key=lambda item: item.Name.lower(),
    )
    for setting in settings:
        try:
            configurations.append((setting.Name, setting.GetDWGExportOptions()))
        except Exception as error:
            report.append(
                "SETUP ERROR: {0}: {1}".format(setting.Name, describe(error))
            )

    ids = List[DB.ElementId]()
    ids.Add(sheet.Id)
    for index, pair in enumerate(configurations):
        setup_name, options = pair
        report.extend(export_options_lines(setup_name, options))
        setup_folder = os.path.join(
            OUTPUT_ROOT, "{0:02d}-{1}".format(index, safe_name(setup_name))
        )
        if not os.path.isdir(setup_folder):
            os.makedirs(setup_folder)
        base_name = "FF-01-{0:02d}".format(index)
        try:
            result = document.Export(setup_folder, base_name, ids, options)
            report.append("  ExportResult={0}".format(result))
        except Exception:
            report.append("  ExportResult=<exception>")
            report.extend(
                "    " + line
                for line in traceback.format_exc().strip().splitlines()
            )

    write_lines(os.path.join(OUTPUT_ROOT, "diagnosis.txt"), report)
    print("TM2_DIAG_OUTPUT={0}".format(OUTPUT_ROOT))
    document.Close(False)


main()
