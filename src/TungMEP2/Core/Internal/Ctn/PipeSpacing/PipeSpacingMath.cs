using System;
using System.Collections.Generic;
using System.Linq;

namespace VP_RevitAPI.Internal.Ctn.PipeSpacing;

internal static class PipeSpacingMath
{
	private const double Epsilon = 1E-09;

	internal static bool TryBuildTargets(
		IReadOnlyList<PipeSpacingScalarInput> orderedInputs,
		PipeSpacingMode mode,
		double requestedSpacing,
		IReadOnlyList<PipeSpacingDnRule> rules,
		double fallbackClearGap,
		out PipeSpacingScalarPlan plan,
		out string error)
	{
		plan = null;
		error = string.Empty;
		if (!TryValidate(
				orderedInputs,
				mode,
				requestedSpacing,
				rules,
				fallbackClearGap,
				out error))
		{
			return false;
		}

		int count = orderedInputs.Count;
		int anchorIndex = orderedInputs[0].EffectiveDiameter + Epsilon >=
		                  orderedInputs[count - 1].EffectiveDiameter
			? 0
			: count - 1;
		double[] targets = new double[count];
		double[] pairClearGaps = new double[count - 1];
		targets[anchorIndex] = orderedInputs[anchorIndex].Coordinate;

		if (anchorIndex == 0)
		{
			for (int index = 1; index < count; index++)
			{
				double step = GetCenterStep(
					orderedInputs[index - 1],
					orderedInputs[index],
					mode,
					requestedSpacing,
					rules,
					fallbackClearGap,
					out double clearGap);
				targets[index] = targets[index - 1] + step;
				pairClearGaps[index - 1] = clearGap;
			}
		}
		else
		{
			for (int index = count - 2; index >= 0; index--)
			{
				double step = GetCenterStep(
					orderedInputs[index],
					orderedInputs[index + 1],
					mode,
					requestedSpacing,
					rules,
					fallbackClearGap,
					out double clearGap);
				targets[index] = targets[index + 1] - step;
				pairClearGaps[index] = clearGap;
			}
		}

		List<PipeSpacingScalarTarget> scalarTargets = new List<PipeSpacingScalarTarget>(count);
		for (int index = 0; index < count; index++)
		{
			scalarTargets.Add(new PipeSpacingScalarTarget(
				orderedInputs[index].Key,
				orderedInputs[index].Coordinate,
				targets[index],
				index == anchorIndex));
		}

		plan = new PipeSpacingScalarPlan(
			anchorIndex,
			scalarTargets,
			pairClearGaps,
			pairClearGaps.Min());
		return true;
	}

	private static bool TryValidate(
		IReadOnlyList<PipeSpacingScalarInput> inputs,
		PipeSpacingMode mode,
		double requestedSpacing,
		IReadOnlyList<PipeSpacingDnRule> rules,
		double fallbackClearGap,
		out string error)
	{
		error = string.Empty;
		if (inputs == null || inputs.Count < 2)
		{
			error = "Cần ít nhất hai ống hợp lệ để dàn khoảng cách.";
			return false;
		}

		for (int index = 0; index < inputs.Count; index++)
		{
			PipeSpacingScalarInput input = inputs[index];
			if (!IsFinite(input.Coordinate) ||
			    !IsFinite(input.EffectiveDiameter) ||
			    !IsFinite(input.NominalDiameter) ||
			    input.EffectiveDiameter <= Epsilon ||
			    input.NominalDiameter <= Epsilon)
			{
				error = $"Ống tại vị trí {index + 1} không có đường kính/tọa độ hợp lệ.";
				return false;
			}

			if (index > 0 && input.Coordinate - inputs[index - 1].Coordinate <= Epsilon)
			{
				error = "Các ống phải có thứ tự hình học riêng biệt theo phương dàn.";
				return false;
			}
		}

		if (mode == PipeSpacingMode.EqualClearGap &&
		    (!IsFinite(requestedSpacing) || requestedSpacing < 0.0))
		{
			error = "Khoảng hở phải là số không âm.";
			return false;
		}

		if (mode == PipeSpacingMode.EqualCenterSpacing &&
		    (!IsFinite(requestedSpacing) || requestedSpacing <= Epsilon))
		{
			error = "Khoảng cách tim phải lớn hơn 0.";
			return false;
		}

		if (mode == PipeSpacingMode.DnRuleTable)
		{
			if (!IsFinite(fallbackClearGap) || fallbackClearGap < 0.0)
			{
				error = "Khoảng hở fallback phải là số không âm.";
				return false;
			}

			foreach (PipeSpacingDnRule rule in rules ?? Array.Empty<PipeSpacingDnRule>())
			{
				if (!IsFinite(rule.MaxNominalDiameter) ||
				    !IsFinite(rule.ClearGap) ||
				    rule.MaxNominalDiameter <= Epsilon ||
				    rule.ClearGap < 0.0)
				{
					error = "Bảng DN chứa giới hạn hoặc khoảng hở không hợp lệ.";
					return false;
				}
			}
		}

		return true;
	}

	private static double GetCenterStep(
		PipeSpacingScalarInput left,
		PipeSpacingScalarInput right,
		PipeSpacingMode mode,
		double requestedSpacing,
		IReadOnlyList<PipeSpacingDnRule> rules,
		double fallbackClearGap,
		out double clearGap)
	{
		double radii = (left.EffectiveDiameter + right.EffectiveDiameter) * 0.5;
		if (mode == PipeSpacingMode.EqualCenterSpacing)
		{
			clearGap = requestedSpacing - radii;
			return requestedSpacing;
		}

		clearGap = mode == PipeSpacingMode.EqualClearGap
			? requestedSpacing
			: ResolveDnGap(left, right, rules, fallbackClearGap);
		return radii + clearGap;
	}

	private static double ResolveDnGap(
		PipeSpacingScalarInput left,
		PipeSpacingScalarInput right,
		IReadOnlyList<PipeSpacingDnRule> rules,
		double fallbackClearGap)
	{
		double maximumDn = Math.Max(left.NominalDiameter, right.NominalDiameter);
		PipeSpacingDnRule? selected = (rules ?? Array.Empty<PipeSpacingDnRule>())
			.OrderBy(rule => rule.MaxNominalDiameter)
			.Cast<PipeSpacingDnRule?>()
			.FirstOrDefault(rule => rule.Value.MaxNominalDiameter + Epsilon >= maximumDn);
		return selected?.ClearGap ?? fallbackClearGap;
	}

	private static bool IsFinite(double value)
	{
		return !double.IsNaN(value) && !double.IsInfinity(value);
	}
}

