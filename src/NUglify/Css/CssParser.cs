// CssParser.cs
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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUglify.Helpers;
using NUglify.JavaScript;
using NUglify.JavaScript.Visitors;

namespace NUglify.Css
{
    /// <summary>
    /// Parser takes Tokens and parses them into rules and statements
    /// </summary>
    public class CssParser
    {
        #region state fields

        CssScanner m_scanner;
        CssToken m_currentToken;
        bool m_noOutput;
        string m_lastOutputString;
        bool m_mightNeedSpace;
        bool m_skippedSpace;

        bool m_insideCalc;
        bool parsingZeroReducibleProperty;
        // not to be confused with "non-reducible"
        bool parsingNoneReducibleProperty;
        bool m_allowNestedRulesInAtRuleBodies;
        bool m_allowNestingScopePrelude;
        int m_nestedSelectorDepth;

        int lineLength;
        bool m_noColorAbbreviation;
        bool m_encounteredNewLine;
        Stack<StringBuilder> m_builders;

        // Bounded token look-ahead buffer used by the block-body classifier to disambiguate
        // a declaration (prop:value) from a nested style rule (selector { ... }) without
        // disturbing the current token. When non-null, NextToken/NextRawToken/NextSignificantToken
        // replay these already-scanned tokens (in order) before pulling from the scanner again,
        // so the peek is completely transparent to the rest of the parser. It is empty in the
        // common case, which keeps non-nested parsing byte-for-byte identical (Requirement 9).
        Queue<PeekedToken> m_peekBuffer;

        // A token captured during look-ahead together with the scanner's end-of-line flag at the
        // time it was scanned, so replay can restore m_encounteredNewLine exactly.
        struct PeekedToken
        {
            public CssToken Token;
            public bool EndOfLine;
        }

        // this is used to make sure we don't output two newlines in a row.
        // start it as true so we don't start off with a blank line
        bool lastOutputWasNewLine = true;

        int indentLevel;

        // set this to true to force a newline before any other output
        bool m_forceNewLine = false;

        public CssSettings Settings
        {
            get; set;
        }

        // sets a text writer that gets raw tokens written to it
        public TextWriter EchoWriter { get; set; }

        readonly HashSet<string> m_namespaces;

        public string FileContext { get; set; }

        CodeSettings jsSettings;
        public CodeSettings JSSettings
        {
            get => jsSettings;
            set
            {
                if (value != null)
                {
                    // clone the settings
                    jsSettings = value.Clone();

                    // and then make SURE the source format is Expression
                    jsSettings.SourceMode = JavaScriptSourceMode.Expression;
                }
                else
                {
                    jsSettings = new CodeSettings()
                        {
                            KillSwitch = (long)TreeModifications.MinifyStringLiterals,
                            SourceMode = JavaScriptSourceMode.Expression
                        };
                }
            }
        }

        #endregion

        static Regex s_vendorSpecific = new Regex(
            @"^(\-(?<vendor>[^\-]+)\-)?(?<root>.+)$",
            RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

        // IE8 @font-face directive has an issue with src properties that are URLs ending with .EOT
        // that don't have any querystring. They end up sending a malformed HTTP request to the server,
        // which is bad for the server. So we want to automatically fix this for developers: if ANY URL
        // ends in .EOT without a querystring parameters, just add a question mark in the appropriate 
        // location. This fixes the IE8 issue.
        static Regex s_eotIE8Fix = new Regex(
            @"\.eot([^?\\/\w])",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #region Comment-related fields

        // / <summary>
        // / regular expression for matching css comments
        // / Format: /*(anything or nothing inside)*/
        // / </summary>
        ////private static Regex s_regexComments = new Regex(
        ////    @"/\*([^*]|(\*+[^*/]))*\*+/",
        ////    RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// regular expression for matching first comment hack
        /// This is the MacIE ignore bug: /*(anything or nothing inside)\*/.../*(anything or nothing inside)*/
        /// </summary>
        static Regex s_regexHack1 = new Regex(
            @"/\*([^*]|(\*+[^*/]))*\**\\\*/(?<inner>.*?)/\*([^*]|(\*+[^*/]))*\*+/",
            RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Regular expression for matching second comment hack
        /// Hide from everything EXCEPT Netscape 4 and Opera 5
        /// Format: /*/*//*/.../*(anything or nothing inside)*/
        /// </summary>
        static Regex s_regexHack2 = new Regex(
            @"/\*/\*//\*/(?<inner>.*?)/\*([^*]|(\*+[^*/]))*\*+/",
            RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Regular expression for matching third comment hack
        /// Hide from Netscape 4
        /// Format: /*/*/.../*(anything or nothing inside)*/
        /// </summary>
        static Regex s_regexHack3 = new Regex(
            @"/\*/\*/(?<inner>.*?)/\*([^*]|(\*+[^*/]))*\*+/",
            RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Regular expression for matching fourth comment hack
        /// Hide from IE6
        /// Format: property /*(anything or nothing inside)*/:value
        /// WARNING: This does not actually parse the property/value -- it simply looks for a
        /// word character followed by at least one whitespace character, followed
        /// by a simple comment, followed by optional space, followed by a colon.
        /// Does not match the simple word, the space or the colon (just the comment) 
        /// </summary>
        static Regex s_regexHack4 = new Regex(
            @"(?<=\w\s+)/\*([^*]|(\*+[^*/]))*\*+/\s*(?=:)",
            RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Regular expression for matching fifth comment hack
        /// Hide from IE5.5
        /// Format: property:/* (anything or nothing inside) */value
        /// WARNING: This does not actually parse the property/value -- it simply looks for a
        /// word character followed by optional whitespace character, followed
        /// by a colon, followed by optional whitespace, followed by a simple comment.
        /// Does not match initial word or the colon, just the comment.
        /// </summary>
        static Regex s_regexHack5 = new Regex(
            @"(?<=[\w/]\s*:)\s*/\*([^*]|(\*+[^*/]))*\*+/",
            RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Regular expression for matching sixth comment hack -- although not a real hack
        /// Hide from IE6, NOT
        /// Format: property/*(anything or nothing inside)*/:value
        /// NOTE: This doesn't actually hide from IE6; it needs a space before the comment to actually work.
        /// but enoough people code this in their CSS and expect it to be output that I recieved enough
        /// requests to add it to the allowed "hacks"
        /// WARNING: This does not actually parse the property/value -- it simply looks for a
        /// word character followed by a simple comment, followed by optional space, followed by a colon.
        /// Does not match the simple word or the colon (just initial whitespace and comment) 
        /// </summary>
        static Regex s_regexHack6 = new Regex(
            @"(?<=\w)/\*([^*]|(\*+[^*/]))*\*+/\s*(?=:)",
            RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Regular expression for empty comments
        /// These comments don't really do anything. But if the developer wrote an empty
        /// comment (/**/ or /* */), then it has no documentation value and might possibly be
        /// an attempted comment hack.
        /// Format: /**/ or /* */ (single space)
        /// </summary>
        static Regex s_regexHack7 = new Regex(
            @"/\*(\s?)\*/",
            RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

        #endregion

        #region color-related fields

        /// <summary>
        /// matches 6 or 8-digit RGB color value where both r digits are the same, both
        /// g digits are the same, both b digits are the same and optionally both a digits are the same (but r, g, b and a
        /// values are not necessarily the same). Used to identify #rrggbb/aa) values
        /// that can be collapsed to #rgb(a)
        /// </summary>
        static Regex s_rrggbbaa = new Regex(
            @"^\#(?<r>[0-9a-fA-F])\k<r>(?<g>[0-9a-fA-F])\k<g>(?<b>[0-9a-fA-F])\k<b>((?<a>[0-9a-fA-F])\k<a>)?$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static Regex s_validHex = new Regex("^#[0-9a-f]+$",
	        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        // whether we are currently parsing the value for a property that might
        // use color names
        bool m_parsingColorValue;

        #endregion

        #region value-replacement fields

        /// <summary>
        /// regular expression for matching css comments containing special formatted identifiers
        /// for value-replacement matching
        /// Format: /* [id] */
        /// </summary>
        static Regex s_valueReplacement = new Regex(
            @"/\*\s*\[(?<id>\w+)\]\s*\*/",
            RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

        // this variable will be set whenever we encounter a value-replacement comment
        // and have a string to replace it with
        string m_valueReplacement;// = null;

        static bool IsZeroReducibleProperty(string propertyName)
        {
            // Are there other delcarations which shouldn't have 0px->0 within them?
            return !propertyName.Equals("flex", StringComparison.OrdinalIgnoreCase)
	            && !propertyName.StartsWith("--", StringComparison.OrdinalIgnoreCase);
        }

        // Not to be confused with "non-reducible"
        static bool IsNoneReducibleProperty(string propertyName)
        {
	        // Are there other delcarations which can be reduced?
	        return propertyName.Equals("border", StringComparison.OrdinalIgnoreCase) 
	               || propertyName.Equals("border-left", StringComparison.OrdinalIgnoreCase)
	               || propertyName.Equals("border-top", StringComparison.OrdinalIgnoreCase)
	               || propertyName.Equals("border-right", StringComparison.OrdinalIgnoreCase)
	               || propertyName.Equals("border-bottom", StringComparison.OrdinalIgnoreCase)
	               || propertyName.Equals("outline", StringComparison.OrdinalIgnoreCase);
        }
        #endregion

        #region Sharepoint replacement comment regex

        /// <summary>
        /// regular expression for matching Sharepoint Theme css comments
        /// Format: /* [ReplaceBGImage] */
        ///         /* [id(parameters)] */
        ///     where id is one of: ReplaceColor, ReplaceFont, or RecolorImage
        ///     and parameters is anything other than a close square-bracket
        /// </summary>
        static Regex s_sharepointReplacement = new Regex(
            @"/\*\s*\[(ReplaceBGImage|((ReplaceColor|ReplaceFont|RecolorImage)\([^\]]*))\]\s*\*/",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region token-related properties

        TokenType CurrentTokenType => m_currentToken?.TokenType ?? TokenType.None;
        string CurrentTokenText => m_currentToken != null ? m_currentToken.Text : string.Empty;

        // True end-of-input for the parser: the scanner has reached EOF AND there are no
        // buffered look-ahead tokens still waiting to be replayed. All parse loops test this
        // instead of m_scanner.EndOfFile so that a classification peek (which may advance the
        // scanner to EOF while tokens remain buffered) never makes a loop exit prematurely.
        bool AtEof => m_scanner.EndOfFile && (m_peekBuffer == null || m_peekBuffer.Count == 0);

        #endregion

        public CssParser()
        {
            // default settings
            Settings = new CssSettings();

            // create the default settings we'll use for JS expression minification
            // use the defaults, other than to set the kill switch so that it leaves
            // string literals alone (so we don't inadvertently change any delimiter chars)
            JSSettings = null;

            // create a list of strings that represent the namespaces declared
            // in a @namespace statement. We will clear this every time we parse a new source string.
            m_namespaces = new HashSet<string>();

            // default is true
            parsingZeroReducibleProperty = true;
            this.indentLevel = 0;
        }

        public string Parse(string source)
        {
            // clear out the list of namespaces
            m_namespaces.Clear();

            // initialize the color-abbreviation flag
            m_noColorAbbreviation = !Settings.AbbreviateHexColor;

            if (source.IsNullOrWhiteSpace())
            {
                // null or blank - return an empty string
                source = string.Empty;
            }
            else
            {
                // pre-process the comments
                var resetToHacks = false;
                try
                {
                    // see if we need to re-encode the text based on a @charset rule
                    // at the front.
                    source = HandleCharset(source);

                    if (Settings.CommentMode == CssComment.Hacks)
                    {
                        // change the various hacks to important comments so they will be kept
                        // in the output
                        source = s_regexHack1.Replace(source, "/*! \\*/${inner}/*!*/");
                        source = s_regexHack2.Replace(source, "/*!/*//*/${inner}/**/");
                        source = s_regexHack3.Replace(source, "/*!/*/${inner}/*!*/");
                        source = s_regexHack4.Replace(source, "/*!*/");
                        source = s_regexHack5.Replace(source, "/*!*/");
                        source = s_regexHack6.Replace(source, "/*!*/");
                        source = s_regexHack7.Replace(source, "/*!*/");

                        // now that we've changed all our hack comments to important comments, we can
                        // change the flag to Important so all non-important hacks are removed. 
                        // And set a flag to remind us to change it back before we exit, or the NEXT
                        // file we process will have the wrong setting.
                        Settings.CommentMode = CssComment.Important;
                        resetToHacks = true;
                    }

                    // set up for the parse
                    using (StringReader reader = new StringReader(source))
                    {
                        m_scanner = new CssScanner(reader);
                        m_scanner.AllowEmbeddedAspNetBlocks = this.Settings.AllowEmbeddedAspNetBlocks;
                        m_scanner.IgnoreRazorEscapeSequence = this.Settings.IgnoreRazorEscapeSequence;
                        m_scanner.DecodeEscapes = this.Settings.DecodeEscapes;
                        m_scanner.ScannerError += (sender, ea) =>
                            {
                                ea.Error.File = this.FileContext;
                                OnCssError(ea.Error);
                            };
                        m_scanner.ContextChange += (sender, ea) =>
                            {
                                FileContext = ea.FileContext;
                            };

                        // create the initial string builder into which we will be 
                        // building our crunched stylesheet.
                        m_builders = new Stack<StringBuilder>();
                        m_builders.Push(StringBuilderPool.Acquire(source.Length / 2));

                        // get the first token
                        NextToken();

                        switch (Settings.CssType)
                        {
                            case CssType.FullStyleSheet:
                                // parse a style sheet!
                                ParseStylesheet();
                                break;

                            case CssType.DeclarationList:
                                SkipIfSpace();
                                ParseBlockBody(false);
                                break;

                            default:
                                Debug.Fail("UNEXPECTED CSS TYPE");
                                goto case CssType.FullStyleSheet;
                        }

                        if (!AtEof)
                        {
                            int errorNumber = (int)CssErrorCode.ExpectedEndOfFile;
                            OnCssError(new UglifyError()
                                {
                                    IsError = true,
                                    Severity = 0,
                                    Subcategory = UglifyError.GetSubcategory(0),
                                    File = FileContext,
                                    ErrorNumber = errorNumber,
                                    ErrorCode = "CSS{0}".FormatInvariant(errorNumber & (0xffff)),
                                    StartLine = m_currentToken.Context.Start.Line,
                                    StartColumn = m_currentToken.Context.Start.Char,
                                    Message = CssStrings.ExpectedEndOfFile,
                                });
                        }

                        // get the crunched string and dump the string builder
                        // (we don't need it anymore)
                        source = UnwindStackCompletely();
                        m_builders = null;
                    }
                }
                finally
                {
                    // if we had changed our setting object...
                    if (resetToHacks)
                    {
                        // ...be sure to change it BACK for next time.
                        Settings.CommentMode = CssComment.Hacks;
                    }
                }
            }

            return source;
        }

        #region output builders stack methods

        /// <summary>
        /// Push a new string builder onto the builders stack
        /// </summary>
        void PushWaypoint()
        {
            m_builders.Push(StringBuilderPool.Acquire());
        }

        /// <summary>
        /// Pop the top waypoint off the stack.
        /// If the Settings RemoveEmptyBlocks property is false, will keep the text, regardless of the passed-in setting.
        /// </summary>
        /// <param name="keepText">true if push the text of the popped waypoint onto the new top waypoint; false to discard</param>
        /// <returns>true if the popped builder has any text within it</returns>
        bool PopWaypoint(bool keepText)
        {
            var topBuilder = m_builders.Pop();
            if (keepText || !Settings.RemoveEmptyBlocks)
            {
                m_builders.Peek().Append(topBuilder.ToString());
            }

            var isNotEmpty = topBuilder.Length > 0;
            topBuilder.Release();
            return isNotEmpty;
        }

        /// <summary>
        /// Pop the top waypoint off the stack and unconditionally discard its text.
        /// Unlike <see cref="PopWaypoint"/>, this always throws the buffered output away
        /// regardless of the <c>RemoveEmptyBlocks</c> setting. Used to fail a construct
        /// atomically (e.g. an invalid nested selector list) so no partial output leaks.
        /// </summary>
        void DiscardWaypoint()
        {
            var topBuilder = m_builders.Pop();
            topBuilder.Release();
        }

        /// <summary>
        /// Get all the text that has been accumulting in the string builders
        /// on the stack, unwinding the stack until it's empty
        /// </summary>
        /// <returns>string representation of all parsed text</returns>
        string UnwindStackCompletely()
        {
            if (m_builders == null || m_builders.Count == 0)
            {
                // no builders - return an empty string
                return string.Empty;
            }

            if (m_builders.Count == 1)
            {
                // just one builder - return it's string after releasing it.
                var topBuilder = m_builders.Pop();
                var code = topBuilder.ToString();
                topBuilder.Release();
                return code;
            }

            // multiple builders in the stack. Rather than unwinding the stack and
            // pushing each builder onto the previous one, which could add take extra 
            // buffer allocations for each one, instead create an array (we know how big it should be),
            // and stuff each builder's string into the array in reverse order. Then use 
            // string.concat for a single new string allocation.
            var codeList = new string[m_builders.Count];
            var ndx = codeList.Length - 1;
            while(ndx >= 0)
            {
                // pop the topmost builder from the stack and get the text that
                // has been built up inside it and add it to the array in reverse order.
                var topBuilder = m_builders.Pop();
                codeList[ndx--] = topBuilder.ToString();
                topBuilder.Release();
            }

            return string.Concat(codeList);
        }

        #endregion

        #region Character set rule handling

        string HandleCharset(string source)
        {
            // normally we let the encoding switch decode the input file for us, so every character in
            // the source string has already been decoded into the proper UNICODE character point.
            // HOWEVER, that doesn't mean the person passing us the source string has used the right encoding
            // to read the file. Check to see if there's a BOM that hasn't been decoded properly. If so, then
            // that indicates a potential error condition. And if we have a proper BOM, then everything was okay,
            // but we want to strip it off the source so it doesn't interfere with the parsing.
            // We SHOULD also check for a @charset rule to see if we need to re-decode the string. But for now, just
            // throw a low-pri warning if we see an improperly-decided BOM.

            // but first, if it starts with a source comment that we probably added, then we need to pull it off
            // and save it for later.
            var initialSourceDirective = string.Empty;
            if (source.StartsWith("/*/#SOURCE", StringComparison.OrdinalIgnoreCase))
            {
                // find the end of the comment
                var endOfComment = source.IndexOf("*/", 10, StringComparison.Ordinal);
                if (endOfComment > 0)
                {
                    // now skip the first line break if there is one
                    endOfComment += 2;
                    if (source[endOfComment] == '\r')
                    {
                        ++endOfComment;
                        if (source[endOfComment] == '\n')
                        {
                            ++endOfComment;
                        }
                    }
                    else if (source[endOfComment] == '\n' || source[endOfComment] == '\f')
                    {
                        ++endOfComment;
                    }

                    // save the comment and strip it off the source (for now)
                    initialSourceDirective = source.Substring(0, endOfComment);
                    source = source.Substring(endOfComment);
                }
            }

            if (source.StartsWith("\u00ef\u00bb\u00bf", StringComparison.Ordinal))
            {
                // if the first three characters are EF BB BF, then the source file had a UTF-8 BOM in it, but 
                // the BOM didn't get stripped. We MIGHT have some issues: the file indicated it's UTF-8 encoded,
                // but if we didn't properly decode the BOM, then other non-ASCII character sequences might also be
                // improperly decoded. Because that's an IF, we will only throw a pri-1 "programmer may not have intended this"
                // error. However, first check to see if there's a @charset "ascii"; statement at the front. If so,
                // then don't throw any error at all because everything should be ascii, in which case we're most-likely
                // good to go. The quote may be single or double, and the ASCII part should be case-insensentive.
                var charsetAscii = "@charset ";
                if (string.CompareOrdinal(source, 3, charsetAscii, 0, charsetAscii.Length) != 0
                    || (source[3 + charsetAscii.Length] != '"' && source[3 + charsetAscii.Length] != '\'')
                    || string.Compare(source, 4 + charsetAscii.Length, "ascii", 0, 5, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    // we either don't have a @charset statement, or it's pointing to something other than ASCII, in which
                    // case we might have a problem here. But because that's a "MIGHT," let's make it a pri-1 instead of
                    // a pri-0. If there are any problems, the output will be wonky and the developer can up the warning-level
                    // and see this error, then use the proper encoding to read the source. 
                    ReportError(1, CssErrorCode.PossibleCharsetError);
                }

                // remove the BOM
                source = source.Substring(3);
            }
            else if (source.StartsWith("\u00fe\u00ff\u0000\u0000", StringComparison.Ordinal)
                || source.StartsWith("\u0000\u0000\u00ff\u00fe", StringComparison.Ordinal))
            {
                // apparently we had a UTF-32 BOM (either BE or LE) that wasn't stripped. Remove it now.
                // throw a syntax-level error because the rest of the file is probably whack.
                ReportError(0, CssErrorCode.PossibleCharsetError);
                source = source.Substring(4);
            }
            else if (source.StartsWith("\u00fe\u00ff", StringComparison.Ordinal)
                || source.StartsWith("\u00ff\u00fe", StringComparison.Ordinal))
            {
                // apparently we had a UTF-16 BOM (either BE or LE) that wasn't stripped. Remove it now.
                // throw a syntax-level error because the rest of the file is probably whack.
                ReportError(0, CssErrorCode.PossibleCharsetError);
                source = source.Substring(2);
            }
            else if (source.Length > 0 && source[0] == '\ufeff')
            {
                // properly-decoded UNICODE BOM was at the front. Everything should be okay, but strip it
                // so it doesn't interfere with the rest of the processing.
                source = source.Substring(1);
            }

            return string.Concat(initialSourceDirective, source);
        }

        /// <summary>
        /// Returns true if the given property is vendor-specific and the vendor prefix
        /// is in the list of excluded prefixes.
        /// </summary>
        /// <param name="propertyName">The property name</param>
        /// <returns>true if excluded; false otherwise</returns>
        bool IsExcludedVendorPrefix(string propertyName)
        {
            bool isExcluded = false;
            var match = s_vendorSpecific.Match(propertyName);
            if (match.Success)
            {
                isExcluded = Settings.ExcludeVendorPrefixes.Contains(match.Result("$vendor"));
            }

            return isExcluded;
        }

        #endregion

        #region Parse... methods

        Parsed ParseStylesheet()
        {
            Parsed parsed = Parsed.False;

            // ignore any semicolons that may be the result of concatenation on the part of NUglify
            SkipSemicolons();

            // the @charset token can ONLY be at the top of the file
            if (CurrentTokenType == TokenType.CharacterSetSymbol)
            {
                ParseCharset();
            }

            // any number of S, Comment, CDO, or CDC elements
            // (or semicolons possibly introduced via concatenation)
            ParseSCDOCDCComments();

            // any number of imports followed by S, Comment, CDO or CDC
            while (ParseImport() == Parsed.True)
            {
                // any number of S, Comment, CDO, or CDC elements
                // (or semicolons possibly introduced via concatenation)
                ParseSCDOCDCComments();
            }

            // any number of namespaces followed by S, Comment, CDO or CDC
            while (ParseNamespace() == Parsed.True)
            {
                // any number of S, Comment, CDO, or CDC elements
                // (or semicolons possibly introduced via concatenation)
                ParseSCDOCDCComments();
            }

            // the main guts of stuff
            while (ParseRule() == Parsed.True
              || ParseMedia() == Parsed.True
              || ParseContainer() == Parsed.True
              || ParseLayer() == Parsed.True
              || ParseScope() == Parsed.True
              || ParsePage() == Parsed.True
              || ParseFontFace() == Parsed.True
              || ParseKeyFrames() == Parsed.True
              || ParseAtKeyword() == Parsed.True
              || ParseAspNetBlock() == Parsed.True)
            {
                // any number of S, Comment, CDO or CDC elements
                // (or semicolons possibly introduced via concatenation)
                ParseSCDOCDCComments();
            }

            // if there weren't any errors, we SHOULD be at the EOF state right now.
            // if we're not, we may have encountered an invalid, unexpected character.
            while (!AtEof)
            {
                // throw an exception
                ReportError(0, CssErrorCode.UnexpectedToken, CurrentTokenText);

                // skip the token
                NextToken();

                // might be a comment again; check just in case
                // (or semicolons possibly introduced via concatenation)
                ParseSCDOCDCComments();

                // try the guts again
                while (ParseRule() == Parsed.True
                  || ParseMedia() == Parsed.True
                  || ParseContainer() == Parsed.True
                  || ParseLayer() == Parsed.True
                  || ParseScope() == Parsed.True
                  || ParsePage() == Parsed.True
                  || ParseFontFace() == Parsed.True
                  || ParseKeyFrames() == Parsed.True
                  || ParseAtKeyword() == Parsed.True
                  || ParseAspNetBlock() == Parsed.True)
                {
                    // any number of S, Comment, CDO or CDC elements
                    // (or semicolons possibly introduced via concatenation)
                    ParseSCDOCDCComments();
                }
            }

            return parsed;
        }

        Parsed ParseCharset()
        {
            AppendCurrent();
            SkipSpace();

            if (CurrentTokenType != TokenType.String)
            {
                ReportError(0, CssErrorCode.ExpectedCharset, CurrentTokenText);
                SkipToEndOfStatement();
                AppendCurrent();
            }
            else
            {
                Append(' ');
                AppendCurrent();
                SkipSpace();

                if (CurrentTokenType != TokenType.Character || CurrentTokenText != ";")
                {
                    ReportError(0, CssErrorCode.ExpectedSemicolon, CurrentTokenText);
                    SkipToEndOfStatement();
                    // be sure to append the closing token (; or })
                    AppendCurrent();
                }
                else
                {
                    Append(';');
                    NextToken();
                }
            }

            return Parsed.True;
        }

        void ParseSCDOCDCComments()
        {
            while (CurrentTokenType == TokenType.Space
              || CurrentTokenType == TokenType.Comment
              || CurrentTokenType == TokenType.CommentOpen
              || CurrentTokenType == TokenType.CommentClose
              || (CurrentTokenType == TokenType.Character && CurrentTokenText == ";"))
            {
                // don't output any space we encounter here, but do output comments.
                // we also want to skip over any semicolons we may encounter at this point
                if (CurrentTokenType != TokenType.Space && CurrentTokenType != TokenType.Character)
                {
                    AppendCurrent();
                }
                NextToken();
            }
        }

        /*
        private void ParseUnknownBlock()
        {
            // output the opening brace and move to the next
            AppendCurrent();
            // skip space -- there shouldn't need to be space after the opening brace
            SkipSpace();

            // loop until we find the closing breace
            while (!AtEof
              && (CurrentTokenType != TokenType.Character || CurrentTokenText != "}"))
            {
                // see if we are recursing unknown blocks
                if (CurrentTokenType == TokenType.Character && CurrentTokenText == "{")
                {
                    // recursive block
                    ParseUnknownBlock();
                }
                else if (CurrentTokenType == TokenType.AtKeyword)
                {
                    // parse the at-keyword
                    ParseAtKeyword();
                }
                else if (CurrentTokenType == TokenType.Character && CurrentTokenText == ";")
                {
                    // append a semi-colon and skip any space after it
                    AppendCurrent();
                    SkipSpace();
                }
                else
                {
                    // whatever -- just append the token and move on
                    AppendCurrent();
                    NextToken();
                }
            }

            // output the closing brace and skip any trailing space
            AppendCurrent();
            SkipSpace();
        }
        */

        Parsed ParseAtKeyword()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.AtKeyword)
            {
                // only report an unexpected at-keyword IF the identifier doesn't start 
                // with a hyphen, because that would be a vendor-specific at-keyword,
                // which is theoretically okay.
                if (!CurrentTokenText.StartsWith("@-", StringComparison.OrdinalIgnoreCase))
                {
                    ReportError(2, CssErrorCode.UnexpectedAtKeyword, CurrentTokenText);
                }

                SkipToEndOfStatement();
                AppendCurrent();
                SkipSpace();
                NewLine();
                parsed = Parsed.True;
            }
            else if(CurrentTokenType == TokenType.Supports)
            {
                parsed = ParseSupports();
            }
            else if (CurrentTokenType == TokenType.ContainerSymbol)
            {
                parsed = ParseContainer();
            }
            else if (CurrentTokenType == TokenType.CharacterSetSymbol)
            {
                // we found a charset at-rule. Problem is, @charset can only be the VERY FIRST token
                // in the file, and we process it special. So if we get here, then it's NOT the first
                // token, and clients will ignore it. Throw a warning, but still process it.
                ReportError(2, CssErrorCode.UnexpectedCharset, CurrentTokenText);
                parsed = ParseCharset();
            }
            return parsed;
        }

        Parsed ParseSupportsCondition(bool notOperatorAllowed, bool andOrOperatorsNeeded)
        {
            bool foundSupportsCondition = false;
            //more operators? no let parent finish ')'
            if (CurrentTokenType == TokenType.Character && andOrOperatorsNeeded)
            {
                if (CurrentTokenText == ")")
                {
                    return Parsed.Empty;
                }
            }

            bool operatorFound = false;
            if (CurrentTokenType == TokenType.Identifier)
            {
                operatorFound =
                ParseSupportsOperator(notOperatorAllowed, andOrOperatorsNeeded) == Parsed.True;
                if (!operatorFound) //no operator should be declaration then let parent finish that...
                {
                    return Parsed.Empty;
                }
            }

            if (CurrentTokenType == TokenType.Character && CurrentTokenText == "(")
            {
                AppendCurrent();
                SkipSpace();
                if (ParseSupportsCondition(true, false) == Parsed.True)
                {
                    foundSupportsCondition = true;
                }
                if (CurrentTokenType == TokenType.Identifier)
                {
                    if (ParseDeclaration() == Parsed.True)
                    {
                        foundSupportsCondition = true;
                        SkipIfSpace();
                        if (CurrentTokenType == TokenType.Character && CurrentTokenText == ")")
                        {
                            AppendCurrent();
                            SkipSpace();
                            ParseSupportsCondition(false, true);
                        }
                    }
                }
                else if (foundSupportsCondition && CurrentTokenType == TokenType.Character && CurrentTokenText == ")")
                {//found a condition somewere so we can close up 
                    AppendCurrent();
                    SkipSpace(); //finish up
                }
                else
                {
                    ReportError(0, CssErrorCode.UnexpectedToken, CurrentTokenText);
                    return Parsed.False;
                }
            }

            if (foundSupportsCondition && CurrentTokenType == TokenType.Identifier)
            {
                var upper = CurrentTokenText.ToUpperInvariant();
                if (upper == "AND" || upper == "OR")
                {
                    ParseSupportsCondition(false, true);
                }
            }

            return foundSupportsCondition ? Parsed.True : Parsed.False;
        }

        Parsed ParseSupportsOperator(bool notOperatorAllowed, bool andOrOperatorsNeeded)
        {
            var parsed = Parsed.False;
            if (notOperatorAllowed)
            {
                if (CurrentTokenText.ToUpperInvariant() == "NOT")
                {
                    Append("not");
                    Append(' ');
                    SkipSpace();
                    parsed = Parsed.True;
                }
            }
            if (andOrOperatorsNeeded)
            {
                var upper = CurrentTokenText.ToUpperInvariant();
                if (upper == "AND" || upper == "OR")
                {
                    Append(' ');
                    Append(upper.ToLower());
                    Append(' ');
                    SkipSpace();
                    parsed = Parsed.True;
                }
                else
                {
                    ReportError(0, CssErrorCode.UnexpectedToken, CurrentTokenText);
                    return Parsed.False;
                }
            }
            return parsed;
        }

        Parsed ParseSupports()
        {
            bool notOperatorAllowed = false;
            bool andOrOperatorsNeeded = false;

            var keepDirective = true;
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.Supports)
            {
                PushWaypoint();
                NewLine();
                AppendCurrent();
                
                SkipSpace();


                if (CurrentTokenType != TokenType.Character || CurrentTokenText != "(")
	                Append(' ');

                if (CurrentTokenType == TokenType.Identifier)
                {
                    if (CurrentTokenText.ToUpperInvariant() == "NOT")
                    {
                        notOperatorAllowed = true;
                    }
                    else
                    {
                        ReportError(0, CssErrorCode.UnexpectedToken, CurrentTokenText);
                        return Parsed.False;
                    }
                }

                if (ParseSupportsCondition(notOperatorAllowed, andOrOperatorsNeeded) == Parsed.True)
                {
                    // expect current token to be the opening brace when calling
                    if (CurrentTokenType != TokenType.Character || CurrentTokenText != "{")
                    {
                        ReportError(0, CssErrorCode.ExpectedOpenBrace, CurrentTokenText);
                        SkipToEndOfStatement();
                        AppendCurrent();
                        SkipSpace();
                    }
                    else
                    {
                        NewLine();
                        AppendCurrent();
                        SkipSpace();


                        PushWaypoint();

                        // the main guts of stuff (copied from stylesheet)
                        while (ParseAtRuleBodyRule() == Parsed.True
                          || ParseMedia() == Parsed.True
                          || ParseContainer() == Parsed.True
                          || ParseLayer() == Parsed.True
                          || ParseScope() == Parsed.True
                          || ParsePage() == Parsed.True
                          || ParseFontFace() == Parsed.True
                          || ParseKeyFrames() == Parsed.True
                          || ParseAtKeyword() == Parsed.True
                          || ParseAspNetBlock() == Parsed.True)
                        {
                            // any number of S, Comment, CDO or CDC elements
                            // (or semicolons possibly introduced via concatenation)
                            ParseSCDOCDCComments();
                        }
                        SkipIfSpace();

                        keepDirective = PopWaypoint(true);

                        if (CurrentTokenType != TokenType.Character || CurrentTokenText != "}")
                        {
                            // distinguish an EOF-truncated block from one closed by an
                            // unexpected non-brace token so the correct error is reported
                            // for nested rules contained in the @supports block (Requirement 7.6)
                            if (AtEof)
                            {
                                ReportError(0, CssErrorCode.UnexpectedEndOfFile);
                            }
                            else
                        {
                            ReportError(0, CssErrorCode.ExpectedClosingBrace, CurrentTokenText);
                            SkipToEndOfStatement();
                        }
                        }
                        else
                        {
                            AppendCurrent();
                            SkipSpace();
                            parsed = Parsed.True;
                        }
                    }
                }
                PopWaypoint(keepDirective);
            }
            return parsed;
        }

        Parsed ParseAspNetBlock()
        {
            Parsed parsed = Parsed.False;
            if (Settings.AllowEmbeddedAspNetBlocks &&
                CurrentTokenType == TokenType.AspNetBlock)
            {
                AppendCurrent();
                SkipSpace();
                parsed = Parsed.True;
            }
            return parsed;
        }

        Parsed ParseNamespace()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.NamespaceSymbol)
            {
                NewLine();
                AppendCurrent();
                SkipSpace();

                if (CurrentTokenType == TokenType.Identifier)
                {
                    Append(' ');
                    AppendCurrent();

                    // if the namespace is not already in the list, 
                    // save current text as a declared namespace value 
                    // that can be used in the rest of the code
                    if (!m_namespaces.Add(CurrentTokenText))
                    {
                        // error -- we already have this namespace in the list
                        ReportError(1, CssErrorCode.DuplicateNamespaceDeclaration, CurrentTokenText);
                    }

                    SkipSpace();
                }

                if (CurrentTokenType != TokenType.String
                  && CurrentTokenType != TokenType.Uri)
                {
                    ReportError(0, CssErrorCode.ExpectedNamespace, CurrentTokenText);
                    SkipToEndOfStatement();
                    AppendCurrent();
                }
                else
                {
                    Append(' ');
                    AppendCurrent();
                    SkipSpace();

                    if (CurrentTokenType == TokenType.Character
                      && CurrentTokenText == ";")
                    {
                        Append(';');
                        SkipSpace();
                        NewLine();
                    }
                    else
                    {
                        ReportError(0, CssErrorCode.ExpectedSemicolon, CurrentTokenText);
                        SkipToEndOfStatement();
                        AppendCurrent();
                    }
                }

                parsed = Parsed.True;
            }
            return parsed;
        }

        void ValidateNamespace(string namespaceIdent)
        {
            // check it against list of all declared @namespace names
            if (!string.IsNullOrEmpty(namespaceIdent)
                && namespaceIdent != "*"
                && !m_namespaces.Contains(namespaceIdent))
            {
                ReportError(0, CssErrorCode.UndeclaredNamespace, namespaceIdent);
            }
        }

        Parsed ParseKeyFrames()
        {
            // '@keyframes' IDENT '{' keyframes-blocks '}'
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.KeyFramesSymbol)
            {
                // found the @keyframes at-rule
                parsed = Parsed.True;

                NewLine();
                AppendCurrent();
                SkipSpace();

                // needs to be followed by an identifier
                if (CurrentTokenType == TokenType.Identifier || CurrentTokenType == TokenType.String)
                {
                    // if this is an identifier, then we need to make sure we output a space
                    // character so the identifier doesn't get attached to the previous @-rule
                    if (CurrentTokenType == TokenType.Identifier || Settings.OutputDeclarationWhitespace)
	                    Append(' ');

                    AppendCurrent();
                    SkipSpace();
                }
                else
                {
                    ReportError(0, CssErrorCode.ExpectedIdentifier, CurrentTokenText);
                }

                // followed by keyframe blocks surrounded with curly-braces
                if (CurrentTokenType == TokenType.Character && CurrentTokenText == "{")
                {
                    if (Settings.BlocksStartOnSameLine == BlockStart.NewLine || Settings.BlocksStartOnSameLine == BlockStart.UseSource && m_encounteredNewLine)
                    {
                        NewLine();
                    }
                    else if (Settings.OutputDeclarationWhitespace)
                    {
                        Append(' ');
                    }

                    AppendCurrent();
                    Indent();
                    NewLine();
                    SkipSpace();

                    ParseKeyFrameBlocks();

                    // better end with a curly-brace
                    Unindent();
                    NewLine();
                    if (CurrentTokenType == TokenType.Character && CurrentTokenText == "}")
                    {
                        NewLine();
                        AppendCurrent();
                        SkipSpace();
                    }
                    else
                    {
                        ReportError(0, CssErrorCode.ExpectedClosingBrace, CurrentTokenText);
                        SkipToEndOfDeclaration();
                    }
                }
                else
                {
                    ReportError(0, CssErrorCode.ExpectedOpenBrace, CurrentTokenText);
                    SkipToEndOfStatement();
                }
            }
            return parsed;
        }

        void ParseKeyFrameBlocks()
        {
            // [ keyframe-selectors block ]*
            while (ParseKeyFrameSelectors() == Parsed.True)
            {
                ParseDeclarationBlock(false);

                // set the force-newline flag to true so that any selectors we may find next
                // will start on a new line
                m_forceNewLine = true;
            }

            // reset the flag
            m_forceNewLine = false;
        }

        Parsed ParseKeyFrameSelectors()
        {
            // [ 'from' | 'to' | PERCENTAGE ] [ ',' [ 'from' | 'to' | PERCENTAGE ] ]*
            Parsed parsed = Parsed.False;

            // see if we start with a percentage or the words "from" or "to"
            if (CurrentTokenType == TokenType.Percentage)
            {
                AppendCurrent();
                SkipSpace();
                parsed = Parsed.True;
            }
            else if (CurrentTokenType == TokenType.Identifier)
            {
                var upperIdent = CurrentTokenText.ToUpperInvariant();
                if (string.CompareOrdinal(upperIdent, "FROM") == 0
                    || string.CompareOrdinal(upperIdent, "TO") == 0)
                {
                    AppendCurrent();
                    SkipSpace();
                    parsed = Parsed.True;
                }
            }

            // if we found one, keep going as long as there are others comma-separated
            while (parsed == Parsed.True && CurrentTokenType == TokenType.Character && CurrentTokenText == ",")
            {
                // append the comma, and if this is multiline mode, follow it with a space for readability
                AppendCurrent();
                if (Settings.OutputDeclarationWhitespace)
	                Append(' ');

                SkipSpace();

                // needs to be either a percentage or "from" or "to"
                if (CurrentTokenType == TokenType.Percentage)
                {
                    AppendCurrent();
                    SkipSpace();
                }
                else if (CurrentTokenType == TokenType.Identifier)
                {
                    var upperIdent = CurrentTokenText.ToUpperInvariant();
                    if (string.CompareOrdinal(upperIdent, "FROM") == 0
                        || string.CompareOrdinal(upperIdent, "TO") == 0)
                    {
                        AppendCurrent();
                        SkipSpace();
                    }
                }
                else
                {
                    ReportError(0, CssErrorCode.ExpectedPercentageFromOrTo, CurrentTokenText);
                }
            }

            return parsed;
        }

        Parsed ParseImport()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.ImportSymbol)
            {
                NewLine();
                AppendCurrent();
                SkipSpace();

                if (CurrentTokenType != TokenType.String
                  && CurrentTokenType != TokenType.Uri)
                {
                    ReportError(0, CssErrorCode.ExpectedImport, CurrentTokenText);
                    SkipToEndOfStatement();
                    AppendCurrent();
                }
                else
                {
                    // only need a space if this is a Uri -- a string starts with a quote delimiter
                    // and won't get parsed as teh end of the @import token
                    if (CurrentTokenType == TokenType.Uri || Settings.OutputDeclarationWhitespace)
                    {
                        Append(' ');
                    }

                    // append the file (string or uri)
                    AppendCurrent();
                    SkipSpace();

                    // optional comma-separated list of media queries
                    // won't need a space because the ending is either a quote or a paren
                    ParseMediaQueryList(false);

                    if (CurrentTokenType == TokenType.Character && CurrentTokenText == ";")
                    {
                        Append(';');
                        NewLine();
                    }
                    else
                    {
                        ReportError(0, CssErrorCode.ExpectedSemicolon, CurrentTokenText);
                        SkipToEndOfStatement();
                        AppendCurrent();
                    }
                }
                SkipSpace();
                parsed = Parsed.True;
            }

            return parsed;
        }

        Parsed ParseMedia()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.MediaSymbol)
            {
                // push a waypoint. We're not going to want to output this @media directive
                // if the rule collection is empty
                var keepDirective = true;
                PushWaypoint();

                NewLine();
                AppendCurrent();
                SkipSpace();

                var indented = false;

                // might need a space because the last token was @media
                if (ParseMediaQueryList(true) == Parsed.True)
                {
                    if (CurrentTokenType == TokenType.Character && CurrentTokenText == "{")
                    {
                        if (Settings.BlocksStartOnSameLine == BlockStart.NewLine || Settings.BlocksStartOnSameLine == BlockStart.UseSource && m_encounteredNewLine)
                        {
                            NewLine();
                        }
                        else if (Settings.OutputDeclarationWhitespace)
                        {
                            Append(' ');
                        }

                        AppendCurrent();
                        Indent();
                        indented = true;
                        SkipSpace();

                        // push a waypoint for the rules inside the @media directive
                        PushWaypoint();

                        // the main guts of stuff
                        while (ParseAtRuleBodyRule() == Parsed.True
                          || ParseMedia() == Parsed.True
                          || ParseContainer() == Parsed.True
                          || ParseLayer() == Parsed.True
                          || ParseScope() == Parsed.True
                          || ParsePage() == Parsed.True
                          || ParseFontFace() == Parsed.True
                          || ParseAtKeyword() == Parsed.True
                          || ParseAspNetBlock() == Parsed.True)
                        {
                            // any number of S, Comment, CDO or CDC elements
                            ParseSCDOCDCComments();
                        }

                        // we want to keep this directive if we've actually parsed anything;
                        // otherwise we'll throw out the whole thing.
                        keepDirective = PopWaypoint(true);
                    }
                    else
                    {
                        SkipToEndOfStatement();
                    }

                    if (CurrentTokenType == TokenType.Character)
                    {
                        if (CurrentTokenText == ";")
                        {
                            AppendCurrent();
                            if (indented)
                            {
                                Unindent();
                            }
                            
                            NewLine();
                        }
                        else if (CurrentTokenText == "}")
                        {
                            if (indented)
                            {
                                Unindent();
                            }

                            NewLine();
                            AppendCurrent();
                        }
                        else
                        {
                            // a block was opened but was closed by an unexpected non-brace
                            // token; report the malformed block so nested rules inside the
                            // @media block are not silently emitted (Requirement 7.6)
                            if (indented)
                            {
                                Unindent();
                                ReportError(0, CssErrorCode.ExpectedClosingBrace, CurrentTokenText);
                            }

                            SkipToEndOfStatement();
                            AppendCurrent();
                        }
                    }
                    else
                    {
                        // reached EOF (or a non-character token) before the block's closing
                        // brace; report the truncated block (Requirement 7.6)
                        if (indented)
                        {
                            Unindent();
                            if (AtEof)
                            {
                                ReportError(0, CssErrorCode.UnexpectedEndOfFile);
                            }
                            else
                            {
                                ReportError(0, CssErrorCode.ExpectedClosingBrace, CurrentTokenText);
                            }
                        }

                        SkipToEndOfStatement();
                        AppendCurrent();
                    }

                    SkipSpace();
                    parsed = Parsed.True;
                }
                else
                {
                    SkipToEndOfStatement();
                }

                PopWaypoint(keepDirective);
            }

            return parsed;
        }

        Parsed ParseContainer()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.ContainerSymbol)
            {
                var keepDirective = true;
                PushWaypoint();

                NewLine();
                AppendCurrent();
                SkipSpace();

                ParseContainerPrelude();

                if (CurrentTokenType == TokenType.Character && CurrentTokenText == "{")
                {
                    keepDirective = ParseGroupingAtRuleBlock();
                }
                else
                {
                    ReportError(0, CssErrorCode.ExpectedOpenBrace, CurrentTokenText);
                    SkipToEndOfStatement();
                    AppendCurrent();
                    SkipSpace();
                }

                PopWaypoint(keepDirective);
                parsed = Parsed.True;
            }

            return parsed;
        }

        // Parses an @layer at-rule (CSS Cascade Layers). Handles both forms:
        //   - statement form:  @layer name;            (and comma lists: @layer a, b, c;)
        //   - block form:      @layer name { ... }     (and the anonymous @layer { ... })
        // For the block form, the same rule-loop used by @media/@supports parses the contained
        // style rules (and, through ParseRule/ParseNestedRule, their nested rules).
        Parsed ParseLayer()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.LayerSymbol)
            {
                // push a waypoint. We may want to throw out an empty @layer block directive.
                var keepDirective = true;
                PushWaypoint();

                NewLine();
                AppendCurrent();
                SkipSpace();

                // optional (possibly comma-separated, dotted) layer-name prelude
                ParseLayerNameList();

                if (CurrentTokenType == TokenType.Character && CurrentTokenText == ";")
                {
                    // statement form (@layer a, b;) -- always kept, there is no block to empty out
                    Append(';');
                    NewLine();
                    SkipSpace();
                }
                else if (CurrentTokenType == TokenType.Character && CurrentTokenText == "{")
                {
                    // block form -- run the shared grouping-at-rule block body loop
                    keepDirective = ParseGroupingAtRuleBlock();
                }
                else
                {
                    // neither a terminating semicolon nor a block
                    ReportError(0, CssErrorCode.ExpectedSemicolonOrOpenBrace, CurrentTokenText);
                    SkipToEndOfStatement();
                    AppendCurrent();
                    SkipSpace();
                }

                PopWaypoint(keepDirective);
                parsed = Parsed.True;
            }

