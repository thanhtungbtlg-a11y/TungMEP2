using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using VP_RevitAPI.State;

namespace VP_RevitAPI.Internal.Ctn;

internal static class DisconnectVentRunner
{
	internal static void Run(UIDocument uiDocument, Document document)
	{
		ICollection<ElementId> selectionIds = uiDocument.Selection.GetElementIds();
		if (selectionIds.Count == 0)
		{
			TaskDialog.Show("DisconnectVent", "Vui lòng chọn phần tử trước khi chạy lệnh.");
			return;
		}
		string ventSystemName = GetData.FromJson("config", "nameVentPipingSystem")?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(ventSystemName))
		{
			TaskDialog.Show("DisconnectVent", "Không tìm thấy cấu hình 'nameVentPipingSystem' trong Store/config.json.");
			return;
		}
		List<Element> selected = (from element in selectionIds.Select(document.GetElement)
			where element != null
			select element).ToList();
		List<Pipe> ventPipes = (from pipe2 in selected.OfType<Pipe>()
			where IsVentPipe(pipe2, document, ventSystemName)
			select pipe2).ToList();
		List<FamilyInstance> ventFittings = (from fitting2 in selected.OfType<FamilyInstance>()
			where IsVentFitting(fitting2, ventSystemName)
			select fitting2).ToList();
		if (ventPipes.Count == 0 && ventFittings.Count == 0)
		{
			TaskDialog.Show("DisconnectVent", "Danh sách chọn không có ống/phụ kiện thuộc hệ Vent.");
			return;
		}
		int disconnectCount = 0;
		using (TransactionGroup group = new TransactionGroup(document, "Disconnect Vent"))
		{
			group.Start();
			using (Transaction transaction = new Transaction(document, "Disconnect Vent Links"))
			{
				transaction.Start();
				foreach (Pipe pipe in ventPipes)
				{
					disconnectCount += DisconnectCrossSystemConnections(pipe.ConnectorManager.Connectors);
				}
				foreach (FamilyInstance fitting in ventFittings)
				{
					ConnectorSet connectorSet = fitting.MEPModel?.ConnectorManager?.Connectors;
					if (connectorSet != null)
					{
						disconnectCount += DisconnectCrossSystemConnections(connectorSet);
					}
				}
				transaction.Commit();
			}
			group.Assimilate();
		}
		TaskDialog.Show("DisconnectVent", $"Đã ngắt {disconnectCount} kết nối khác hệ.");
	}

	private static bool IsVentPipe(Pipe pipe, Document document, string ventSystemName)
	{
		Category category = pipe.Category;
		if (category == null || category.Id?.Value != -2008044)
		{
			return false;
		}
		if (IsSameName(pipe.MEPSystem?.Name, ventSystemName))
		{
			return true;
		}
		ElementId systemTypeId = pipe.MEPSystem?.GetTypeId();
		if (systemTypeId == null || systemTypeId == ElementId.InvalidElementId)
		{
			return false;
		}
		return IsSameName((document.GetElement(systemTypeId) is PipingSystemType systemType) ? systemType.Name : null, ventSystemName);
	}

	private static bool IsVentFitting(FamilyInstance fitting, string ventSystemName)
	{
		Category category = fitting.Category;
		if (category == null || category.Id?.Value != -2008049)
		{
			return false;
		}
		string systemName = ((Element)fitting).get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsValueString();
		return IsSameName(systemName, ventSystemName);
	}

	private static int DisconnectCrossSystemConnections(ConnectorSet connectorSet)
	{
		int count = 0;
		foreach (Connector connector in connectorSet)
		{
			string ownSystemName = connector.MEPSystem?.Name ?? string.Empty;
			List<Connector> references = connector.AllRefs.Cast<Connector>().ToList();
			foreach (Connector reference in references)
			{
				if (reference != connector && reference.Domain == Domain.DomainPiping)
				{
					string referenceSystemName = reference.MEPSystem?.Name ?? string.Empty;
					if (!IsSameName(referenceSystemName, ownSystemName) && connector.IsConnectedTo(reference))
					{
						connector.DisconnectFrom(reference);
						count++;
					}
				}
			}
		}
		return count;
	}

	private static bool IsSameName(string? first, string? second)
	{
		return string.Equals(first?.Trim(), second?.Trim(), StringComparison.OrdinalIgnoreCase);
	}
}
