namespace NUglify.JavaScript
{
	/// <summary>
	/// Settings for how local variables and functions can be renamed
	/// </summary>
	public enum LocalRenaming
	{
		/// <summary>
		/// Keep all names; don't rename anything
		/// </summary>
		KeepAll,

		/// <summary>
		/// Rename all local variables and functions that do not begin with "L_"
		/// </summary>
		KeepLocalizationVars,

		/// <summary>
		/// Rename all local variables and functions. (default)
		/// </summary>
		CrunchAll
	}
}