            return parsed;
        }

        // Parses an @scope at-rule (CSS Scoping). Handles the prelude
        //   @scope (start) to (end) { ... }
        // where the scope-start "(selector-list)" and the scope-end "to (selector-list)" are both
        // optional (including the fully-anonymous @scope { ... }). For the block form the same
        // rule-loop used by @media/@supports parses the contained style rules and their nested rules.
        Parsed ParseScope()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.ScopeSymbol)
            {
                // push a waypoint. We may want to throw out an empty @scope block directive.
                var keepDirective = true;
                PushWaypoint();

                NewLine();
                AppendCurrent();
                SkipSpace();

                // optional scope-start / scope-end prelude
                ParseScopePrelude();

                if (CurrentTokenType == TokenType.Character && CurrentTokenText == "{")
                {
                    // block form -- run the shared grouping-at-rule block body loop
                    keepDirective = ParseGroupingAtRuleBlock();
                }
                else
                {
                    ReportError(0, CssErrorCode.ExpectedOpenBrace, CurrentTokenText);
                    SkipToEndOfStatement();
                    AppendCurrent();
                    SkipSpace();
                }

                PopWaypoint(keepDirective);
                parsed = Parsed.True;
            }

            return parsed;
        }

        // Parses the optional comma-separated list of layer names in an @layer prelude. A single
        // space separates "@layer" from the first name; commas use the same formatting as a
        // selector list (no surrounding whitespace when minifying, a trailing space in pretty
        // output). No name at all is the valid anonymous form (@layer { ... }).
        void ParseLayerNameList()
        {
            if (CurrentTokenType != TokenType.Identifier)
            {
                // anonymous layer -- nothing to emit
                return;
            }

            // "@layer" needs a space before the first name so they don't run together
            Append(' ');
            ParseLayerName();

            while (CurrentTokenType == TokenType.Character && CurrentTokenText == ",")
            {
                Append(',');

                // check the line length -- if we're past the threshold, start a new line;
                // otherwise add a single space when producing pretty output.
                if (lineLength >= Settings.LineBreakThreshold)
                    AddNewLine();
                else if (Settings.OutputDeclarationWhitespace)
                    Append(' ');

                SkipSpace();
                ParseLayerName();
            }
        }

        // Parses a single (possibly dotted) layer name: IDENT ( '.' IDENT )*. The dots are emitted
        // with no surrounding whitespace so a nested layer name such as "framework.component" is
        // preserved exactly.
        void ParseLayerName()
        {
            if (CurrentTokenType != TokenType.Identifier)
                return;

            AppendCurrent();
            SkipSpace();

            while (CurrentTokenType == TokenType.Character && CurrentTokenText == ".")
            {
                Append('.');
                SkipSpace();

                if (CurrentTokenType == TokenType.Identifier)
                {
                    AppendCurrent();
                    SkipSpace();
                }
                else
                {
                    ReportError(0, CssErrorCode.ExpectedIdentifier, CurrentTokenText);
                    break;
                }
            }
        }

        // Parses the optional @scope prelude: an optional scope-start "( selector-list )" followed
        // by an optional scope-end "to ( selector-list )". Selectors inside the parens are parsed
        // with the existing selector machinery so combinator/whitespace minification is identical
        // to non-nested selectors.
        void ParseScopePrelude()
        {
            // optional scope-start: ( selector-list )
            if (CurrentTokenType == TokenType.Character && CurrentTokenText == "(")
            {
                ParseScopeSelectorGroup();
                SkipIfSpace();
            }

            // optional scope-end: 'to' ( selector-list )
            if (CurrentTokenType == TokenType.Identifier
                && string.Equals(CurrentTokenText, "to", StringComparison.OrdinalIgnoreCase))
            {
                // separate 'to' from whatever precedes it and from the following '('
                Append(' ');
                AppendCurrent();
                Append(' ');
                SkipSpace();

                if (CurrentTokenType == TokenType.Character && CurrentTokenText == "(")
                {
                    ParseScopeSelectorGroup();
                    SkipIfSpace();
                }
                else
                {
                    ReportError(0, CssErrorCode.UnexpectedToken, CurrentTokenText);
                    SkipToEndOfStatement();
                }
            }
        }

        // Parses a "( selector-list )" group used by an @scope scope-start / scope-end. Expects the
        // current token to be the opening paren; emits the parens and the (optionally empty)
        // selector list they contain.
        void ParseScopeSelectorGroup()
        {
            // current token is '('
            AppendCurrent();
            SkipSpace();

            if (!(CurrentTokenType == TokenType.Character && CurrentTokenText == ")"))
            {
                if (m_allowNestingScopePrelude)
                {
                    ParseNestedSelectorList();
                }
                else
                {
                    ParseSelectorList();
                }
            }

            if (CurrentTokenType == TokenType.Character && CurrentTokenText == ")")
            {
                AppendCurrent();
                SkipSpace();
            }
            else
            {
                ReportError(0, CssErrorCode.UnexpectedToken, CurrentTokenText);
                SkipToEndOfStatement();
            }
        }

        void ParseContainerPrelude()
        {
            CssToken previousToken = null;
            var isFirstToken = true;
            while (!AtEof)
            {
                if (CurrentTokenType == TokenType.Character && CurrentTokenText == "{")
                    break;

                if (CurrentTokenType == TokenType.Character && CurrentTokenText == ";")
                    break;

                if (isFirstToken || NeedsContainerPreludeSpace(previousToken, m_currentToken))
                    Append(' ');

                AppendCurrent();
                previousToken = m_currentToken;
                isFirstToken = false;
                SkipSpace();
            }
        }

        static bool NeedsContainerPreludeSpace(CssToken previousToken, CssToken currentToken)
        {
            if (previousToken == null || currentToken == null)
                return false;

            // Function tokens already include their opening parenthesis (for example "style("),
            // so the next token belongs immediately after that text with no inserted space.
            if (previousToken.TokenType == TokenType.Function)
                return false;

            if (previousToken.TokenType == TokenType.Character)
            {
                var previousText = previousToken.Text;
                if (previousText == "(" || previousText == "[" || previousText == "{" || previousText == ":" || previousText == ",")
                    return false;
            }

            if (currentToken.TokenType == TokenType.Character)
            {
                var currentText = currentToken.Text;
                if (currentText == ")" || currentText == "]" || currentText == "}" || currentText == "," || currentText == ":" || currentText == ";")
                    return false;

                if (currentText == "(")
                {
                    return IsContainerPreludeWordLike(previousToken);
                }

                return false;
            }

            return IsContainerPreludeWordLike(previousToken);
        }

        static bool IsContainerPreludeWordLike(CssToken token)
        {
            if (token == null)
                return false;

            switch (token.TokenType)
            {
                case TokenType.Identifier:
                case TokenType.Function:
                case TokenType.Number:
                case TokenType.Dimension:
                case TokenType.RelativeLength:
                case TokenType.AbsoluteLength:
                case TokenType.Resolution:
                case TokenType.Angle:
                case TokenType.Time:
                case TokenType.Frequency:
                case TokenType.Speech:
                case TokenType.Percentage:
                case TokenType.Hash:
                case TokenType.String:
                case TokenType.Uri:
                case TokenType.Not:
                case TokenType.Any:
                case TokenType.Has:
                case TokenType.Is:
                case TokenType.Where:
                case TokenType.Matches:
                    return true;

                default:
                    return token.TokenType == TokenType.Character && token.Text == ")";
            }
        }

        // Shared block-body loop for the grouping at-rules @layer and @scope. Expects the current
        // token to be the opening brace of the block. Mirrors the @media/@supports body loop so
        // contained style rules (and their nested rules) parse correctly, then consumes the closing
        // brace. Returns true when the directive should be kept (its body produced output).
        bool ParseGroupingAtRuleBlock()
        {
            if (Settings.BlocksStartOnSameLine == BlockStart.NewLine || Settings.BlocksStartOnSameLine == BlockStart.UseSource && m_encounteredNewLine)
            {
                NewLine();
            }
            else if (Settings.OutputDeclarationWhitespace)
            {
                Append(' ');
            }

            AppendCurrent();
            Indent();
            SkipSpace();

            // push a waypoint for the rules inside the block so an empty block can be dropped
            PushWaypoint();

            // the main guts of stuff (same body loop used by @media/@supports)
            while (ParseAtRuleBodyRule() == Parsed.True
              || ParseMedia() == Parsed.True
              || ParseContainer() == Parsed.True
              || ParseLayer() == Parsed.True
              || ParseScope() == Parsed.True
              || ParsePage() == Parsed.True
              || ParseFontFace() == Parsed.True
              || ParseKeyFrames() == Parsed.True
              || ParseAtKeyword() == Parsed.True
              || ParseAspNetBlock() == Parsed.True)
            {
                // any number of S, Comment, CDO or CDC elements
                ParseSCDOCDCComments();
            }

            var keepDirective = PopWaypoint(true);

            Unindent();

            if (CurrentTokenType == TokenType.Character && CurrentTokenText == "}")
            {
                NewLine();
                AppendCurrent();
                SkipSpace();
            }
            else if (AtEof)
            {
                // no closing brace, just the end of the file
                ReportError(0, CssErrorCode.UnexpectedEndOfFile);
            }
            else
            {
                ReportError(0, CssErrorCode.ExpectedClosingBrace, CurrentTokenText);
                SkipToEndOfStatement();
                AppendCurrent();
                SkipSpace();
            }

            return keepDirective;
        }

        Parsed ParseAtRuleBodyRule()
        {
            if (!m_allowNestedRulesInAtRuleBodies)
                return ParseRule();

            var itemKind = ClassifyBlockItem();
            if (itemKind == BlockItemKind.Declaration)
            {
                var parsedDeclaration = ParseDeclaration();
                if (parsedDeclaration == Parsed.True)
                {
                    if (CurrentTokenType == TokenType.Character && CurrentTokenText == ";")
                    {
                        AppendCurrent();
                        SkipSpace();
                    }
                    else if (!(CurrentTokenType == TokenType.Character && CurrentTokenText == "}"))
                    {
                        ReportError(0, CssErrorCode.ExpectedSemicolon, CurrentTokenText);
                        SkipToEndOfDeclaration();
                    }
                }

                return parsedDeclaration;
            }

            if (itemKind == BlockItemKind.NestedAtRule)
                return Parsed.False;

            if (CurrentTokenType == TokenType.NestingSelector
                || CurrentTokenType == TokenType.Hash
                || CurrentTokenType == TokenType.Identifier
                || (CurrentTokenType == TokenType.Character
                    && (CurrentTokenText == "."
                        || CurrentTokenText == "*"
                        || CurrentTokenText == "["
                        || CurrentTokenText == ":"
                        || CurrentTokenText == "|"
                        || CurrentTokenText == ">"
                        || CurrentTokenText == "+"
                        || CurrentTokenText == "~"
                        || CurrentTokenText == ",")))
            {
                return ParseNestedRule();
            }

            return ParseRule();
        }

        Parsed ParseMediaQueryList(bool mightNeedSpace)
        {
            // see if we have a media query
            Parsed parsed = ParseMediaQuery(mightNeedSpace);

            // it's a comma-separated list, so as long as we find a comma, keep parsing queries
            while(CurrentTokenType == TokenType.Character && CurrentTokenText == ",")
            {
                // output the comma and skip any space
                AppendCurrent();
                SkipSpace();

                if (ParseMediaQuery(false) != Parsed.True)
                {
                    // fail
                    ReportError(0, CssErrorCode.ExpectedMediaQuery, CurrentTokenText);
                }
            }

            return parsed;
        }

        Parsed ParseMediaQuery(bool firstQuery)
        {
            var parsed = Parsed.False;
            var mightNeedSpace = firstQuery;

            // we have an optional word ONLY or NOT -- they will show up as identifiers here
            if (CurrentTokenType == TokenType.Identifier &&
                (string.Compare(CurrentTokenText, "ONLY", StringComparison.OrdinalIgnoreCase) == 0
                || string.Compare(CurrentTokenText, "NOT", StringComparison.OrdinalIgnoreCase) == 0))
            {
                // if this is the first query, the last thing we output was @media, which will need a separator.
                // if it's not the first, the last thing was a comma, so no space is needed.
                // but if we're expanding the output, we always want a space
                if (firstQuery || Settings.OutputDeclarationWhitespace)
                {
                    Append(' ');
                }

                // output the only/not string and skip any subsequent space
                AppendCurrent();
                SkipSpace();
                
                // we might need a space since the last thing was the only/not
                mightNeedSpace = true;
            }

            // we should be at a either a media type or an expression
            if (CurrentTokenType == TokenType.Identifier)
            {
                // media type
                // if we might need a space, output it now
                if (mightNeedSpace || Settings.OutputDeclarationWhitespace)
	                Append(' ');

                // output the media type
                AppendCurrent();
                SkipSpace();

                // the media type is an identifier, so we might need a space
                mightNeedSpace = true;

                // the next item should be either AND or the start of the block
                parsed = Parsed.True;
            }
            else if (CurrentTokenType == TokenType.Character && CurrentTokenText == "(")
            {
                // no media type -- straight to an expression
                ParseMediaQueryExpression();

                // The straight-up spec says the whitespace is optional, so at this point you'd
                // THINK we wouldn't need whitespace between a close-paren and the word "and."
                // However, there is an errata published:
                // http://www.w3.org/Style/2012/REC-mediaqueries-20120619-errata.html
                // that makes whitespace before the AND mandatory.
                // The errata is completely correct with regards to making the whitespace AFTER
                // the "and" mandatory -- we need to be disambiguous: is it "and" followed by a "(",
                // or is it the FUNCTION "and("? Not sure the before is strictly mandatory, but
                // let's roll with it.
                mightNeedSpace = true;

                // the next item should be either AND or the start of the block
                parsed = Parsed.True;
            }
            else if (CurrentTokenType != TokenType.Character || CurrentTokenText != ";")
            {
                // expected a media type
                ReportError(0, CssErrorCode.ExpectedMediaIdentifier, CurrentTokenText);
            }

            // either we have no more combinator-delimited expressions,
            // OR we have an *identifier* AND/OR (combinator followed by space)
            // OR we have a *function* AND/OR (combinator followed by the opening paren, scanned as a function)
            while ((CurrentTokenType == TokenType.Identifier
                && (string.Compare(CurrentTokenText, "AND", StringComparison.OrdinalIgnoreCase) == 0
                || string.Compare(CurrentTokenText, "OR", StringComparison.OrdinalIgnoreCase) == 0))
                || (CurrentTokenType == TokenType.Function
                && (string.Compare(CurrentTokenText, "AND(", StringComparison.OrdinalIgnoreCase) == 0
                || string.Compare(CurrentTokenText, "OR(", StringComparison.OrdinalIgnoreCase) == 0)))
            {
                // if we might need a space, output it now
                if (mightNeedSpace || Settings.OutputDeclarationWhitespace)
	                Append(' ');

                // output the AND/OR text.
                // MIGHT be AND( or OR( if it was a function, so first set a flag so we will know
                // wether or not to expect the opening paren
                if (CurrentTokenType == TokenType.Function)
                {
                    // this is not strictly allowed by the CSS3 spec!
                    // we are going to throw an error
                    ReportError(1, CssErrorCode.MediaQueryRequiresSpace, CurrentTokenText);

                    //and then fix what the developer wrote and make sure there is a space
                    // between the combinator and the (. The CSS3 spec says it is invalid to not
                    // have a space there.
                    Append(CurrentTokenText.Substring(0, CurrentTokenText.Length - 1).ToLowerInvariant());
                    Append(" (");
                    SkipSpace();

                    // included the paren
                    ParseMediaQueryExpression();
                }
                else
                {
                    // didn't include the paren -- it BETTER be the next token
                    // (after we output the AND/OR token)
                    AppendCurrent();
                    SkipSpace();
                    if (CurrentTokenType == TokenType.Character
                        && CurrentTokenText == "(")
                    {
                        // put a space between the AND and the (
                        Append(' ');

                        ParseMediaQueryExpression();
                    }
                    else
                    {
                        // error -- we expected another media query expression
                        ReportError(0, CssErrorCode.ExpectedMediaQueryExpression, CurrentTokenText);

                        // break out of the loop so we can exit
                        break;
                    }
                }
            }

            return parsed;
        }

        void ParseMediaQueryExpression()
        {
            // expect current token to be the opening paren when calling
            if (CurrentTokenType == TokenType.Character && CurrentTokenText == "(")
            {
                // output the paren and skip any space
                AppendCurrent();
                SkipSpace();
            }

            // the expression normally starts with the media feature ident, but the
            // Media Queries Level 4 range syntax may lead with a value instead,
            // e.g. (400px <= width <= 700px)
            if (CurrentTokenType == TokenType.Identifier)
            {
                // output the media feature and skip any space
                AppendCurrent();
                SkipSpace();

                // the next token should either be a colon (followed by an expression),
                // a range comparison operator, or the closing paren
                if (CurrentTokenType == TokenType.Character && CurrentTokenText == ":")
                {
                    // got an expression.
                    // output the colon and skip any whitespace
                    AppendCurrent();
                    SkipSpace();

                    // if we are expanding the output, we want a space after the colon
                    if (Settings.OutputDeclarationWhitespace)
                    {
                        Append(' ');
                    }

                    // parse the expression -- it's not optional
                    if (ParseExpr() != Parsed.True)
                    {
                        ReportError(0, CssErrorCode.ExpectedExpression, CurrentTokenText);
                    }

                    // better be the closing paren
                    if (CurrentTokenType == TokenType.Character && CurrentTokenText == ")")
                    {
                        // output the closing paren and skip any whitespace
                        AppendCurrent();
                        SkipSpace();
                    }
                    else
                    {
                        ReportError(0, CssErrorCode.ExpectedClosingParenthesis, CurrentTokenText);
                    }
                }
                else if (IsMediaQueryComparison())
                {
                    // range syntax with the media feature first, e.g. (width < 250px)
                    ParseMediaQueryRange();
                }
                else if (CurrentTokenType == TokenType.Character && CurrentTokenText == ")")
                {
                    // end of the expressions -- output the closing paren and skip any whitespace
                    AppendCurrent();
                    SkipSpace();
                }
                else
                {
                    ReportError(0, CssErrorCode.ExpectedClosingParenthesis, CurrentTokenText);
                }
            }
            else if (IsMediaQueryValue())
            {
                // range syntax with a value first, e.g. (400px <= width <= 700px).
                // parse the leading value; it will stop at the comparison operator
                if (ParseExpr() != Parsed.True)
                {
                    ReportError(0, CssErrorCode.ExpectedExpression, CurrentTokenText);
                }

                if (IsMediaQueryComparison())
                {
                    ParseMediaQueryRange();
                }
                else
                {
                    ReportError(0, CssErrorCode.ExpectedClosingParenthesis, CurrentTokenText);
                }
            }
            else
            {
                ReportError(0, CssErrorCode.ExpectedMediaFeature, CurrentTokenText);
            }
        }

        bool IsMediaQueryComparison()
        {
            return CurrentTokenType == TokenType.Character
                && (CurrentTokenText == "<" || CurrentTokenText == ">" || CurrentTokenText == "=");
        }

        bool IsMediaQueryValue()
        {
            switch (CurrentTokenType)
            {
                case TokenType.Number:
                case TokenType.Percentage:
                case TokenType.Dimension:
                case TokenType.AbsoluteLength:
                case TokenType.RelativeLength:
                case TokenType.Angle:
                case TokenType.Time:
                case TokenType.Frequency:
                case TokenType.Resolution:
                case TokenType.Fraction:
                case TokenType.Function:
                    return true;

                case TokenType.Character:
                    // a unary sign starting a numeric value
                    return CurrentTokenText == "-" || CurrentTokenText == "+";

                default:
                    return false;
            }
        }

        void ParseMediaQueryRange()
        {
            // Media Queries Level 4 range syntax: one or two comparisons,
            // e.g. (width >= 600px) or (400px <= width <= 700px)
            do
            {
                ParseMediaQueryComparison();

                // parse the value on the other side of the comparison -- it's not optional
                if (ParseExpr() != Parsed.True)
                {
                    ReportError(0, CssErrorCode.ExpectedExpression, CurrentTokenText);
                    break;
                }
            }
            while (IsMediaQueryComparison());

            // better be the closing paren
            if (CurrentTokenType == TokenType.Character && CurrentTokenText == ")")
            {
                // output the closing paren and skip any whitespace
                AppendCurrent();
                SkipSpace();
            }
            else
            {
                ReportError(0, CssErrorCode.ExpectedClosingParenthesis, CurrentTokenText);
            }
        }

        void ParseMediaQueryComparison()
        {
            // current token is the first character of the comparison operator: <, >, or =
            if (Settings.OutputDeclarationWhitespace)
            {
                Append(' ');
            }

            var isEquals = CurrentTokenText == "=";
            AppendCurrent();
            SkipSpace();

            // less-than and greater-than may be followed by an equals sign (<= or >=)
            if (!isEquals && CurrentTokenType == TokenType.Character && CurrentTokenText == "=")
            {
                AppendCurrent();
                SkipSpace();
            }

            if (Settings.OutputDeclarationWhitespace)
            {
                Append(' ');
            }
        }

        Parsed ParseDeclarationBlock(bool allowMargins)
        {
            var parsed = Parsed.True;

            // expect current token to be the opening brace when calling
            if (CurrentTokenType != TokenType.Character || CurrentTokenText != "{")
            {
                ReportError(0, CssErrorCode.ExpectedOpenBrace, CurrentTokenText);
                SkipToEndOfStatement();
                AppendCurrent();
                SkipSpace();
            }
            else
            {
                if (Settings.BlocksStartOnSameLine == BlockStart.NewLine || Settings.BlocksStartOnSameLine == BlockStart.UseSource && m_encounteredNewLine)
                {
                    NewLine();
                }
                else if (Settings.OutputDeclarationWhitespace)
                {
                    Append(' ');
                }

                Append('{');

                Indent();
                SkipSpace();

                if (CurrentTokenType == TokenType.Character && CurrentTokenText == "}")
                {
                    // shortcut nothing in the block to have the close on the same line
                    Unindent();
                    AppendCurrent();
                    SkipSpace();

                    // return parsed empty to indicate that the block is a valid, empty block
                    parsed = Parsed.Empty;
                }
                else
                {
                    var bodyParsed = ParseBlockBody(allowMargins, out var containedNestedRule);
                    if (CurrentTokenType == TokenType.Character && CurrentTokenText == "}")
                    {
                        // append the closing brace
                        Unindent();
                        NewLine();
                        Append('}');
                        // skip past it
                        SkipSpace();

                        // If the block contained nested rules that were all removed (leaving it
                        // empty even though it was not literally empty in the source), report the
                        // block as empty so the enclosing rule can be dropped too (Requirement 8.5).
                        // Gated on containedNestedRule so declaration-only blocks keep the existing
                        // (non-empty) result and stay byte-for-byte identical (Requirement 9).
                        if (bodyParsed == Parsed.Empty && containedNestedRule)
                            parsed = Parsed.Empty;
                    }
                    else if (AtEof)
                    {
                        // no closing brace, just the end of the file
                        ReportError(0, CssErrorCode.UnexpectedEndOfFile);
                    }
                    else
                    {
                        // I'm pretty sure ParseDeclarationList will only return on two situations:
                        //   1. closing brace (}), or
                        //   2. EOF.
                        // shouldn't get here, but just in case.
                        ReportError(0, CssErrorCode.ExpectedClosingBrace, CurrentTokenText);
                        Debug.Fail("UNEXPECTED CODE");
                    }
                }
            }

            return parsed;
        }

        // Classification of a single item found inside a declaration block body.
        enum BlockItemKind
        {
            // property : value  (parsed by the unchanged ParseDeclaration path)
            Declaration,
            // a nested style rule: selector (possibly & / relative) followed by { ... }
            NestedRule,
            // a nested conditional/grouping at-rule (@media / @supports / ...)
            NestedAtRule,
        }

        // Decides what the upcoming block-body item is by inspecting the current token and,
        // for the ambiguous cases, a bounded look-ahead into a buffered waypoint (see
        // PeekSignificantTokens). The look-ahead never consumes the current token, so when the
        // item turns out to be a declaration it is handed to the completely unchanged
        // ParseDeclaration path -- preserving byte-for-byte output and error fidelity for
        // non-nested input (Requirement 9).
        BlockItemKind ClassifyBlockItem()
        {
            switch (CurrentTokenType)
            {
                case TokenType.NestingSelector:
                    // '&' can only begin a nested selector; declarations never start with it.
                    return BlockItemKind.NestedRule;

                case TokenType.Hash:
                    // '#id' is a selector; no declaration begins with a hash.
                    return BlockItemKind.NestedRule;

                case TokenType.MediaSymbol:
                case TokenType.Supports:
                case TokenType.ContainerSymbol:
                case TokenType.LayerSymbol:
                case TokenType.ScopeSymbol:
                    // a nested conditional/grouping at-rule that we can already parse.
                    return BlockItemKind.NestedAtRule;

                case TokenType.Character:
                    {
                        var text = CurrentTokenText;

                        // a relative nested selector may begin with a leading combinator.
                        if (text == ">" || text == "+" || text == "~")
                            return BlockItemKind.NestedRule;

                        // '[attr]' attribute selector and ':pseudo' selector both begin nested
                        // rules; a declaration never starts with '[' or ':'.
                        if (text == "[" || text == ":")
                            return BlockItemKind.NestedRule;

                        // a leading ',' can start neither a declaration nor a valid nested selector.
                        // Route it through the nested-rule path so it fails and is rejected
                        // atomically (no partial/flattened output leaks - Requirement 2.5/5.6).
                        if (text == ",")
                            return BlockItemKind.NestedRule;

                        // '.' and '*' are ambiguous: they can begin the IE property-prefix hack
                        // (".prop: value" / "*prop: value" -- a declaration that MUST be preserved
                        // for fidelity) OR a class/universal selector nested rule (".foo { }",
                        // "* { }"). Disambiguate with bounded look-ahead.
                        if (text == "." || text == "*")
                            return ClassifyLeadingSelectorCharacter();

                        // anything else falls through to the existing declaration path.
                        return BlockItemKind.Declaration;
                    }

                case TokenType.Identifier:
                    // ambiguous: a property name ("color: red") or a nested selector that begins
                    // with a type selector ("div { }", "div.foo { }", "div:hover { }",
                    // "svg|rect { }", "div span { }"). Use bounded look-ahead so common
                    // declaration forms stay on the unchanged path while selector continuations
                    // that eventually reach a block opener are recognized as nested rules.
                    return ClassifyLeadingIdentifier();

                default:
                    return BlockItemKind.Declaration;
            }
        }

        // Disambiguates a leading '.' or '*' between the IE property-prefix hack declaration and a
        // class / universal-selector nested rule. The hack always has the shape
        // ( '.' | '*' ) identifier ':'  (a valueless "( '.' | '*' ) identifier ;|}" also stays a
        // declaration). Any other continuation after the identifier -- or a non-identifier token
        // right after '.'/'*' for the '*' universal case -- is a nested rule.
        BlockItemKind ClassifyLeadingSelectorCharacter()
        {
            var prefix = CurrentTokenText;
            var next = PeekSignificantTokens(2);

            if (next.Count >= 1 && next[0].TokenType == TokenType.Identifier)
            {
                // "( . | * ) identifier <something>"
                if (next.Count >= 2)
                {
                    // a colon ADJACENT to the identifier ("prop:") is the property-prefix hack ->
                    // declaration. A colon SEPARATED by whitespace (".foo :hover") is a descendant
                    // pseudo-class selector -> nested rule.
                    if (IsCharacterToken(next[1], ":"))
                        return WhitespaceSeparatesFirstTwoSignificant()
                            ? BlockItemKind.NestedRule
                            : BlockItemKind.Declaration;

                    // ';' or '}' right here means a valueless / malformed declaration; keep the
                    // existing declaration behavior.
                    if (IsCharacterToken(next[1], ";") || IsCharacterToken(next[1], "}"))
                        return BlockItemKind.Declaration;

                    // otherwise it continues a selector (".x.y", ".foo>", ".foo{", ...).
                    return BlockItemKind.NestedRule;
                }

                // "( . | * ) identifier <EOF>": no disambiguating token -> keep the existing
                // declaration behavior for fidelity.
                return BlockItemKind.Declaration;
            }

            // '*' followed by a non-identifier is the universal selector ("* { }", "*.foo", "*:x").
            if (prefix == "*")
                return BlockItemKind.NestedRule;

            // '.' not followed by an identifier is not a valid class selector; leave it on the
            // existing declaration path so its error handling is unchanged.
            return BlockItemKind.Declaration;
        }

        BlockItemKind ClassifyLeadingIdentifier()
        {
            var next = PeekSignificantTokens(1);
            if (next.Count == 0)
                return BlockItemKind.Declaration;

            var first = next[0];
            if (IsCharacterToken(first, "{"))
                return BlockItemKind.NestedRule;

            if (IsCharacterToken(first, ";") || IsCharacterToken(first, "}"))
                return BlockItemKind.Declaration;

            if (IsCharacterToken(first, ".")
                || IsCharacterToken(first, "#")
                || IsCharacterToken(first, "[")
                || IsCharacterToken(first, "|")
                || IsCharacterToken(first, ",")
                || IsCharacterToken(first, "+")
                || IsCharacterToken(first, ">")
                || IsCharacterToken(first, "~")
                || first.TokenType == TokenType.NestingSelector
                || first.TokenType == TokenType.Identifier)
            {
                return ContainsOpenBraceBeforeDeclarationTerminator(PeekSignificantTokens(5), 0)
                    ? BlockItemKind.NestedRule
                    : BlockItemKind.Declaration;
            }

            if (IsCharacterToken(first, ":"))
            {
                var extended = PeekSignificantTokens(4);
                if (extended.Count < 2)
                    return BlockItemKind.Declaration;

                var second = extended[1];
                if (second.TokenType == TokenType.Identifier
                    || second.TokenType == TokenType.Function
                    || second.TokenType == TokenType.Not
                    || second.TokenType == TokenType.Any
                    || second.TokenType == TokenType.Matches
                    || second.TokenType == TokenType.Has
                    || IsCharacterToken(second, ":"))
                {
                    return ContainsOpenBraceBeforeDeclarationTerminator(extended, 1)
                        ? BlockItemKind.NestedRule
                        : BlockItemKind.Declaration;
                }
            }

            return BlockItemKind.Declaration;
        }

        static bool IsCharacterToken(CssToken token, string text)
        {
            return token != null && token.TokenType == TokenType.Character && token.Text == text;
        }

        static bool ContainsOpenBraceBeforeDeclarationTerminator(IList<CssToken> tokens, int startIndex)
        {
            for (var i = startIndex; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (IsCharacterToken(token, "{"))
                    return true;

                if (IsCharacterToken(token, ";") || IsCharacterToken(token, "}"))
                    return false;
            }

            return false;
        }

        // After a PeekSignificantTokens call, returns true when a Space or Comment token appears
        // between the first and second significant tokens in the buffered look-ahead (i.e. they
        // are NOT adjacent in the source). Used to tell "prop:" (declaration) from ".foo :pseudo".
        bool WhitespaceSeparatesFirstTwoSignificant()
        {
            if (m_peekBuffer == null)
                return false;

            var significantSeen = 0;
            foreach (var entry in m_peekBuffer)
            {
                var type = entry.Token?.TokenType ?? TokenType.None;
                if (type == TokenType.Space || type == TokenType.Comment)
                {
                    // whitespace/comment AFTER the first significant token but BEFORE the second.
                    if (significantSeen >= 1)
                        return true;
                }
                else if (type != TokenType.None)
                {
                    significantSeen++;
                    if (significantSeen >= 2)
                        return false;
                }
            }

            return false;
        }

        bool WhitespaceSeparatesCurrentAndFirstPeekedSignificant()
        {
            if (m_peekBuffer == null)
                return false;

            foreach (var entry in m_peekBuffer)
            {
                var token = entry.Token;
                if (token == null || token.TokenType == TokenType.None)
                    break;

                if (token.TokenType == TokenType.Space || token.TokenType == TokenType.Comment)
                    return true;

                return false;
            }

            return false;
        }

        // Routes a recognized nested at-rule symbol to its parser (Requirement 7.3).
        void ParseNestedAtRule()
        {
            var restoreAllowNestedRulesInAtRuleBodies = m_allowNestedRulesInAtRuleBodies;
            var restoreAllowNestingScopePrelude = m_allowNestingScopePrelude;
            m_allowNestedRulesInAtRuleBodies = true;
            m_allowNestingScopePrelude = true;
            try
            {
                if (ParseMedia() == Parsed.True)
                    return;

                if (ParseSupports() == Parsed.True)
                    return;

                if (ParseContainer() == Parsed.True)
                    return;

                if (ParseLayer() == Parsed.True)
                    return;

                if (ParseScope() == Parsed.True)
                    return;
            }
            finally
            {
                m_allowNestedRulesInAtRuleBodies = restoreAllowNestedRulesInAtRuleBodies;
                m_allowNestingScopePrelude = restoreAllowNestingScopePrelude;
            }

            // no matching parser consumed the symbol -- recover so we cannot loop forever.
            ReportError(0, CssErrorCode.UnexpectedToken, CurrentTokenText);
            SkipToEndOfStatement();
            AppendCurrent();
            SkipSpace();
        }

        // Discards (without emitting) the remainder of the current block body up to its closing
        // brace or EOF, honoring nested (), [], and {} pairs. Used to fail a malformed nested
        // construct atomically so no partial or flattened output leaks into the enclosing rule
        // (Requirement 2.5 / 5.5 / 5.6).
        void DiscardToEndOfBlockBody()
        {
            // buffer skipped text into a throwaway waypoint so any appends (e.g. from SkipToClose)
            // are thrown away rather than emitted.
            PushWaypoint();

            var closingStack = new Stack<string>();
            while (!AtEof)
            {
                if (CurrentTokenType == TokenType.Character)
                {
                    if (CurrentTokenText == "}" && closingStack.Count == 0)
                        break;

                    if (CurrentTokenText == "(")
                        closingStack.Push(")");
                    else if (CurrentTokenText == "[")
                        closingStack.Push("]");
                    else if (CurrentTokenText == "{")
                        closingStack.Push("}");
                    else if (closingStack.Count > 0 && CurrentTokenText == closingStack.Peek())
                        closingStack.Pop();
                }

                NextToken();
            }

            DiscardWaypoint();
        }

        // After a nested-rule parse fails, try to recover locally by skipping only that malformed
        // rule instead of discarding the entire parent block. This mirrors browser recovery for
        // invalid inner rules that still have a bounded "{...}" body: we consume through the end
        // of that body and leave the next sibling item available to parse normally.
        bool RecoverMalformedNestedRule()
        {
            // Classification / selector parsing may have prefetched part of the malformed
            // selector into the look-ahead buffer. We are about to discard the entire bad rule,
            // so drop any buffered selector fragments and continue from the scanner's raw state.
            m_peekBuffer = null;

            var braceDepth = 0;
            var sawOpenBrace = false;

            while (!AtEof)
            {
                if (CurrentTokenType == TokenType.Character)
                {
                    if (CurrentTokenText == "{")
                    {
                        sawOpenBrace = true;
                        ++braceDepth;
                    }
                    else if (CurrentTokenText == "}")
                    {
                        if (!sawOpenBrace)
                        {
                            return false;
                        }

                        --braceDepth;
                        NextToken();

                        if (braceDepth <= 0)
                        {
                            SkipIfSpace();
                            return true;
                        }

                        continue;
                    }
                    else if (!sawOpenBrace && CurrentTokenText == ";")
                    {
                        NextToken();
                        SkipIfSpace();
                        return true;
                    }
                }

                NextToken();
            }

            return sawOpenBrace;
        }

        // Removes a single trailing ';' from the current output waypoint, if present. Used after a
        // trailing nested rule is dropped as empty so the semicolon that separated it from the
        // previous declaration does not linger as a redundant terminator. At the call site (the
        // block-body loop, before the closing brace is emitted) the builder ends in the ';' itself
        // in both minified and pretty output, so no surrounding whitespace needs to be considered.
        void TrimTrailingSemicolon()
        {
            var top = m_builders.Peek();
            if (top.Length > 0 && top[top.Length - 1] == ';')
                top.Length--;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        Parsed ParseBlockBody(bool allowMargins)
        {
            return ParseBlockBody(allowMargins, out _);
        }

        // containedNestedRule is set true when the body routed at least one item to
        // ParseNestedRule. The caller (ParseDeclarationBlock) uses it to decide whether an
        // empty result may drop the enclosing rule: only blocks that actually contained nested
        // rules can newly become empty through nested-rule removal. Declaration-only blocks keep
        // their historical return value so non-nested output stays byte-for-byte identical (Req 9).
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        Parsed ParseBlockBody(bool allowMargins, out bool containedNestedRule)
        {
            containedNestedRule = false;
            var parsed = Parsed.Empty;
            while (!AtEof)
            {
                // check the line length before each new declaration -- if we're past the threshold, start a new line
                if (lineLength >= Settings.LineBreakThreshold)
	                AddNewLine();

                // classify the upcoming item. Nested rules and nested at-rules are streamed as
                // they are recognized (preserving source order, Requirement 2.2/2.3), then the
                // loop continues with the next item. Declarations take the unchanged path below.
                var itemKind = ClassifyBlockItem();
                if (itemKind == BlockItemKind.NestedRule)
                {
                    containedNestedRule = true;
                    Parsed parsedRule = ParseNestedRule();
                    if (parsedRule == Parsed.False)
                    {
                        // When the malformed nested selector still has its own block, recover by
                        // skipping just that inner rule so following siblings can survive. If we
                        // cannot find a bounded malformed rule, fall back to rejecting the rest of
                        // this block body so no partial/flattened output leaks (Req 2.5/5.5).
                        if (!RecoverMalformedNestedRule())
                        {
                            DiscardToEndOfBlockBody();
                            break;
                        }

                        if (AtEof)
                            break;
                        if (CurrentTokenType == TokenType.Character && CurrentTokenText == "}")
                            break;

                        continue;
                    }

                    if (parsed == Parsed.Empty && parsedRule != Parsed.Empty)
                        parsed = parsedRule;

                    // a nested rule ends after its own closing brace (or at EOF); no ';' terminator
                    // is expected and no separator is inserted before the next item (Requirement 8.6).
                    if (AtEof)
                        break;
                    if (CurrentTokenType == TokenType.Character && CurrentTokenText == "}")
                    {
                        // this nested rule was the last item in the block. If it was dropped as
                        // empty, a ';' emitted after the preceding declaration is now a redundant
                        // trailing semicolon (e.g. ".a{color:red;.b{}}" -> ".a{color:red}"); trim
                        // it unless terminating semicolons are being forced.
                        if (parsedRule == Parsed.Empty && !Settings.TermSemicolons)
                            TrimTrailingSemicolon();
                        break;
                    }

                    continue;
                }

                if (itemKind == BlockItemKind.NestedAtRule)
                {
                    ParseNestedAtRule();
                    if (parsed == Parsed.Empty)
                        parsed = Parsed.True;

                    if (AtEof)
                        break;
                    if (CurrentTokenType == TokenType.Character && CurrentTokenText == "}")
                        break;

                    continue;
                }

                Parsed parsedDecl = ParseDeclaration();
                if (parsed == Parsed.Empty && parsedDecl != Parsed.Empty)
	                parsed = parsedDecl;

                // if we are allowed to have margin at-keywords in this block, and
                // we didn't find a declaration, check to see if it's a margin
                var parsedMargin = false;
                if (allowMargins && parsedDecl == Parsed.Empty)
                {
                    parsedMargin = ParseMargin() == Parsed.True;
                }

                // if we parsed a margin, we DON'T expect there to be a semi-colon.
                // if we didn't parse a margin, then there better be either a semicolon or a closing brace.
                if (!parsedMargin)
                {
                    if ((CurrentTokenType != TokenType.Character
                        || (CurrentTokenText != ";" && CurrentTokenText != "}"))
                        && !AtEof)
                    {
                        ReportError(0, CssErrorCode.ExpectedSemicolonOrClosingBrace, CurrentTokenText);

                        // we'll get here if we decide to ignore the error and keep trudging along. But we still
                        // need to skip to the end of the declaration.
                        SkipToEndOfDeclaration();
                    }
                }

                // if we're at the end, close it out
                if (AtEof)
                {
                    // if we want to force a terminating semicolon, add it now
                    if (Settings.TermSemicolons)
                    {
                        Append(';');
                    }
                }
                else if (CurrentTokenText == "}")
                {
                    // if we want terminating semicolons but the source
                    // didn't have one (evidenced by a non-empty declaration)...
                    if (Settings.TermSemicolons && parsedDecl == Parsed.True)
                    {
                        // ...then add one now.
                        Append(';');
                    }

                    break;
                }
                else if (CurrentTokenText == ";")
                {
                    // token is a semi-colon
                    // if we always want to add the semicolons, add it now
                    if (Settings.TermSemicolons)
                    {
                        Append(';');
                        SkipSpace();
                    }
                    else
                    {
                        // we have a semicolon, but we don't know if we can
                        // crunch it out or not. If the NEXT token is a closing brace, then
                        // we can crunch out the semicolon.
                        // PROBLEM: if there's a significant comment AFTER the semicolon, then the 
                        // comment gets output before we output the semicolon, which could
                        // reverse the intended code.

                        // skip any whitespace to see if we need to add a semicolon
                        // to the end, or if we can crunch it out, but use a special function
                        // that doesn't send any comments to the stream yet -- it batches them
                        // up and returns them (if any)
                        string comments = NextSignificantToken();

                        if (AtEof)
                        {
                            // if we have an EOF after the semicolon and no comments, then we don't want
                            // to output anything else.
                            if (comments.Length > 0)
                            {
                                // but if we have comments after the semicolon....
                                // if there's a non-empty comment, it might be a significant hack, so add the semi-colon just in case.
                                if (comments != "/* */" && comments != "/**/")
                                {
                                    Append(';');
                                }

                                // and comments always end on a new line
                                Append(comments);
                                lastOutputWasNewLine = true;
                                lineLength = 0;
                            }
                            break;
                        }
                        else if (CurrentTokenType != TokenType.Character
                            || (CurrentTokenText != "}" && CurrentTokenText != ";")
                            || (comments.Length > 0 && comments != "/* */" && comments != "/**/"))
                        {
                            // if the significant token after the 
                            // semicolon is not a cosing brace, then we'll add the semicolon.
                            // if there are two semi-colons in a row, don't add it because we'll double it.
                            // if there's a non-empty comment, it might be a significant hack, so add the semi-colon just in case.
                            Append(';');
                        }

                        // now that we've possibly added our semi-colon, we're safe
                        // to add any comments we may have found before the current token
                        if (comments.Length > 0)
                        {
                            Append(comments);

                            // and comments always end on a new line
                            lastOutputWasNewLine = true;
                            lineLength = 0;
                        }
                    }
                }
            }

            return parsed;
        }

        Parsed ParsePage()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.PageSymbol)
            {
                var keepDirective = true;
                PushWaypoint();

                NewLine();
                AppendCurrent();
                SkipSpace();

                if (CurrentTokenType == TokenType.Identifier)
                {
                    Append(' ');
                    AppendCurrent();
                    NextToken();
                }
                // optional
                ParsePseudoPage();

                if (CurrentTokenType == TokenType.Space)
                {
                    SkipSpace();
                }

                if (CurrentTokenType == TokenType.Character && CurrentTokenText == "{")
                {
                    // allow margin at-keywords
                    parsed = ParseDeclarationBlock(true);
                    if (parsed == Parsed.Empty)
                    {
                        keepDirective = false;
                        parsed = Parsed.True;
                    }

                    NewLine();
                }
                else
                {
                    SkipToEndOfStatement();
                    AppendCurrent();
                    SkipSpace();
                }

                PopWaypoint(keepDirective);
            }
            return parsed;
        }

        Parsed ParsePseudoPage()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.Character && CurrentTokenText == ":")
            {
                Append(':');
                NextToken();

                if (CurrentTokenType != TokenType.Identifier)
                {
                    ReportError(0, CssErrorCode.ExpectedIdentifier, CurrentTokenText);
                }

                AppendCurrent();
                NextToken();
                parsed = Parsed.True;
            }
            return parsed;
        }

        Parsed ParseMargin()
        {
            var parsed = Parsed.Empty;
            var keepDirective = true;
            switch (CurrentTokenType)
            {
                case TokenType.TopLeftCornerSymbol:
                case TokenType.TopLeftSymbol:
                case TokenType.TopCenterSymbol:
                case TokenType.TopRightSymbol:
                case TokenType.TopRightCornerSymbol:
                case TokenType.BottomLeftCornerSymbol:
                case TokenType.BottomLeftSymbol:
                case TokenType.BottomCenterSymbol:
                case TokenType.BottomRightSymbol:
                case TokenType.BottomRightCornerSymbol:
                case TokenType.LeftTopSymbol:
                case TokenType.LeftMiddleSymbol:
                case TokenType.LeftBottomSymbol:
                case TokenType.RightTopSymbol:
                case TokenType.RightMiddleSymbol:
                case TokenType.RightBottomSymbol:
                    // these are the margin at-keywords
                    PushWaypoint();
                    NewLine();
                    AppendCurrent();
                    SkipSpace();

                    // don't allow margin at-keywords
                    parsed = ParseDeclarationBlock(false);
                    if (parsed == Parsed.Empty)
                    {
                        keepDirective = false;
                        parsed = Parsed.True;
                    }

                    NewLine();
                    PopWaypoint(keepDirective);
                    break;

                default:
                    // we're not interested
                    break;
            }
            return parsed;
        }

        Parsed ParseFontFace()
        {
            var parsed = Parsed.False;
            if (CurrentTokenType == TokenType.FontFaceSymbol)
            {
                var keepDirective = true;
                PushWaypoint();

                NewLine();
                AppendCurrent();
                SkipSpace();

                // don't allow margin at-keywords
                parsed = ParseDeclarationBlock(false);
                if (parsed == Parsed.Empty)
                {
                    keepDirective = false;
                    parsed = Parsed.True;
                }

                NewLine();
                PopWaypoint(keepDirective);
            }
            return parsed;
        }

        Parsed ParseOperator()
        {
            Parsed parsed = Parsed.Empty;
            if (CurrentTokenType == TokenType.Character
              && (CurrentTokenText == "/" || CurrentTokenText == ","))
            {
                AppendCurrent();
                SkipSpace();
                parsed = Parsed.True;
            }
            return parsed;
        }

        Parsed ParseCombinator()
        {
            Parsed parsed = Parsed.Empty;
            if (CurrentTokenType == TokenType.Character
              && (CurrentTokenText == "+" || CurrentTokenText == ">" || CurrentTokenText == "~"))
            {
                AppendCurrent();
                SkipSpace();
                parsed = Parsed.True;
            }
            return parsed;
        }

        Parsed ParseRule()
        {
            var keepRule = true;
            PushWaypoint();

            // check the line length before each new declaration -- if we're past the threshold, start a new line
            if (lineLength >= Settings.LineBreakThreshold)
	            AddNewLine();

            m_forceNewLine = true;
            Parsed parsed = ParseSelector();
            if (parsed == Parsed.True)
            {
                if (AtEof)
                {
                    // we parsed a selector expecting this to be a rule, but then WHAM! we hit
                    // the end of the file. That isn't correct. Throw an error.
                    ReportError(0, CssErrorCode.UnexpectedEndOfFile);
                }

                while (!AtEof)
                {
                    if (CurrentTokenType != TokenType.Character
                        || (CurrentTokenText != "," && CurrentTokenText != "{"))
                    {
                        ReportError(0, CssErrorCode.ExpectedCommaOrOpenBrace, CurrentTokenText);
                        SkipToEndOfStatement();
                        AppendCurrent();
                        SkipSpace();
                        break;
                    }

                    if (CurrentTokenText == "{")
                    {
                        // REVIEW: IE6 has an issue where the "first-letter" and "first-line" 
                        // pseudo-classes need to be separated from the opening curly-brace 
                        // of the following rule set by a space or it doesn't get picked up. 
                        // So if the last-outputted word was "first-letter" or "first-line",
                        // add a space now (since we know the next character at this point 
                        // is the opening brace of a rule-set).
                        // Maybe some day this should be removed or put behind an "IE6-compat" switch.
                        if (m_lastOutputString == "first-letter" || m_lastOutputString == "first-line")
                        {
                            Append(' ');
                        }

                        // don't allow margin at-keywords
                        parsed = ParseDeclarationBlock(false);
                        if (parsed == Parsed.Empty)
                        {
                            keepRule = false;
                            parsed = Parsed.True;
                        }

                        break;
                    }

                    Append(',');

                    // check the line length before each new declaration -- if we're past the threshold, start a new line
                    if (lineLength >= Settings.LineBreakThreshold)
	                    AddNewLine();
                    else if (Settings.OutputDeclarationWhitespace)
	                    Append(' ');

                    SkipSpace();

                    if (ParseSelector() != Parsed.True)
                    {
                        if (CurrentTokenType == TokenType.Character && CurrentTokenText == "{")
                        {
                            // the author ended the last selector with a comma, but didn't include
                            // the next selector before starting the declaration block. Or maybe it's there,
                            // but commented out. Still okay, but flag a style warning.
                            ReportError(4, CssErrorCode.ExpectedSelector, CurrentTokenText);
                            continue;
                        }
                        else
                        {
                            // not something we know about -- skip the whole statement
                            ReportError(0, CssErrorCode.ExpectedSelector, CurrentTokenText);
                            SkipToEndOfStatement();
                        }
                        AppendCurrent();
                        SkipSpace();
                        break;
                    }
                }
            }

            PopWaypoint(keepRule);
            return parsed;
        }

        Parsed ParseSelectorList()
        {
            var parsed = ParseSelector();
            while (CurrentTokenType == TokenType.Character && CurrentTokenText == ",")
            {
                // append the comma to the output and skip it any any other
                // space following it
                AppendCurrent();
                SkipSpace();

                // parse another selector. Don't save the parsed result because this
                // function should return true now, since we parsed at least one selected
                // successfully
                if (ParseSelector() != Parsed.True)
                {
                    // but if we fail for any reason, then break out of the loop after reporting
                    // an error
                    ReportError(0, CssErrorCode.ExpectedSelector, CurrentTokenText);
                    break;
                }
            }

            return parsed;
        }

        Parsed ParseSelector()
        {
            // should start with a selector
            Parsed parsed = ParseSimpleSelector();
            if (parsed == Parsed.False && CurrentTokenType != TokenType.None)
            {
                // no selector? See if it starts with a combinator.
                // common IE-7 hack to start with a combinator, because that browser will assume a beginning *
                var currentContext = m_currentToken.Context;
                var possibleCombinator = CurrentTokenText;
                parsed = ParseCombinator();
                if (parsed == Parsed.True)
                {
                    ReportError(4, CssErrorCode.HackGeneratesInvalidCss, currentContext, possibleCombinator);
                }
            }

            if (parsed == Parsed.True)
            {
                // save whether or not we are skipping anything by checking the type before we skip
                bool spaceWasSkipped = SkipIfSpace();

                while (!AtEof)
                {
                    Parsed parsedCombinator = ParseCombinator();
                    if (parsedCombinator != Parsed.True)
                    {
                        // we know the selector ends with a comma or an open brace,
                        // so if the next token is one of those, we're done.
                        // otherwise we're going to slap a space in the stream (if we found one)
                        // and look for the next selector
                        if (CurrentTokenType == TokenType.Character
                          && (CurrentTokenText == "," || CurrentTokenText == "{" || CurrentTokenText == ")"))
                        {
                            break;
                        }
                        else if (spaceWasSkipped)
                        {
                            Append(' ');
                        }
                    }

                    if (ParseSimpleSelector() == Parsed.False)
                    {
                        ReportError(0, CssErrorCode.ExpectedSelector, CurrentTokenText);
                        break;
                    }
                    else
                    {
                        // save the "we skipped whitespace" flag before skipping the whitespace
                        spaceWasSkipped = SkipIfSpace();
                    }
                }
            }
            return parsed;
        }

        // Parses a style rule nested inside a declaration block. Structurally parallels
        // ParseRule, but parses a nested selector list (which understands the & nesting
        // selector and relative selectors) and, on the opening brace, recurses into the
        // existing ParseDeclarationBlock so nesting works to arbitrary depth.
        //
        // A waypoint is pushed so that, when RemoveEmptyBlocks is enabled, a nested rule
        // whose block ends up empty is discarded. Emission uses the same NewLine/Indent/
        // Append helpers as ParseRule, so pretty-mode output indents each nested rule one
        // level deeper than its parent's declarations.
        Parsed ParseNestedRule()
        {
            var keepRule = true;
            PushWaypoint();

            // check the line length before each new rule -- if we're past the threshold, start a new line
            if (lineLength >= Settings.LineBreakThreshold)
                AddNewLine();

            m_forceNewLine = true;

            // parse the nested selector list (handles &, relative selectors, and comma-separated lists).
            Parsed parsed = ParseNestedSelectorList();
            if (parsed == Parsed.True)
            {
                if (AtEof)
                {
                    // we parsed a selector expecting this to be a nested rule, but then hit
                    // the end of the file before the declaration block. That isn't correct.
                    ReportError(0, CssErrorCode.UnexpectedEndOfFile);
                }
                else
                {
                    // on the opening brace, recurse into the existing declaration block parser.
                    // Because the block body loop routes nested rules back here, this recursion
                    // gives arbitrary-depth nesting for free. If the brace is missing,
                    // ParseDeclarationBlock reports the error and recovers.
                    parsed = ParseDeclarationBlock(false);
                    if (parsed == Parsed.Empty)
                    {
                        // the nested rule's block is empty (either literally, or because every
                        // item inside it was removed). Drop the rule -- PopWaypoint discards its
                        // buffered text when RemoveEmptyBlocks is enabled (Requirement 8.4). We
                        // deliberately keep the Parsed.Empty result (rather than promoting it to
                        // Parsed.True) so an enclosing block that ends up containing only dropped
                        // nested rules can detect its own emptiness and be dropped too (8.5).
                        keepRule = false;
                    }
                }
            }

            PopWaypoint(keepRule);
            return parsed;
        }

        // Parses a comma-separated list of nested selectors that share a single declaration
        // block. Optional whitespace is allowed around the commas. Emits the selectors
        // separated by a single comma using the same formatting non-nested selector lists use
        // (no surrounding whitespace when minifying; a trailing space in pretty output).
        //
        // The list fails atomically: if any selector is invalid, or a selector position is
        // empty (a leading, trailing, or doubled comma), the entire buffered list output is
        // discarded so that none of the selectors are emitted, ExpectedSelector is reported,
        // and Parsed.False is returned.
        Parsed ParseNestedSelectorList()
        {
            // buffer the whole list in its own waypoint so we can discard it wholesale
            // (regardless of RemoveEmptyBlocks) if any part of the list turns out invalid.
            PushWaypoint();

            // the first selector is required -- a leading comma leaves an empty position.
            if (ParseNestedSelector() != Parsed.True)
            {
                ReportError(0, CssErrorCode.ExpectedSelector, CurrentTokenText);
                DiscardWaypoint();
                return Parsed.False;
            }

            // ParseNestedSelector leaves trailing whitespace already skipped, but be safe.
            SkipIfSpace();

            while (CurrentTokenType == TokenType.Character && CurrentTokenText == ",")
            {
                // emit the comma using the existing non-nested selector-list formatting.
                Append(',');

                // check the line length -- if we're past the threshold, start a new line;
                // otherwise add a single space when producing pretty output.
                if (lineLength >= Settings.LineBreakThreshold)
                    AddNewLine();
                else if (Settings.OutputDeclarationWhitespace)
                    Append(' ');

                // step past the comma and any whitespace preceding the next selector.
                SkipSpace();

                // a trailing or doubled comma leaves an empty selector position -- invalid.
                if (ParseNestedSelector() != Parsed.True)
                {
                    ReportError(0, CssErrorCode.ExpectedSelector, CurrentTokenText);
                    DiscardWaypoint();
                    return Parsed.False;
                }

                // skip any whitespace before the next comma or the opening brace.
                SkipIfSpace();
            }

            // the whole list parsed successfully -- keep the buffered selectors.
            PopWaypoint(true);
            return Parsed.True;
        }

        // Parses a single nested selector (a complex selector as it appears inside a
        // declaration block). Handles the nesting selector (&) in every valid position
        // (standalone, joined, doubled, repeated, and after another selector) as well as
        // relative selectors that begin with a combinator or a bare compound selector
        // (an implied leading & is NOT emitted). Does NOT skip whitespace after the selector
        // and does NOT parse a following comma-separated list (see ParseNestedSelectorList).
        Parsed ParseNestedSelector()
        {
            ++m_nestedSelectorDepth;
            try
            {
            Parsed parsed;

            // a relative nested selector may begin with a leading combinator (>, +, ~).
            // The leading & is implied and is NOT inserted into the output -- we emit
            // exactly the combinator that was written.
                Parsed leadingCombinator = ParseCombinator();
                if (leadingCombinator == Parsed.True)
                {
                    // the leading combinator MUST be followed by a valid compound selector
                    if (ParseNestedCompoundSelector() != Parsed.True)
                    {
                        ReportError(0, CssErrorCode.ExpectedSelector, CurrentTokenText);
                        return Parsed.False;
                    }

                    parsed = Parsed.True;
                }
                else
                {
                    // otherwise it starts with a compound selector, which may lead with & or
                    // be a bare compound selector (relative selector with an implied &).
                    parsed = ParseNestedCompoundSelector();
                    if (parsed != Parsed.True)
                    {
                        return parsed;
                    }
                }

                // continue the complex selector: (combinator compound-selector)*
                // this mirrors ParseSelector so that whitespace/combinator handling is identical.
                bool spaceWasSkipped = SkipIfSpace();
                while (!AtEof)
                {
                    Parsed parsedCombinator = ParseCombinator();
                    if (parsedCombinator != Parsed.True)
                    {
                        // the selector ends at a comma, an open brace, or a close paren.
                        if (CurrentTokenType == TokenType.Character
                          && (CurrentTokenText == "," || CurrentTokenText == "{" || CurrentTokenText == ")"))
                        {
                            break;
                        }
                        else if (spaceWasSkipped)
                        {
                            // descendant combinator -- retain a single space
                            Append(' ');
                        }
                    }

                    if (ParseNestedCompoundSelector() == Parsed.False)
                    {
                        ReportError(0, CssErrorCode.ExpectedSelector, CurrentTokenText);
                        break;
                    }
                    else
                    {
                        spaceWasSkipped = SkipIfSpace();
                    }
                }

                return parsed;
            }
            finally
            {
                --m_nestedSelectorDepth;
            }
        }

        // Parses a compound selector that may contain nesting selectors (&) interspersed
        // with the usual simple-selector parts, e.g. &, &.bar, .bar&, &&, &:hover.
        // Does NOT skip whitespace after the selector. Returns Parsed.True if at least one
        // selector part (a & or a simple selector) was consumed.
        Parsed ParseNestedCompoundSelector()
        {
            Parsed parsed = Parsed.False;
            var sawNonNestingSelector = false;
            while (!AtEof)
            {
                if (CurrentTokenType == TokenType.NestingSelector)
                {
                    var next = PeekSignificantTokens(1);
                    if (!sawNonNestingSelector
                        && next.Count >= 1
                        && !WhitespaceSeparatesCurrentAndFirstPeekedSignificant()
                        && (next[0].TokenType == TokenType.Identifier
                            || IsCharacterToken(next[0], "*")
                            || IsCharacterToken(next[0], "|")))
                    {
                        ReportError(0, CssErrorCode.ExpectedSelector, CurrentTokenText);
                        return Parsed.False;
                    }

                    // emit the & verbatim with zero added whitespace
                    AppendCurrent();
                    NextToken();
                    parsed = Parsed.True;
                }
                else if (ParseSimpleSelector() == Parsed.True)
                {
                    parsed = Parsed.True;
                    sawNonNestingSelector = true;
                }
                else
                {
                    break;
                }
            }

            return parsed;
        }

        // does NOT skip whitespace after the selector
        Parsed ParseSimpleSelector()
        {
            // the element name is optional
            Parsed parsed = ParseElementName();
            while (!AtEof)
            {
                if (CurrentTokenType == TokenType.NestingSelector)
                {
                    // Outside nested-rule-specific parsing, the nesting selector '&' is still
                    // valid in selector contexts and behaves like :scope per the nesting spec.
                    // Preserve the source spelling verbatim rather than rewriting it.
                    AppendCurrent();
                    NextToken();
                    parsed = Parsed.True;
                }
                else if (CurrentTokenType == TokenType.Hash)
                {
                    AppendCurrent();
                    NextToken();
                    parsed = Parsed.True;
                }
                else if (ParseClass() == Parsed.True)
                {
                    parsed = Parsed.True;
                }
                else if (ParseAttrib() == Parsed.True)
                {
                    parsed = Parsed.True;
                }
                else if (ParsePseudo() == Parsed.True)
                {
                    parsed = Parsed.True;
                }
                else
                {
                    break;
                }
            }
            return parsed;
        }

        Parsed ParseClass()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.Character
              && CurrentTokenText == ".")
            {
                AppendCurrent();
                NextToken();

                if (CurrentTokenType == TokenType.Identifier)
                {
                    AppendCurrent();
                    NextToken();
                    parsed = Parsed.True;
                }
                else if (CurrentTokenType == TokenType.Character && CurrentTokenText == "%")
                {
                    UpdateIfReplacementToken();
                    if (CurrentTokenType == TokenType.ReplacementToken)
                    {
                        AppendCurrent();
                        NextToken();
                        parsed = Parsed.True;
                    }
                    else
                    {
                        ReportError(0, CssErrorCode.ExpectedIdentifier, CurrentTokenText);
                    }
                }
                else
                {
                    ReportError(0, CssErrorCode.ExpectedIdentifier, CurrentTokenText);
                }
            }
            else if (CurrentTokenType == TokenType.Dimension || CurrentTokenType == TokenType.Number)
            {
                string rawNumber = m_scanner.RawNumber;
                if (rawNumber != null && rawNumber.StartsWith(".", StringComparison.Ordinal))
                {
                    // if we are expecting a class but we got dimension or number that starts with a period,
                    // then what we REALLY have is a class name that starts with a digit. If it's all digits,
                    // it will be a number, and it it's just an identifier that starts with a digit, it will
                    // be a dimension.
                    // The problem here is that both of those those token type format the number, eg: 
                    // .000foo would get shrunk to 0foo.
                    // Be sure to use the RawNumber property on the scanner to get the raw text exactly as
                    // it was from the input
                    parsed = Parsed.True;

                    // but check the next token to see if it's an identifier.
                    // if the next token is an identifier with no whitespace between it and the previous
                    // "number," then it's part of this identifier
                    NextToken();
                    if (CurrentTokenType == TokenType.Identifier)
                    {
                        // add that identifier to the raw number
                        rawNumber += CurrentTokenText;
                        NextToken();
                    }

                    // report a low-sev warning before outputting the raw number text and advancing
                    ReportError(2, CssErrorCode.PossibleInvalidClassName, rawNumber);
                    Append(rawNumber);
                }
            }
            return parsed;
        }

        Parsed ParseElementName()
        {
            Parsed parsed = Parsed.False;
            bool foundNamespace = false;

            // if the next character is a pipe, then we have an empty namespace prefix
            if (CurrentTokenType == TokenType.Character && CurrentTokenText == "|")
            {
                foundNamespace = true;
                AppendCurrent();
                NextToken();
            }

            if (CurrentTokenType == TokenType.Identifier
                || (CurrentTokenType == TokenType.Character && CurrentTokenText == "*"))
            {
                // if we already found a namespace, then there was none specified and the
                // element name started with |. Otherwise, save the current ident as a possible
                // namespace identifier
                string identifier = foundNamespace ? null : CurrentTokenText;

                AppendCurrent();
                NextToken();
                parsed = Parsed.True;

                // if the next character is a pipe, then that previous identifier or asterisk
                // was the namespace prefix
                if (!foundNamespace
                    && CurrentTokenType == TokenType.Character && CurrentTokenText == "|")
                {
                    // throw an error if identifier wasn't prevously defined by @namespace statement
                    ValidateNamespace(identifier);

                    // output the pipe and move to the true element name
                    AppendCurrent();
                    NextToken();

                    // a namespace and the bar character should ALWAYS be followed by
                    // either an identifier or an asterisk
                    if (CurrentTokenType == TokenType.Identifier
                        || (CurrentTokenType == TokenType.Character && CurrentTokenText == "*"))
                    {
                        AppendCurrent();
                        NextToken();
                    }
                    else
                    {
                        // we have an error condition
                        parsed = Parsed.False;
                        // handle the error
                        ReportError(0, CssErrorCode.ExpectedIdentifier, CurrentTokenText);
                    }
                }
            }
            else if (foundNamespace)
            {
                // we had found an empty namespace, but no element or universal following it!
                // handle the error
                ReportError(0, CssErrorCode.ExpectedIdentifier, CurrentTokenText);
            }

            return parsed;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        Parsed ParseAttrib()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.Character
              && CurrentTokenText == "[")
            {
                Append('[');
                SkipSpace();

                bool foundNamespace = false;
                
                // must be either an identifier, an asterisk, or a namespace separator
                if (CurrentTokenType == TokenType.Character && CurrentTokenText == "|")
                {
                    // has an empty namespace
                    foundNamespace = true;
                    AppendCurrent();
                    NextToken();
                }

                if (CurrentTokenType == TokenType.Identifier
                    || (CurrentTokenType == TokenType.Character && CurrentTokenText == "*"))
                {
                    // if we already found a namespace, then there was none specified and the
                    // element name started with |. Otherwise, save the current ident as a possible
                    // namespace identifier
                    string identifier = foundNamespace ? null : CurrentTokenText;

                    AppendCurrent();
                    SkipSpace();

                    // check to see if that identifier is actually a namespace because the current
                    // token is a namespace separator
                    if (!foundNamespace 
                        && CurrentTokenType == TokenType.Character && CurrentTokenText == "|")
                    {
                        // namespaced attribute
                        // throw an error if the namespace hasn't previously been defined by a @namespace statement
                        ValidateNamespace(identifier);

                        // output the pipe and move to the next token,
                        // which should be the attribute name
                        AppendCurrent();
                        SkipSpace();

                        // must be either an identifier or an asterisk
                        if (CurrentTokenType == TokenType.Identifier
                            || (CurrentTokenType == TokenType.Character && CurrentTokenText == "*"))
                        {
                            // output the namespaced attribute name
                            AppendCurrent();
                            SkipSpace();
                        }
                        else
                        {
                            ReportError(0, CssErrorCode.ExpectedIdentifier, CurrentTokenText);
                        }
                    }
                }
                else
                {
                    // neither an identifier nor an asterisk
                    ReportError(0, CssErrorCode.ExpectedIdentifier, CurrentTokenText);
                }

                // check to see if there's an (optional) attribute operator
                if ((CurrentTokenType == TokenType.Character && CurrentTokenText == "=")
                  || (CurrentTokenType == TokenType.Includes)
                  || (CurrentTokenType == TokenType.DashMatch)
                  || (CurrentTokenType == TokenType.PrefixMatch)
                  || (CurrentTokenType == TokenType.SuffixMatch)
                  || (CurrentTokenType == TokenType.SubstringMatch))
                {
                    AppendCurrent();
                    SkipSpace();

                    if (CurrentTokenType == TokenType.Character
                        && CurrentTokenText == "%")
                    {
                        UpdateIfReplacementToken();
                        if (CurrentTokenType != TokenType.ReplacementToken)
                        {
                            ReportError(0, CssErrorCode.ExpectedIdentifierOrString, CurrentTokenText);
                        }
                    }
                    else if (CurrentTokenType != TokenType.Identifier
                      && CurrentTokenType != TokenType.String)
                    {
                        ReportError(0, CssErrorCode.ExpectedIdentifierOrString, CurrentTokenText);
                    }

                    AppendCurrent();
                    SkipSpace();
                }

                if (CurrentTokenType == TokenType.Identifier)
                {
	                var lowerText = CurrentTokenText.ToLowerInvariant();
	                if (lowerText == "i" || lowerText == "s")
	                {
		                Append(lowerText);
		                NextToken();
	                }
                }

                if (CurrentTokenType != TokenType.Character
                  || CurrentTokenText != "]")
                {
                    ReportError(0, CssErrorCode.ExpectedClosingBracket, CurrentTokenText);
                }

                // we're done!
                Append(']');
                NextToken();
                parsed = Parsed.True;
            }
            return parsed;
        }

        Parsed ParsePseudo()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.Character
              && CurrentTokenText == ":")
            {
                Append(':');
                NextToken();

                // CSS3 has pseudo-ELEMENTS that are specified with a double-colon.
                // IF we find a double-colon, we will treat it exactly the same as if it were a pseudo-CLASS.
                if (CurrentTokenType == TokenType.Character && CurrentTokenText == ":")
                {
                    Append(':');
                    NextToken();
                }

                switch (CurrentTokenType)
                {
                    case TokenType.Identifier:
                        AppendCurrent();
                        NextToken();
                        break;

                    case TokenType.Not:
                    case TokenType.Any:
                    case TokenType.Matches:
                    case TokenType.Is:
                    case TokenType.Where:
                    case TokenType.Has:
                        AppendCurrent();
                        SkipSpace();

                        // the argument of an ANY/MATCHES pseudo function is a selector list,
                        // and Selectors 4 standards say NOT is also a selector list. Selectors 3
                        // says NOT is only a simple selector, but let's go with the later standard
                        parsed = m_nestedSelectorDepth > 0
                            ? ParseNestedSelectorList()
                            : ParseSelectorList();
                        if (parsed != Parsed.True)
                        {
                            // TODO: error? shouldn't we ALWAYS have a selector list inside a not()/matches()/any() function?
                        }

                        // skip any whitespace if we have it
                        SkipIfSpace();

                        // don't forget the closing paren
                        if (CurrentTokenType != TokenType.Character
                          || CurrentTokenText != ")")
                        {
                            ReportError(0, CssErrorCode.ExpectedIdentifier, CurrentTokenText);
                        }
                        AppendCurrent();
                        NextToken();
                        break;

                    case TokenType.Function:
                        AppendCurrent();
                        SkipSpace();

                        // parse the function argument expression
                        ParseExpression();

                        // IE extends CSS3 grammar to provide for multiple arguments to pseudo-class
                        // functions. So as long as the current token is a comma, keep on parsing
                        // expressions.
                        while (CurrentTokenType == TokenType.Character
                            && CurrentTokenText == ",")
                        {
                            AppendCurrent();
                            NextToken();
                            ParseExpression();
                        }

                        if (CurrentTokenType != TokenType.Character
                          || CurrentTokenText != ")")
                        {
                            ReportError(0, CssErrorCode.ExpectedIdentifier, CurrentTokenText);
                        }
                        AppendCurrent();
                        NextToken();
                        break;

                    default:
                        ReportError(0, CssErrorCode.ExpectedIdentifier, CurrentTokenText);
                        break;
                }
                parsed = Parsed.True;
            }
            return parsed;
        }

        Parsed ParseExpression()
        {
            Parsed parsed = Parsed.Empty;
            while(true)
            {
                switch(CurrentTokenType)
                {
                    case TokenType.Dimension:
                    case TokenType.Number:
                    case TokenType.String:
                    case TokenType.Identifier:
                        // just output these token types
                        parsed = Parsed.True;
                        AppendCurrent();
                        NextToken();
                        break;

                    case TokenType.Space:
                        // ignore spaces
                        NextToken();
                        break;

                    case TokenType.Character:
                        if (CurrentTokenText == "+" || CurrentTokenText == "-")
                        {
                            parsed = Parsed.True;
                            AppendCurrent();
                            NextToken();
                        }
                        else
                        {
                            // don't care if this finds one or not, just process it
	                        ParseCombinator();

	                        return ParseSelector();
                        }
                        break;

                    case TokenType.Of:
                        AppendCurrent();
                        NextToken();
                        ParseSelector(); // Selectors v4 indicates that :nth-child and :nth-last-child can have their n argument followed by "of S" with S being a selector
                        break;

                    default:
                        // anything else and we bail
                        return parsed;
                }
            }
        }

        Parsed ParseDeclaration()
        {
            Parsed parsed = Parsed.Empty;

            // see if the developer is using an IE hack of prefacing property names
            // with an asterisk -- IE seems to ignore it; other browsers will recognize
            // the invalid property name and ignore it.
            string prefix = null;
            if (CurrentTokenType == TokenType.Character 
                && (CurrentTokenText == "*" || CurrentTokenText == "."))
            {
                // spot a low-pri error because this is actually invalid CSS
                // taking advantage of an IE "feature"
                ReportError(4, CssErrorCode.HackGeneratesInvalidCss, CurrentTokenText);

                // save the prefix and skip it
                prefix = CurrentTokenText;
                NextToken();
            }

            if (CurrentTokenType == TokenType.Identifier)
            {
                // save the property name
                string propertyName = CurrentTokenText;

                // if this is an excluded property name, then set the no-output flag
                // so the declaration is not outputted (we'll always reset this flag at
                // the end of the function)
                if (Settings.ExcludeVendorPrefixes.Count > 0 && IsExcludedVendorPrefix(propertyName))
	                m_noOutput = true;

                NewLine();
                if (prefix != null)
	                Append(prefix);

                AppendCurrent();

                // we want to skip space BUT we want to preserve a space if there is a whitespace character
                // followed by a comment. So don't call the simple SkipSpace method -- that will output the
                // comment but ignore all whitespace.
                SkipSpaceComment();

                if (CurrentTokenType != TokenType.Character
                  || CurrentTokenText != ":")
                {
                    ReportError(0, CssErrorCode.ExpectedColon, CurrentTokenText);
                    SkipToEndOfDeclaration();
                    return Parsed.True;
                }

                Append(':');

                if (Settings.OutputDeclarationWhitespace)
	                Append(' ');

                parsingZeroReducibleProperty = IsZeroReducibleProperty(propertyName);
                parsingNoneReducibleProperty = IsNoneReducibleProperty(propertyName);

                SkipSpace();

                if (m_valueReplacement != null)
                {
                    // output the replacement string
                    Append(m_valueReplacement);

                    // clear the replacement string
                    m_valueReplacement = null;

                    // set the no-output flag, parse the value, the reset the flag.
                    // we don't care if it actually finds a value or not
                    var notOutputting = m_noOutput;
                    m_noOutput = true;
                    ParseExpr();
                    m_noOutput = notOutputting;
                }
                else 
                {
                    // valueless variable declarations are legal
	                if (propertyName.StartsWith("--") && (CurrentTokenText == ";" || CurrentTokenText == "}"))
		                return Parsed.True;

	                m_parsingColorValue = MightContainColorNames(propertyName);
                    parsed = ParseExpr();
                    m_parsingColorValue = false;

                    if (parsed != Parsed.True)
                    {
                        ReportError(0, CssErrorCode.ExpectedExpression, CurrentTokenText);
                        SkipToEndOfDeclaration();
                        return Parsed.True;
                    }

                    parsingZeroReducibleProperty = true;
                }

                // optional
                ParsePrio();

                parsed = Parsed.True;
                m_noOutput = false;
            }
            return parsed;
        }

        Parsed ParsePrio()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.ImportantSymbol)
            {
                // NUglify Bug 314: I don't know what the below issue is/was, but it should be ok for modern browsers to omit the space
	            // AjaxMin Issue #21057 - do not strip space before !important keyword.
                // if (m_skippedSpace)
                // {
                //     Append(' ');
                // }

                AppendCurrent();
                SkipSpace();

                // a common IE7-and-below hack is to append another ! at the end of !important.
                if (CurrentTokenType == TokenType.Character && CurrentTokenText == "!")
                {
                    ReportError(4, CssErrorCode.HackGeneratesInvalidCss, CurrentTokenText);
                    AppendCurrent();
                    SkipSpace();
                }

                parsed = Parsed.True;
            }
            else if (CurrentTokenType == TokenType.Character && CurrentTokenText == "!")
            {
                // another common IE7-and-below hack is to use an identifier OTHER than "important". All other browsers will see this
                // as an error, but IE7 and below will keep on processing. A common thing is to put !ie at the end to mark
                // the declaration as only for IE.
                if (Settings.OutputDeclarationWhitespace)
	                Append(' ');

                AppendCurrent();
                NextToken();
                if (CurrentTokenType == TokenType.Identifier)
                {
                    ReportError(4, CssErrorCode.HackGeneratesInvalidCss, CurrentTokenText);

                    AppendCurrent();
                    SkipSpace();
                    parsed = Parsed.True;
                }
                else
                {
                    // but we need SOME identifier here....
                    ReportError(0, CssErrorCode.ExpectedIdentifier, CurrentTokenText);
                }
            }
            return parsed;
        }

        Parsed ParseExpr()
        {
            Parsed parsed = ParseTerm(false);
            if (parsed == Parsed.True)
            {
                while (!AtEof)
                {
                    Parsed parsedOp = ParseOperator();
                    if (parsedOp != Parsed.False)
                    {
                        if (ParseTerm(parsedOp == Parsed.Empty) == Parsed.False)
                        {
                            break;
                        }
                    }
                }
            }

            // The nesting selector '&' is only valid in a selector, never where a term/value
            // is expected (e.g. inside a declaration value such as "color:&" or "width:10&px").
            // If term parsing stopped on a '&', report it against the offending token and
            // recover to the end of the declaration WITHOUT emitting the '&'. We return
            // Parsed.True so the caller does not additionally report ExpectedExpression for
            // the same construct (Requirements 1.5, 3.6).
            if (CurrentTokenType == TokenType.NestingSelector)
            {
                ReportError(0, CssErrorCode.UnexpectedNestingSelector, CurrentTokenText);

                // skip past the offending '&' so recovery never streams it to the output
                NextToken();
                SkipToEndOfDeclaration();
                return Parsed.True;
            }

            return parsed;
        }

        Parsed ParseFunctionParameters()
        {
            Parsed parsed = ParseTerm(false);
            if (parsed == Parsed.True)
            {
                while (!AtEof)
                {
                    if (CurrentTokenType == TokenType.Character
                      && CurrentTokenText == "=")
                    {
                        AppendCurrent();
                        SkipSpace();
                        ParseTerm(false);
                    }

                    Parsed parsedOp = ParseOperator();
                    if (parsedOp != Parsed.False)
                    {
                        if (ParseTerm(parsedOp == Parsed.Empty) == Parsed.False)
                        {
                            break;
                        }
                    }
                }
            }
            else if (parsed == Parsed.False
              && CurrentTokenType == TokenType.Character
              && CurrentTokenText == ")")
            {
                // it's okay to have no parameters in functions
                parsed = Parsed.Empty;
            }
            return parsed;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        Parsed ParseTerm(bool wasEmpty)
        {
            Parsed parsed = Parsed.False;
            bool hasUnary = false;
            if (CurrentTokenType == TokenType.Character
                && (CurrentTokenText == "-" || CurrentTokenText == "+"))
            {
                if (wasEmpty)
                {
                    if (m_skippedSpace)
                    {
                        Append(' ');
                    }

                    wasEmpty = false;
                }

                AppendCurrent();
                NextToken();
                hasUnary = true;
            }

            switch (CurrentTokenType)
            {
                case TokenType.Hash:
                    if (hasUnary)
                    {
                        ReportError(0, CssErrorCode.HashAfterUnaryNotAllowed, CurrentTokenText);
                    }

                    if (wasEmpty)
                    {
                        Append(' ');
                        wasEmpty = false;
                    }
                    if (ParseHexcolor() == Parsed.False)
                    {
                        ReportError(0, CssErrorCode.ExpectedHexColor, CurrentTokenText);

                        // we expected the hash token to be a proper color -- but it's not.
                        // we threw an error -- go ahead and output the token as-is and keep going.
                        AppendCurrent();
                        SkipSpace();
                    }
                    parsed = Parsed.True;
                    break;

                case TokenType.String:
                case TokenType.Identifier:
                case TokenType.Uri:
                //case TokenType.RGB:
                case TokenType.UnicodeRange:
                    if (hasUnary)
                    {
                        ReportError(0, CssErrorCode.TokenAfterUnaryNotAllowed, CurrentTokenText);
                    }

                    // wasEmpty will be false if we DIDN'T find an operator
                    // as the last token. If we had an operator, then we can ignore
                    // any whitespace; but if we DIDN'T find an operator, then we
                    // will need to preserve a whitespace character to keep them 
                    // separated.
                    if (wasEmpty)
                    {
                        // if we had skipped any space, then add one now
                        if (m_skippedSpace)
                        {
                            Append(' ');
                        }

                        wasEmpty = false;
                    }

                    if (parsingNoneReducibleProperty && CurrentTokenText.ToLowerInvariant() == "none" && m_lastOutputString == ":")
	                    Append('0');
                    else
	                    AppendCurrent();
                    SkipSpace();
                    parsed = Parsed.True;
                    break;

                case TokenType.Dimension:
                    ReportError(2, CssErrorCode.UnexpectedDimension, CurrentTokenText);
                    goto case TokenType.Number;

                case TokenType.Number:
                case TokenType.Percentage:
                case TokenType.AbsoluteLength:
                case TokenType.RelativeLength:
                case TokenType.Angle:
                case TokenType.Time:
                case TokenType.Frequency:
                case TokenType.Resolution:
                case TokenType.Fraction:
                    if (wasEmpty)
                    {
                        Append(' ');
                        wasEmpty = false;
                    }

                    AppendCurrent();
                    SkipSpace();
                    parsed = Parsed.True;
                    break;

                case TokenType.ProgId:
                    if (wasEmpty)
                    {
                        Append(' ');
                        wasEmpty = false;
                    }
                    if (ParseProgId() == Parsed.False)
                    {
                        ReportError(0, CssErrorCode.ExpectedProgId, CurrentTokenText);
                    }
                    parsed = Parsed.True;
                    break;

                case TokenType.Function:
                    if (wasEmpty)
                    {
                        Append(' ');
                        wasEmpty = false;
                    }
                    if (ParseFunction() == Parsed.False)
                    {
                        ReportError(0, CssErrorCode.ExpectedFunction, CurrentTokenText);
                    }
                    parsed = Parsed.True;
                    break;

                case TokenType.Character:
                    // Handle custom identifiers (e.g. used with grid-template-columns)
                    if (CurrentTokenText == "[")
                    {
                        if (wasEmpty)
                        {
                            // if we had skipped any space, then add one now
                            if (m_skippedSpace)
                            {
                                Append(' ');
                            }

                            wasEmpty = false;
                        }
                        AppendCurrent();
                        SkipSpace();
                        if (CurrentTokenType != TokenType.Identifier)
                        {
                            ReportError(0, CssErrorCode.ExpectedIdentifier, CurrentTokenText);
                        }
                        AppendCurrent();
                        SkipSpace();
                        if (CurrentTokenText != "]")
                        {
                            ReportError(0, CssErrorCode.ExpectedClosingBracket, CurrentTokenText);
                        }
                        AppendCurrent();
                        SkipSpace();
                        parsed = Parsed.True;
                        break;
                    }
                    else if (CurrentTokenText == "(")
                    {
                        // the term starts with an opening paren.
                        // parse an expression followed by the close paren.
                        if (wasEmpty)
                        {
                            if (m_skippedSpace)
                            {
                                Append(' ');
                            }

                            wasEmpty = false;
                        }

                        AppendCurrent();
                        SkipSpace();

                        if (ParseExpr() == Parsed.False)
                        {
                            ReportError(0, CssErrorCode.ExpectedExpression, CurrentTokenText);
                        }

                        if (CurrentTokenType == TokenType.Character
                            && CurrentTokenText == ")")
                        {
                            AppendCurrent();
                            parsed = Parsed.True;

                            // the closing paren can only be followed IMMEDIATELY by the opening brace
                            // without any space if it's a repeat syntax.
                            m_skippedSpace = false;
                            NextRawToken();
                            if (CurrentTokenType == TokenType.Space)
                            {
                                m_skippedSpace = true;
                            }

                            // if the next token is an opening brace, then this might be
                            // a repeat operator
                            if (CurrentTokenType == TokenType.Character
                                && CurrentTokenText == "[")
                            {
                                AppendCurrent();
                                SkipSpace();

                                if (CurrentTokenType == TokenType.Number)
                                {
                                    AppendCurrent();
                                    SkipSpace();

                                    if (CurrentTokenType == TokenType.Character
                                        && CurrentTokenText == "]")
                                    {
                                        AppendCurrent();
                                        SkipSpace();
                                    }
                                    else
                                    {
                                        ReportError(0, CssErrorCode.ExpectedClosingBracket, CurrentTokenText);
                                        parsed = Parsed.False;
                                    }
                                }
                                else
                                {
                                    ReportError(0, CssErrorCode.ExpectedNumber, CurrentTokenText);
                                    parsed = Parsed.False;
                                }
                            }
                        }
                        else
                        {
                            ReportError(0, CssErrorCode.ExpectedClosingParenthesis, CurrentTokenText);
                        }
                    }
                    else if ( CurrentTokenText == "%")
                    {
                        // see if this is the start of a replacement token
                        UpdateIfReplacementToken();
                        if (CurrentTokenType == TokenType.ReplacementToken)
                        {
                            // it was -- output it and move along
                            if (wasEmpty)
                            {
                                Append(' ');
                                wasEmpty = false;
                            }

                            AppendCurrent();
                            SkipSpace();
                            parsed = Parsed.True;
                        }
                        else
                        {
                            // nope; just a percent. 
                            goto default;
                        }
                    }
                    else
                    {
                        goto default;
                    }
                    break;

                case TokenType.NestingSelector:
                    // '&' is never a valid term. Leave parsed as False and do NOT report here;
                    // ParseExpr reports UnexpectedNestingSelector once against the offending
                    // token and recovers, so the '&' is never emitted (Requirements 1.5, 3.6).
                    break;

                default:
                    if (hasUnary)
                    {
                        ReportError(0, CssErrorCode.UnexpectedToken, CurrentTokenText);
                    }
                    break;
            }
            return parsed;
        }

        Parsed ParseProgId()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.ProgId)
            {
                ReportError(4, CssErrorCode.ProgIdIEOnly);

                // set the state flag that tells us we should NOT abbreviate color
                // hash values as we are parsing our parameters
                m_noColorAbbreviation = true;

                // comma-separated lists of progid expressions should have a space after
                // the comma, or IE will ignore all but the last
                if (m_lastOutputString == ",")
                {
                    Append(" ");
                }

                // append the progid and opening paren
                AppendCurrent();
                SkipSpace();

                // the rest is a series of parameters: name=value, separated
                // by commas and ending with a close paren
                while (CurrentTokenType == TokenType.Identifier)
                {
                    AppendCurrent();
                    SkipSpace();

                    if (CurrentTokenType != TokenType.Character
                      && CurrentTokenText != "=")
                    {
                        ReportError(0, CssErrorCode.ExpectedEqualSign, CurrentTokenText);
                    }

                    Append('=');
                    SkipSpace();

                    if (ParseTerm(false) != Parsed.True)
                    {
                        ReportError(0, CssErrorCode.ExpectedTerm, CurrentTokenText);
                    }

                    if (CurrentTokenType == TokenType.Character
                      && CurrentTokenText == ",")
                    {
                        Append(',');
                        SkipSpace();
                    }
                }

                // reset the color-abbreviation flag
                m_noColorAbbreviation = !Settings.AbbreviateHexColor;

                // make sure we're at the close paren
                if (CurrentTokenType == TokenType.Character
                  && CurrentTokenText == ")")
                {
                    Append(')');
                    SkipSpace();
                }
                else
                {
                    ReportError(0, CssErrorCode.UnexpectedToken, CurrentTokenText);
                }
                parsed = Parsed.True;
            }
            return parsed;
        }

        static string GetRoot(string text)
        {
            if (text.StartsWith("-", StringComparison.Ordinal))
            {
                var match = s_vendorSpecific.Match(text);
                if (match.Success)
                {
                    text = match.Result("${root}");
                }
            }

            return text;
        }

        Parsed ParseFunction()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.Function)
            {
                var functionText = GetRoot(CurrentTokenText);
                switch (functionText.ToUpperInvariant())
                {
                    case "RGB(":
                        parsed = ParseRgb();
                        break;

                    case "EXPRESSION(":
                        parsed = ParseExpressionFunction();
                        break;

                    case "CALC(":
                        parsed = ParseCalc();
                        break;

                    case "MIN(":
                    case "MAX(":
                        parsed = ParseMinMax();
                        break;

                    case "CLAMP(":
                        parsed = ParseClamp();
                        break;

                    default:
                        // generic function parsing
                        AppendCurrent();
                        SkipSpace();

                        if (ParseFunctionParameters() == Parsed.False)
                        {
                            ReportError(0, CssErrorCode.ExpectedExpression, CurrentTokenText);
                        }

                        if (CurrentTokenType == TokenType.Character && CurrentTokenText == ")")
                        {
                            AppendCurrent();
                            SkipSpace();
                            parsed = Parsed.True;
                        }
                        else
                        {
                            ReportError(0, CssErrorCode.UnexpectedToken, CurrentTokenText);
                        }
                        break;
                }
            }
            return parsed;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "we want lower-case output")]
        Parsed ParseRgb()
        {
            var parsed = Parsed.False;
            if (CurrentTokenType == TokenType.Function
                && string.Compare(CurrentTokenText, "rgb(", StringComparison.OrdinalIgnoreCase) == 0)
            {
                // rgb function parsing
                var useRGB = false;
                var crunchedRGB = false;
                // converting to #rrggbb or #rgb IF we don't find any significant comments!
                // skip any space or comments
                var rgb = new int[3];

                // we're going to be building up the rgb function just in case we need it
                var sbRGB = StringBuilderPool.Acquire();
                try
                {
                    sbRGB.Append(CurrentTokenText.ToLowerInvariant());

                    var comments = NextSignificantToken();
                    if (comments.Length > 0)
                    {
                        // add the comments
                        sbRGB.Append(comments);
                        // and signal that we need to use the RGB function because of them
                        useRGB = true;
                    }

                    bool? usingSpace = null;
                    bool usingFunc = false;
                    for (var ndx = 0; ndx < 4; ++ndx)
                    {
                        // if this isn't the first number, we better find a comma separator or space
                        if (ndx > 0)
                        {
                            if (usingSpace != true && CurrentTokenType == TokenType.Character && CurrentTokenText == ",")
                            {
                                // add it to the rgb string builder
                                sbRGB.Append(',');
                                usingSpace = false;
                                if (ndx == 3)
                                {
                                    // 4 part format, can't handle that
                                    useRGB = true;
                                }
                            }
                            else if (usingSpace != false && 
                                (CurrentTokenType == TokenType.Number ||
                                 CurrentTokenType == TokenType.Function))
                            {
                                sbRGB.Append(' ');
                                usingSpace = true;
                            }
                            else if (((usingSpace == true && ndx == 3) || usingFunc) &&
                                CurrentTokenType == TokenType.Character && CurrentTokenText == "/")
                            {
                                sbRGB.Append('/');
                            }
                            else if (CurrentTokenType == TokenType.Character && CurrentTokenText == ")")
                            {
                                // 3 part is OK, or used a func
                                if (ndx == 3 || usingFunc)
                                    break;

                                ReportError(0, CssErrorCode.ExpectedComma, CurrentTokenText);

                                // closing paren is the end of the function! exit the loop
                                useRGB = true;
                                break;
                            }
                            else
                            {
                                ReportError(0, CssErrorCode.ExpectedComma, CurrentTokenText);
                                sbRGB.Append(CurrentTokenText);
                                useRGB = true;
                            }

                            if (usingSpace != true || 
                                (CurrentTokenType != TokenType.Number &&
                                 CurrentTokenType != TokenType.Function))
                            {
                                // skip to the next significant
                                comments = NextSignificantToken();
                                if (comments.Length > 0)
                                {
                                    // add the comments
                                    sbRGB.Append(comments);
                                    // and signal that we need to use the RGB function because of them
                                    useRGB = true;
                                }
                            }
                        }

                        // although we ALLOW negative numbers here, we'll trim them
                        // later. But in the mean time, save a negation flag.
                        var negateNumber = false;
                        if (CurrentTokenType == TokenType.Character && CurrentTokenText == "-")
                        {
                            negateNumber = true;
                            comments = NextSignificantToken();
                            if (comments.Length > 0)
                            {
                                // add the comments
                                sbRGB.Append(comments);
                                // and signal that we need to use the RGB function because of them
                                useRGB = true;
                            }
                        }

                        // we might adjust the value, so save the token text
                        var tokenText = CurrentTokenText;

                        if (CurrentTokenType == TokenType.Function)
                        {
                            useRGB = true;
                            usingFunc = true;
                            Append(sbRGB.ToString());
                            sbRGB.Clear();

                            if (ParseFunction() == Parsed.False)
                            {
                                ReportError(0, CssErrorCode.ExpectedRgbNumberOrPercentage, CurrentTokenText);
                                return Parsed.False;
                            }

                            continue;
                        }
                        else if (CurrentTokenType != TokenType.Number && CurrentTokenType != TokenType.Percentage)
                        {
                            ReportError(0, CssErrorCode.ExpectedRgbNumberOrPercentage, CurrentTokenText);
                            useRGB = true;
                        }
                        else if (ndx < 3)
                        {
                            if (CurrentTokenType == TokenType.Number)
                            {
                                // get the number value
                                float numberValue;
                                if (tokenText.TryParseSingleInvariant(out numberValue))
                                {
                                    numberValue *= (negateNumber ? -1 : 1);
                                    // make sure it's between 0 and 255
                                    if (numberValue < 0)
                                    {
                                        tokenText = "0";
                                        rgb[ndx] = 0;
                                    }
                                    else if (numberValue > 255)
                                    {
                                        tokenText = "255";
                                        rgb[ndx] = 255;
                                    }
                                    else
                                    {
                                        rgb[ndx] = System.Convert.ToInt32(numberValue);
                                    }
                                }
                                else
                                {
                                    // error -- not even a number. Keep the rgb function
                                    // (and don't change the token)
                                    useRGB = true;
                                }
                            }
                            else
                            {
                                // percentage
                                float percentageValue;
                                if (tokenText.Substring(0, tokenText.Length - 1).TryParseSingleInvariant(out percentageValue))
                                {
                                    percentageValue *= (negateNumber ? -1 : 1);
                                    if (percentageValue < 0)
                                    {
                                        tokenText = "0%";
                                        rgb[ndx] = 0;
                                    }
                                    else if (percentageValue > 100)
                                    {
                                        tokenText = "100%";
                                        rgb[ndx] = 255;
                                    }
                                    else
                                    {
                                        rgb[ndx] = System.Convert.ToInt32(percentageValue * 255 / 100);
                                    }
                                }
                                else
                                {
                                    // error -- not even a number. Keep the rgb function
                                    // (and don't change the token)
                                    useRGB = true;
                                }
                            }
                        }
                        else if (ndx == 3)
                        {
                            useRGB = true;
                        }

                        // add the number to the rgb string builder
                        sbRGB.Append(tokenText);

                        // skip to the next significant
                        comments = NextSignificantToken();
                        if (comments.Length > 0)
                        {
                            // add the comments
                            sbRGB.Append(comments);
                            // and signal that we need to use the RGB function because of them
                            useRGB = true;
                        }
                    }

                    if (useRGB)
                    {
                        // something prevented us from collapsing the rgb function
                        // just output the rgb function we've been building up
                        Append(sbRGB.ToString());
                    }
                    else
                    {
                        // we can collapse it to either #rrggbb or #rgb
                        // calculate the full hex string and crunch it
                        var fullCode = "#{0:x2}{1:x2}{2:x2}".FormatInvariant(rgb[0], rgb[1], rgb[2]);
                        var result= CrunchHexColor(fullCode, Settings.ColorNames, m_noColorAbbreviation);
                        Append(result.Color);

                        // set the flag so we know we don't want to add the closing paren
                        crunchedRGB = true;
                    }
                }
                finally
                {
                    sbRGB.Release();
                }

                if (CurrentTokenType == TokenType.Character && CurrentTokenText == ")")
                {
                    if (!crunchedRGB)
                    {
                        AppendCurrent();
                    }

                    SkipSpace();
                    parsed = Parsed.True;
                }
                else
                {
                    ReportError(0, CssErrorCode.ExpectedClosingParenthesis, CurrentTokenText);
                }
            }

            return parsed;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "we want lower-case output")]
        Parsed ParseExpressionFunction()
        {
            var parsed = Parsed.False;
            if (CurrentTokenType == TokenType.Function
                && string.Compare(CurrentTokenText, "expression(", StringComparison.OrdinalIgnoreCase) == 0)
            {
                Append(CurrentTokenText.ToLowerInvariant());
                NextToken();

                // for now, just echo out everything up to the matching closing paren, 
                // taking into account that there will probably be other nested paren pairs. 
                // The content of the expression is JavaScript, so we'd really
                // need a full-blown JS-parser to crunch it properly. Kinda scary.
                // Start the parenLevel at 0 because the "expression(" token contains the first paren.
                string expressionCode = null;
                var jsBuilder = StringBuilderPool.Acquire();
                try
                {
                    int parenLevel = 0;

                    while (!AtEof
                      && (CurrentTokenType != TokenType.Character
                        || CurrentTokenText != ")"
                        || parenLevel > 0))
                    {
                        if (CurrentTokenType == TokenType.Function)
                        {
                            // the function token INCLUDES the opening parenthesis,
                            // so up the paren level whenever we find a function.
                            // AND this includes the actual expression( token -- so we'll
                            // hit this branch at the beginning. Make sure the parenLevel
                            // is initialized to take that into account
                            ++parenLevel;
                        }
                        else if (CurrentTokenType == TokenType.Character)
                        {
                            switch (CurrentTokenText)
                            {
                                case "(":
                                    // start a nested paren
                                    ++parenLevel;
                                    break;

                                case ")":
                                    // end a nested paren 
                                    // (we know it's nested because if it wasn't, we wouldn't
                                    // have entered the loop)
                                    --parenLevel;
                                    break;
                            }
                        }
                        jsBuilder.Append(CurrentTokenText);
                        NextToken();
                    }

                    // create a JSParser object with the source we found, crunch it, and send 
                    // the minified script to the output
                    expressionCode = jsBuilder.ToString();
                }
                finally
                {
                    jsBuilder.Release();
                }

                if (Settings.MinifyExpressions)
                {
                    // we want to minify the javascript expressions
                    JSParser jsParser = new JSParser();

                    // hook the error handler and set the "contains errors" flag to false.
                    // the handler will set the value to true if it encounters any errors
                    var containsErrors = false;
                    jsParser.CompilerError += (sender, ea) =>
                    {
                        ReportError(0, CssErrorCode.ExpressionError, ea.Error.Message);
                        containsErrors = true;
                    };

                    // parse the source as an expression using our common JS settings
                    var block = jsParser.Parse(new DocumentContext(expressionCode) { FileContext = this.FileContext }, jsSettings);

                    // if we got back a parsed block and there were no errors, output the minified code.
                    // if we didn't get back the block, or if there were any errors at all, just output
                    // the raw expression source.
                    if (block != null && !containsErrors)
                    {
                        Append(OutputVisitor.Apply(block, jsParser.Settings));
                    }
                    else
                    {
                        Append(expressionCode);
                    }
                }
                else
                {
                    // we don't want to minify expression code for some reason.
                    // just output the code exactly as we parsed it
                    Append(expressionCode);
                }

                if (CurrentTokenType == TokenType.Character && CurrentTokenText == ")")
                {
                    AppendCurrent();
                    SkipSpace();
                    parsed = Parsed.True;
                }
                else
                {
                    ReportError(0, CssErrorCode.ExpectedClosingParenthesis, CurrentTokenText);
                }
            }

            return parsed;
        }

        Parsed ParseHexcolor()
        {
            Parsed parsed = Parsed.False;

            if (CurrentTokenType == TokenType.Hash)
            {
                var colorHash = CurrentTokenText;
                var appendEscapedTab = false;

                // valid hash colors are #rgb, #rgba, #rrggbb, and #rrggbbaa.
                // but there is a commonly-used IE hack that puts \9 at the end of properties, so
                // if we have 5, 8, or 10 characters, let's first check to see if the color
                // ends in a tab.
                if ((colorHash.Length == 5 || colorHash.Length == 8 || colorHash.Length == 10)
                    && colorHash.EndsWith("\t", StringComparison.Ordinal))
                {
                    // it is -- strip that last character and set a flag
                    colorHash = colorHash.Substring(0, colorHash.Length - 1);
                    appendEscapedTab = true;
                }

                if (colorHash.Length == 4 || colorHash.Length == 5 || colorHash.Length == 7 || colorHash.Length == 9)
                {
	                var result = CrunchHexColor(colorHash, Settings.ColorNames, m_noColorAbbreviation);

	                if (!result.IsValidColor)
		                return Parsed.False;

                    parsed = Parsed.True;

                    Append(result.Color);

                    if (appendEscapedTab)
	                    Append("\\9");

                    SkipSpace();
                }
            }
            return parsed;
        }

        Parsed ParseUnit()
        {
            var parsed = Parsed.Empty;

            // optional sign
            if (CurrentTokenType == TokenType.Character
                && (CurrentTokenText == "+" || CurrentTokenText == "-"))
            {
                AppendCurrent();
                NextToken();

                // set the parsed flag to false -- if we don't get a valid token
                // next and set it to true, then we know we had an error
                parsed = Parsed.False;
            }

            // followed by a number, a percentage, a dimension, a min(, a max(, or a parenthesized sum
            switch (CurrentTokenType)
            {
                case TokenType.Number:
                case TokenType.Percentage:
                case TokenType.Dimension:
                case TokenType.RelativeLength:
                case TokenType.AbsoluteLength:
                case TokenType.Angle:
                case TokenType.Time:
                case TokenType.Resolution:
                case TokenType.Frequency:
                    // output it, skip any whitespace, and mark us as okay
                    AppendCurrent();
                    SkipSpace();
                    parsed = Parsed.True;
                    break;

                case TokenType.Function:
                    // calc( or attr( are allowed here.
                    parsed = ParseFunction();

                    // if parsed is false, then we encountered an error with the function
                    // and probably already output an error message. So only output an error
                    // message if we didn't find ANYTHING
                    if (parsed == Parsed.Empty)
                    {
                        ReportError(0, CssErrorCode.UnexpectedFunction, CurrentTokenText);
                        parsed = Parsed.False;
                    }
                    break;

                case TokenType.Character:
                    // only open parenthesis allowed
                    if (CurrentTokenText == "(")
                    {
                        // TODO: make sure there is whitespace before the ( if it would cause
                        // it to be the opening paren of a function token

                        AppendCurrent();
                        SkipSpace();

                        // better be a sum inside the parens
                        parsed = ParseSum();
                        if (parsed != Parsed.True)
                        {
                            // report error and change the parsed flag to false so we know there was an error
                            ReportError(0, CssErrorCode.ExpectedSum, CurrentTokenText);
                            parsed = Parsed.False;
                        }
                        else if (CurrentTokenType != TokenType.Character || CurrentTokenText != ")")
                        {
                            // needs to be a closing paren here
                            ReportError(0, CssErrorCode.ExpectedClosingParenthesis, CurrentTokenText);
                            parsed = Parsed.False;
                        }
                        else
                        {
                            // we're at the closing paren, so output it now, advance past any
                            // subsequent whitespace, and mark us as okay
                            AppendCurrent();
                            SkipSpace();
                            parsed = Parsed.True;
                        }
                    }
                    break;
            }

            return parsed;
        }

        Parsed ParseProduct()
        {
            // there needs to be at least one unit here
            var parsed = ParseUnit();
            if (parsed == Parsed.True)
            {
                // keep going while we have product operators
                // "mod" isn't a final operator, but it was in earlier drafts so keep allowing it.
                while ((CurrentTokenType == TokenType.Character && (CurrentTokenText == "*" || CurrentTokenText == "/"))
                    || (CurrentTokenType == TokenType.Identifier && string.Compare(CurrentTokenText, "mod", StringComparison.OrdinalIgnoreCase) == 0))
                {
                    if (CurrentTokenText == "*" || CurrentTokenText == "/")
                    {
                        // multiplication and dicision operators don't need spaces around them
                        // UNLESS we are outputting multi-line mode
                        if (Settings.OutputDeclarationWhitespace)
	                        Append(' ');

                        AppendCurrent();
                        if (Settings.OutputDeclarationWhitespace)
	                        Append(' ');
                    }
                    else
                    {
                        // the mod-operator usually needs space around it.
                        // and keep it lower-case.
                        Append(" mod ");
                    }

                    // skip any whitespace
                    SkipSpace();

                    // grab the next unit -- and there better be one
                    // technically the candidate spec says / can only be followed by NUMBER, not a UNIT, but
                    // let's let this slide and just parse a unit for both.
                    parsed = ParseUnit();
                    if (parsed != Parsed.True)
                    {
                        ReportError(0, CssErrorCode.ExpectedUnit, CurrentTokenText);
                        parsed = Parsed.False;
                    }
                }
            }
            else
            {
                // report an error and make sure we return false
                ReportError(0, CssErrorCode.ExpectedUnit, CurrentTokenText);
                parsed = Parsed.False;
            }

            return parsed;
        }

        Parsed ParseSum()
        {
            // there needs to be at least one product here
            var parsed = ParseProduct();
            if (parsed == Parsed.True)
            {
                // keep going while we have sum operators
                while (CurrentTokenType == TokenType.Character && (CurrentTokenText == "+" || CurrentTokenText == "-"))
                {
                    // plus and minus operators need space around them.
                    Append(' ');
                    AppendCurrent();

                    // plus and minus operators both need spaces after them.
                    // the minus needs to not be an identifier.
                    Append(' ');

                    SkipSpace();

                    // grab the next product -- and there better be one
                    parsed = ParseProduct();
                    if (parsed != Parsed.True)
                    {
                        ReportError(0, CssErrorCode.ExpectedProduct, CurrentTokenText);
                        parsed = Parsed.False;
                    }
                }
            }
            else
            {
                // report an error and make sure we return false
                ReportError(0, CssErrorCode.ExpectedProduct, CurrentTokenText);
                parsed = Parsed.False;
            }

            return parsed;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification="we want lower-case output")]
        Parsed ParseMinMax()
        {
            // return false if the function isn't min or max
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.Function
                && (string.Compare(CurrentTokenText, "min(", StringComparison.OrdinalIgnoreCase) == 0
                || string.Compare(CurrentTokenText, "max(", StringComparison.OrdinalIgnoreCase) == 0))
            {
                // output lower-case version and skip any space
                Append(CurrentTokenText.ToLowerInvariant());
                SkipSpace();

                // must be at least one sum
                parsed = ParseSum();

                // comma-delimited sums continue
                while (parsed == Parsed.True
                    && CurrentTokenType == TokenType.Character
                    && CurrentTokenText == ",")
                {
                    AppendCurrent();
                    SkipSpace();

                    parsed = ParseSum();
                }

                // end with the closing paren
                if (CurrentTokenType == TokenType.Character && CurrentTokenText == ")")
                {
                    AppendCurrent();
                    SkipSpace();
                    parsed = Parsed.True;
                }
                else
                {
                    ReportError(0, CssErrorCode.ExpectedClosingParenthesis, CurrentTokenText);
                    parsed = Parsed.False;
                }
            }

            return parsed;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "we want lower-case output")]
        Parsed ParseCalc()
        {
            var parsed = Parsed.False;
            if (CurrentTokenType == TokenType.Function
                && string.Compare(GetRoot(CurrentTokenText), "calc(", StringComparison.OrdinalIgnoreCase) == 0)
            {
                m_insideCalc = true;
                Append(CurrentTokenText.ToLowerInvariant());
                SkipSpace();

                // contains one sum
                if (ParseSum() != Parsed.True)
                {
                    ReportError(0, CssErrorCode.ExpectedSum, CurrentTokenText);
                }

                if (CurrentTokenType == TokenType.Character && CurrentTokenText == ")")
                {
                    AppendCurrent();
                    SkipSpace();
                    parsed = Parsed.True;
                }
                else
                {
                    ReportError(0, CssErrorCode.ExpectedClosingParenthesis, CurrentTokenText);
                }

                m_insideCalc = false;
            }

            return parsed;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "we want lower-case output")]
        Parsed ParseClamp()
        {
            var parsed = Parsed.False;
            if (CurrentTokenType == TokenType.Function
                && string.Compare(GetRoot(CurrentTokenText), "clamp(", StringComparison.OrdinalIgnoreCase) == 0)
            {
                Append(CurrentTokenText.ToLowerInvariant());
                SkipSpace();

                // clamp() has three parameters: min, preferred, max
                for (int i = 0; i < 3; i++)
                {
                    if (ParseSum() != Parsed.True)
                    {
                        ReportError(0, CssErrorCode.ExpectedExpression, CurrentTokenText);
                    }

                    if (i < 2) // Expecting a comma between values
                    {
                        if (CurrentTokenType == TokenType.Character && CurrentTokenText == ",")
                        {
                            AppendCurrent();
                            SkipSpace();
                        }
                        else
                        {
                            ReportError(0, CssErrorCode.ExpectedComma, CurrentTokenText);
                        }
                    }
                }

                if (CurrentTokenType == TokenType.Character && CurrentTokenText == ")")
                {
                    AppendCurrent();
                    SkipSpace();
                    parsed = Parsed.True;
                }
                else
                {
                    ReportError(0, CssErrorCode.ExpectedClosingParenthesis, CurrentTokenText);
                }
            }

            return parsed;
        }

        #endregion

        #region Next... methods

        // skip to the next token, but output any comments we may find as we go along
        // Pull the next raw token from the source. If a classification look-ahead buffered
        // tokens, replay them (in order) first -- restoring the scanner's end-of-line flag for
        // each one -- before pulling fresh tokens from the scanner. When the buffer is empty
        // (the common case) this is identical to calling m_scanner.NextToken directly.
        CssToken PullRawToken(bool reduceZeros)
        {
            if (m_peekBuffer != null)
            {
                var entry = m_peekBuffer.Dequeue();
                if (m_peekBuffer.Count == 0)
                {
                    m_peekBuffer = null;
                }

                // restore the end-of-line flag that was in effect when this token was scanned,
                // so callers that read m_scanner.GotEndOfLine right after see the correct value.
                m_scanner.GotEndOfLine = entry.EndOfLine;
                return NormalizePeekedToken(entry.Token, reduceZeros);
            }

            return m_scanner.NextToken(reduceZeros);
        }

        TokenType NextToken()
        {
            m_currentToken = PullRawToken(!m_insideCalc && parsingZeroReducibleProperty);
            if (EchoWriter != null)
            {
                EchoWriter.Write(CurrentTokenText);
            }

            m_encounteredNewLine = m_scanner.GotEndOfLine;
            while (CurrentTokenType == TokenType.Comment)
            {
                // the append statement might not actually append anything.
                // if it doesn't, we don't need to output a newline
                if (AppendCurrent())
                {
                    NewLine();
                }
                m_currentToken = PullRawToken(!m_insideCalc);
                if (EchoWriter != null)
                {
                    EchoWriter.Write(CurrentTokenText);
                }

                m_encounteredNewLine = m_encounteredNewLine || m_scanner.GotEndOfLine;
            }
            return CurrentTokenType;
        }

        // just skip to the next token; don't skip over comments
        TokenType NextRawToken()
        {
            m_currentToken = PullRawToken(!m_insideCalc);
            if (EchoWriter != null)
            {
                EchoWriter.Write(CurrentTokenText);
            }

            m_encounteredNewLine = m_scanner.GotEndOfLine;
            return CurrentTokenType;
        }

        // ---- Block-body classification look-ahead --------------------------------------
        //
        // Peeks up to <paramref name="count"/> significant tokens (skipping Space and Comment)
        // that FOLLOW the current token, WITHOUT consuming the current token or emitting any
        // output. Every token read (including the skipped spaces/comments) is buffered so that
        // subsequent NextToken calls replay them in order -- leaving parser state untouched.
        // Returns the significant tokens found, in order (may be fewer than requested at EOF).
        List<CssToken> PeekSignificantTokens(int count)
        {
            var consumed = new List<PeekedToken>();
            var significant = new List<CssToken>();

            // If a previous peek left buffered tokens, drain them into our working list first so
            // we don't lose them (in practice the buffer is empty when classification runs).
            if (m_peekBuffer != null)
            {
                consumed.AddRange(m_peekBuffer);
                foreach (var pending in m_peekBuffer)
                {
                    if (pending.Token != null
                        && pending.Token.TokenType != TokenType.Space
                        && pending.Token.TokenType != TokenType.Comment
                        && pending.Token.TokenType != TokenType.None
                        && significant.Count < count)
                    {
                        significant.Add(pending.Token);
                    }
                }
                m_peekBuffer = null;
            }

            while (significant.Count < count && !AtEof)
            {
                var token = m_scanner.NextToken(false);
                consumed.Add(new PeekedToken { Token = token, EndOfLine = m_scanner.GotEndOfLine });

                var type = token?.TokenType ?? TokenType.None;
                if (type == TokenType.None)
                {
                    break;
                }

                if (type != TokenType.Space && type != TokenType.Comment)
                {
                    significant.Add(token);

                    // Replacement tokens are recognized later from a leading '%' plus the
                    // following raw token stream. If classification peeks only the opening '%'
                    // (or any truncated prefix), replaying the buffered tokens later desynchronizes
                    // replacement-token parsing. Once we see a '%' during look-ahead, keep buffering
                    // through its terminating '%' (or a declaration terminator) so replay always
                    // has a whole token to work with.
                    if (IsCharacterToken(token, "%"))
                    {
                        while (!AtEof)
                        {
                            var continuation = m_scanner.NextToken(false);
                            consumed.Add(new PeekedToken { Token = continuation, EndOfLine = m_scanner.GotEndOfLine });

                            var continuationType = continuation?.TokenType ?? TokenType.None;
                            if (continuationType == TokenType.None)
                                break;

                            if (continuationType != TokenType.Space && continuationType != TokenType.Comment)
                            {
                                significant.Add(continuation);
                            }

                            if (IsCharacterToken(continuation, "%")
                                || IsCharacterToken(continuation, ";")
                                || IsCharacterToken(continuation, "}")
                                || IsCharacterToken(continuation, "{"))
                            {
                                break;
                            }
                        }
                    }

                    if (IsCharacterToken(token, "{")
                        || IsCharacterToken(token, ";")
                        || IsCharacterToken(token, "}"))
                    {
                        break;
                    }
                }
            }

            // stash everything we read so the parser replays it transparently.
            if (consumed.Count > 0)
            {
                m_peekBuffer = new Queue<PeekedToken>(consumed);
            }

            return significant;
        }

        string NextSignificantToken()
        {
            // MOST of the time we won't need to save anything,
            // so don't bother allocating a string builder unless we need it
            string text = null;
            StringBuilder sb = null;
            try
            {
                // get the next token
                m_currentToken = PullRawToken(!m_insideCalc);
                if (EchoWriter != null)
                {
                    EchoWriter.Write(CurrentTokenText);
                }

                m_encounteredNewLine = m_scanner.GotEndOfLine;
                while (CurrentTokenType == TokenType.Space || CurrentTokenType == TokenType.Comment)
                {
                    // if this token is a comment, add it to the builder
                    if (CurrentTokenType == TokenType.Comment)
                    {
                        // check for important comment
                        string commentText = CurrentTokenText;
                        bool importantComment = commentText.StartsWith("/*!", StringComparison.Ordinal);
                        if (importantComment)
                        {
                            // get rid of the exclamation mark in some situations
                            commentText = NormalizeImportantComment(commentText);
                        }

                        // if the comment mode is none, don't ever output it.
                        // if the comment mode is all, always output it.
                        // otherwise only output it if it is an important comment.
                        bool writeComment = Settings.CommentMode == CssComment.All
                            || (importantComment && Settings.CommentMode != CssComment.None);

                        if (!importantComment)
                        {
                            if (s_sharepointReplacement.IsMatch(commentText))
                            {
                                // we ALWAYS want to output sharepoint styling comments
                                // (unless settings say NO comments)
                                writeComment = Settings.CommentMode != CssComment.None;
                            }
                            else
                            {
                                // see if this is a value-replacement id
                                Match match = s_valueReplacement.Match(commentText);
                                if (match.Success)
                                {
                                    // check all the resource strings objects to see if one is a match.
                                    m_valueReplacement = null;

                                    var resourceList = Settings.ResourceStrings;
                                    if (resourceList.Count > 0)
                                    {
                                        // get the id of the string we want to substitute
                                        string ident = match.Result("${id}");

                                        // walk the list BACKWARDS so later resource string objects override previous ones
                                        for (var ndx = resourceList.Count - 1; ndx >= 0; --ndx)
                                        {
                                            m_valueReplacement = resourceList[ndx][ident];
                                            if (m_valueReplacement != null)
	                                            break;
                                        }
                                    }

                                    // if there is such a string, we will have saved the value in the value replacement
                                    // variable so it will be substituted for the next value.
                                    // if there is no such string, we ALWAYS want to output the comment so we know 
                                    // there was a problem (even if the comments mode is to output none)
                                    writeComment = m_valueReplacement == null;
                                    if (writeComment)
                                    {
                                        // make sure the comment is normalized
                                        commentText = NormalizedValueReplacementComment(commentText);
                                    }
                                }
                            }
                        }

                        if (writeComment)
                        {
                            // if we haven't yet allocated a string builder, do it now
                            if (sb == null)
                            {
                                sb = StringBuilderPool.Acquire();
                            }

                            // add the comment to the builder
                            sb.Append(commentText);
                        }
                    }

                    // next token
                    m_currentToken = PullRawToken(!m_insideCalc);
                    if (EchoWriter != null)
                    {
                        EchoWriter.Write(CurrentTokenText);
                    }

                    m_encounteredNewLine = m_encounteredNewLine || m_scanner.GotEndOfLine;
                }

                text = sb == null ? string.Empty : sb.ToString();
            }
            finally
            {
                sb.Release();
            }

            // return any comments we found in the mean time
            return text;
        }

        void UpdateIfReplacementToken()
        {
            m_currentToken = TryScanBufferedReplacementToken() ?? m_scanner.ScanReplacementToken() ?? m_currentToken;
        }

        CssToken TryScanBufferedReplacementToken()
        {
            if (m_peekBuffer == null
                || CurrentTokenType != TokenType.Character
                || CurrentTokenText != "%")
            {
                return null;
            }

            var buffered = m_peekBuffer.ToArray();
            if (buffered.Length == 0)
                return null;

            var builder = StringBuilderPool.Acquire();
            try
            {
                builder.Append('%');

                var index = 0;
                if (!TryAppendBufferedReplacementName(buffered, ref index, builder))
                    return null;

                if (index < buffered.Length && IsCharacterToken(buffered[index].Token, ":"))
                {
                    builder.Append(':');
                    ++index;

                    // Empty fallbacks such as %MissingToken:% are valid.
                    TryAppendBufferedReplacementName(buffered, ref index, builder);
                }

                if (index >= buffered.Length || !IsCharacterToken(buffered[index].Token, "%"))
                    return null;

                builder.Append('%');
                ++index;

                for (var i = 0; i < index; i++)
                {
                    m_peekBuffer.Dequeue();
                }

                if (m_peekBuffer.Count == 0)
                {
                    m_peekBuffer = null;
                }

                return new CssToken(TokenType.ReplacementToken, builder.ToString(), m_currentToken.Context);
            }
            finally
            {
                builder.Release();
            }
        }

        static bool TryAppendBufferedReplacementName(IList<PeekedToken> buffered, ref int index, StringBuilder builder)
        {
            if (index >= buffered.Count || buffered[index].Token == null || buffered[index].Token.TokenType != TokenType.Identifier)
                return false;

            while (index < buffered.Count
                && buffered[index].Token != null
                && buffered[index].Token.TokenType == TokenType.Identifier)
            {
                builder.Append(buffered[index].Token.Text);
                ++index;

                if (index < buffered.Count && IsCharacterToken(buffered[index].Token, "."))
                {
                    builder.Append('.');
                    ++index;
                    continue;
                }

                break;
            }

            return true;
        }

        static CssToken NormalizePeekedToken(CssToken token, bool reduceZeros)
        {
            if (!reduceZeros || token == null)
                return token;

            if (token.TokenType != TokenType.RelativeLength
                && token.TokenType != TokenType.AbsoluteLength
                && token.TokenType != TokenType.Speech)
            {
                return token;
            }

            var text = token.Text;
            if (text.IsNullOrWhiteSpace())
                return token;

            var numberLength = 0;
            while (numberLength < text.Length && (char.IsDigit(text[numberLength]) || text[numberLength] == '.'))
            {
                ++numberLength;
            }

            if (numberLength == 0 || numberLength == text.Length)
                return token;

            var numericText = text.Substring(0, numberLength);
            for (var i = 0; i < numericText.Length; i++)
            {
                var ch = numericText[i];
                if (ch != '0' && ch != '.')
                    return token;
            }

            return new CssToken(TokenType.Number, "0", token.Context);
        }

        #endregion

        #region Skip... methods

        /// <summary>
        /// This method advances to the next token FIRST -- effectively skipping the current one -- 
        /// and then skips any space tokens that FOLLOW it.
        /// </summary>
        void SkipSpace()
        {
            // reset the skipped-space flag
            m_skippedSpace = false;

            // move to the next token
            NextToken();

            // we need to collate this flag for this method call
            var encounteredNewLine = m_encounteredNewLine;

            // while space, keep stepping
            while (CurrentTokenType == TokenType.Space)
            {
                m_skippedSpace = true;
                NextToken();
                encounteredNewLine = encounteredNewLine || m_encounteredNewLine;
            }

            m_encounteredNewLine = encounteredNewLine;
        }

        void SkipSpaceComment()
        {
            // reset the skipped-space flag
            m_skippedSpace = false;

            // move to the next token
            if (NextRawToken() == TokenType.Space)
            {
                // starts with whitespace! If the next token is a comment, we want to make sure that
                // whitespace is preserved. Keep going until we find something that isn't a space
                m_skippedSpace = true;
                var encounteredNewLine = m_encounteredNewLine;
                while (NextRawToken() == TokenType.Space)
                {
                    // iteration is in the condition
                    encounteredNewLine = encounteredNewLine || m_encounteredNewLine;
                }

                // now, if the first thing after space is a comment....
                if (CurrentTokenType == TokenType.Comment)
                {
                    // preserve the space character IF we're going to keep the comment.
                    // SO, if the comment mode is ALL, or if this is an important comment,
                    // (if the comment mode is hacks, then this comment will probably have already
                    // been changed into an important comment), then we output the space
                    // and the comment (don't bother outputting the comment if we already know we
                    // aren't going to)
                    if (Settings.CommentMode == CssComment.All
                        || CurrentTokenText.StartsWith("/*!", StringComparison.Ordinal))
                    {
                        Append(' ');

                        // output the comment
                        AppendCurrent();
                    }

                    // and do normal skip-space logic
                    SkipSpace();
                    encounteredNewLine = encounteredNewLine || m_encounteredNewLine;
                }

                m_encounteredNewLine = encounteredNewLine;
            }
            else if (CurrentTokenType == TokenType.Comment)
            {
                // doesn't start with whitespace.
                // append the comment and then do the normal skip-space logic
                var encounteredNewLine = m_encounteredNewLine;
                AppendCurrent();
                SkipSpace();
                m_encounteredNewLine = m_encounteredNewLine || encounteredNewLine;
            }
        }

        /// <summary>
        /// This method only skips the space that is already the current token.
        /// </summary>
        /// <returns>true if space was skipped; false if the current token is not space</returns>
        bool SkipIfSpace()
        {
            // reset the skipped-space flag
            m_skippedSpace = false;

            bool tokenIsSpace = CurrentTokenType == TokenType.Space;
            var encounteredNewLine = m_encounteredNewLine;
            // while space, keep stepping
            while (CurrentTokenType == TokenType.Space)
            {
                m_skippedSpace = true;
                NextToken();
                encounteredNewLine = encounteredNewLine || m_encounteredNewLine;
            }

            m_encounteredNewLine = encounteredNewLine;
            return tokenIsSpace;
        }

        void SkipToEndOfStatement()
        {
            bool possibleSpace = false;
            // skip to next semicolon or next block
            // AND honor opening/closing pairs of (), [], and {}
            while (!AtEof
                && (CurrentTokenType != TokenType.Character || CurrentTokenText != ";"))
            {
                // if the token is one of the characters we need to match closing characters...
                if (CurrentTokenType == TokenType.Character
                    && (CurrentTokenText == "(" || CurrentTokenText == "[" || CurrentTokenText == "{"))
                {
                    // see if this is this a block -- if so, we'll bail when we're done
                    bool isBlock = (CurrentTokenText == "{");

                    SkipToClose();

                    // if that was a block, bail now
                    if (isBlock)
                    {
                        return;
                    }
                    possibleSpace = false;
                }
                if (CurrentTokenType == TokenType.Space)
                {
                    possibleSpace = true;
                }
                else
                {
                    if (possibleSpace && NeedsSpaceBefore(CurrentTokenText)
                        && NeedsSpaceAfter(m_lastOutputString))
                    {
                        Append(' ');
                    }
                    AppendCurrent();
                    possibleSpace = false;
                }
                NextToken();
            }
        }

        void SkipToEndOfDeclaration()
        {
            bool possibleSpace = false;
            // skip to end of declaration: ; or }
            // BUT honor opening/closing pairs of (), [], and {}
            while (!AtEof
                && (CurrentTokenType != TokenType.Character
                  || (CurrentTokenText != ";" && CurrentTokenText != "}")))
            {
                // if the token is one of the characters we need to match closing characters...
                if (CurrentTokenType == TokenType.Character
                    && (CurrentTokenText == "(" || CurrentTokenText == "[" || CurrentTokenText == "{"))
                {
                    if (possibleSpace)
                    {
                        Append(' ');
                    }

                    SkipToClose();
                    possibleSpace = false;
                }

                if (CurrentTokenType == TokenType.Space)
                {
                    possibleSpace = true;
                }
                else
                {
                    if (possibleSpace && NeedsSpaceBefore(CurrentTokenText)
                        && NeedsSpaceAfter(m_lastOutputString))
                    {
                        Append(' ');
                    }

                    AppendCurrent();
                    possibleSpace = false;
                }

                m_skippedSpace = false;
                NextToken();
                if (CurrentTokenType == TokenType.Space)
                {
                    m_skippedSpace = true;
                }
            }

            // make sure we reset this flag
            m_noOutput = false;
        }

        void SkipToClose()
        {
            bool possibleSpace = false;
            string closingText;
            switch (CurrentTokenText)
            {
                case "(":
                    closingText = ")";
                    break;

                case "[":
                    closingText = "]";
                    break;

                case "{":
                    closingText = "}";
                    break;

                default:
                    throw new ArgumentException("invalid closing match");
            }

            if (m_skippedSpace && CurrentTokenText != "{")
            {
                Append(' ');
            }

            AppendCurrent();

            m_skippedSpace = false;
            NextToken();
            if (CurrentTokenType == TokenType.Space)
            {
                m_skippedSpace = true;
            }

            while (!AtEof
                && (CurrentTokenType != TokenType.Character || CurrentTokenText != closingText))
            {
                // if the token is one of the characters we need to match closing characters...
                if (CurrentTokenType == TokenType.Character
                    && (CurrentTokenText == "(" || CurrentTokenText == "[" || CurrentTokenText == "{"))
                {
                    SkipToClose();
                    possibleSpace = false;
                }

                if (CurrentTokenType == TokenType.Space)
                {
                    possibleSpace = true;
                }
                else
                {
                    if (possibleSpace && NeedsSpaceBefore(CurrentTokenText)
                        && NeedsSpaceAfter(m_lastOutputString))
                    {
                        Append(' ');
                    }

                    AppendCurrent();
                    possibleSpace = false;
                }

                m_skippedSpace = false;
                NextToken();
                if (CurrentTokenType == TokenType.Space)
                {
                    m_skippedSpace = true;
                }
            }
        }

        void SkipSemicolons()
        {
            while (CurrentTokenType == TokenType.Character && CurrentTokenText == ";")
            {
                NextToken();
            }
        }

        static bool NeedsSpaceBefore(string text)
        {
            return text == null ? false : !("{}()[],;".Contains(text));
        }

        static bool NeedsSpaceAfter(string text)
        {
            return text == null ? false : !("{}([],;:".Contains(text));
        }

        #endregion

        #region output methods

        bool AppendCurrent()
        {
            return Append(
                CurrentTokenText, 
                CurrentTokenType);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity"),
         System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1505:AvoidUnmaintainableCode"),
         System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        bool Append(object obj, TokenType tokenType)
        {
            bool outputText = false;
            bool textEndsInEscapeSequence = false;

            // if the no-output flag is true, don't output anything
            // or process value replacement comments
            if (!m_noOutput)
            {
                var parsed = m_builders.Peek();
                var text = obj.ToString();

                // first if there are replacement tokens in the settings, then we'll want to
                // replace any tokens with the appropriate replacement values
                if (Settings.ReplacementTokens.Count > 0)
                {
                    text = CommonData.ReplacementToken.Replace(text, GetReplacementValue);
                }

                if (tokenType == TokenType.Identifier || tokenType == TokenType.Dimension)
                {
                    // need to make sure invalid identifier characters are properly escaped
                    StringBuilder escapedBuilder = null;
                    try
                    {
                        var startIndex = 0;
                        var protectNextHexCharacter = false;
                        var firstIndex = 0;

                        // if the token type is an identifier, we need to make sure the first character
                        // is a proper identifier start, or is escaped. But if it's a dimension, the first
                        // character will be a numeric digit -- which wouldn't be a valid identifier. So
                        // for dimensions, skip the first character -- subsequent numeric characters will
                        // be okay.
                        if (tokenType == TokenType.Identifier)
                        {
                            // for identifiers, if the first character is a hyphen or an underscore, then it's a prefix
                            // and we want to look at the next character for nmstart.
                            firstIndex = text[0] == '_' || text[0] == '-' ? 1 : 0;

                            var isCssVariable = text.StartsWith("--") && text.Length >= 3;
                            if (isCssVariable)
                            {
                                // Custom property names and var() references are allowed to start
                                // with digits after the leading double dash, so skip generic
                                // identifier-start validation for that position.
                                firstIndex = 2;
                            }

                            if (!isCssVariable && firstIndex < text.Length)
                            {
                                // identifiers (including element names, classes, and IDs in selectors) can contain only the characters [a-zA-Z0-9] and ISO 10646 characters U+0080 and higher, plus the hyphen (-) and the underscore (_); they cannot start with a digit, two hyphens, or a hyphen followed by a digit. Identifiers can also contain escaped characters and any ISO 10646 character as a numeric code (see next item). For instance, the identifier "B&W?" may be written as "B\&W\?" or "B\26 W\3F".
                                var firstChar = text[firstIndex];

                                // anything at or above 0x80 is okay for identifiers
                                if (firstChar < 0x80)
                                {
                                    // if it's not an a-z or A-Z or underscore, we want to escape it
                                    // also leave literal back-slashes as-is, too. The identifier might start with an escape
                                    // sequence that we didn't decode to its Unicode character for whatever reason.
                                    if ((firstChar < 'A' || 'Z' < firstChar)
                                        && (firstChar < 'a' || 'z' < firstChar)
                                        && firstChar != '\\'
                                        && firstChar != '_')
                                    {
                                        // invalid first character -- create the string builder
                                        escapedBuilder = StringBuilderPool.Acquire();

                                        // if we had a prefix, output it
                                        if (firstIndex > 0)
                                            escapedBuilder.Append(text[0]);
                                        
                                        // output the escaped first character
                                        protectNextHexCharacter = EscapeCharacter(escapedBuilder, text[firstIndex]);
                                        textEndsInEscapeSequence = true;
                                        startIndex = firstIndex + 1;
                                    }
                                }
                            }
                        }
                        else
                        {
                            // for dimensions, we want to skip over the numeric part. So any sign, then decimal
                            // digits, then a decimal point (period), then decimal digits. The rest will be the identifier
                            // part that we want to escape.
                            if (text[0] == '+' || text[0] == '-')
                            {
                                ++firstIndex;
                            }

                            while ('0' <= text[firstIndex] && text[firstIndex] <= '9')
                            {
                                ++firstIndex;
                            }

                            if (text[firstIndex] == '.')
                            {
                                ++firstIndex;
                            }

                            while ('0' <= text[firstIndex] && text[firstIndex] <= '9')
                            {
                                ++firstIndex;
                            }

                            // since we start at the first character AFTER firstIndex, subtract
                            // one so we get back to the first character that isn't a part of
                            // the number portion
                            --firstIndex;
                        }

                        // loop through remaining characters, escaping any invalid nmchar characters
                        for (var ndx = firstIndex + 1; ndx < text.Length; ++ndx)
                        {
                            char nextChar = text[ndx];
                            char? prevChar = text[ndx - 1];

                            // anything at or above 0x80, then it's okay and doesnt need to be escaped
                            if (nextChar < 0x80 && prevChar != '\\')
                            {
                                // only -, _, 0-9, a-z, A-Z are allowed without escapes
                                // but we also want to NOT escape \ or space characters. If the identifier had
                                // an escaped space character, it will still be escaped -- so any spaces would
                                // be necessary whitespace for the end of unicode escapes.
                                if (nextChar == '\\')
                                {
                                    // escape characters cause the next character -- no matter what it is -- to
                                    // be part of the escape and not escaped itself. Even if this is part of a
                                    // unicode or character escape, this will hold true. Increment the index and
                                    // loop around again so that we skip over both the backslash and the following
                                    // character.
                                    ++ndx;
                                }
                                else if (nextChar != '-'
                                    && nextChar != '_'
                                    && nextChar != ' '
                                    && ('0' > nextChar || nextChar > '9')
                                    && ('a' > nextChar || nextChar > 'z')
                                    && ('A' > nextChar || nextChar > 'Z'))
                                {
                                    // need to escape this character -- create the builder if we haven't already
                                    if (escapedBuilder == null)
                                    {
                                        escapedBuilder = StringBuilderPool.Acquire();
                                    }

                                    // output any okay characters we have so far
                                    if (startIndex < ndx)
                                    {
                                        // if the first character of the unescaped string is a valid hex digit,
                                        // then we need to add a space so that characer doesn't get parsed as a
                                        // digit in the previous escaped sequence.
                                        // and if the first character is a space, we need to protect it from the
                                        // previous escaped sequence with another space, too.
                                        string unescapedSubstring = text.Substring(startIndex, ndx - startIndex);
                                        if ((protectNextHexCharacter && CssScanner.IsH(unescapedSubstring[0]))
                                            || (textEndsInEscapeSequence && unescapedSubstring[0] == ' '))
                                        {
                                            escapedBuilder.Append(' ');
                                        }

                                        escapedBuilder.Append(unescapedSubstring);
                                    }

                                    // output the escape sequence for the current character
                                    protectNextHexCharacter = EscapeCharacter(escapedBuilder, text[ndx]);
                                    textEndsInEscapeSequence = true;

                                    // update the start pointer to the next character
                                    startIndex = ndx + 1;
                                }
                            }
                        }

                        // if we escaped anything, get the text from what we built
                        if (escapedBuilder != null)
                        {
                            // append whatever is left over
                            if (startIndex < text.Length)
                            {
                                // if the first character of the unescaped string is a valid hex digit,
                                // then we need to add a space so that characer doesn't get parsed as a
                                // digit in the previous escaped sequence.
                                // same for spaces! a trailing space will be part of the escape, so if we need
                                // a real space to follow, need to make sure there are TWO.
                                string unescapedSubstring = text.Substring(startIndex);
                                if ((protectNextHexCharacter && CssScanner.IsH(unescapedSubstring[0]))
                                    || unescapedSubstring[0] == ' ')
                                {
                                    escapedBuilder.Append(' ');
                                }

                                escapedBuilder.Append(unescapedSubstring);
                                textEndsInEscapeSequence = false;
                            }

                            // get the full string
                            text = escapedBuilder.ToString();
                        }
                    }
                    finally
                    {
                        escapedBuilder.Release();
                    }
                }
                else if (tokenType == TokenType.String)
                {
                    // we need to make sure that control codes are properly escaped
                    StringBuilder sb = null;
                    try
                    {
                        var startRaw = 0;
                        for (var ndx = 0; ndx < text.Length; ++ndx)
                        {
                            // if it's a control code...
                            var ch = text[ndx];
                            if (ch < ' ')
                            {
                                // if we haven't created our string builder yet, do it now
                                if (sb == null)
                                {
                                    sb = StringBuilderPool.Acquire();
                                }

                                // add the raw text up to but not including the current character.
                                // but only if start raw is BEFORE the current index
                                if (startRaw < ndx)
                                {
                                    sb.Append(text.Substring(startRaw, ndx - startRaw));
                                }

                                // regular unicode escape
                                sb.Append("\\{0:x}".FormatInvariant(char.ConvertToUtf32(text, ndx)));

                                // if the NEXT character (if there is one) is a hex digit, 
                                // we will need to append a space to signify the end of the escape sequence, since this
                                // will never have more than two digits (0 - 1f).
                                if (ndx + 1 < text.Length
                                    && CssScanner.IsH(text[ndx + 1]))
                                {
                                    sb.Append(' ');
                                }

                                // and update the raw pointer to the next character
                                startRaw = ndx + 1;
                            }
                        }

                        // if we have something left over, add the rest now
                        if (sb != null && startRaw < text.Length)
                        {
                            sb.Append(text.Substring(startRaw));
                        }

                        // if we built up a string, use it. Otherwise just use what we have.
                        text = sb == null ? text : sb.ToString();
                    }
                    finally
                    {
                        sb.Release();
                    }
                }
                else if (tokenType == TokenType.Uri && Settings.FixIE8Fonts)
                {
                    // IE8 @font-face directive has an issue with src properties that are URLs ending with .EOT
                    // that don't have any querystring. They end up sending a malformed HTTP request to the server,
                    // which is bad for the server. So we want to automatically fix this for developers: if ANY URL
                    // ends in .EOT without a querystring parameters, just add a question mark in the appropriate 
                    // location. This fixes the IE8 issue.
                    text = s_eotIE8Fix.Replace(text, ".eot?$1");
                }

                // if it's not a comment, we're going to output it.
                // if it is a comment, we're not going to SAY we've output anything,
                // even if we end up outputting the comment
                var isImportant = false;
                outputText = (tokenType != TokenType.Comment);
                if (!outputText)
                {
                    // if the comment mode is none, we never want to output it.
                    // if the comment mode is all, then we always want to output it.
                    // otherwise we only want to output if it's an important /*! */ comment
                    if (text.StartsWith("/*!", StringComparison.Ordinal))
                    {
                        // this is an important comment. We will always output it
                        // UNLESS the comment mode is none. If it IS none, bail now.
                        if (Settings.CommentMode == CssComment.None)
                        {
                            return false;    
                        }

                        // this is an important comment that we always want to output
                        // (after we get rid of the exclamation point in some situations)
                        text = NormalizeImportantComment(text);

                        // find the index of the initial / character
                        var indexSlash = text.IndexOf('/');
                        if (indexSlash > 0)
                        {
                            // it's not the first character!
                            // the only time that should happen is if we put a line-feed in front.
                            // if the string builder is empty, or if the LAST character is a \r or \n,
                            // then trim off everything before that opening slash
                            if (lastOutputWasNewLine)
                            {
                                // trim off everything before it
                                text = text.Substring(indexSlash);
                            }
                        }
                    }
                    else if (s_sharepointReplacement.IsMatch(text))
                    {
                        // if it's a sharepoint replacement comment, then  always output it
                        // (unless settings say NO comments)
                        if (Settings.CommentMode == CssComment.None)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        // not important, and not sharepoint.
                        // check to see if it's a special value-replacement comment
                        Match match = s_valueReplacement.Match(CurrentTokenText);
                        if (match.Success)
                        {
                            m_valueReplacement = null;

                            var resourceList = Settings.ResourceStrings;
                            if (resourceList.Count > 0)
                            {
                                // it is! see if we have a replacement string
                                string id = match.Result("${id}");

                                // if we have resource strings in the settings, check each one for the
                                // id and set the value replacement field to the value.
                                // walk backwards so later objects override earlier ones.
                                for (var ndx = resourceList.Count - 1; ndx >= 0; --ndx)
                                {
                                    m_valueReplacement = resourceList[ndx][id];
                                    if (m_valueReplacement != null)
                                    {
                                        break;
                                    }
                                }
                            }

                            if (m_valueReplacement != null)
                            {
                                // we do. Don't output the comment. Instead, save the value replacement
                                // for the next time we encounter a value
                                return false;
                            }
                            else
                            {
                                // make sure the comment is normalized
                                text = NormalizedValueReplacementComment(text);
                            }
                        }
                        else if (Settings.CommentMode != CssComment.All)
                        {
                            // don't want to output, bail now
                            return false;
                        }
                    }

                    // see if it's still important
                    isImportant = text.StartsWith("/*!", StringComparison.Ordinal);
                }
                else if (m_parsingColorValue
                    && (tokenType == TokenType.Identifier || tokenType == TokenType.ReplacementToken))
                {
                    if (!text.StartsWith("#", StringComparison.Ordinal))
                    {
                        bool nameConvertedToHex = false;
                        string lowerCaseText = text.ToLowerInvariant();
                        string rgbString;

                        switch (Settings.ColorNames)
                        {
                            case CssColor.Hex:
                                // we don't want any color names in our code.
                                // convert ALL known color names to hex, so see if there is a match on
                                // the set containing all the name-to-hex values
                                if (ColorSlice.AllColorNames.TryGetValue(lowerCaseText, out rgbString))
                                {
                                    text = rgbString;
                                    nameConvertedToHex = true;
                                }
                                break;

                            case CssColor.Strict:
                                // we only want strict names in our css.
                                // convert all non-strict name to hex, AND any strict names to hex if the hex is
                                // shorter than the name. So check the set that contains all non-strict name-to-hex
                                // values and all the strict name-to-hex values where hex is shorter than name.
                                if (ColorSlice.StrictHexShorterThanNameAndAllNonStrict.TryGetValue(lowerCaseText, out rgbString))
                                {
                                    text = rgbString;
                                    nameConvertedToHex = true;
                                }
                                break;

                            case CssColor.Major:
                                // we don't care if there are non-strict color name. So check the set that only
                                // contains name-to-hex pairs where the hex is shorter than the name.
                                if (ColorSlice.HexShorterThanName.TryGetValue(lowerCaseText, out rgbString))
                                {
                                    text = rgbString;
                                    nameConvertedToHex = true;
                                }
                                break;

                            case CssColor.NoSwap:
                                // nope; leave it a name and don't swap it with the equivalent hex value
                                break;
                        }

                        // if we didn't convert the color name to hex, let's see if it is a color
                        // name -- if so, we want to make it lower-case for readability. We don't need
                        // to do this check if our color name setting is hex-only, because we would
                        // have already converted the name if we know about it
                        if (Settings.ColorNames != CssColor.Hex && !nameConvertedToHex
                            && ColorSlice.AllColorNames.TryGetValue(lowerCaseText, out rgbString))
                        {
                            // the color exists in the table, so we're pretty sure this is a color.
                            // make sure it's lower case
                            text = lowerCaseText;
                        }
                    }
                    else if (CurrentTokenType == TokenType.ReplacementToken)
                    {
                        // a replacement token is a color hash
                        var result = CrunchHexColor(text, Settings.ColorNames, m_noColorAbbreviation);
                        text = result.Color;
                    }
                }

                // if the global might-need-space flag is set and the first character we're going to
                // output if a hex digit or a space, we will need to add a space before our text
                if (m_mightNeedSpace
                    && (CssScanner.IsH(text[0]) || text[0] == ' '))
                {
                    if (lineLength >= Settings.LineBreakThreshold)
                    {
                        // we want to add whitespace, but we're over the line-length threshold, so
                        // output a line break instead
                        AddNewLine();
                    }
                    else
                    {
                        // output a space on the same line
                        parsed.Append(' ');
                        ++lineLength;
                    }
                }

                if (tokenType == TokenType.Comment && isImportant)
                {
                    // don't bother resetting line length after this because 
                    // we're going to follow the comment with another blank line
                    // and we'll reset the length at that time
                    AddNewLine();
                }

                if (text == " ")
                {
                    // we are asking to output a space character. At this point, if we are
                    // over the line-length threshold, we can substitute a line break for a space.
                    if (lineLength >= Settings.LineBreakThreshold)
                    {
                        AddNewLine();
                    }
                    else
                    {
                        // just output a space, and don't change the newline flag
                        parsed.Append(' ');
                        ++lineLength;
                    }
                }
                else
                {
                    // normal text
                    // see if we wanted to force a newline
                    if (m_forceNewLine)
                    {
                        // only output a newline if we aren't already on a new line
                        // AND we are in multiple-line mode
                        if (!lastOutputWasNewLine && Settings.OutputMode == OutputMode.MultipleLines)
	                        AddNewLine();

                        // reset the flag
                        m_forceNewLine = false;
                    }

                    parsed.Append(text);
                    lastOutputWasNewLine = false;

                    if (tokenType == TokenType.Comment && isImportant)
                    {
                        AddNewLine();
                        lineLength = 0;
                        lastOutputWasNewLine = true;
                    }
                    else
                    {
                        lineLength += text.Length;
                    }
                }

                // if the text we just output ENDS in an escape, we might need a space later
                m_mightNeedSpace = textEndsInEscapeSequence;

                // save a copy of the string so we can check the last output
                // string later if we need to
                m_lastOutputString = text;
            }

            return outputText;
        }

        string GetReplacementValue(Match match)
        {
            string tokenValue = null;
            var tokenName = match.Result("${token}");
            if (!tokenName.IsNullOrWhiteSpace())
            {
                if (!Settings.ReplacementTokens.TryGetValue(tokenName, out tokenValue))
                {
                    // no match. Check for a fallback
                    var fallbackClass = match.Result("${fallback}");
                    if (!fallbackClass.IsNullOrWhiteSpace())
                    {
                        Settings.ReplacementFallbacks.TryGetValue(fallbackClass, out tokenValue);
                    }
                }
            }

            // if we found a replacement, use it. Otherwise use a blank string to remove the token
            return tokenValue.IfNullOrWhiteSpace(string.Empty);
        }

        static bool EscapeCharacter(StringBuilder sb, char character)
        {
            // output the hex value of the escaped character. If it's less than seven digits
            // (the slash followed by six hex digits), we might
            // need to append a space before the next valid character if it is a valid hex digit.
            // (we will always need to append another space after an escape sequence if the next valid character is a space)
            var hex = "\\{0:x}".FormatInvariant((int)character);
            sb.Append(hex);
            return hex.Length < 7;
        }

        bool Append(object obj)
        {
            return Append(obj, TokenType.None);
        }

        void NewLine()
        {
	        if (lastOutputWasNewLine)
		        return;

            // if we've output something other than a newline, output one now
            if (Settings.OutputMode == OutputMode.MultipleLines)
            {
                AddNewLine();
                lineLength = 0;
                lastOutputWasNewLine = true;
            } 
            else if (Settings.OutputDeclarationWhitespace)
            {
	            Append(' ');
            }
        }

        /// <summary>
        /// Always add new line to the stream
        /// </summary>
        void AddNewLine()
        {
            if (!lastOutputWasNewLine)
            {
                var parsed = m_builders.Peek();
                parsed.Append(Settings.LineTerminator);
                if (Settings.OutputMode == OutputMode.MultipleLines)
                {
                    lineLength = this.indentLevel * this.Settings.Indent.Length;
                    for(var i = 0; i < this.indentLevel; i++)
	                    parsed.Append(this.Settings.Indent);
                }
                else
                {
                    lineLength = 0;
                }

                lastOutputWasNewLine = true;
            }
        }

        void Indent()
        {
	        this.indentLevel++;
        }

        void Unindent()
        {
	        if (this.indentLevel > 0)
		        this.indentLevel--;
        }

        #endregion

        #region color methods

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        static CssColorCrunchResult CrunchHexColor(string hexColor, CssColor colorNames, bool noAbbr)
        {
	        if (!noAbbr)
		        hexColor = s_rrggbbaa.Replace(hexColor, "#${r}${g}${b}${a}").ToLowerInvariant();

            if (colorNames == CssColor.Strict || colorNames == CssColor.Major)
            {
                // check for the hex values that can be swapped with the W3C color names to save bytes?
                //      #808080 - gray
                //      #008000 - green
                //      #800000 - maroon
                //      #000080 - navy
                //      #808000 - olive
                //      #ffa500 - orange
                //      #800080 - purple
                //      #f00    - red
                //      #c0c0c0 - silver
                //      #008080 - teal
                // (these are the only colors we can use and still validate)
                // if we don't care about validating, there are even more colors that work in all
                // major browsers that would save up some bytes. But if we convert to those names,
                // we'd really need to be able to convert back to make it validate again.
                //
                // if the map contains an entry for this color, then we
                // should use the name instead because it's smaller.
                string colorName;
                if (ColorSlice.StrictNameShorterThanHex.TryGetValue(hexColor, out colorName))
	                return new CssColorCrunchResult(true, colorName);

                if (colorNames == CssColor.Major && ColorSlice.NameShorterThanHex.TryGetValue(hexColor, out colorName))
	                return new CssColorCrunchResult(true, colorName);
            }

            return new CssColorCrunchResult(s_validHex.IsMatch(hexColor), hexColor);
        }

        static bool MightContainColorNames(string propertyName)
        {
            bool hasColor = (propertyName.EndsWith("color", StringComparison.Ordinal));
            if (!hasColor)
            {
                switch (propertyName)
                {
                    case "background":
                    case "border-top":
                    case "border-right":
                    case "border-bottom":
                    case "border-left":
                    case "border":
                    case "outline":
                        hasColor = true;
                        break;
                }
            }
            return hasColor;
        }

        #endregion

        #region Error methods

        public static string ErrorFormat(CssErrorCode errorCode)
        {
            return CssStrings.ResourceManager.GetString(errorCode.ToString(), CssStrings.Culture);
        }

        void ReportError(int severity, CssErrorCode errorNumber, CssContext context, params object[] arguments)
        {
            // guide: 0 == syntax error
            //        1 == the programmer probably did not intend to do this
            //        2 == this can lead to problems in the future.
            //        3 == this can lead to performance problems
            //        4 == this is just not right

            string message = ErrorFormat(errorNumber).FormatInvariant(arguments);
            Debug.Assert(!message.IsNullOrWhiteSpace());
            var error = new UglifyError()
                {
                    IsError = severity < 2,
                    Severity = severity,
                    Subcategory = UglifyError.GetSubcategory(severity),
                    File = FileContext,
                    ErrorNumber = (int)errorNumber,
                    ErrorCode = "CSS{0}".FormatInvariant(((int)errorNumber) & (0xffff)),
                    Message = message,
                };

            if (context != null)
            {
                error.StartLine = context.Start.Line;
                error.StartColumn = context.Start.Char;
            }

            // but warnings we want to just report and carry on
            OnCssError(error);
        }

        // just use the current context for the error
        void ReportError(int severity, CssErrorCode errorNumber, params object[] arguments)
        {
            ReportError(severity, errorNumber, m_currentToken != null ? m_currentToken.Context : null, arguments);
        }

        public event EventHandler<ContextErrorEventArgs> CssError;

        protected void OnCssError(UglifyError cssError)
        {
            if (CssError != null && cssError != null && !Settings.IgnoreAllErrors)
            {
                // if we have no errors in our error ignore list, or if we do but this error code is not in
                // that list, fire the event to whomever is listening for it.
                if (!Settings.IgnoreErrorCollection.Contains(cssError.ErrorCode))
                {
                    CssError(this, new ContextErrorEventArgs()
                        {
                            Error = cssError
                        });
                }
            }
        }

        #endregion

        #region comment methods

        /// <summary>
        /// regular expression for matching newline characters
        /// </summary>
        ////private static Regex s_regexNewlines = new Regex(
        ////    @"\r\n|\f|\r|\n",
        ////    RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

        static string NormalizedValueReplacementComment(string source)
        {
            return s_valueReplacement.Replace(source, "/*[${id}]*/");
        }

        static bool CommentContainsText(string comment)
        {
            for (var ndx = 0; ndx < comment.Length; ++ndx)
            {
                if (char.IsLetterOrDigit(comment[ndx]))
                {
                    return true;
                }
            }

            // if we get here, we didn't find any text characters
            return false;
        }

        string NormalizeImportantComment(string source)
        {
            // if this important comment does not contain any text, assume it's for a comment hack
            // and return a normalized string without the exclamation mark.
            if (CommentContainsText(source))
            {
                // first check to see if the comment is in the form /*!/ ...text... /**/
                // if so, then it's probably a part of the Opera5&NS4-only comment hack and we want
                // to make SURE that exclamation point does not get in the output because it would
                // mess up the results.
                if (source[3] == '/' && source.EndsWith("/**/", StringComparison.Ordinal))
                {
                    // it is. output the comment as-is EXCEPT without the exclamation mark
                    // (and don't put any line-feeds around it)
                    source = "/*" + source.Substring(3);
                }
            }
            else
            {
                // important comment, but it doesn't contain text. So instead, leave it inline
                // (don't add a newline character before it) but take out the exclamation mark.
                source = "/*" + source.Substring(3);
            }

            // if this is single-line mode, make sure CRLF-pairs are all converted to just CR
            if (Settings.OutputMode == OutputMode.SingleLine)
	            source = source.Replace("\r\n", "\n");

            return source;
        }
        #endregion

        #region private enums

        enum Parsed
        {
            True,
            False,
            Empty
        }

        #endregion
    }
}
