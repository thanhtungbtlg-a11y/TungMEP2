using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;

namespace VP_RevitAPI.Internal.Ctn.PipeSpacing;

internal static class PipeSpacingAnalysisService
{
	private const double Epsilon = 1E-09;

	private sealed class Candidate
	{
		internal Pipe SeedPipe { get; set; }
		internal PipeSpacingCluster Cluster { get; set; }
		internal Line Line { get; set; }
		internal double NominalDiameter { get; set; }
		internal double EffectiveDiameter { get; set; }
		internal double Coordinate { get; set; }
		internal List<string> Warnings { get; } = new List<string>();
	}

	internal static bool TryCreatePlan(
		UIDocument uiDocument,
		IReadOnlyCollection<ElementId> selectedIds,
		PipeSpacingSettings settings,
		out PipeSpacingPlan plan,
		out PipeSpacingAnalysisReport report,
		out string error)
	{
		plan = null;
		report = new PipeSpacingAnalysisReport();
		error = string.Empty;
		Document document = uiDocument?.Document;
		if (document == null || document.IsFamilyDocument || document.IsReadOnly)
		{
			error = "PipeSpacing chỉ chạy trong project document có thể chỉnh sửa.";
			return false;
		}
		if (settings == null)
		{
			error = "Thiết lập PipeSpacing không hợp lệ.";
			return false;
		}

		List<Pipe> selectedPipes = (selectedIds ?? Array.Empty<ElementId>())
			.Select(document.GetElement)
			.OfType<Pipe>()
			.Where(pipe => pipe.Location is LocationCurve { Curve: Line })
			.GroupBy(pipe => pipe.Id.Value)
			.Select(group => group.First())
			.ToList();
		report.SelectedPipeCount = selectedPipes.Count;
		if (selectedPipes.Count < 2)
		{
			error = "Vui lòng chọn ít nhất hai Pipe thẳng.";
			return false;
		}

		List<Candidate> candidates = new List<Candidate>();
		HashSet<string> signatures = new HashSet<string>(StringComparer.Ordinal);
		foreach (Pipe pipe in selectedPipes.OrderBy(item => item.Id.Value))
		{
			if (!PipeSpacingClusterCollector.TryCollect(document, pipe, out PipeSpacingCluster cluster, out string clusterError))
			{
				report.Rows.Add(ExcludedRow(pipe, "Invalid", clusterError));
				continue;
			}

			string signature = string.Join(",", cluster.AllOwnedElementIds.Select(id => id.Value).OrderBy(id => id));
			if (!signatures.Add(signature))
			{
				report.Warnings.Add($"Pipe {pipe.Id.Value} thuộc cụm đã được chọn; đã hợp nhất một lần.");
				continue;
			}
			if (cluster.IsConstrained)
			{
				report.Rows.Add(ExcludedRow(pipe, "Constrained", cluster.StatusMessage));
				continue;
			}

			Line line = (Line)((LocationCurve)pipe.Location).Curve;
			Candidate candidate = new Candidate
			{
				SeedPipe = pipe,
				Cluster = cluster,
				Line = line
			};
			candidates.Add(candidate);
		}

		if (candidates.Count < 2)
		{
			error = "Không còn đủ hai cụm Pipe an toàn để dàn khoảng cách.";
			return false;
		}

		List<PipeSpacingOwnershipSet> ownershipSets = candidates.Select(candidate =>
			new PipeSpacingOwnershipSet(
				candidate.SeedPipe.Id.Value,
				candidate.Cluster.AllOwnedElementIds.Select(id => id.Value).ToArray())).ToList();
		if (PipeSpacingAnalysisPolicy.TryFindOwnershipOverlap(
				ownershipSets,
				out long firstOverlap,
				out long secondOverlap))
		{
			error = $"Hai lựa chọn Pipe {firstOverlap} và {secondOverlap} dùng chung phần tử kết nối; hãy chọn lại.";
			return false;
		}

		View activeView = document.GetElement(uiDocument.ActiveView.Id) as View;
		if (activeView == null)
		{
			error = "Không đọc được active view.";
			return false;
		}
		double angleTolerance = Math.Max(0.1, Math.Min(10.0, settings.ParallelAngleToleranceDegrees));
		double parallelDotTolerance = Math.Cos(UnitUtils.ConvertToInternalUnits(angleTolerance, UnitTypeId.Degrees));
		if (!PipeSpacingGeometry.TryBuildAxes(
				candidates.Select(candidate => candidate.Line.Direction).ToList(),
				activeView.RightDirection,
				activeView.UpDirection,
				parallelDotTolerance,
				out XYZ runAxis,
				out XYZ spacingAxis,
				out error))
		{
			return false;
		}

		foreach (Candidate candidate in candidates)
		{
			candidate.Coordinate = PipeSpacingGeometry.Midpoint(candidate.Line).DotProduct(spacingAxis);
			if (!TryResolveDiameters(document, candidate, spacingAxis, settings, out error))
			{
				return false;
			}
		}
		candidates = candidates.OrderBy(candidate => candidate.Coordinate).ToList();

		List<PipeSpacingScalarInput> scalarInputs = candidates.Select(candidate =>
			new PipeSpacingScalarInput(
				candidate.SeedPipe.Id.Value,
				candidate.Coordinate,
				candidate.EffectiveDiameter,
				candidate.NominalDiameter)).ToList();
		double requestedSpacing = settings.Mode == PipeSpacingMode.EqualCenterSpacing
			? ToInternal(settings.CenterSpacingMm)
			: ToInternal(settings.ClearGapMm);
		List<PipeSpacingDnRule> rules = (settings.DnRules ?? Array.Empty<PipeSpacingDnRule>())
			.Select(rule => new PipeSpacingDnRule(
				ToInternal(rule.MaxNominalDiameter),
				ToInternal(rule.ClearGap)))
			.ToList();
		if (!PipeSpacingMath.TryBuildTargets(
				scalarInputs,
				settings.Mode,
				requestedSpacing,
				rules,
				ToInternal(settings.FallbackClearGapMm),
				out PipeSpacingScalarPlan scalarPlan,
				out error))
		{
			return false;
		}
		if (settings.Mode == PipeSpacingMode.EqualCenterSpacing && scalarPlan.MinimumClearGap < 0.0)
		{
			report.Warnings.Add(
				$"Khoảng cách tim đã nhập làm envelope ống chồng nhau {FromInternal(-scalarPlan.MinimumClearGap):0.##} mm ở cặp bất lợi nhất.");
		}
		if (settings.Mode == PipeSpacingMode.DnRuleTable)
		{
			for (int index = 1; index < candidates.Count; index++)
			{
				double maximumDn = Math.Max(candidates[index - 1].NominalDiameter, candidates[index].NominalDiameter);
				bool hasMatchingBand = rules.Any(rule => rule.MaxNominalDiameter + Epsilon >= maximumDn);
				if (!hasMatchingBand)
				{
					report.Warnings.Add(
						$"Cặp Pipe {candidates[index - 1].SeedPipe.Id.Value}/{candidates[index].SeedPipe.Id.Value} không khớp bảng DN; dùng fallback {settings.FallbackClearGapMm:0.##} mm.");
				}
			}
		}
		for (int index = 0; index < candidates.Count; index++)
		{
			PipeSpacingScalarTarget target = scalarPlan.Targets[index];
			if (candidates[index].Cluster.HasPinnedElement && !target.IsAnchor)
			{
				error = $"Cụm Pipe {candidates[index].SeedPipe.Id.Value} có phần tử bị Pin và không phải cụm neo. Hãy bỏ Pin hoặc chọn lại dãy.";
				return false;
			}
		}

		List<PipeSpacingPlanEntry> entries = new List<PipeSpacingPlanEntry>();
		for (int index = 0; index < candidates.Count; index++)
		{
			Candidate candidate = candidates[index];
			PipeSpacingScalarTarget target = scalarPlan.Targets[index];
			XYZ moveVector = spacingAxis.Multiply(target.Delta);
			List<PipeSpacingElementSnapshot> snapshots = candidate.Cluster.AllOwnedElementIds
				.Select(document.GetElement)
				.Where(element => element != null)
				.Select(element => new PipeSpacingElementSnapshot(element))
				.ToList();
			PipeSpacingPlanEntry entry = new PipeSpacingPlanEntry(
				candidate.Cluster,
				candidate.SeedPipe.UniqueId,
				candidate.NominalDiameter,
				candidate.EffectiveDiameter,
				candidate.Coordinate,
				target.TargetCoordinate,
				moveVector,
				target.IsAnchor,
				candidate.Line.GetEndPoint(0),
				candidate.Line.GetEndPoint(1),
				snapshots,
				candidate.Warnings);
			entries.Add(entry);
			report.Rows.Add(new PipeSpacingAnalysisRow
			{
				Key = target.Key,
				PipeId = candidate.SeedPipe.Id.Value.ToString(),
				NominalDiameterMm = FromInternal(candidate.NominalDiameter),
				EffectiveDiameterMm = FromInternal(candidate.EffectiveDiameter),
				CurrentCoordinateMm = FromInternal(candidate.Coordinate),
				TargetCoordinateMm = FromInternal(target.TargetCoordinate),
				MoveMm = FromInternal(target.Delta),
				IsAnchor = target.IsAnchor,
				Status = target.IsAnchor ? "Anchor" : "Ready",
				Warning = string.Join("; ", candidate.Warnings)
			});
		}

		PipeSpacingPlanEntry anchor = entries[scalarPlan.AnchorIndex];
		string anchorReason =
			$"Giữ Pipe {anchor.Cluster.SeedPipeId.Value}: đường kính ngoài hiệu dụng lớn hơn trong hai Pipe biên";
		if (Math.Abs(entries[0].EffectiveDiameter - entries[entries.Count - 1].EffectiveDiameter) <= Epsilon)
		{
			anchorReason = $"Giữ Pipe {anchor.Cluster.SeedPipeId.Value}: hai Pipe biên cùng đường kính, dùng quy tắc trái/dưới ổn định";
		}
		plan = new PipeSpacingPlan(
			activeView.Id,
			runAxis,
			spacingAxis,
			settings,
			entries,
			scalarPlan.MinimumClearGap,
			anchorReason);
		return true;
	}

