using System;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper;

public static class TagUtilities
{
	public static void AutoAdjustTagArrow(Element tagElement)
	{
		AutoAdjustTagArrow(tagElement, LeaderElbowMode.HorizontalFirst);
	}

	public static void AutoAdjustTagArrow(Element tagElement, LeaderElbowMode mode)
	{
		if (tagElement is IndependentTag independentTag)
		{
			AdjustIndependentTag(independentTag, mode);
		}
		else if (tagElement is SpatialElementTag spatialElementTag)
		{
			AdjustSpatialElementTag(spatialElementTag, mode);
		}
	}

	private static void AdjustIndependentTag(IndependentTag tag, LeaderElbowMode mode)
	{
		Document document = tag.Document;
		tag.HasLeader = true;
		tag.LeaderEndCondition = LeaderEndCondition.Free;
		tag.LeadersPresentationMode = LeadersPresentationMode.ShowAll;
		document.Regenerate();
		IList<Reference> references = tag.GetTaggedReferences();
		if (references.Count == 0 || !(document.GetElement(tag.OwnerViewId) is View view))
		{
			return;
		}
		XYZ head = tag.TagHeadPosition;
		XYZ right = view.RightDirection.Normalize();
		XYZ up = view.UpDirection.Normalize();
		XYZ viewDir = view.ViewDirection.Normalize();
		foreach (Reference reference in references)
		{
			XYZ end = XYZ.Zero;
			try
			{
				end = tag.GetLeaderEnd(reference);
			}
			catch
			{
				Element host = document.GetElement(reference);
				if (host == null || !TryGetElementPoint(host, view, out end))
				{
					continue;
				}
			}
			tag.SetLeaderEnd(reference, end);
			XYZ elbow = LeaderElbowCalculator.Calculate(right, up, viewDir, head, end, mode);
			tag.SetLeaderElbow(reference, elbow);
		}
	}

	private static void AdjustSpatialElementTag(SpatialElementTag tag, LeaderElbowMode mode)
	{
		Document document = tag.Document;
		if (!(document.GetElement(tag.OwnerViewId) is View view))
		{
			return;
		}
		TrySetTagProperty(tag, "HasLeader", true);
		TrySetTagProperty(tag, "LeaderEndCondition", LeaderEndCondition.Attached);
		if (!TryGetTaggedElementId(tag, out ElementId taggedId))
		{
			return;
		}
		Element host = document.GetElement(taggedId);
		if (host != null)
		{
			XYZ head = tag.TagHeadPosition;
			if (TryGetElementPoint(host, view, out XYZ end))
			{
				XYZ right = view.RightDirection.Normalize();
				XYZ up = view.UpDirection.Normalize();
				XYZ viewDir = view.ViewDirection.Normalize();
				XYZ elbow = LeaderElbowCalculator.Calculate(right, up, viewDir, head, end, mode);
				TrySetLeaderElbow(tag, elbow);
			}
		}
	}

	private static bool TryGetElementPoint(Element element, View view, out XYZ point)
	{
		if (element.Location is LocationPoint locationPoint)
		{
			point = locationPoint.Point;
			return true;
		}
		if (element.Location is LocationCurve locationCurve)
		{
			XYZ p0 = locationCurve.Curve.GetEndPoint(0);
			XYZ p1 = locationCurve.Curve.GetEndPoint(1);
			point = (p0 + p1) * 0.5;
			return true;
		}
		BoundingBoxXYZ box = element.get_BoundingBox(view) ?? element.get_BoundingBox((View)null);
		if (box != null)
		{
			point = (box.Min + box.Max) * 0.5;
			return true;
		}
		point = XYZ.Zero;
		return false;
	}

	private static bool TryGetFirst<T>(IEnumerable<T> values, out T result)
	{
		using (IEnumerator<T> enumerator = values.GetEnumerator())
		{
			if (enumerator.MoveNext())
			{
				T value = enumerator.Current;
				result = value;
				return true;
			}
		}
		result = default(T);
		return false;
	}

	private static bool TryGetTaggedElementId(SpatialElementTag tag, out ElementId elementId)
	{
		if (TryGetElementIdProperty(tag, "TaggedLocalElementId", out elementId))
		{
			return true;
		}
		if (TryGetElementIdProperty(tag, "TaggedElementId", out elementId))
		{
			return true;
		}
		if (TryGetLinkElementIdProperty(tag, "TaggedElementId", out elementId))
		{
			return true;
		}
		if (TryGetElementIdProperty(tag, "RoomId", out elementId))
		{
			return true;
		}
		if (TryGetElementIdProperty(tag, "AreaId", out elementId))
		{
			return true;
		}
		if (TryGetElementIdProperty(tag, "SpaceId", out elementId))
		{
			return true;
		}
		elementId = ElementId.InvalidElementId;
		return false;
	}

	private static bool TryGetElementIdProperty(object target, string propertyName, out ElementId elementId)
	{
		PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
		if (property == null)
		{
			elementId = ElementId.InvalidElementId;
			return false;
		}
		object value = property.GetValue(target);
		if (value is ElementId id && id != ElementId.InvalidElementId)
		{
			elementId = id;
			return true;
		}
		elementId = ElementId.InvalidElementId;
		return false;
	}

	private static bool TryGetLinkElementIdProperty(object target, string propertyName, out ElementId elementId)
	{
		PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
		if (property == null)
		{
			elementId = ElementId.InvalidElementId;
			return false;
		}
		object value = property.GetValue(target);
		if (value is LinkElementId linkId && linkId.LinkInstanceId == ElementId.InvalidElementId)
		{
			ElementId hostId = linkId.HostElementId;
			if (hostId != ElementId.InvalidElementId)
			{
				elementId = hostId;
				return true;
			}
		}
		elementId = ElementId.InvalidElementId;
		return false;
	}

	private static void TrySetTagProperty(object target, string propertyName, object value)
	{
		PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
		if (!(property == null) && property.CanWrite)
		{
			property.SetValue(target, value);
		}
	}

	private static void TrySetLeaderElbow(object tag, XYZ elbow)
	{
		PropertyInfo property = tag.GetType().GetProperty("LeaderElbow", BindingFlags.Instance | BindingFlags.Public);
		if (property != null && property.CanWrite && property.PropertyType == typeof(XYZ))
		{
			property.SetValue(tag, elbow);
			return;
		}
		MethodInfo method = tag.GetType().GetMethod("SetLeaderElbow", BindingFlags.Instance | BindingFlags.Public, null, new Type[1] { typeof(XYZ) }, null);
		if (method != null)
		{
			method.Invoke(tag, new object[1] { elbow });
		}
	}
}
