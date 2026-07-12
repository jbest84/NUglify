// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.IO;

namespace NUglify.Html
{
    /// <summary>
    /// Class responsible from extracting only text nodes from an HTML document, used by <see cref="Uglify.HtmlToText"/> function.
    /// </summary>
    public class HtmlWriterToText : HtmlWriterBase
    {
	    bool outputEnabled;
	    bool documentHasBody;
	    int bodyDepth;
	    int headDepth;
	    bool previousOutputEndedWithWhitespace;
	    readonly HtmlToTextOptions options;

	    public TextWriter Writer { get; }

	    bool ShouldKeepStructure => (options & HtmlToTextOptions.KeepStructure) != 0;
        bool ShouldKeepFormatting => (options & HtmlToTextOptions.KeepFormatting) != 0;
        bool ShouldKeepHtmlEscape => (options & HtmlToTextOptions.KeepHtmlEscape) != 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="HtmlWriterToText"/> class.
        /// </summary>
        /// <param name="writer">The writer.</param>
        /// <param name="options">The options.</param>
        /// <exception cref="System.ArgumentNullException">if writer is null</exception>
        public HtmlWriterToText(TextWriter writer, HtmlToTextOptions options)
        {
	        Writer = writer ?? throw new ArgumentNullException(nameof(writer));
	        this.options = options;
        }

        void UpdateOutputEnabled()
        {
            outputEnabled = bodyDepth > 0 || (!documentHasBody && headDepth == 0);
        }

        protected override void Write(string text)
        {
            if (!outputEnabled)
            {
                return;
            }

            if (ShouldKeepFormatting || !ShouldKeepHtmlEscape)
            {
                text = text.Replace("&lt;", "<");
                text = text.Replace("&amp;", "&");
            }

            if (ShouldKeepStructure)
            {
                Writer.Write(text);
                previousOutputEndedWithWhitespace = text.Length > 0 && char.IsWhiteSpace(text[text.Length - 1]);
                return;
            }

            foreach (var c in text)
            {
                WriteNormalizedChar(c);
            }
        }

        protected override void Write(char c)
        {
            if (!outputEnabled)
            {
                return;
            }

            if (ShouldKeepStructure)
            {
                Writer.Write(c);
                previousOutputEndedWithWhitespace = char.IsWhiteSpace(c);
                return;
            }

            WriteNormalizedChar(c);
        }

        void WriteNormalizedChar(char c)
        {
            if (c.IsSpace())
            {
                if (!previousOutputEndedWithWhitespace)
                {
                    Writer.Write(' ');
                    previousOutputEndedWithWhitespace = true;
                }
            }
            else
            {
                Writer.Write(c);
                previousOutputEndedWithWhitespace = false;
            }
        }

        protected override void Write(HtmlCDATA node)
        {
        }

        protected override void Write(HtmlComment node)
        {
        }

        protected override void Write(HtmlDOCTYPE node)
        {
        }

        protected override void WriteStartTag(HtmlElement node)
        {
            if (node.Name == "head")
            {
                headDepth++;
                UpdateOutputEnabled();
            }

            if (node.Name == "body")
            {
                bodyDepth++;
                UpdateOutputEnabled();
            }
            else if (ShouldKeepFormatting && node.Descriptor != null && (node.Descriptor.Category & ContentKind.Phrasing) != 0)
            {
                base.WriteStartTag(node);
            } else if (ShouldKeepStructure && node.Descriptor != null && node.Descriptor.Name == "br")
            {
                Write('\n');
            }
        }

        protected override void Write(HtmlRaw node)
        {
        }

        protected override void WriteEndTag(HtmlElement node)
        {
	        if (node.Name == "body")
	        {
		        bodyDepth--;
		        UpdateOutputEnabled();
	        }
	        else if (node.Name == "head")
	        {
		        headDepth--;
		        UpdateOutputEnabled();
	        }
	        else if ((node.Descriptor == null || (node.Descriptor.Category & ContentKind.Phrasing) == 0 ||
                node.Name == "li"))
            {
                Write('\n');
            }
	        else if (ShouldKeepFormatting && node.Descriptor != null && (node.Descriptor.Category & ContentKind.Phrasing) != 0)
            {
                base.WriteEndTag(node);
            }
        }

        protected override void WriteChildren(HtmlNode node)
        {
            if (node is HtmlDocument)
            {
                documentHasBody = false;
                bodyDepth = 0;
                headDepth = 0;
                previousOutputEndedWithWhitespace = false;

                foreach (var descendant in node.FindAllDescendants())
                {
                    if (descendant is HtmlElement element && element.Name == "body")
                    {
                        documentHasBody = true;
                        break;
                    }
                }

                UpdateOutputEnabled();
            }

            base.WriteChildren(node);
        }
    }
}
