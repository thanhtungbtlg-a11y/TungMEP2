using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace VP_RevitAPI.Helper.Layout.AutoDimFamilyInstancePoint;

internal sealed class AutoDimFamilyInstancePointWallService
{
	private readonly Document _document;

	private readonly double _maxDimDistanceInternal;

	public AutoDimFamilyInstancePointWallService(Document document, double maxDimDistanceMillimeters)
	{
		_document = document;
		_maxDimDistanceInternal = UnitUtils.ConvertToInternalUnits(maxDimDistanceMillimeters, UnitTypeId.Millimeters);
	}

	public int Execute(IReadOnlyList<FamilyInstance> points, AutoDimDatabaseSnapshot snapshot)
	{
		int createdCount = 0;
		using TransactionGroup group = new TransactionGroup(_document, "Auto DIM FamilyInstance Point Wall");
		group.Start();
		foreach (FamilyInstance point in points)
		{
			createdCount += DimSinglePoint(point, snapshot, 0.5);
		}
		group.Assimilate();
		return createdCount;
	}

	private int DimSinglePoint(FamilyInstance sk, AutoDimDatabaseSnapshot snapshot, double offsetDimension)
	{
		if (!(sk.Location is LocationPoint locationPoint))
		{
			return 0;
		}
		List<Reference> centerReferences = new List<Reference>
		{
			sk.GetReferences(FamilyInstanceReferenceType.CenterFrontBack).FirstOrDefault(),
			sk.GetReferences(FamilyInstanceReferenceType.CenterLeftRight).FirstOrDefault()
		};
		XYZ skPoint = locationPoint.Point;
		XYZ skPointOnXy = skPoint.PullOnto();
		List<Line> probeLines = CreateProbeLines(skPointOnXy, 100.0);
		List<object> objectX = new List<object>();
		List<object> objectY = new List<object>();
		for (int index = 0; index < probeLines.Count; index++)
		{
			object nearest = FindNearestObjectOnProbe(probeLines[index], skPointOnXy, snapshot);
			if (nearest != null)
			{
				if (index <= 1)
				{
					objectX.Add(nearest);
				}
				else
				{
					objectY.Add(nearest);
				}
			}
		}
		Line lineX = probeLines[0];
		Line lineY = probeLines[2];
		Line lineXMove = lineX.CreateTransformed(Transform.CreateTranslation(new XYZ(0.0, 1.0, 0.0).Multiply(offsetDimension))) as Line;
		Line lineYMove = lineY.CreateTransformed(Transform.CreateTranslation(new XYZ(1.0, 0.0, 0.0).Multiply(offsetDimension))) as Line;
		int createdCount = 0;
		foreach (Reference skReference in centerReferences)
		{
			if (skReference == null)
			{
				continue;
			}
			using Transaction transaction = new Transaction(_document, "Auto DIM Point");
			transaction.Start();
			ReferenceArray referenceArray = new ReferenceArray();
			referenceArray.Append(skReference);
			SketchPlane sketchPlane = SketchPlane.Create(_document, skReference);
			XYZ planeNormal = sketchPlane.GetPlane().Normal.Normalize();
			double dotProduct = planeNormal.DotProduct(lineX.Direction.Normalize());
			List<object> targets = ((Math.Abs(dotProduct) == 1.0) ? objectX : objectY);
			foreach (object target in targets)
			{
				if (1 == 0)
				{
				}
				Reference reference = ((target is Wall wall) ? GetReferenceFromWall(wall, skPoint, planeNormal, snapshot.ArcLink, snapshot.ArcTransform) : ((!(target is FamilyInstance family)) ? null : GetReferenceFromFamily(family, skPoint, planeNormal, snapshot.StrLink, snapshot.StrTransform)));
				if (1 == 0)
				{
				}
				Reference targetReference = reference;
				if (targetReference != null)
				{
					referenceArray.Append(targetReference);
				}
			}
			if (referenceArray.Size > 1)
			{
				if (Math.Abs(planeNormal.DotProduct(lineXMove.Direction.Normalize())) == 1.0)
				{
					_document.Create.NewDimension(_document.ActiveView, lineXMove, referenceArray);
				}
				else
				{
					_document.Create.NewDimension(_document.ActiveView, lineYMove, referenceArray);
				}
				createdCount++;
			}
			transaction.Commit();
		}
		return createdCount;
	}

