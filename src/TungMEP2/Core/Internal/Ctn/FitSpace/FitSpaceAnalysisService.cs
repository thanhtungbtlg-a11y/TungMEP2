using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.Internal.Ctn.FitSpace;

internal static class FitSpaceAnalysisService
{
	internal static bool TryCreatePlan(
		UIDocument uiDocument,
		IReadOnlyCollection<ElementId> selectedIds,
		out FitSpacePlan plan,
		out FitSpaceAnalysisReport report,
		out string error)
	{
		plan = new FitSpacePlan(Array.Empty<FitSpacePlanEntry>());
		report = new FitSpaceAnalysisReport(Array.Empty<FitSpaceReportRow>(), 0);
		error = string.Empty;
		Document document = uiDocument?.Document;
		if (document == null || document.IsFamilyDocument)
		{
			error = "FitSpace chỉ chạy trong project Revit.";
			return false;
		}
		if (document.IsReadOnly)
		{
			error = "Document đang ở chế độ chỉ đọc.";
			return false;
		}

		if (!FitSpaceCollector.TryCollectMany(document, selectedIds, out IReadOnlyList<FitSpaceCluster> clusters,
			out FitSpaceCollectionReport collectionReport, out error))
		{
			report = new FitSpaceAnalysisReport(collectionReport.Skipped, 0);
			return false;
		}

		List<FitSpacePlanEntry> entries = new List<FitSpacePlanEntry>();
		List<FitSpaceReportRow> excluded = new List<FitSpaceReportRow>(collectionReport.Skipped);
		int alreadyNormalized = 0;
		foreach (FitSpaceCluster cluster in clusters)
		{
			if (!CanEditCluster(document, cluster, out string reason))
			{
				excluded.Add(new FitSpaceReportRow(cluster.Key, reason));
				continue;
			}

			if (!FitSpaceGeometry.TryBuildTarget(document, cluster, out FitSpaceTargetGeometry target, out reason))
			{
				excluded.Add(new FitSpaceReportRow(cluster.Key, reason));
				continue;
			}

			if (target.IsAlreadyNormalized)
			{
				alreadyNormalized++;
			}
			entries.Add(new FitSpacePlanEntry(cluster, target));
		}

		plan = new FitSpacePlan(entries);
		report = new FitSpaceAnalysisReport(excluded, alreadyNormalized);
		if (entries.Count == 0)
		{
			error = excluded.LastOrDefault()?.Reason ?? "Không có cụm FitSpace hợp lệ.";
			return false;
		}
		return true;
	}

	private static bool CanEditCluster(Document document, FitSpaceCluster cluster, out string error)
	{
		error = string.Empty;
		ElementId activeDesignOptionId = DesignOption.GetActiveDesignOptionId(document);
		foreach (FitSpaceElementSnapshot snapshot in cluster.ElementSnapshots)
		{
			Element element = document.GetElement(snapshot.Id);
			if (element == null || element.UniqueId != snapshot.UniqueId)
			{
				error = "Một phần tử trong cụm đã bị thay đổi hoặc thay thế.";
				return false;
			}
			if (element.Pinned)
			{
				error = "Cụm có phần tử đang Pin.";
				return false;
			}
			if (element.GroupId != ElementId.InvalidElementId)
			{
				error = "Cụm có phần tử nằm trong Group.";
				return false;
			}
			ElementId designOptionId = element.DesignOption?.Id ?? ElementId.InvalidElementId;
			if (designOptionId != ElementId.InvalidElementId && designOptionId != activeDesignOptionId)
			{
				error = "Cụm nằm trong design option không hoạt động.";
				return false;
			}
			if (document.IsWorkshared &&
				WorksharingUtils.GetCheckoutStatus(document, element.Id) == CheckoutStatus.OwnedByOtherUser)
			{
				error = "Cụm có phần tử đang do người dùng khác sở hữu.";
				return false;
			}
			if (!element.IsModifiable)
			{
				error = "Cụm có phần tử không thể chỉnh sửa trong trạng thái hiện tại.";
				return false;
			}
		}
		return true;
	}
}
