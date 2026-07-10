// CssSettings.cs
//
// Copyright 2010 Microsoft Corporation
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Collections.Generic;

namespace NUglify.Css
{
	/// <summary>
	/// Settings Object for CSS Uglify
	/// </summary>
	public class CssSettings : CommonSettings
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="CssSettings"/> class with default settings.
		/// </summary>
		public CssSettings()
		{
			ColorNames = CssColor.Hex;
			CommentMode = CssComment.Important;
			MinifyExpressions = true;
			CssType = CssType.FullStyleSheet;
			RemoveEmptyBlocks = true;
			FixIE8Fonts = true;
			ExcludeVendorPrefixes = new List<string>();
			DecodeEscapes = true;
            AbbreviateHexColor = true;
		}

		/// <summary>
		/// Returns a CssSettings object configured to output "pretty" css, as opposed to minified
		/// </summary>
		/// <returns></returns>
		public static CssSettings Pretty()
		{
			var settings = new CssSettings();
			settings.CommentMode = CssComment.All;
			settings.MinifyExpressions = false;
			settings.OutputMode = OutputMode.MultipleLines;
			settings.TermSemicolons = true;
			return settings;
		}

		public CssSettings Clone()
		{
			// create the new settings object and copy all the properties from
			// the current settings
			var newSettings = new CssSettings
			{
				AllowEmbeddedAspNetBlocks = AllowEmbeddedAspNetBlocks,
				ColorNames = ColorNames,
				CommentMode = CommentMode,
				FixIE8Fonts = FixIE8Fonts,
				IgnoreAllErrors = IgnoreAllErrors,
				IgnoreErrorList = IgnoreErrorList,
				Indent = Indent,
				KillSwitch = KillSwitch,
				LineBreakThreshold = LineBreakThreshold,
				MinifyExpressions = MinifyExpressions,
				OutputMode = OutputMode,
				PreprocessorDefineList = PreprocessorDefineList,
				TermSemicolons = TermSemicolons,
				CssType = CssType,
				BlocksStartOnSameLine = BlocksStartOnSameLine,
				RemoveEmptyBlocks = RemoveEmptyBlocks,
				IgnoreRazorEscapeSequence = IgnoreRazorEscapeSequence,
				DecodeEscapes = DecodeEscapes,
                AbbreviateHexColor = AbbreviateHexColor
            };

			// add the resource strings (if any)
			newSettings.AddResourceStrings(ResourceStrings);

			foreach (var item in ReplacementTokens)
				newSettings.ReplacementTokens.Add(item);

			foreach (var item in ReplacementFallbacks)
				newSettings.ReplacementTokens.Add(item);

			foreach (var item in ExcludeVendorPrefixes)
				newSettings.ExcludeVendorPrefixes.Add(item);

			return newSettings;
		}

		/// <summary>
		/// Gets or sets ColorNames setting. Default is Strict.
		/// </summary>
		public CssColor ColorNames { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether to abbreviate hex colors to #rgb(a) format. Default is true
		/// </summary>
		public bool AbbreviateHexColor { get; set; }

        /// <summary>
        /// Gets or sets CommentMode setting. Default is Important.
        /// </summary>
        public CssComment CommentMode { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether to minify the javascript within expression functions. Deault is true.
		/// </summary>
		public bool MinifyExpressions { get; set; }

		/// <summary>
		/// Gets or sets a value indicating how to treat the input source. Default is FullStyleSheet.
		/// </summary>
		public CssType CssType { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether empty blocks removes the corresponding rule or directive. Default is true.
		/// </summary>
		public bool RemoveEmptyBlocks { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether IE8 .EOT fonts should get a question-mark appended to the URL
		/// (if not there already) to prevent the browser from generating invalid HTTP requests to the server. Default is true.
		/// </summary>
		public bool FixIE8Fonts { get; set; }

		/// <summary>
		/// Gets or sets a list of vendor-specific prefixes ("ms", "webkit", "moz") that will be omitted from the output.
		/// Default is no exclusions.
		/// </summary>
		public IList<string> ExcludeVendorPrefixes { get; private set; }

		/// <summary>
		/// Gets or sets a value indicating whether a double-at Razor escape sequence is ignored.
		/// </summary>
		public bool IgnoreRazorEscapeSequence { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether unicode escape strings (eg. '\ff0e') would be replaced by it's actual character or not. Default is true.
		/// </summary>
		public bool DecodeEscapes { get; set; }

		/// <summary>
		/// Controls if individual declarations should contain whitespace
		/// </summary>
		public bool OutputDeclarationWhitespace {
			get
			{
				if (!this.outputDeclarationWhitespace.HasValue)
					return this.OutputMode == OutputMode.MultipleLines;
				return this.outputDeclarationWhitespace.Value;
			}
			set
			{
				this.outputDeclarationWhitespace = value;
			} }

		bool? outputDeclarationWhitespace;
	}
}