	private object? FindNearestObjectOnProbe(Line probe, XYZ skPointOnXy, AutoDimDatabaseSnapshot snapshot)
	{
		double nearestDistance = double.MaxValue;
		object nearest = null;
		foreach (Wall wall in snapshot.Walls)
		{
			if (!(wall.Location is LocationCurve locationCurve))
			{
				continue;
			}
			Curve transformed = locationCurve.Curve.CreateTransformed(snapshot.ArcTransform).PullOnto();
			XYZ intersection = probe.FindIntersect(transformed);
			if (intersection != null)
			{
				double distance = intersection.DistanceTo(skPointOnXy);
				if (distance < nearestDistance)
				{
					nearestDistance = distance;
					nearest = wall;
				}
			}
		}
		foreach (FamilyInstance element in snapshot.Columns)
		{
			UpdateNearestByFamilyAxisLine(probe, skPointOnXy, snapshot.StrTransform, element, ref nearestDistance, ref nearest);
		}
		foreach (FamilyInstance element2 in snapshot.Framings)
		{
			UpdateNearestByFamilyAxisLine(probe, skPointOnXy, snapshot.StrTransform, element2, ref nearestDistance, ref nearest);
		}
		if (nearestDistance <= _maxDimDistanceInternal)
		{
			return nearest;
		}
		return null;
	}

	private static void UpdateNearestByFamilyAxisLine(Line probe, XYZ skPointOnXy, Transform linkTransform, FamilyInstance element, ref double nearestDistance, ref object? nearest)
	{
		PlanarFace bottomFace = GetBottomFace(element);
		if (bottomFace == null)
		{
			return;
		}
		List<Line> axes = GetAndConnectEqualLengthLinePairs(bottomFace);
		foreach (Line axis in axes)
		{
			Curve transformedAxis = axis.CreateTransformed(linkTransform).PullOnto();
			XYZ intersection = transformedAxis.FindIntersect(probe);
			if (intersection != null)
			{
				double distance = intersection.DistanceTo(skPointOnXy);
				if (distance < nearestDistance)
				{
					nearestDistance = distance;
					nearest = element;
					break;
				}
			}
		}
	}

	private static List<Line> CreateProbeLines(XYZ originOnXy, double distance)
	{
		List<XYZ> directions = new List<XYZ>
		{
			new XYZ(1.0, 0.0, 0.0),
			new XYZ(-1.0, 0.0, 0.0),
			new XYZ(0.0, 1.0, 0.0),
			new XYZ(0.0, -1.0, 0.0)
		};
		return directions.Select((XYZ direction) => Line.CreateBound(originOnXy, originOnXy + direction.Multiply(distance))).ToList();
	}

	private static PlanarFace? GetBottomFace(Element element)
	{
		GeometryElement geometryElement = element.get_Geometry(new Options());
		PlanarFace bottomFace = null;
		double minElevation = double.MaxValue;
		foreach (GeometryObject geometryObject in geometryElement)
		{
			if (1 == 0)
			{
			}
			IEnumerable<Solid> enumerable = ((geometryObject is Solid solid) ? new Solid[1] { solid } : ((!(geometryObject is GeometryInstance instance)) ? Enumerable.Empty<Solid>() : instance.GetInstanceGeometry().OfType<Solid>()));
			if (1 == 0)
			{
			}
			IEnumerable<Solid> solids = enumerable;
			foreach (Solid solid2 in solids)
			{
				foreach (PlanarFace planarFace in solid2.Faces.OfType<PlanarFace>())
				{
					if (planarFace.FaceNormal.IsAlmostEqualTo(XYZ.BasisZ.Negate()))
					{
						BoundingBoxUV uvBox = planarFace.GetBoundingBox();
						UV midpointUv = (uvBox.Min + uvBox.Max) / 2.0;
						XYZ midpoint = planarFace.Evaluate(midpointUv);
						if (midpoint.Z < minElevation)
						{
							minElevation = midpoint.Z;
							bottomFace = planarFace;
						}
					}
				}
			}
		}
		return bottomFace;
	}

