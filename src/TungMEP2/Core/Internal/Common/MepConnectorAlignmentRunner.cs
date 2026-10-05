using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using VP_RevitAPI.Helper;

namespace VP_RevitAPI.Internal.Common;

internal enum MepConnectorAlignmentMode
{
	MoveConnect,
	MoveConnectAlign,
	AlignIn3D
}

internal static class MepConnectorAlignmentRunner
{
	private const double DirectionTolerance = 0.999;
	private const double DistanceTolerance = 1E-06;
	private const double AngleTolerance = 1E-07;

	private sealed class MepElementSelectionFilter : ISelectionFilter
	{
		public bool AllowElement(Element element)
		{
			if (!(element is MEPCurve) &&
			    !(element is FamilyInstance) &&
			    !(element is FabricationPart))
			{
				return false;
			}

			return MepConnectedClusterCollector
				.GetPhysicalConnectors(element).Count > 0;
		}

		public bool AllowReference(Reference reference, XYZ position)
		{
			return true;
		}
	}

	private readonly record struct ConnectorPair(Connector Fixed, Connector Moving);

	internal static void Run(UIDocument uiDocument, Document document, MepConnectorAlignmentMode mode)
	{
		MepElementSelectionFilter filter = new MepElementSelectionFilter();
		while (true)
		{
			Reference fixedReference = uiDocument.Selection.PickObject(
				ObjectType.Element,
				filter,
				"Chọn Pipe/MEP element làm mốc.");
			Reference movingReference = uiDocument.Selection.PickObject(
				ObjectType.Element,
				filter,
				"Chọn Pipe/MEP element thuộc cụm cần dịch chuyển.");

			Element fixedElement = document.GetElement(fixedReference);
			Element movingElement = document.GetElement(movingReference);
			if (fixedElement.Id == movingElement.Id)
			{
				TaskDialog.Show(GetDialogTitle(mode), "Hai phần tử phải khác nhau.");
				continue;
			}

			if (!TryExecuteOnce(document, fixedElement, movingElement, mode, out string error))
			{
				TaskDialog.Show(GetDialogTitle(mode), error);
			}
		}
	}

	private static bool TryExecuteOnce(
		Document document,
		Element fixedElement,
		Element movingElement,
		MepConnectorAlignmentMode mode,
		out string error)
	{
		error = string.Empty;
		if (movingElement.Pinned)
		{
			error = "Phần tử cần dịch chuyển đang bị Pin.";
			return false;
		}

		if (!TryFindClosestPair(
				fixedElement,
				movingElement,
				mode,
				out ConnectorPair pair,
				out MepConnectorPairDecision decision,
				out error))
		{
			return false;
		}

		List<Connector> fixedPhysicalConnectors = MepConnectedClusterCollector
			.GetPhysicalConnectors(fixedElement);
		bool fixedHasOpenEnd = fixedPhysicalConnectors.Any(connector =>
			connector.ConnectorType == ConnectorType.End &&
			!connector.IsConnected);
		bool movingSelectedHasOpenEnd = movingElement is Pipe movingPipe &&
			MepConnectedClusterCollector.GetPhysicalConnectors(movingPipe)
				.Any(connector =>
					connector.ConnectorType == ConnectorType.End &&
					connector.Domain == Domain.DomainPiping &&
					!connector.IsConnected);
		MepAlignmentConnectionStrategy strategy =
			MepPipeBodyConnectionPolicy.ChooseStrategy(
				mode,
				decision.ShouldConnect,
				fixedElement is Pipe,
				fixedHasOpenEnd,
				movingElement is Pipe,
				movingSelectedHasOpenEnd);
		MepPipeBodyConnectionPlan bodyPlan = null;
		if (strategy == MepAlignmentConnectionStrategy.PipeBody &&
		    !MepPipeBodyConnectionService.TryCreatePlan(
			    document,
			    (Pipe)fixedElement,
			    movingElement,
			    out bodyPlan,
			    out error))
		{
			return false;
		}

		ICollection<ElementId> movingElementIds;
		if (MepConnectorAlignmentPolicy.ShouldUseConnectedCluster(mode))
		{
			if (!MepConnectedClusterCollector.TryCollect(
					movingElement,
					fixedElement,
					out MepConnectedCluster cluster,
					out error))
			{
				return false;
			}

			movingElementIds = cluster.ElementIds;
		}
		else
		{
			movingElementIds = new List<ElementId> { movingElement.Id };
		}

		using Transaction transaction = new Transaction(document, GetTransactionName(mode));
		transaction.Start();
		try
		{
			if (strategy == MepAlignmentConnectionStrategy.PipeBody)
			{
				if (!MepPipeBodyConnectionService.TryExecute(
						document,
						bodyPlan,
						movingElementIds,
						out FamilyInstance _,
						out error))
				{
					transaction.RollBack();
					return false;
				}

				transaction.Commit();
				return true;
			}

			Connector fixedConnector = pair.Fixed;
			Connector movingConnector = pair.Moving;
			if (mode != MepConnectorAlignmentMode.MoveConnect)
			{
				if (!TryAlignConnectorOrientation(
						document,
						movingElementIds,
						movingElement,
						fixedConnector,
						movingConnector,
						out movingConnector,
						out error))
				{
					transaction.RollBack();
					return false;
				}
			}

			XYZ moveVector;
			if (mode == MepConnectorAlignmentMode.AlignIn3D)
			{
				moveVector = MepAlignmentGeometry.ComputeAxisAlignmentMove(
					fixedConnector.Origin,
					movingConnector.Origin,
					fixedConnector.CoordinateSystem.BasisZ);
			}
			else
			{
				moveVector = fixedConnector.Origin - movingConnector.Origin;
			}

			if (moveVector.GetLength() > DistanceTolerance)
			{
				XYZ expectedMovingOrigin =
					movingConnector.Origin + moveVector;
				ElementTransformUtils.MoveElements(
					document,
					movingElementIds,
					moveVector);
				document.Regenerate();
				movingConnector = FindMatchingConnector(
					movingElement,
					movingConnector,
					expectedMovingOrigin);
			}

			if (mode != MepConnectorAlignmentMode.AlignIn3D)
			{
				if (movingConnector == null ||
				    movingConnector.Origin.DistanceTo(fixedConnector.Origin) > DistanceTolerance * 10.0)
				{
					error = "Không thể đưa hai connector về cùng vị trí.";
					transaction.RollBack();
					return false;
				}

				if (!AreOpposite(fixedConnector, movingConnector))
				{
					error = decision.ShouldConnect
						? "Hai connector chưa đối hướng nên không thể kết nối an toàn."
						: "Không thể căn hai connector về hướng đối diện.";
					transaction.RollBack();
					return false;
				}

				if (strategy == MepAlignmentConnectionStrategy.Endpoint &&
				    MepConnectorAlignmentPolicy.ShouldCreateConnection(
						mode,
						decision))
				{
					Connect(document, fixedConnector, movingConnector);
				}
			}

			transaction.Commit();
			return true;
		}
		catch (Exception exception)
		{
			if (transaction.GetStatus() == TransactionStatus.Started)
			{
				transaction.RollBack();
			}

			error = "Không thể hoàn thành thao tác: " + exception.Message;
			return false;
		}
	}

