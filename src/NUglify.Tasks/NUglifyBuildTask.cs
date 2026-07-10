// NUglifyBuildTask.cs
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

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Resources;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using NUglify.Css;
using NUglify.JavaScript;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace NUglify
{
    /// <summary>
    /// Provides the MS Build task for Microsoft Ajax Uglify. Please see the list of supported properties below.
    /// </summary>
    [SecurityCritical]
    public class NUglify : Task
    {
        #region private fields

        /// <summary>
        /// regular expression used to determine if a source file ends in a semicolon (optionally followed by whitespace)
        /// </summary>
        static Regex s_endsInSemicolon = new Regex(@";\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// NUglify command-line switch parser
        /// </summary>
        UglifyCommandParser uglifyCommandParser;

        /// <summary>
        /// An optional file mapping implementation name
        /// </summary>
        string m_symbolsMapName;

        /// <summary>
        /// A collection of other input files we will use, as specified by swithc parameters
        /// </summary>
        ICollection<string> m_otherInputFiles;

        #endregion

        #region command-line style switches

        /// <summary>
        /// EXE-style command-line switch string for initializing the CSS and/or JS settings at one time
        /// </summary>
        public string Switches 
        {
            get
            {
                // return an empty string instead of null
                return m_switches ?? string.Empty;
            }
            set
            {
                // empty the list of other input files
                m_otherInputFiles.Clear();

                // parse the switches
                uglifyCommandParser.Parse(value);

                // just so we can grab them later, let's keep track of all the switches we are passed
                if (m_switches == null)
                {
                    // this is the first set of switches
                    m_switches = value;
                }
                else
                {
                    // just appends this set to the previous set
                    m_switches += value;
                }
            }
        }

        string m_switches = null;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        void OnUnknownParameter(object sender, UnknownParameterEventArgs ea)
        {
            // we only care about rename, res, and r -- ignore all other switches.
            switch (ea.SwitchPart)
            {
                case "RENAME":
                    // only care if the parameter part is null
                    if (ea.ParameterPart == null)
                    {
                        // needs to be another parameter still left
                        if (ea.Index < ea.Arguments.Count - 1)
                        {
                            // get the renaming path and add it to the other input file if it exists
                            var renamePath = ea.Arguments[++ea.Index];
                            if (File.Exists(renamePath))
                            {
                                m_otherInputFiles.Add(renamePath);
                            }

                            // the renaming file is specified as the NEXT argument
                            using (var fileReader = new StreamReader(renamePath))
                            {
                                using (var reader = XmlReader.Create(fileReader))
                                {
                                    // let the manifest factory do all the heavy lifting of parsing the XML
                                    // into config objects
                                    var config = ManifestFactory.Create(reader);
                                    if (config != null)
                                    {
                                        // add any rename pairs
                                        foreach (var pair in config.RenameIdentifiers)
                                        {
                                            uglifyCommandParser.JSSettings.AddRenamePair(pair.Key, pair.Value);
                                        }

                                        // add any no-rename identifiers
                                        uglifyCommandParser.JSSettings.SetNoAutoRenames(config.NoRenameIdentifiers);
                                    }
                                }
                            }
                        }
                    }
                    break;

                case "RES":
                case "R":
                    // needs to be another parameter still left
                    if (ea.Index < ea.Arguments.Count - 1)
                    {
                        // the resource file path is specified as the NEXT argument
                        var resourceFile = ea.Arguments[++ea.Index];

                        var isValid = true;
                        var objectName = ea.ParameterPart;
                        if (string.IsNullOrEmpty(objectName))
                        {
                            // use the default object name
                            objectName = "Strings";
                        }
                        else
                        {
                            // the parameter part needs to be in the pattern of IDENT[.IDENT]*
                            var parts = objectName.Split('.');

                            // assume it's okay unless we prove otherwise
                            foreach (var part in parts)
                            {
                                if (!JSScanner.IsValidIdentifier(part))
                                {
                                    isValid = false;
                                    break;
                                }
                            }
                        }

                        if (isValid)
                        {
                            var resourceStrings = new ResourceStrings();

                            // process the appropriate resource type
                            switch(Path.GetExtension(resourceFile).ToUpperInvariant())
                            {
                                case ".RESX":
                                    using (var reader = new ResXResourceReader(resourceFile))
                                    {
                                        foreach (DictionaryEntry item in reader)
                                        {
                                            resourceStrings[item.Key.ToString()] = item.Value.ToString();
                                        }
                                    }
                                    break;

                                case ".RESOURCES":
                                    using (var reader = new ResourceReader(resourceFile))
                                    {
                                        foreach (DictionaryEntry item in reader)
                                        {
                                            resourceStrings[item.Key.ToString()] = item.Value.ToString();
                                        }
                                    }
                                    break;

                                default:
                                    // ignore all other extensions
                                    break;
                            }

                            // add it to the settings objects
                            if (resourceStrings.Count > 0)
                            {
                                m_otherInputFiles.Add(resourceFile);

                                // set the object name
                                resourceStrings.Name = objectName;

                                // and add it to the parsers
                                uglifyCommandParser.JSSettings.AddResourceStrings(resourceStrings);
                                uglifyCommandParser.CssSettings.AddResourceStrings(resourceStrings);
                            }
                        }
                    }
                    break;
            }
        }

        #endregion

        #region Common properties

        /// <summary>
        /// Warning level threshold for reporting errors. Defalut valus is 0 (syntax/run-time errors)
        /// </summary>
        public int WarningLevel 
        { 
            get
            {
                return uglifyCommandParser.WarningLevel;
            }
            set
            {
                uglifyCommandParser.WarningLevel = value;
            }
        }

        /// <summary>
        /// Whether to treat NUglify warnings as build errors (true) or not (false). Default value is false.
        /// </summary>
        public bool TreatWarningsAsErrors { get; set; }

        /// <summary>
        /// Whether to attempt to over-write read-only files (default is false)
        /// </summary>
        public bool Clobber
        {
            get
            {
                // we return true for this property only if we are, indeed, clobbering all files.
                return uglifyCommandParser.Clobber == ExistingFileTreatment.Overwrite;
            }
            set
            {
                // since this is a boolean property, we don't care about the Preserve treatment
                uglifyCommandParser.Clobber = value ? ExistingFileTreatment.Overwrite : ExistingFileTreatment.Auto;
            }
        }

        /// <summary>
        /// <see cref="CodeSettings.IgnoreErrorList"/> for more information.
        /// </summary>
        public string IgnoreErrorList
        {
            get 
            { 
                // there are technically separate lists for JS and CSS, but we'll set them
                // to the same value, so just use the JS list as the reference here.
                return this.uglifyCommandParser.JSSettings.IgnoreErrorList; 
            }
            set 
            { 
                // there are technically separate lists for JS and CSS, but we'll just set them
                // to the same values.
                this.uglifyCommandParser.JSSettings.IgnoreErrorList = value;
                this.uglifyCommandParser.CssSettings.IgnoreErrorList = value;
            }
        }

        /// <summary>
        /// Source map implementation name. If maps to one of the available source map implementations,
        /// will cause source maps to be generated for the output source. Possible values so far
        /// are XML and V3.
        /// </summary>
        public string SourceMapType
        {
            get { return this.m_symbolsMapName; }
            set { this.m_symbolsMapName = value; }
        }

        /// <summary>
        /// Gets or sets the source map root URI for any relative source file paths specified in the generated source map.
        /// </summary>
        public string SourceMapRoot
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets a flag indicating whether the source map should output an an extra "safe" header string
        /// </summary>
        public bool SourceMapSafeHeader
        {
            get;
            set;
        }

        #endregion

        #region JavaScript parameters

        /// <summary>
        /// JavaScript source files to minify.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public ITaskItem[] JsSourceFiles { get; set; }

        /// <summary>
        /// Target extension for individually-minified JS files.
        /// Must use wih JsSourceExtensionPattern; cannot be used with JsCombinedFileName.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public string JsTargetExtension { get; set; }

        /// <summary>
        /// Source extension pattern for individually-minified JS files.
        /// Must use wih JsTargetExtension; cannot be used with JsCombinedFileName.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public string JsSourceExtensionPattern { get; set; }

        /// <summary>
        /// Combine and minify all source files to this name.
        /// Cannot be used with JsTargetExtension/JsSourceExtensionPattern.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public string JsCombinedFileName { get; set; }

        /// <summary>
        /// Ensures the final semicolon in minified JS file.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public bool JsEnsureFinalSemicolon 
        {
            get { return this.uglifyCommandParser.JSSettings.TermSemicolons; }
            set { this.uglifyCommandParser.JSSettings.TermSemicolons = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.CollapseToLiteral"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public bool JsCollapseToLiteral
        {
            get { return this.uglifyCommandParser.JSSettings.CollapseToLiteral;  }
            set { this.uglifyCommandParser.JSSettings.CollapseToLiteral = value; }
        }
        
        /// <summary>
        /// <see cref="CodeSettings.EvalTreatment"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public string JsEvalTreatment
        {
            get { return this.uglifyCommandParser.JSSettings.EvalTreatment.ToString(); }
            set { this.uglifyCommandParser.JSSettings.EvalTreatment = ParseEnumValue<EvalTreatment>(value); }
        }
        
        /// <summary>
        /// <see cref="CodeSettings.IndentSize"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public int JsIndentSize
        {
#pragma warning disable 618
            get { return this.uglifyCommandParser.JSSettings.IndentSize; }
            set { this.uglifyCommandParser.JSSettings.IndentSize = value; }
#pragma warning restore 618
        }

        /// <summary>
        /// <see cref="CodeSettings.Indent"/> for more information.
        /// </summary>
        public string JsIndent
        {
	        get => this.uglifyCommandParser.JSSettings.Indent;
	        set => this.uglifyCommandParser.JSSettings.Indent = value;
        }

        /// <summary>
        /// <see cref="CodeSettings.InlineSafeStrings"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public bool JsInlineSafeStrings
        {
            get { return this.uglifyCommandParser.JSSettings.InlineSafeStrings; }
            set { this.uglifyCommandParser.JSSettings.InlineSafeStrings = value; }
        }
        
        /// <summary>
        /// <see cref="CodeSettings.LocalRenaming"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public string JsLocalRenaming
        {
            get { return this.uglifyCommandParser.JSSettings.LocalRenaming.ToString(); }
            set { this.uglifyCommandParser.JSSettings.LocalRenaming = ParseEnumValue<LocalRenaming>(value); }
        }

        /// <summary>
        /// <see cref="CodeSettings.AddRenamePairs"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public string JsManualRenamePairs
        {
            get { return this.uglifyCommandParser.JSSettings.RenamePairs; }
            set { this.uglifyCommandParser.JSSettings.RenamePairs = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.SetNoAutoRename"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public string JsNoAutoRename
        {
            get { return this.uglifyCommandParser.JSSettings.NoAutoRenameList; }
            set { this.uglifyCommandParser.JSSettings.NoAutoRenameList = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.SetKnownGlobalNames"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public string JsKnownGlobalNames
        {
            get { return this.uglifyCommandParser.JSSettings.KnownGlobalNamesList; }
            set { this.uglifyCommandParser.JSSettings.KnownGlobalNamesList = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.SetKnownGlobalNames"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public string JsDebugLookups
        {
            get { return this.uglifyCommandParser.JSSettings.DebugLookupList; }
            set { this.uglifyCommandParser.JSSettings.DebugLookupList = value; }
        }
        
        /// <summary>
        /// <see cref="CodeSettings.MacSafariQuirks"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public bool JsMacSafariQuirks
        {
            get { return this.uglifyCommandParser.JSSettings.MacSafariQuirks; }
            set { this.uglifyCommandParser.JSSettings.MacSafariQuirks = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.IgnoreConditionalCompilation"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public bool JsIgnoreConditionalCompilation
        {
            get { return this.uglifyCommandParser.JSSettings.IgnoreConditionalCompilation; }
            set { this.uglifyCommandParser.JSSettings.IgnoreConditionalCompilation = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.MinifyCode"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public bool JsMinifyCode
        {
            get { return this.uglifyCommandParser.JSSettings.MinifyCode; }
            set { this.uglifyCommandParser.JSSettings.MinifyCode = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.OutputMode"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public string JsOutputMode
        {
            get { return this.uglifyCommandParser.JSSettings.OutputMode.ToString(); }
            set { this.uglifyCommandParser.JSSettings.OutputMode = ParseEnumValue<OutputMode>(value); }
        }

        /// <summary>
        /// <see cref="CodeSettings.PreserveFunctionNames"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public bool JsPreserveFunctionNames
        {
            get { return this.uglifyCommandParser.JSSettings.PreserveFunctionNames; }
            set { this.uglifyCommandParser.JSSettings.PreserveFunctionNames = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.RemoveFunctionExpressionNames"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public bool JsRemoveFunctionExpressionNames
        {
            get { return this.uglifyCommandParser.JSSettings.RemoveFunctionExpressionNames; }
            set { this.uglifyCommandParser.JSSettings.RemoveFunctionExpressionNames = value; }
        }
        
        /// <summary>
        /// <see cref="CodeSettings.RemoveUnneededCode"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public bool JsRemoveUnneededCode
        {
            get { return this.uglifyCommandParser.JSSettings.RemoveUnneededCode; }
            set { this.uglifyCommandParser.JSSettings.RemoveUnneededCode = value; }
        }
        
        /// <summary>
        /// <see cref="CodeSettings.StripDebugStatements"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public bool JsStripDebugStatements
        {
            get { return this.uglifyCommandParser.JSSettings.StripDebugStatements; }
            set { this.uglifyCommandParser.JSSettings.StripDebugStatements = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.AllowEmbeddedAspNetBlocks"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public bool JsAllowEmbeddedAspNetBlocks
        {
            get { return this.uglifyCommandParser.JSSettings.AllowEmbeddedAspNetBlocks; }
            set { this.uglifyCommandParser.JSSettings.AllowEmbeddedAspNetBlocks = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.PreprocessorDefineList"/> for more information.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Js")]
        public string JsPreprocessorDefines
        {
            get { return this.uglifyCommandParser.JSSettings.PreprocessorDefineList; }
            set { this.uglifyCommandParser.JSSettings.PreprocessorDefineList = value; }
        }

        #endregion

        #region CSS parameters

        /// <summary>
        /// CSS source files to minify.
        /// </summary>
        public ITaskItem[] CssSourceFiles { get; set; }

        /// <summary>
        /// Target extension for minified CSS files.
        /// Cannot use with CssCombinedFileName.
        /// </summary>
        public string CssTargetExtension { get; set; }

        /// <summary>
        /// Source extension pattern for CSS files.
        /// Cannot use with CssCombinedFileName.
        /// </summary>
        public string CssSourceExtensionPattern { get; set; }

        /// <summary>
        /// Combine all source files and minify to this the given file name.
        /// Cannot use with CssTargetExtension/CssSourceExtensionPattern.
        /// </summary>
        public string CssCombinedFileName { get; set; }

        /// <summary>
        /// <see cref="CssSettings.ColorNames"/> for more information.
        /// </summary>
        public string CssColorNames
        {
            get { return this.uglifyCommandParser.CssSettings.ColorNames.ToString(); }
            set { this.uglifyCommandParser.CssSettings.ColorNames = ParseEnumValue<CssColor>(value); }
        }
        
        /// <summary>
        /// <see cref="CssSettings.CommentMode"/> for more information.
        /// </summary>
        public string CssCommentMode
        {
            get { return this.uglifyCommandParser.CssSettings.CommentMode.ToString(); }
            set { this.uglifyCommandParser.CssSettings.CommentMode = ParseEnumValue<CssComment>(value); }
        }
        
        /// <summary>
        /// <see cref="CssSettings.ExpandOutput"/> for more information.
        /// </summary>
        public bool CssExpandOutput
        {
            get { return this.uglifyCommandParser.CssSettings.OutputMode == OutputMode.MultipleLines; }
            set { this.uglifyCommandParser.CssSettings.OutputMode = value ? OutputMode.MultipleLines : OutputMode.SingleLine; }
        }

        /// <summary>
        /// <see cref="CssSettings.IndentSpaces"/> for more information.
        /// </summary>
        public int CssIndentSpaces
        {
#pragma warning disable 618
            get { return this.uglifyCommandParser.CssSettings.IndentSize; }
            set { this.uglifyCommandParser.CssSettings.IndentSize = value; }
#pragma warning restore 618
        }

        /// <summary>
        /// <see cref="CssSettings.Indent"/> for more information.
        /// </summary>
        public string CssIndent
        {
	        get => this.uglifyCommandParser.CssSettings.Indent;
	        set => this.uglifyCommandParser.CssSettings.Indent = value;
        }

        /// <summary>
        /// <see cref="CssSettings.TermSemicolons"/> for more information.
        /// </summary>
        public bool CssTermSemicolons
        {
            get { return this.uglifyCommandParser.CssSettings.TermSemicolons; }
            set { this.uglifyCommandParser.CssSettings.TermSemicolons = value; }
        }

        /// <summary>
        /// <see cref="CssSettings.MinifyExpressions"/> for more information.
        /// </summary>
        public bool CssMinifyExpressions
        {
            get { return this.uglifyCommandParser.CssSettings.MinifyExpressions; }
            set { this.uglifyCommandParser.CssSettings.MinifyExpressions = value; }
        }

        /// <summary>
        /// <see cref="CssSettings.AllowEmbeddedAspNetBlocks"/> for more information.
        /// </summary>
        public bool CssAllowEmbeddedAspNetBlocks
        {
            get { return this.uglifyCommandParser.CssSettings.AllowEmbeddedAspNetBlocks; }
            set { this.uglifyCommandParser.CssSettings.AllowEmbeddedAspNetBlocks = value; }
        }

        #endregion

        /// <summary>
        /// Constructor for <see cref="NUglify"/> class. Initializes the default
        /// values for all parameters.
        /// </summary>
        public NUglify()
        {
            this.uglifyCommandParser = new UglifyCommandParser();
            this.uglifyCommandParser.UnknownParameter += OnUnknownParameter;
            this.m_otherInputFiles = new HashSet<string>();
            this.JsEnsureFinalSemicolon = true;
        }

        #region Execute method

        /// <summary>
        /// Executes the Ajax Uglify build task
        /// </summary>
        /// <returns>True if the build task successfully succeded; otherwise, false.</returns>
        //[SecurityCritical]
        public override bool Execute()
        {
            // Deal with JS minification
            if (this.JsSourceFiles != null && this.JsSourceFiles.Length > 0)
            {
                if (string.IsNullOrWhiteSpace(this.JsCombinedFileName))
                {
                    // no combined name; the source extension and target extension properties must be set.
                    if (string.IsNullOrWhiteSpace(this.JsSourceExtensionPattern))
                    {
                        Log.LogError(Strings.RequiredParameterIsEmpty, "JsSourceExtensionPattern");
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(this.JsTargetExtension))
                    {
                        Log.LogError(Strings.RequiredParameterIsEmpty, "JsTargetExtension");
                        return false;
                    }
                }
                else
                {
                    // a combined name was specified - must NOT use source/target extension properties
                    if (!string.IsNullOrWhiteSpace(this.JsSourceExtensionPattern))
                    {
                        Log.LogError(Strings.CannotUseCombinedAndIndividual, "JsSourceExtensionPattern");
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(JsTargetExtension))
                    {
                        Log.LogError(Strings.CannotUseCombinedAndIndividual, "JsTargetExtension");
                        return false;
                    }
                }

                MinifyJavaScript();
            }
            else
            {
                Log.LogMessage(Strings.NoJavaScriptToMinify);
            }

            // Deal with CSS minification
            if (this.CssSourceFiles != null && this.CssSourceFiles.Length > 0)
            {
                if (string.IsNullOrWhiteSpace(this.CssCombinedFileName))
                {
                    if (string.IsNullOrWhiteSpace(CssSourceExtensionPattern))
                    {
                        Log.LogError(Strings.RequiredParameterIsEmpty, "CssSourceExtensionPattern");
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(CssTargetExtension))
                    {
                        Log.LogError(Strings.RequiredParameterIsEmpty, "CssTargetExtension");
                        return false;
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(CssSourceExtensionPattern))
                    {
                        Log.LogError(Strings.CannotUseCombinedAndIndividual, "CssSourceExtensionPattern");
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(CssTargetExtension))
                    {
                        Log.LogError(Strings.CannotUseCombinedAndIndividual, "CssTargetExtension");
                        return false;
                    }
                }

                MinifyStyleSheets();
            }
            else
            {
                Log.LogMessage(Strings.NoStyleSheetsToMinify);
            }

            return !Log.HasLoggedErrors;
        }

        #endregion

        #region JavaScript methods

        /// <summary>
        /// Minifies JS files provided by the caller of the build task.
        /// </summary>
        void MinifyJavaScript()
        {
            // find the most-recent other input file (if any)
            var mostRecentOtherInput = DateTime.MinValue;
            foreach (var otherPath in m_otherInputFiles)
            {
                var writeTime = File.GetLastWriteTimeUtc(otherPath);
                if (writeTime >= mostRecentOtherInput)
                {
                    mostRecentOtherInput = writeTime;
                }
            }

            if (string.IsNullOrWhiteSpace(JsCombinedFileName))
            {
                // individually-minified files
                foreach (ITaskItem item in this.JsSourceFiles)
                {
                    // construct the output JS file path
                    string outputPath = Regex.Replace(item.ItemSpec, this.JsSourceExtensionPattern, this.JsTargetExtension,
                                                RegexOptions.IgnoreCase);

                    // if we want a symbol map, validate the name and return the map output path 
                    // based on this output file path
                    var symbolMapPath = GetMapFilePath(outputPath);
                    var wantSymbolMap = !string.IsNullOrWhiteSpace(symbolMapPath);
                    if (FileIsWritable(outputPath))
                    {
                        // if the output file doesn't exist, we need to minify the sources.
                        // or if we want a map file and IT doesn't exist, we need to minify the sources.
                        // but if they both exists, check the last-write time; and if any input file is
                        // newer than the output, we need to re-minify.
                        var needToMinify = !File.Exists(outputPath) || (wantSymbolMap && !File.Exists(symbolMapPath));
                        if (!needToMinify)
                        {
                            // get the time of the output file
                            var outputTime = File.GetLastWriteTimeUtc(outputPath);
                            if (wantSymbolMap)
                            {
                                // if the mapfile is more recent than the output file, use that time instead
                                var mapFileTime = File.GetCreationTimeUtc(symbolMapPath);
                                if (mapFileTime < outputTime)
                                {
                                    outputTime = mapFileTime;
                                }
                            }

                            // check the output time against the most-recent other-input files
                            // and the one input file.
                            if (outputTime <= mostRecentOtherInput
                                || outputTime <= File.GetLastWriteTimeUtc(item.ItemSpec))
                            {
                                needToMinify = true;
                            }
                        }

                        if (needToMinify)
                        {
                            string source = File.ReadAllText(item.ItemSpec, GetEncoding(uglifyCommandParser.EncodingInputName, new JsEncoderFallback()));
                            MinifyJavaScript(source, item.ItemSpec, outputPath, symbolMapPath);
                        }
                        else
                        {
                            Log.LogMessage(Strings.DidNotMinify, Path.GetFileName(item.ItemSpec), Strings.AlreadyDone);
                        }
                    }
                    else
                    {
                        // log a WARNING that the minification was skipped -- don't break the build
                        Log.LogWarning(Strings.DestinationIsReadOnly, Path.GetFileName(item.ItemSpec), outputPath);
                    }
                }
            }
            else
            {
                // if we want a symbol map, validate the name and return the map output path 
                // based on the combined output file
                var symbolMapPath = GetMapFilePath(this.JsCombinedFileName);
                var wantSymbolMap = !string.IsNullOrWhiteSpace(symbolMapPath);

                // combine the sources into a single file and minify the results
                if (FileIsWritable(this.JsCombinedFileName))
                {
                    // if the output file doesn't exist, we need to minify the sources.
                    // or if we want a map file and IT doesn't exist, we need to minify the sources.
                    // but if they both exists, check the last-write time; and if any input file is
                    // newer than the output, we need to re-minify.
                    var needToMinify = !File.Exists(this.JsCombinedFileName) || (wantSymbolMap && !File.Exists(symbolMapPath));
                    if (!needToMinify)
                    {
                        var outputTime = File.GetLastWriteTimeUtc(this.JsCombinedFileName);
                        if (wantSymbolMap)
                        {
                            // if the mapfile is more recent than the output file, use that time instead
                            var mapFileTime = File.GetCreationTimeUtc(symbolMapPath);
                            if (mapFileTime < outputTime)
                            {
                                outputTime = mapFileTime;
                            }
                        }

                        if (outputTime <= mostRecentOtherInput)
                        {
                            needToMinify = true;
                        }
                        else
                        {
                            foreach (var inputFile in this.JsSourceFiles)
                            {
                                if (outputTime <= File.GetLastWriteTimeUtc(inputFile.ItemSpec))
                                {
                                    needToMinify = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (needToMinify)
                    {
                        ConcatenateAndMinifyJavaScript(symbolMapPath);
                    }
                    else
                    {
                        Log.LogMessage(Strings.DidNotMinify, Path.GetFileName(this.JsCombinedFileName), Strings.AlreadyDone);
                    }
                }
                else
                {
                    // log a WARNING that the minification was skipped -- don't break the build
                    Log.LogWarning(Strings.DestinationIsReadOnly, Path.GetFileName(this.JsCombinedFileName), this.JsCombinedFileName);
                }
            }
        }

        /// <summary>
        /// Minify the given source code from the given named source, to the given output path
        /// </summary>
        /// <param name="sourceCode">source code to minify</param>
        /// <param name="sourceName">name of the source</param>
        /// <param name="outputPath">destination path for resulting minified code</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "disposes in the finally block")]
        void MinifyJavaScript(string sourceCode, string sourceName, string outputPath, string mapFilePath)
        {
            TextWriter mapWriter = null;
            ISourceMap sourceMap = null;
            try
            {
                if (string.IsNullOrWhiteSpace(mapFilePath))
                {
                    // just in case, make darn-sure the settings is null-out
                    uglifyCommandParser.JSSettings.SymbolsMap = null;
                }
                else
                {
                    // we want to generate a map file for this output. Create the proper implementation
                    // and set it on the settings object
                    if (FileIsWritable(mapFilePath))
                    {
                        // be sure to use a UTF-8 encoding that does NOT output a BOM or Chrome won't read it!
                        mapWriter = new StreamWriter(mapFilePath, false, new UTF8Encoding(false));
                        sourceMap = SourceMapFactory.Create(mapWriter, m_symbolsMapName);
                        if (sourceMap != null)
                        {
                            mapWriter = null;

                            // start the package off
                            uglifyCommandParser.JSSettings.SymbolsMap = sourceMap;

                            // set some properties used by the V3 implementation
                            sourceMap.SourceRoot = this.SourceMapRoot;
                            sourceMap.SafeHeader = this.SourceMapSafeHeader;
                            sourceMap.StartPackage(outputPath, mapFilePath);
                        }
                    }
                    else
                    {
                        // log a WARNING that the symbol map generation was skipped -- don't break the build
                        Log.LogWarning(Strings.MapDestinationIsReadOnly, mapFilePath);
                    }
                }


                uglifyCommandParser.JSSettings.WarningLevel = WarningLevel;
                var minifiedJs = Uglify.Js(sourceCode, sourceName, this.uglifyCommandParser.JSSettings);
                if (minifiedJs.HasErrors)
                {
                    foreach (var error in minifiedJs.Errors)
                    {
                        LogContextError(error);
                    }
                }

                if (!Log.HasLoggedErrors)
                {
                    try
                    {
                        var encoding = GetEncoding(uglifyCommandParser.EncodingOutputName, new JsEncoderFallback());
                        using (var outputWriter = new StreamWriter(outputPath, false, encoding))
                        {
                            // output the minified code
                            outputWriter.Write(minifiedJs.Code);

                            // give the symbol map a chance to add a little something, if we have one
                            if (sourceMap != null)
                            {
                                sourceMap.EndFile(outputWriter, uglifyCommandParser.JSSettings.LineTerminator);
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        LogFileError(sourceName, Strings.NoWritePermission, outputPath);
                    }
                }
                else
                {
                    Log.LogWarning(Strings.DidNotMinify, outputPath, Strings.ThereWereErrors);
                }
            }
            catch (Exception e)
            {
                LogFileError(sourceName, Strings.DidNotMinify, outputPath, e.Message);
                throw;
            }
            finally
            {
                if (sourceMap != null)
                {
                    // close shut it down and close it out
                    mapWriter = null;
                    uglifyCommandParser.JSSettings.SymbolsMap = null;
                    sourceMap.EndPackage();
                    sourceMap.Dispose();
                }

                // make sure we clean up the writer if anything went wrong
                if (mapWriter != null)
                {
                    mapWriter.Close();
                }
            }
        }

        void ConcatenateAndMinifyJavaScript(string mapFilePath)
        {
            // concatenate all the input files together, with each one prefaced by the
            // special #SOURCE comment so the errors and warnings turn out right.
            var inputBuilder = new StringBuilder(8192);
            var endsInSemicolon = true;
            foreach (var itemSpec in this.JsSourceFiles)
            {
                // start a new line so any previous single-line comments are terminated, then
                // if the previous file didn't end in a semicolon, add one now.
                // (doesn't hurt to have an extra semicolon)
                inputBuilder.AppendLine();
                if (!endsInSemicolon)
                {
                    inputBuilder.Append(';');
                }

                inputBuilder.AppendFormat("///#SOURCE 1 1 {0}\n", itemSpec.ItemSpec);

                string fileContent = File.ReadAllText(itemSpec.ItemSpec, GetEncoding(uglifyCommandParser.EncodingInputName, new JsEncoderFallback()));
                inputBuilder.Append(fileContent);

                // set the flag for whether or not this file ends in a semicolon
                endsInSemicolon = s_endsInSemicolon.IsMatch(fileContent);
            }

            MinifyJavaScript(inputBuilder.ToString(), string.Empty, this.JsCombinedFileName, mapFilePath);
        }

        #endregion

        #region Stylesheet methods

        /// <summary>
        /// Minifies CSS files provided by the caller of the build task.
        /// </summary>
        void MinifyStyleSheets()
        {
            // find the most-recent other input file (if any)
            var mostRecentOtherInput = DateTime.MinValue;
            foreach (var otherPath in m_otherInputFiles)
            {
                var writeTime = File.GetLastWriteTimeUtc(otherPath);
                if (writeTime >= mostRecentOtherInput)
                {
                    mostRecentOtherInput = writeTime;
                }
            }

            if (string.IsNullOrWhiteSpace(CssCombinedFileName))
            {
                // individually-minified files
                foreach (ITaskItem item in this.CssSourceFiles)
                {
                    string outputPath = Regex.Replace(item.ItemSpec, this.CssSourceExtensionPattern, this.CssTargetExtension, RegexOptions.IgnoreCase);
                    if (FileIsWritable(outputPath))
                    {
                        var needToMinify = !File.Exists(outputPath);
                        if (!needToMinify)
                        {
                            var outputTime = File.GetLastWriteTimeUtc(outputPath);
                            if (outputTime <= mostRecentOtherInput
                                || outputTime <= File.GetLastWriteTimeUtc(item.ItemSpec))
                            {
                                needToMinify = true;
                            }
                        }

                        if (needToMinify)
                        {
                            try
                            {
                                string source = File.ReadAllText(item.ItemSpec, GetEncoding(uglifyCommandParser.EncodingInputName, new CssEncoderFallback()));
                                MinifyStyleSheet(source, item.ItemSpec, outputPath);
                            }
                            catch (Exception e)
                            {
                                LogFileError(item.ItemSpec, Strings.DidNotMinify, outputPath, e.Message);
                                throw;
                            }
                        }
                        else
                        {
                            Log.LogMessage(Strings.DidNotMinify, Path.GetFileName(item.ItemSpec), Strings.AlreadyDone);
                        }
                    }
                    else
                    {
                        // log a WARNING that the minification was skipped -- don't break the build
                        Log.LogWarning(Strings.DestinationIsReadOnly, Path.GetFileName(item.ItemSpec), outputPath);
                    }
                }
            }
            else
            {
                // combine the source files and minify the results to a single file
                if (FileIsWritable(this.CssCombinedFileName))
                {
                    // if the output file doesn't exist, we need to minify the sources.
                    // but if it does, check the last-write time; and if any input file is
                    // newer than the output, we need to re-minify.
                    var needToMinify = !File.Exists(this.CssCombinedFileName);
                    if (!needToMinify)
                    {
                        var outputTime = File.GetLastWriteTimeUtc(this.CssCombinedFileName);
                        if (outputTime <= mostRecentOtherInput)
                        {
                            needToMinify = true;
                        }
                        else
                        {
                            foreach (var inputFile in this.CssSourceFiles)
                            {
                                if (File.GetLastWriteTimeUtc(inputFile.ItemSpec) >= outputTime)
                                {
                                    needToMinify = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (needToMinify)
                    {
                        ConcatenateAndMinifyStyleSheet();
                    }
                    else
                    {
                        Log.LogMessage(Strings.DidNotMinify, Path.GetFileName(this.CssCombinedFileName), Strings.AlreadyDone);
                    }
                }
                else
                {
                    // log a WARNING that the minification was skipped -- don't break the build
                    Log.LogWarning(Strings.DestinationIsReadOnly, Path.GetFileName(this.CssCombinedFileName), this.CssCombinedFileName);
                }
            }
        }

        /// <summary>
        /// Minify the given CSS source with the given name, to the given output path
        /// </summary>
        /// <param name="sourceCode">CSS source to minify</param>
        /// <param name="sourceName">name of hte source entity</param>
        /// <param name="outputPath">output path for the minified results</param>
        void MinifyStyleSheet(string sourceCode, string sourceName, string outputPath)
        {
            try
            {
                var result = Uglify.Css(sourceCode, sourceName, this.uglifyCommandParser.CssSettings);
                if (result.HasErrors)
                {
                    foreach (var error in result.Errors)
                    {
                        LogContextError(error);
                    }
                }

                if (!Log.HasLoggedErrors)
                {
                    try
                    {
                        var encoding = GetEncoding(uglifyCommandParser.EncodingOutputName, new CssEncoderFallback());
                        File.WriteAllText(outputPath, result.Code, encoding);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        LogFileError(outputPath, Strings.NoWritePermission, outputPath);
                    }
                }
                else
                {
                    Log.LogWarning(Strings.DidNotMinify, outputPath, Strings.ThereWereErrors);
                }
            }
            catch (Exception e)
            {
                LogFileError(sourceName, Strings.DidNotMinify, outputPath, e.Message);
                throw;
            }
        }

        void ConcatenateAndMinifyStyleSheet()
        {
            // concatenate all the input files together, with each one prefaced by the
            // special #SOURCE comment so the errors and warnings turn out right.
            var inputBuilder = new StringBuilder(8192);
            foreach (var itemSpec in this.CssSourceFiles)
            {
                inputBuilder.AppendFormat("///#SOURCE 1 1 {0}\n", itemSpec.ItemSpec);
                inputBuilder.Append(File.ReadAllText(itemSpec.ItemSpec, GetEncoding(uglifyCommandParser.EncodingInputName, new CssEncoderFallback())));
            }

            MinifyStyleSheet(inputBuilder.ToString(), string.Empty, this.CssCombinedFileName);
        }

        #endregion

        #region Logging methods

        /// <summary>
        /// Call this method to log an error against the build of a particular source file
        /// </summary>
        /// <param name="path">path of the input source file</param>
        /// <param name="messageIdentifier">String resource identifier</param>
        /// <param name="messageArguments">any optional formatting arguments</param>
        void LogFileError(string path, string message, params object[] messageArguments)
        {
            Log.LogError(
                null,
                null,
                null,
                path,
                0,
                0,
                0,
                0,
                message, 
                messageArguments);
        }

        /// <summary>
        /// Call this method to log an error using a ContextError object
        /// </summary>
        /// <param name="error">Error to log</param>
        void LogContextError(UglifyError error)
        {
            // log it either as an error or a warning
            if(TreatWarningsAsErrors || error.Severity < 2)
            {
                Log.LogError(
                    error.Subcategory,  // subcategory 
                    error.ErrorCode,    // error code
                    error.HelpKeyword,  // help keyword
                    error.File,         // file
                    error.StartLine,    // start line
                    error.StartColumn,  // start column
                    error.EndLine > error.StartLine ? error.EndLine : 0,      // end line
                    error.EndLine > error.StartLine || error.EndColumn > error.StartColumn ? error.EndColumn : 0,    // end column
                    error.Message       // message
                    );
            }
            else
            {
                Log.LogWarning(
                    error.Subcategory,  // subcategory 
                    error.ErrorCode,    // error code
                    error.HelpKeyword,  // help keyword
                    error.File,         // file
                    error.StartLine,    // start line
                    error.StartColumn,  // start column
                    error.EndLine > error.StartLine ? error.EndLine : 0,      // end line
                    error.EndLine > error.StartLine || error.EndColumn > error.StartColumn ? error.EndColumn : 0,    // end column
                    error.Message       // message
                    );
            }
        }

        #endregion

        #region Utility methods

        /// <summary>
        /// Validate the symbol map implementation name and return the
        /// map file path based on the output JS file if valid.
        /// </summary>
        /// <param name="outputPath">output JS file path</param>
        /// <returns>output symbol map path, if the implementation name is valid. Otherwise returns empty string.</returns>
        string GetMapFilePath(string outputPath)
        {
            var symbolMapPath = string.Empty;
            if (!string.IsNullOrWhiteSpace(m_symbolsMapName))
            {
                // we want to provide a symbol map
                if (string.Compare(m_symbolsMapName, ScriptSharpSourceMap.ImplementationName, StringComparison.OrdinalIgnoreCase) == 0
                    || string.Compare(m_symbolsMapName, V3SourceMap.ImplementationName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    // valid implementations. See if the symbol map exists -- if not, then we
                    // need to minify. The map file is the output file with the extension ".map" added
                    // (don't replace the .js extension)
                    symbolMapPath = outputPath + ".map";
                }
                else
                {
                    // not valid implementations! no symbol map will be generated
                    Log.LogWarning(Strings.InvalidSymbolMapName, m_symbolsMapName);
                }
            }

            return symbolMapPath;
        }

        /// <summary>
        /// Determine if the supplied path is writable. If the clobber flag is set, this method
        /// will MAKE the file writable if it isn't.
        /// </summary>
        /// <param name="path">file path to be written</param>
        /// <returns>true if the file is writable</returns>
        bool FileIsWritable(string path)
        {
            // the file is writable if it doesn't exist, or is NOT marked readonly
            var fileInfo = new FileInfo(path);
            var writable = !fileInfo.Exists || !fileInfo.IsReadOnly;

            // BUT, if it exists and isn't writable, check the clobber flag. If we want to clobber
            // the file...
            if (fileInfo.Exists && !writable && Clobber)
            {
                // try resetting the read-only flag
                fileInfo.Attributes &= ~FileAttributes.ReadOnly; 

                // and check again
                writable = !fileInfo.IsReadOnly;
            }

            // return the flag
            return writable;
        }

        /// <summary>
        /// Parses the enum value of the given enum type from the string.
        /// </summary>
        /// <typeparam name="T">Type of the enum.</typeparam>
        /// <param name="strValue">Value of the parameter in the string form.</param>
        /// <returns>Parsed enum value</returns>
        T ParseEnumValue<T>(string strValue) where T: struct
        {
            if (!string.IsNullOrWhiteSpace(strValue))
            {
                try
                {
                    return (T)Enum.Parse(typeof(T), strValue, true);
                }
                catch (ArgumentNullException) { }
                catch (ArgumentException) { }
                catch (OverflowException) { }
            }

            // if we cannot parse it for any reason, post the error and stop the task.
            Log.LogError(Strings.InvalidInputParameter, strValue);
            return default(T);
        }

        Encoding GetEncoding(string encodingName, EncoderFallback fallbackEncoder)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(encodingName))
                {
                    // we have specified a different encoding. Create it from the name with
                    // the appropriate fallback encoder (in case it's an encoding that can't 
                    // properly encode ALL possible characters)
                    return Encoding.GetEncoding(encodingName, fallbackEncoder, new DecoderReplacementFallback("\ufffd"));
                }
            }
            catch (ArgumentException)
            {
                // something wrong with the encoding name. log a build warning and use the default
                Log.LogWarning(Strings.InvalidEncodingName, encodingName);
            }

            // use a default: UTF-8 with no BOM. Since UTF-8 can encode everything, we
            // don't need the fallback encoder.
            return new UTF8Encoding(false);
        }

        #endregion
    }
}