	private static List<Line> GetAndConnectEqualLengthLinePairs(PlanarFace planarFace)
	{
		List<Line> connectingLines = new List<Line>();
		Dictionary<double, List<Edge>> edgeGroups = new Dictionary<double, List<Edge>>();
		foreach (EdgeArray edgeArray in planarFace.EdgeLoops)
		{
			foreach (Edge edge in edgeArray)
			{
				double length = edge.ApproximateLength;
				double? existingKey = null;
				foreach (double key in edgeGroups.Keys)
				{
					if (Math.Abs(key - length) < 1E-06)
					{
						existingKey = key;
						break;
					}
				}
				if (existingKey.HasValue)
				{
					edgeGroups[existingKey.Value].Add(edge);
					continue;
				}
				edgeGroups[length] = new List<Edge> { edge };
			}
		}
		List<(Line, Line)> equalLengthPairs = new List<(Line, Line)>();
		foreach (KeyValuePair<double, List<Edge>> group in edgeGroups)
		{
			if (group.Value.Count < 2)
			{
				continue;
			}
			for (int i = 0; i < group.Value.Count - 1; i++)
			{
				for (int j = i + 1; j < group.Value.Count; j++)
				{
					if (!(group.Value[i].AsCurve() is Line lineA) || !(group.Value[j].AsCurve() is Line lineB))
					{
						continue;
					}
					equalLengthPairs.Add((lineA, lineB));
					if (equalLengthPairs.Count != 2)
					{
						continue;
					}
					foreach (var item in equalLengthPairs)
					{
						Line first = item.Item1;
						Line second = item.Item2;
						XYZ midpointA = (first.GetEndPoint(0) + first.GetEndPoint(1)) / 2.0;
						XYZ midpointB = (second.GetEndPoint(0) + second.GetEndPoint(1)) / 2.0;
						connectingLines.Add(Line.CreateBound(midpointA, midpointB));
					}
					return connectingLines;
				}
			}
		}
		return connectingLines;
	}

	private static double CalculateDistanceFromFaceToPoint(PlanarFace face, XYZ point, Transform transform)
	{
		XYZ transformedOrigin = transform.OfPoint(face.Origin);
		XYZ transformedNormal = transform.OfVector(face.FaceNormal).Normalize();
		Plane plane = Plane.CreateByNormalAndOrigin(transformedNormal, transformedOrigin);
		return point.PullOnto(plane).DistanceTo(point);
	}

	private Reference? GetReferenceFromWall(Wall wall, XYZ skPoint, XYZ normalizedDirection, RevitLinkInstance link, Transform transform)
	{
		return GetReferenceFromElement(wall, skPoint, normalizedDirection, link, transform);
	}

	private Reference? GetReferenceFromFamily(FamilyInstance family, XYZ skPoint, XYZ normalizedDirection, RevitLinkInstance link, Transform transform)
	{
		return GetReferenceFromElement(family, skPoint, normalizedDirection, link, transform);
	}

	private Reference? GetReferenceFromElement(Element element, XYZ skPoint, XYZ normalizedDirection, RevitLinkInstance link, Transform transform)
	{
		Options options = new Options
		{
			ComputeReferences = true,
			DetailLevel = ViewDetailLevel.Fine,
			IncludeNonVisibleObjects = false
		};
		GeometryElement geometryElement = element.get_Geometry(options);
		if (geometryElement == null)
		{
			return null;
		}
		List<PlanarFace> matchingFaces = EnumerateSolids(geometryElement).SelectMany((Solid solid) => solid.Faces.OfType<PlanarFace>()).Where(delegate(PlanarFace face)
		{
			XYZ xYZ = face.ComputeNormal(new UV()).Normalize();
			double num = xYZ.DotProduct(normalizedDirection);
			return Math.Abs(num - 1.0) < 1E-06 || Math.Abs(num + 1.0) < 1E-06;
		}).ToList();
		if (matchingFaces.Count < 2)
		{
			return null;
		}
		PlanarFace first = matchingFaces[0];
		PlanarFace second = matchingFaces[1];
		double d1 = CalculateDistanceFromFaceToPoint(first, skPoint, transform);
		double d2 = CalculateDistanceFromFaceToPoint(second, skPoint, transform);
		PlanarFace selectedFace = ((d1 < d2) ? first : second);
		Reference linkReference = selectedFace.Reference.CreateLinkReference(link);
		string stableRepresentation = linkReference.ConvertToStableRepresentation(_document);
		string converted = ConvertStableReference(stableRepresentation);
		return Reference.ParseFromStableRepresentation(_document, converted);
	}

	private static IEnumerable<Solid> EnumerateSolids(IEnumerable<GeometryObject> geometryObjects)
	{
		foreach (GeometryObject geometryObject in geometryObjects)
		{
			if (geometryObject is Solid solid)
			{
				yield return solid;
			}
			else
			{
				if (!(geometryObject is GeometryInstance instance))
				{
					continue;
				}
				foreach (Solid item in EnumerateSolids(instance.GetInstanceGeometry()))
				{
					yield return item;
				}
			}
		}
	}

	private static string ConvertStableReference(string stableReference)
	{
		string[] tokens = stableReference.Split(':');
		if (tokens.Length != 5)
		{
			throw new ArgumentException("Invalid stable reference format.");
		}
		string[] nestedTokens = tokens[1].Split('/');
		if (nestedTokens.Length != 2)
		{
			throw new ArgumentException("Invalid nested stable reference format.");
		}
		return tokens[0] + ":0:RVTLINK:" + tokens[2] + ":" + tokens[3] + ":" + tokens[4];
	}
}
