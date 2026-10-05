using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using VP_RevitAPI.State;

namespace VP_RevitAPI.Helper.ThreeDSync;

public static class ThreeDSyncService
{
	private const double ZoomPaddingRatio = 0.1;

	private const double MinimumZoomSpan = 1E-06;

	private static UIApplication? _subscribedApplication;

	public static bool Start(UIApplication uiApplication, Document document, ElementId planViewId, IReadOnlyList<ElementId> view3DIds)
	{
		if (uiApplication == null || document == null || planViewId == null || view3DIds == null || view3DIds.Count == 0)
		{
			return false;
		}
		ThreeDSyncSessionStore.Save(new ThreeDSyncSession
		{
			DocumentKey = ThreeDSyncSessionStore.GetDocumentKey(document),
			PlanViewId = planViewId,
			View3DIds = view3DIds.ToList(),
			Corner1 = XYZ.Zero,
			Corner2 = XYZ.Zero
		});
		EnsureSubscribed(uiApplication);
		return true;
	}

	public static bool Stop(UIApplication uiApplication, Document? document)
	{
		ThreeDSyncSession session;
		bool hadSession = document != null && ThreeDSyncSessionStore.TryGet(document, out session);
		ThreeDSyncSessionStore.Clear();
		uiApplication.Idling -= OnIdling;
		if (_subscribedApplication != null)
		{
			if (_subscribedApplication != uiApplication)
			{
				_subscribedApplication.Idling -= OnIdling;
			}
			_subscribedApplication = null;
		}
		return hadSession;
	}

	private static void EnsureSubscribed(UIApplication uiApplication)
	{
		if (_subscribedApplication != uiApplication)
		{
			if (_subscribedApplication != null)
			{
				_subscribedApplication.Idling -= OnIdling;
			}
			_subscribedApplication = uiApplication;
			uiApplication.Idling += OnIdling;
		}
	}

	private static void OnIdling(object? sender, IdlingEventArgs e)
	{
		if (!(sender is UIApplication { ActiveUIDocument: { Document: var document } uiDocument } uiApplication))
		{
			return;
		}
		if (!ThreeDSyncSessionStore.TryGet(document, out ThreeDSyncSession session) || session == null)
		{
			Stop(uiApplication, document);
			return;
		}
		UIView planUiView = FindOpenUIView(uiDocument, session.PlanViewId);
		if (planUiView == null)
		{
			Stop(uiApplication, document);
			return;
		}
		IList<XYZ> corners = planUiView.GetZoomCorners();
		if (corners.Count != 2)
		{
			return;
		}
		XYZ corner1 = corners[0];
		XYZ corner2 = corners[1];
		if (AreSame(corner1, session.Corner1) && AreSame(corner2, session.Corner2))
		{
			return;
		}
		using Transaction transaction = new Transaction(document, "Sync 3D Section Boxes");
		transaction.Start();
		int updatedCount = 0;
		Dictionary<ElementId, BoundingBoxXYZ> updatedSectionBoxes = new Dictionary<ElementId, BoundingBoxXYZ>();
		foreach (ElementId viewId in session.View3DIds.Distinct())
		{
			if (document.GetElement(viewId) is View3D { IsTemplate: false, IsPerspective: false } view3D)
			{
				if (!view3D.IsSectionBoxActive)
				{
					view3D.IsSectionBoxActive = true;
				}
				BoundingBoxXYZ currentSectionBox = view3D.GetSectionBox();
				BoundingBoxXYZ updatedSectionBox = ThreeDSyncSectionBoxMapper.UpdateXY(currentSectionBox, corner1, corner2);
				view3D.SetSectionBox(updatedSectionBox);
				updatedCount++;
				updatedSectionBoxes[viewId] = updatedSectionBox;
			}
		}
		transaction.Commit();
		if (updatedCount == 0)
		{
			Stop(uiApplication, document);
			return;
		}
		int zoomedCount = 0;
		foreach (KeyValuePair<ElementId, BoundingBoxXYZ> updatedSectionBox2 in updatedSectionBoxes)
		{
			ElementId viewId2 = updatedSectionBox2.Key;
			BoundingBoxXYZ sectionBox = updatedSectionBox2.Value;
			UIView uiView = FindOpenUIView(uiDocument, viewId2);
			if (uiView == null)
			{
				continue;
			}
			try
			{
				if (document.GetElement(viewId2) is View3D view3D2 && TryCreateZoomRectangle(view3D2, sectionBox, out XYZ rectangleMin, out XYZ rectangleMax))
				{
					uiView.ZoomAndCenterRectangle(rectangleMin, rectangleMax);
				}
				else
				{
					uiView.ZoomToFit();
				}
				zoomedCount++;
			}
			catch
			{
				try
				{
					uiView.ZoomToFit();
					zoomedCount++;
				}
				catch
				{
				}
			}
		}
		if (zoomedCount == 0)
		{
			Stop(uiApplication, document);
			return;
		}
		session.Corner1 = corner1;
		session.Corner2 = corner2;
	}