	private static bool TryFindClosestPair(
		Element fixedElement,
		Element movingElement,
		MepConnectorAlignmentMode mode,
		out ConnectorPair pair,
		out MepConnectorPairDecision decision,
		out string error)
	{
		pair = default;
		decision = default;
		error = string.Empty;
		List<Connector> fixedConnectors = MepConnectedClusterCollector
			.GetPhysicalConnectors(fixedElement);
		List<Connector> movingConnectors = MepConnectedClusterCollector
			.GetPhysicalConnectors(movingElement);
		if (fixedConnectors.Count == 0 || movingConnectors.Count == 0)
		{
			error = "Mỗi phần tử phải có ít nhất một connector vật lý.";
			return false;
		}

		List<MepConnectorPairCandidate> candidates =
			new List<MepConnectorPairCandidate>();
		for (int fixedIndex = 0; fixedIndex < fixedConnectors.Count; fixedIndex++)
		{
			Connector fixedConnector = fixedConnectors[fixedIndex];
			for (int movingIndex = 0;
				movingIndex < movingConnectors.Count;
				movingIndex++)
			{
				Connector movingConnector = movingConnectors[movingIndex];
				candidates.Add(new MepConnectorPairCandidate(
					fixedIndex,
					movingIndex,
					fixedConnector.IsConnected,
					movingConnector.IsConnected,
					AreDomainsCompatible(fixedConnector, movingConnector),
					AreOpposite(fixedConnector, movingConnector),
					fixedConnector.Origin.DistanceTo(movingConnector.Origin)));
			}
		}

		bool requireExistingAlignment =
			mode == MepConnectorAlignmentMode.MoveConnect;
		bool allowConnectedFallback =
			MepConnectorAlignmentPolicy.ShouldUseConnectedCluster(mode);
		if (!MepConnectorAlignmentPolicy.TryChoosePair(
				candidates,
				requireExistingAlignment,
				allowConnectedFallback,
				out decision))
		{
			error = requireExistingAlignment
				? "Không tìm thấy cặp connector trống cùng hệ và đã đối hướng. Hãy dùng MC-Align nếu cần tự xoay."
				: "Không tìm thấy cặp connector vật lý cùng domain.";
			return false;
		}

		pair = new ConnectorPair(
			fixedConnectors[decision.FixedIndex],
			movingConnectors[decision.MovingIndex]);
		return true;
	}

