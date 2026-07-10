namespace NUglify.JavaScript
{
	/// <summary>
	/// Enum describing the type of input expected
	/// </summary>
	public enum JavaScriptSourceMode
	{
		/// <summary>Default input mode: a program, a block of top-level global statements</summary>
		Program = 0,

		/// <summary>Input is a single JavaScript Expression</summary>
		Expression,

		/// <summary>Input is an implicit function block, as in the value of an HTML onclick attribute</summary>
		EventHandler,

		/// <summary>Input is an implicit module block, as referenced by an import statement</summary>
		Module,
	}
}