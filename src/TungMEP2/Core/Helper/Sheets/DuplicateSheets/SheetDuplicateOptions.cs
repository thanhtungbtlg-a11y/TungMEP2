using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper.Sheets.DuplicateSheets;

public sealed class SheetDuplicateOptions
{
	public NameTransformRule ViewNameRule { get; } = new NameTransformRule();

	public NameTransformRule SheetNumberRule { get; } = new NameTransformRule();

	public NameTransformRule SheetNameRule { get; } = new NameTransformRule();

	public bool DuplicateViews { get; set; } = true;

	public bool DuplicateLegends { get; set; } = true;

	public bool DuplicateSchedules { get; set; } = true;

	public bool DuplicateImages { get; set; } = true;

	public bool DuplicateLines { get; set; } = true;

	public bool DuplicateText { get; set; } = true;

	public bool DuplicateClouds { get; set; }

	public bool DuplicateDwgs { get; set; }

	public bool DuplicateSymbols { get; set; }

	public bool DuplicateDimensions { get; set; }

	public bool DuplicateAdditionalRevisions { get; set; }

	public bool UseExistingLegends { get; set; } = true;

	public bool UseExistingSchedules { get; set; } = true;

	public ViewDuplicateOption ViewDuplicateOption { get; set; } = ViewDuplicateOption.Duplicate;

	public SheetViewTemplateMode ViewTemplateMode { get; set; } = SheetViewTemplateMode.Keep;

	public bool PreserveTitleBlockPosition { get; set; } = true;

	public List<string> SheetParametersToCopy { get; } = new List<string>();
}
