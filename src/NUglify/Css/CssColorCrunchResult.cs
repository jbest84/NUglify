// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

namespace NUglify.Css
{
	public sealed class CssColorCrunchResult
	{
		public bool IsValidColor { get; private set; }
		public string Color { get; private set; }

		public CssColorCrunchResult(bool isValidColor, string color)
		{
			IsValidColor = isValidColor;
			Color = color;
		}
	}
}