namespace NUglify.Css
{
	/// <summary>
	/// Enumeration for the type of CSS that will be parsed
	/// </summary>
	public enum CssType
	{
		/// <summary>
		/// Default setting: expecting a full CSS stylesheet
		/// </summary>
		FullStyleSheet = 0,

		/// <summary>
		/// Expecting just a declaration list, for instance: the value of an HTML style attribute
		/// </summary>
		DeclarationList
	}
}