	private static bool TryAlignConnectorOrientation(
		Document document,
		ICollection<ElementId> movingElementIds,
		Element movingElement,
		Connector fixedConnector,
		Connector originalMovingConnector,
		out Connector alignedMovingConnector,
		out string error)
	{
		alignedMovingConnector = originalMovingConnector;
		error = string.Empty;
		XYZ pivot = originalMovingConnector.Origin;
		XYZ movingDirection = originalMovingConnector.CoordinateSystem.BasisZ.Normalize();
		XYZ targetDirection = fixedConnector.CoordinateSystem.BasisZ.Negate().Normalize();
		double angle = movingDirection.AngleTo(targetDirection);
		if (angle > AngleTolerance)
		{
			XYZ axis = movingDirection.CrossProduct(targetDirection);
			if (axis.GetLength() < DistanceTolerance)
			{
				axis = originalMovingConnector.CoordinateSystem.BasisX;
			}
			if (axis == null || axis.GetLength() < DistanceTolerance)
			{
				axis = originalMovingConnector.CoordinateSystem.BasisY;
			}
			if (axis == null || axis.GetLength() < DistanceTolerance)
			{
				error = "Không xác định được trục xoay connector.";
				return false;
			}

			RotateElements(
				document,
				movingElementIds,
				pivot,
				axis,
				angle);
			alignedMovingConnector = FindMatchingConnector(movingElement, originalMovingConnector, pivot);
			if (alignedMovingConnector == null || !AreOpposite(fixedConnector, alignedMovingConnector))
			{
				RotateElements(
					document,
					movingElementIds,
					pivot,
					axis,
					-2.0 * angle);
				alignedMovingConnector = FindMatchingConnector(movingElement, originalMovingConnector, pivot);
			}
		}

		if (alignedMovingConnector == null || !AreOpposite(fixedConnector, alignedMovingConnector))
		{
			error = "Không thể xoay connector thứ hai về hướng đối diện connector mốc.";
			return false;
		}

		if (NeedsRollAlignment(fixedConnector, alignedMovingConnector))
		{
			XYZ rollAxis = fixedConnector.CoordinateSystem.BasisZ.Negate().Normalize();
			XYZ movingX = alignedMovingConnector.CoordinateSystem.BasisX;
			XYZ targetX = fixedConnector.CoordinateSystem.BasisX;
			double rollAngle = MepAlignmentGeometry.ComputeSignedAngle(movingX, targetX, rollAxis);
			if (Math.Abs(rollAngle) > AngleTolerance)
			{
				RotateElements(
					document,
					movingElementIds,
					pivot,
					rollAxis,
					rollAngle);
				alignedMovingConnector = FindMatchingConnector(movingElement, originalMovingConnector, pivot);
			}
		}

		return alignedMovingConnector != null;
	}

	private static void RotateElements(
		Document document,
		ICollection<ElementId> elementIds,
		XYZ pivot,
		XYZ axisDirection,
		double angle)
	{
		XYZ axis = axisDirection.Normalize();
		Line rotationAxis = Line.CreateBound(pivot, pivot + axis);
		ElementTransformUtils.RotateElements(
			document,
			elementIds,
			rotationAxis,
			angle);
		document.Regenerate();
	}

	private static Connector FindMatchingConnector(Element owner, Connector original, XYZ expectedOrigin)
	{
		List<Connector> candidates = MepConnectedClusterCollector
			.GetPhysicalConnectors(owner)
			.Where(connector => AreDomainsCompatible(original, connector))
			.ToList();
		return candidates
			.OrderBy(connector => connector.Origin.DistanceTo(expectedOrigin))
			.FirstOrDefault();
	}

	private static bool AreDomainsCompatible(Connector first, Connector second)
	{
		return first.Domain == second.Domain;
	}

	private static bool AreOpposite(Connector fixedConnector, Connector movingConnector)
	{
		try
		{
			XYZ fixedDirection = fixedConnector.CoordinateSystem.BasisZ.Normalize();
			XYZ movingDirection = movingConnector.CoordinateSystem.BasisZ.Normalize();
			return fixedDirection.DotProduct(movingDirection) <= -DirectionTolerance;
		}
		catch
		{
			return false;
		}
	}

	private static bool NeedsRollAlignment(Connector fixedConnector, Connector movingConnector)
	{
		try
		{
			return fixedConnector.Shape != ConnectorProfileType.Round ||
			       movingConnector.Shape != ConnectorProfileType.Round;
		}
		catch
		{
			return false;
		}
	}

	private static void Connect(Document document, Connector fixedConnector, Connector movingConnector)
	{
		if (fixedConnector.Owner is FabricationPart && movingConnector.Owner is FabricationPart)
		{
			FabricationPart.ConnectAndCouple(document, movingConnector, fixedConnector);
			return;
		}

		movingConnector.ConnectTo(fixedConnector);
	}

	private static string GetDialogTitle(MepConnectorAlignmentMode mode)
	{
		return mode switch
		{
			MepConnectorAlignmentMode.MoveConnect => "Move Connect",
			MepConnectorAlignmentMode.MoveConnectAlign => "Move Connect Align",
			_ => "Align MEP 3D"
		};
	}

	private static string GetTransactionName(MepConnectorAlignmentMode mode)
	{
		return "TungMEP2 - " + GetDialogTitle(mode);
	}
}