	private static bool TryResolveDiameters(
		Document document,
		Candidate candidate,
		XYZ spacingAxis,
		PipeSpacingSettings settings,
		out string error)
	{
		error = string.Empty;
		Parameter outsideParameter = candidate.SeedPipe.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER);
		double outsideDiameter = outsideParameter?.AsDouble() ?? 0.0;
		if (outsideDiameter <= Epsilon)
		{
			outsideDiameter = PipeSpacingGeometry.ProjectExtent(candidate.SeedPipe.get_BoundingBox(null), spacingAxis);
			candidate.Warnings.Add("Pipe không có OD parameter hợp lệ; đã dùng geometry envelope.");
		}
		Parameter nominalParameter = candidate.SeedPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
		double nominalDiameter = nominalParameter?.AsDouble() ?? 0.0;
		if (outsideDiameter <= Epsilon)
		{
			error = $"Pipe {candidate.SeedPipe.Id.Value} không có đường kính ngoài hoặc geometry hợp lệ.";
			return false;
		}
		if (nominalDiameter <= Epsilon)
		{
			nominalDiameter = outsideDiameter;
			candidate.Warnings.Add("Không đọc được DN; bảng DN sẽ dùng OD thay thế.");
		}

		double effectiveDiameter = outsideDiameter;
		if (settings.IncludeInsulation)
		{
			double maximumThickness = InsulationLiningBase.GetInsulationIds(document, candidate.SeedPipe.Id)
				.Select(document.GetElement)
				.OfType<PipeInsulation>()
				.Select(insulation => insulation.Thickness)
				.DefaultIfEmpty(0.0)
				.Max();
			effectiveDiameter += maximumThickness * 2.0;
		}
		if (settings.IncludeFittingEnvelope)
		{
			double fittingEnvelope = candidate.Cluster.RigidElementIds
				.Select(document.GetElement)
				.Where(element => element != null && !(element is Pipe))
				.Select(element => PipeSpacingGeometry.ProjectExtent(element.get_BoundingBox(null), spacingAxis))
				.DefaultIfEmpty(0.0)
				.Max();
			effectiveDiameter = Math.Max(effectiveDiameter, fittingEnvelope);
		}

		candidate.NominalDiameter = nominalDiameter;
		candidate.EffectiveDiameter = effectiveDiameter;
		return true;
	}

	private static PipeSpacingAnalysisRow ExcludedRow(Pipe pipe, string status, string warning)
	{
		return new PipeSpacingAnalysisRow
		{
			Key = pipe.Id.Value,
			PipeId = pipe.Id.Value.ToString(),
			Status = status,
			Warning = warning ?? string.Empty
		};
	}

	private static double ToInternal(double millimetres)
	{
		return UnitUtils.ConvertToInternalUnits(millimetres, UnitTypeId.Millimeters);
	}

	private static double FromInternal(double internalValue)
	{
		return UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.Millimeters);
	}
}
