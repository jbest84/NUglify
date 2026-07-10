using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUglify.Css;
using NUglify.Helpers;
using NUglify.JavaScript;

namespace NUglify.Html
{
    /// <summary>
    /// Settings for Html minification.
    /// </summary>
    public class HtmlSettings
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HtmlSettings"/> class using the most aggressive minification settings.
        /// </summary>
        public HtmlSettings()
        {
            AttributesCaseSensitive = false;
            TagsCaseSensitive = false;
            CollapseWhitespaces = true;
            RemoveComments = true;
            RemoveOptionalTags = true;
            RemoveInvalidClosingTags = true;
            RemoveEmptyAttributes = true;
            RemoveAttributeQuotes = true;
            DecodeEntityCharacters = true;
            RemoveScriptStyleTypeAttribute = true;
            ShortBooleanAttribute = true;
            IsFragmentOnly = false;
            RemoveAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            MinifyJs = true;
            MinifyJsAttributes = true;
            JsSettings = new CodeSettings();
            MinifyCss = true;
            MinifyCssAttributes = true;
            CssSettings = new CssSettings();
            Indent = "  ";
            OutputTextNodesOnNewLine = true;

            InlineTagsPreservingSpacesAround = new[]
            {
                "a",
                "abbr",
                "acronym",
                "b",
                "bdi",
                "bdo",
                "big",
                "button",
                "cite",
                "code",
                "del",
                "dfn",
                "em",
                "font",
                "i",
                "ins",
                "img",
                "kbd",
                "label",
                "mark",
                "math",
                "nobr",
                "q",
                "rp",
                "rt",
                "s",
                "samp",
                "small",
                "span",
                "strike",
                "strong",
                "sub",
                "sup",
                "svg",
                "time",
                "tt",
                "u",
                "var",
            }.ToDictionaryBool(false);

            KeepTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            TagsWithNonCollapsibleWhitespaces = new[]
            {
                "pre",
                "textarea",
            }.ToDictionaryBool(false);

            KeepCommentsRegex = new List<Regex>
            {
                new Regex(@"^!"), // Keep conditional comments
                new Regex(@"^\s?/?ko(?:[\s\-]|$)") // Keep knockout comments
            };
        }
        
