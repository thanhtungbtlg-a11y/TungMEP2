using System;
using Autodesk.Revit.DB;
using MathNet.Numerics.LinearAlgebra;

namespace VP_RevitAPI.Helper;

public static class GeometryUtilities
{
	public static XYZ Align3D(Element element1, Element element2)
	{
		if (element1 == null || element2 == null)
		{
			return XYZ.Zero;
		}
		if (!(element1.Location is LocationCurve lc1))
		{
			return XYZ.Zero;
		}
		if (!(element2.Location is LocationCurve lc2))
		{
			return XYZ.Zero;
		}
		Curve c1 = lc1.Curve;
		Curve c2 = lc2.Curve;
		XYZ a0 = c1.GetEndPoint(0);
		XYZ a1 = c1.GetEndPoint(1);
		XYZ b0 = c2.GetEndPoint(0);
		XYZ b1 = c2.GetEndPoint(1);
		return Align3D(a0, a1, b0, b1);
	}

	public static XYZ Align3D(XYZ a0, XYZ a1, XYZ b0, XYZ b1)
	{
		XYZ d = a1 - a0;
		XYZ e = b1 - b0;
		if (d.GetLength() < 1E-09 || e.GetLength() < 1E-09)
		{
			return XYZ.Zero;
		}
		d = d.Normalize();
		e = e.Normalize();
		double crossLen = d.CrossProduct(e).GetLength();
		if (crossLen < 1E-09)
		{
			XYZ projOnL1 = a0 + (b0 - a0).DotProduct(d) * d;
			return projOnL1 - b0;
		}
		XYZ r = a0 - b0;
		double dot = d.DotProduct(e);
		double denom = 1.0 - dot * dot;
		if (Math.Abs(denom) < 1E-12)
		{
			return XYZ.Zero;
		}
		double d2 = d.DotProduct(r);
		double e2 = e.DotProduct(r);
		double t = (dot * e2 - d2) / denom;
		double s = (e2 - dot * d2) / denom;
		XYZ p1 = a0 + t * d;
		XYZ p2 = b0 + s * e;
		return p1 - p2;
	}

	public static XYZ Intersect2Line(this Line line1, Line line2)
	{
		if (line1 == null || line2 == null)
		{
			return XYZ.Zero;
		}
		Line unbound1 = ToUnbound(line1);
		Line unbound2 = ToUnbound(line2);
		if (unbound1 == null || unbound2 == null)
		{
			return XYZ.Zero;
		}
		XYZ direction1 = unbound1.Direction;
		XYZ direction2 = unbound2.Direction;
		if (direction1.CrossProduct(direction2).GetLength() < 1E-09)
		{
			return XYZ.Zero;
		}
		XYZ origin1 = unbound1.Origin;
		XYZ origin2 = unbound2.Origin;
		Matrix<double> matrix = Matrix<double>.Build.DenseOfArray(new double[3, 2]
		{
			{
				direction1.X,
				0.0 - direction2.X
			},
			{
				direction1.Y,
				0.0 - direction2.Y
			},
			{
				direction1.Z,
				0.0 - direction2.Z
			}
		});
		Vector<double> vector = Vector<double>.Build.Dense(new double[3]
		{
			origin2.X - origin1.X,
			origin2.Y - origin1.Y,
			origin2.Z - origin1.Z
		});
		Vector<double> solution = matrix.QR().Solve(vector);
		double t = solution[0];
		double s = solution[1];
		XYZ point1 = origin1 + t * direction1;
		XYZ point2 = origin2 + s * direction2;
		if ((point1 - point2).GetLength() > 1E-09)
		{
			return XYZ.Zero;
		}
		return (point1 + point2) * 0.5;
	}

	public static XYZ ProjectToPlane(this XYZ p, Plane plane, XYZ? direction = null)
	{
		XYZ dir = direction ?? (-plane.Normal.Normalize());
		XYZ n = plane.Normal.Normalize();
		double denom = n.DotProduct(dir);
		if (Math.Abs(denom) < 1E-09)
		{
			throw new InvalidOperationException("Projection direction is parallel to plane.");
		}
		double t = n.DotProduct(plane.Origin - p) / denom;
		return p + t * dir;
	}

	public static Line ProjectLineToPlane(this Line line, Plane plane, XYZ? direction = null)
	{
		if (line == null)
		{
			throw new ArgumentNullException("line");
		}
		if (line.IsBound)
		{
			XYZ p0 = line.GetEndPoint(0).ProjectToPlane(plane, direction);
			XYZ p1 = line.GetEndPoint(1).ProjectToPlane(plane, direction);
			return Line.CreateBound(p0, p1);
		}
		XYZ o0 = line.Origin.ProjectToPlane(plane, direction);
		XYZ o1 = (line.Origin + line.Direction).ProjectToPlane(plane, direction);
		XYZ dirProjected = (o1 - o0).Normalize();
		return Line.CreateUnbound(o0, dirProjected);
	}

	private static Line? ToUnbound(Line line)
	{
		XYZ direction = (line.IsBound ? (line.GetEndPoint(1) - line.GetEndPoint(0)) : line.Direction);
		if (direction.GetLength() < 1E-09)
		{
			return null;
		}
		XYZ origin = (line.IsBound ? line.GetEndPoint(0) : line.Origin);
		return Line.CreateUnbound(origin, direction.Normalize());
	}
}
