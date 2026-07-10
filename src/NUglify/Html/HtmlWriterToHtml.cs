using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NUglify.Html
{
    public class HtmlWriterToHtml : HtmlWriterBase
    {
	    protected static readonly char[] AttributeCharsForcingQuote = {' ', '\t', '\n', '\f', '\r', '"', '\'', '`', '=', '<', '>'};
	    protected readonly HtmlSettings settings;
	    protected readonly TextWriter writer;
	    protected bool isFirstWrite;

        public HtmlWriterToHtml(TextWriter writer, HtmlSettings settings = null)
        {
	        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
            this.writer.NewLine = "\n";
            this.settings = settings;
            this.isFirstWrite = true;
        }
        
        protected virtual void WriteIndent()
        {
	        for (var i = 0; i < Depth; i++)
		        writer.Write(settings.Indent);
        }

        protected override void Write(string text)
        {
            writer.Write(text);
            this.isFirstWrite = false;
        }

        protected override void Write(char c)
        {
            writer.Write(c);
            this.isFirstWrite = false;
        }

        protected override void WriteStartTag(HtmlElement node)
        {
            if (ShouldPretty(node))
            {
                writer.WriteLine();
                this.WriteIndent();
            }

            Write('<');
            var isProcessing = (node.Kind & ElementKind.ProcessingInstruction) != 0;
            if (isProcessing) 
	            Write('?');

            Write(node.Name);

            if (node.Attributes != null)
            {
                IEnumerable<HtmlAttribute> attributes = node.Attributes;

                if (settings.AlphabeticallyOrderAttributes)
                    attributes = attributes.OrderBy(a => a.Name);

                var count = node.Attributes.Count;
                var i = 0;
                foreach (var attribute in attributes)
                {
                    Write(' ');
                    WriteAttribute(node, attribute, i++ == count);
                }
            }

            if (isProcessing) 
	            Write('?');

            if ((node.Kind & ElementKind.SelfClosing) != 0 && (XmlNamespaceLevel > 0 || !settings.RemoveOptionalTags)) 
	            Write(" /");

            Write('>');
        }

        protected override void WriteEndTag(HtmlElement node)
        {
	        var descriptorName = node.Descriptor?.Name;
	        if (ShouldPretty(node.Parent) && (descriptorName == null || !settings.TagsWithNonCollapsibleWhitespaces.ContainsKey(descriptorName)))
            {
	            if (!node.Children.All(n => n is HtmlText) || settings.OutputTextNodesOnNewLine)
	            {
		            writer.WriteLine();
		            this.WriteIndent();
                }
            }

            base.WriteEndTag(node);
        }

        protected override void WriteAttributeValue(HtmlElement element, HtmlAttribute attribute, bool isLast)
        {
            var attrValue = attribute.Value;

            char quoteChar;

            if (settings.AttributeQuoteChar == null)
            {
                var quoteCount = 0;
                var doubleQuoteCount = 0;

                for (int i = 0; i < attrValue.Length; i++)
                {
                    var c = attrValue[i];
                    if (c == '\'')
                    {
                        quoteCount++;
                    }
                    else if (c == '"')
                    {
                        doubleQuoteCount++;
                    }

                    // We also count escapes so that we have an exact count for both
                    if (c == '&')
                    {
                        if (attrValue.IndexOf("&#34;", i, StringComparison.OrdinalIgnoreCase) > 0
                            || attrValue.IndexOf("&quot;", i, StringComparison.OrdinalIgnoreCase) > 0)
                        {
                            doubleQuoteCount++;
                        }
                        else if (attrValue.IndexOf("&#39;", i, StringComparison.OrdinalIgnoreCase) > 0
                                 || attrValue.IndexOf("&apos;", i, StringComparison.OrdinalIgnoreCase) > 0)
                        {
                            quoteCount++;
                        }
                    }
                }

                quoteChar = quoteCount < doubleQuoteCount ? '\'' : '"';
            }
            else
            {
                quoteChar = settings.AttributeQuoteChar.Value == '"' ? '"' : '\'';
            }

            if (quoteChar == '"')
            {
                attrValue = attrValue.Replace("&#39;", "'");
                attrValue = attrValue.Replace("&apos;", "'");
                attrValue = attrValue.Replace("\"", "&#34;");
            }
            else
            {
                attrValue = attrValue.Replace("&#34;", "\"");
                attrValue = attrValue.Replace("&quot;", "\"");
                attrValue = attrValue.Replace("'", "&#39;");
            }

            var canRemoveQuotes = settings.RemoveAttributeQuotes && attrValue != string.Empty && attrValue.IndexOfAny(AttributeCharsForcingQuote) < 0;

            if (!canRemoveQuotes)
            {
                Write(quoteChar);
            }

            Write(attrValue);

            if (!canRemoveQuotes)
            {
                Write(quoteChar);
            }
            else
            {
                bool emitSpace = false;

                if (isLast)
                {
                    if (attrValue.EndsWith("/"))
                    {
                        emitSpace = true;
                    }
                }

                if (emitSpace)
                {
                    Write(' ');
                }
            }
        }

        protected override void Write(HtmlComment node)
        {
	        if (ShouldPretty())
	        {
		        writer.WriteLine();
		        this.WriteIndent();
	        }

            Write("<!--");
	        Write(node.Slice.ToString());
	        Write("-->");
        }

        protected override void Write(HtmlDOCTYPE node)
        {
	        if (ShouldPretty())
	        {
		        writer.WriteLine();
		        this.WriteIndent();
	        }

            base.Write(node);
        }


        protected override void Write(HtmlText node)
        {
	        var descriptorName = node.Parent.Descriptor?.Name;

	        var isOnlyChild = node.Parent.FirstChild == node.Parent.LastChild && node.IsFirstChild();
            
	        var newlineForText = !isOnlyChild || settings.OutputTextNodesOnNewLine;
	        var previousNodeIsNonBreaking = node.PreviousSibling is HtmlElement e && settings.InlineTagsPreservingSpacesAround.ContainsKey(e.Descriptor?.Name ?? "null");

	        if (ShouldPretty(node.Parent) && newlineForText && (descriptorName == null || !settings.TagsWithNonCollapsibleWhitespaces.ContainsKey(descriptorName)) && !previousNodeIsNonBreaking)
	        {
		        writer.WriteLine();
		        this.WriteIndent();
                node.Slice.TrimStart();
            }
            
            base.Write(node);
        }

        protected override void Write(HtmlRaw node)
        {
	        if (ShouldPretty(node.Parent) && (node.Parent?.Name == "script" || node.Parent?.Name == "style"))
	        {
                var lines = node.Slice.ToString().Split(new [] { writer.NewLine }, StringSplitOptions.None);
		        
                for (var i = 0; i < lines.Length; i++)
                {
                    writer.WriteLine();
                    WriteIndent();
	                Write(lines[i]);
				}
            }
            else
				base.Write(node);
        }

        protected override void Write(HtmlAspDelimiter node)
        {
	        if (ShouldPretty(node.Parent))
	        {
		        writer.WriteLine();
		        this.WriteIndent();
	        }

            base.Write(node);
        }

        protected virtual bool ShouldPretty(HtmlElement node)
        {
	        if (isFirstWrite)
		        return false;

	        if (!settings.PrettyPrint)
		        return false;

	        var isFirstChild = node.Parent != null && node.Parent.FirstChild == node;
            
            if (!isFirstChild && node.Descriptor != null && settings.InlineTagsPreservingSpacesAround.ContainsKey(node.Descriptor.Name))
		        return false;
            
	        return true;
        }


        protected virtual bool ShouldPretty()
        {
	        if (isFirstWrite)
		        return false;

	        if (!settings.PrettyPrint)
		        return false;

	        return true;
        }
    }
}