        /// <summary>
        /// Gets or sets a value indicating whether to treat attributes as case sensitive.
        /// Default is <c>false</c>
        /// </summary>
        public bool AttributesCaseSensitive { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to treat tag names as case sensitive.
        /// Default is <c>false</c>
        /// </summary>
        public bool TagsCaseSensitive { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to collapse whitespaces.
        /// Default is <c>true</c>
        /// </summary>
        public bool CollapseWhitespaces { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to remove comments.
        /// Note that the KeepCommentsRegex applies when RemoveComments == true to preserve the specified comments
        /// Default is <c>true</c>
        /// </summary>
        public bool RemoveComments { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to remove optional tags (e.g: &lt;/p&gt; or &lt;/li&gt;).
        /// Default is <c>true</c>
        /// </summary>
        public bool RemoveOptionalTags { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to remove invalid closing tags (tags with only a end tag and a missing start tag)
        /// Default is <c>true</c>
        /// </summary>
        public bool RemoveInvalidClosingTags { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to remove empty attributes with whitespace only characters.
        /// Default is <c>true</c>
        /// </summary>
        public bool RemoveEmptyAttributes { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to remove the quotes around attributes when possible.
        /// Default is <c>true</c>
        /// </summary>
        public bool RemoveAttributeQuotes { get; set; }

        [Obsolete("Use RemovedAttributeQuotes instead")]
        public bool RemoveQuotedAttributes
        {
	        get => this.RemoveAttributeQuotes;
	        set => this.RemoveAttributeQuotes = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether to decode entity characters to their shorter character equivalents.
        /// Default is <c>true</c>
        /// </summary>
        public bool DecodeEntityCharacters { get; set; }

        /// <summary>
        /// Gets or sets the quote character used for attribute values. Default is null, meaning that it will let the minifier decide which is best.
        /// Default is <c>null</c>
        /// </summary>
        public char? AttributeQuoteChar { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to remove script/style type attribute.
        /// Default is <c>true</c>
        /// </summary>
        public bool RemoveScriptStyleTypeAttribute { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to use the short version of a boolean attribute if value is true.
        /// Default is <c>true</c>
        /// </summary>
        public bool ShortBooleanAttribute { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the parsing is occuring on an HTML fragment to avoid creating missing tags (like html, body, head).
        /// Default is <c>false</c>
        /// </summary>
        public bool IsFragmentOnly { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether minify js inside &lt;script&gt; tags.
        /// Default is <c>true</c> using <see cref="Uglify.Js(string, CodeSettings)"/>
        /// </summary>
        public bool MinifyJs { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether minify js inside JS event attributes (e.g. onclick, onfocus).
        /// Default is <c>true</c> using <see cref = "Uglify.Js(string, CodeSettings)" />
        /// </summary>
        public bool MinifyJsAttributes { get; set; }

        /// <summary>
        /// Gets or sets the minify js settings.
        /// </summary>
        public CodeSettings JsSettings { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether minify css inside &lt;style&gt; tags.
        /// Default is <c>true</c> using <see cref="Uglify.Css(string, Css.CssSettings, CodeSettings)"/>
        /// </summary>
        public bool MinifyCss { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether minify css inside style attribute.
        /// Default is <c>true</c>
        /// </summary>
        public bool MinifyCssAttributes { get; set; }

        /// <summary>
        /// Gets or sets the minify css settings.
        /// </summary>
        public CssSettings CssSettings{ get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to output an indented html. See Indent property
        /// Default is <c>false</c>
        /// </summary>
        public bool PrettyPrint { get; set; }

        /// <summary>
        /// When PrettyPrint is true, should text nodes be outputted on their own line, or within their parent element
        /// /// Default is <c>true</c>
        /// </summary>
        public bool OutputTextNodesOnNewLine { get; set; }

        /// <summary>
        /// The string used for one level of indent (e.g. a tab or 4 spaces)
        /// Default: two spaces
        /// </summary>
        public string Indent { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to remove any JavaScript code (in script tag or in html attributes)
        /// Default is <c>false</c>
        /// </summary>
        public bool RemoveJavaScript { get; set; }

        /// <summary>
        /// Gets the inline tags preserving spaces around
        /// Default: a, abbr, acronym, b, bdi, bdo, big, button, cite, code, del, dfn, em, font, i, ins, kbd, label, mark, math, nobr, q, rp, rt, s, samp, small, span, strike, strong, sub, sup,  svg, time, tt, u, var
        /// </summary>
        public Dictionary<string, bool> InlineTagsPreservingSpacesAround { get; }

        /// <summary>
        /// Gets or sets a value indicating whether to keep one space when collapsing multiple adjacent whitespace characters.
        /// </summary>
        /// <value><c>true</c> to keep one space when collapsing; otherwise, <c>false</c>.</value>
        /// Default: false
        public bool KeepOneSpaceWhenCollapsing { get; set; }

        /// <summary>
        /// Gets the tags with non collapsible whitespaces
        /// Default: pre, textarea
        /// </summary>
        [Obsolete("Use TagsWithNonCollapsibleWhitespaces (correct spelling) instead")]
        public Dictionary<string, bool> TagsWithNonCollapsableWhitespaces => this.TagsWithNonCollapsibleWhitespaces;

        /// <summary>
        /// Gets the tags with non collapsible whitespaces
        /// Default: pre, textarea
        /// </summary>
        public Dictionary<string, bool> TagsWithNonCollapsibleWhitespaces { get; }

        /// <summary>
        /// Gets a list of regex that will be matched against a HTML comment content. If a regex matches a HTML comment content, the comment will be kept
        /// Only apllies when RemoveComments == true
        /// Default: Conditional and knockout comments
        /// </summary>
        public List<Regex> KeepCommentsRegex { get; }

        /// <summary>
        /// Gets the list of tags that will be kept even if they have an optional start/end tag.
        /// Default: none
        /// </summary>
        public HashSet<string> KeepTags { get; }

        /// <summary>
        /// A list of attributes (names) to be removed from all tags
        /// Default: none
        /// </summary>
        public HashSet<string> RemoveAttributes { get; }

        /// <summary>
        /// Output the attributes of each element in alphabetical order or the order they were in the source HTML
        /// Default: false
        /// </summary>
        public bool AlphabeticallyOrderAttributes { get; set; }

        /// <summary>
        /// Returns settings to output pretty/formatted HTML
        /// </summary>
        public static HtmlSettings Pretty()
        {
	        var htmlSettings = new HtmlSettings
	        {
		        RemoveComments = false,
		        RemoveOptionalTags = false,
		        RemoveAttributeQuotes = false,
		        MinifyJs = false,
		        MinifyCss = false,
		        PrettyPrint = true
	        };
	        htmlSettings.JsSettings = CodeSettings.Pretty();
            htmlSettings.CssSettings = CssSettings.Pretty();
	        return htmlSettings;
        }
    }
}