	private static UIView? FindOpenUIView(UIDocument uiDocument, ElementId viewId)
	{
		foreach (UIView uiView in uiDocument.GetOpenUIViews())
		{
			if (uiView.ViewId == viewId)
			{
				return uiView;
			}
		}
		return null;
	}

	private static bool TryCreateZoomRectangle(View3D view3D, BoundingBoxXYZ sectionBox, out XYZ rectangleMin, out XYZ rectangleMax)
	{
		rectangleMin = XYZ.Zero;
		rectangleMax = XYZ.Zero;
		ViewOrientation3D orientation;
		try
		{
			orientation = view3D.GetOrientation();
		}
		catch
		{
			return false;
		}
		XYZ up = orientation.UpDirection;
		XYZ forward = orientation.ForwardDirection;
		if (up == null || forward == null)
		{
			return false;
		}
		if (up.GetLength() <= 1E-06 || forward.GetLength() <= 1E-06)
		{
			return false;
		}
		up = up.Normalize();
		forward = forward.Normalize();
		XYZ right = forward.CrossProduct(up);
		if (right.GetLength() <= 1E-06)
		{
			return false;
		}
		right = right.Normalize();
		XYZ centerLocal = new XYZ((sectionBox.Min.X + sectionBox.Max.X) * 0.5, (sectionBox.Min.Y + sectionBox.Max.Y) * 0.5, (sectionBox.Min.Z + sectionBox.Max.Z) * 0.5);
		XYZ centerWorld = sectionBox.Transform.OfPoint(centerLocal);
		XYZ[] worldCorners = GetWorldCorners(sectionBox);
		double minU = double.MaxValue;
		double minV = double.MaxValue;
		double maxU = double.MinValue;
		double maxV = double.MinValue;
		XYZ[] array = worldCorners;
		foreach (XYZ worldCorner in array)
		{
			XYZ delta = worldCorner.Subtract(centerWorld);
			double u = delta.DotProduct(right);
			double v = delta.DotProduct(up);
			if (!IsFinite(u) || !IsFinite(v))
			{
				return false;
			}
			minU = Math.Min(minU, u);
			minV = Math.Min(minV, v);
			maxU = Math.Max(maxU, u);
			maxV = Math.Max(maxV, v);
		}
		double width = maxU - minU;
		double height = maxV - minV;
		if (width <= 1E-06 || height <= 1E-06)
		{
			return false;
		}
		double paddingU = width * 0.1;
		double paddingV = height * 0.1;
		minU -= paddingU;
		minV -= paddingV;
		maxU += paddingU;
		maxV += paddingV;
		rectangleMin = centerWorld.Add(right.Multiply(minU)).Add(up.Multiply(minV));
		rectangleMax = centerWorld.Add(right.Multiply(maxU)).Add(up.Multiply(maxV));
		return rectangleMin.DistanceTo(rectangleMax) > 1E-06;
	}

	private static XYZ[] GetWorldCorners(BoundingBoxXYZ sectionBox)
	{
		XYZ min = sectionBox.Min;
		XYZ max = sectionBox.Max;
		Transform transform = sectionBox.Transform;
		return new XYZ[8]
		{
			transform.OfPoint(new XYZ(min.X, min.Y, min.Z)),
			transform.OfPoint(new XYZ(min.X, min.Y, max.Z)),
			transform.OfPoint(new XYZ(min.X, max.Y, min.Z)),
			transform.OfPoint(new XYZ(min.X, max.Y, max.Z)),
			transform.OfPoint(new XYZ(max.X, min.Y, min.Z)),
			transform.OfPoint(new XYZ(max.X, min.Y, max.Z)),
			transform.OfPoint(new XYZ(max.X, max.Y, min.Z)),
			transform.OfPoint(new XYZ(max.X, max.Y, max.Z))
		};
	}

	private static bool IsFinite(double value)
	{
		return !double.IsNaN(value) && !double.IsInfinity(value);
	}

	private static bool AreSame(XYZ left, XYZ right)
	{
		return left.DistanceTo(right) <= 1E-09;
	}
}
