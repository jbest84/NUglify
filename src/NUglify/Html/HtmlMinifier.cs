// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using NUglify.Css;
using NUglify.Helpers;
using NUglify.JavaScript;

namespace NUglify.Html
{
    public class HtmlMinifier
    {
        readonly HtmlDocument html;
        int pendingTagNonCollapsibleWithSpaces;
        readonly List<HtmlText> pendingTexts;
        readonly HtmlSettings settings;
        int xmlNamespaceCount;

        static readonly Dictionary<string, bool> AttributesRemovedIfEmpty = new[]
        {
            "class", "id", "style", "title", "lang", "dir",
            "onafterprint", "onbeforeprint", "onbeforeunload", "onerror", "onhashchange", "onload", "onmessage", "onoffline", "ononline", "onpagehide", "onpageshow", "onpopstate", "onresize", "onstorage", "onunload", "onblur", "onchange", "oncontextmenu", "onfocus", "oninput", "oninvalid", "onreset", "onsearch", "onselect", "onsubmit", "onkeydown", "onkeypress", "onkeyup", "onclick", "ondblclick", "onmousedown", "onmousemove", "onmouseout", "onmouseover", "onmouseup", "onmousewheel", "onwheel", "ondrag", "ondragend", "ondragenter", "ondragleave", "ondragover", "ondragstart", "ondrop", "onscroll", "oncopy", "oncut", "onpaste", "onabort", "oncanplay", "oncanplaythrough", "oncuechange", "ondurationchange", "onemptied", "onended", "onerror", "onloadeddata", "onloadedmetadata", "onloadstart", "onpause", "onplay", "onplaying", "onprogress", "onratechange", "onseeked", "onseeking", "onstalled", "onsuspend", "ontimeupdate", "onvolumechange", "onwaiting", "ontoggle"
        }.ToDictionaryBool(false);

        static readonly Dictionary<string, bool> ScriptAttributes = new [] { "onafterprint", "onbeforeprint", "onbeforeunload", "onerror", "onhashchange", "onload", "onmessage", "onoffline", "ononline", "onpagehide", "onpageshow", "onpopstate", "onresize", "onstorage", "onunload", "onblur", "onchange", "oncontextmenu", "onfocus", "oninput", "oninvalid", "onreset", "onsearch", "onselect", "onsubmit", "onkeydown", "onkeypress", "onkeyup", "onclick", "ondblclick", "onmousedown", "onmousemove", "onmouseout", "onmouseover", "onmouseup", "onmousewheel", "onwheel", "ondrag", "ondragend", "ondragenter", "ondragleave", "ondragover", "ondragstart", "ondrop", "onscroll", "oncopy", "oncut", "onpaste", "onabort", "oncanplay", "oncanplaythrough", "oncuechange", "ondurationchange", "onemptied", "onended", "onerror", "onloadeddata", "onloadedmetadata", "onloadstart", "onpause", "onplay", "onplaying", "onprogress", "onratechange", "onseeked", "onseeking", "onstalled", "onsuspend", "ontimeupdate", "onvolumechange", "onwaiting", "ontoggle"}.ToDictionaryBool(false);

        public bool HasErrors { get; private set; }

        public List<UglifyError> Errors { get; private set; }

        public HtmlMinifier(HtmlDocument html, HtmlSettings settings = null)
        {
	        this.html = html ?? throw new ArgumentNullException(nameof(html));

	        this.settings = settings ?? new HtmlSettings();
            
	        pendingTexts = new List<HtmlText>();
	        Errors = new List<UglifyError>();
        }

        public void Minify()
        {
            ProcessChildren(html);
            if (settings.CollapseWhitespaces)
            {
                TrimPendingTextNodes();
            }
        }

        void ProcessNode(HtmlNode node)
        {
            var element = node as HtmlElement;
            bool isInXml = element?.Descriptor != null && (element.Descriptor.Category & ContentKind.Xml) != 0;
            if (isInXml)
	            xmlNamespaceCount++;

            // If we have a rogue closing tag, just remove it and pretend we never encountered it
            if (settings.RemoveInvalidClosingTags && element?.Kind == ElementKind.Closing)
            {
                element.Remove();
            }
            else
            {
                TrimNodeOnStart(node);

                bool isContentNonCollapsible = false;
                if (element != null && settings.TagsWithNonCollapsibleWhitespaces.ContainsKey(element.Name))
                {
                    pendingTagNonCollapsibleWithSpaces++;
                    isContentNonCollapsible = true;
                }

                ProcessChildren(node);

                TrimNodeOnEnd(node);

                if (isContentNonCollapsible)
	                pendingTagNonCollapsibleWithSpaces--;
            }

            if (isInXml)
	            xmlNamespaceCount--;
        }

        void ProcessChildren(HtmlNode node)
        {
            foreach (var subNode in node.Children)
	            ProcessNode(subNode);
        }

        void TrimNodeOnStart(HtmlNode node)
        {
            var textNode = node as HtmlText;
            if (textNode != null)
	            TrimNodeOnStart(textNode);

            if (node is HtmlComment && settings.RemoveComments)
            {
                var comment = (HtmlComment) node;
                bool keepComment = false;
                foreach (var regex in settings.KeepCommentsRegex)
                {
                    var result = regex.Match(comment.Slice.Text, comment.Slice.Start, comment.Slice.Length);
                    if (result.Success)
                    {
                        keepComment = true;
                        break;
                    }
                }

                // Don't remove special ignoring comments
                if (!keepComment)
	                node.Remove();
            }

            // Remove HTML script
            if (settings.RemoveJavaScript && node is HtmlElement && ((HtmlElement) node).Descriptor?.Name == "script")
	            node.Remove();

            // If current node requires preserving formatting inside it we need to trim all pending text node that we collected before
            var nodeName = node is HtmlElement ? ((HtmlElement) node).Name : null;
            if (settings.CollapseWhitespaces && !string.IsNullOrEmpty(nodeName) && settings.TagsWithNonCollapsibleWhitespaces.ContainsKey(nodeName))
	            TrimPendingTextNodes();
        }

        void TrimNodeOnEnd(HtmlNode node)
        {
            if (node is HtmlElement)
	            TrimNodeOnEnd((HtmlElement) node);
        }

        void TrimNodeOnStart(HtmlText textNode)
        {
            // If we need to decode entities
            if (settings.DecodeEntityCharacters)
            {
                if (textNode.Slice.IndexOf('&') >= 0)
                {
                    var text = textNode.Slice.ToString();
                    textNode.Slice = new StringSlice(EntityHelper.Unescape(text));
                }
            }

            // If we don't do anything for TextNode, we can early exit
            if (!settings.CollapseWhitespaces)
	            return;

            // Find the first non-transparent parent
            var parent = textNode.Parent;
            while (parent != null && (parent.Descriptor == null || parent.Descriptor.Category == ContentKind.Transparent))
            {
                parent = parent.Parent;
            }

            if (!textNode.Slice.IsEmptyOrWhiteSpace() || (parent?.Descriptor != null && xmlNamespaceCount == 0))
            {
                pendingTexts.Add(textNode);
            }
            else
            {
                textNode.Remove();
            }
        }

        void TrimNodeOnEnd(HtmlElement element)
        {
            // If the element is a valid HTML descriptor, we can safely turn-it all lowercase
            if (xmlNamespaceCount == 0 && !settings.TagsCaseSensitive)
	            element.Name = element.Name.ToLowerInvariant();

            // If the element being visited is not an inline tag, we need to clear the previous text node
            if (settings.CollapseWhitespaces && !settings.InlineTagsPreservingSpacesAround.ContainsKey(element.Name) && element.Descriptor != null && element.Kind != ElementKind.SelfClosing)
	            TrimPendingTextNodes();

            if (element.Attributes != null)
            {
                for (int i = element.Attributes.Count - 1; i >= 0; i--)
                {
                    var attribute = element.Attributes[i];
                    if (TrimAttribute(element, attribute))
                    {
                        element.Attributes.RemoveAt(i);
                    }
                }
            }

            // Remove optional tags
            if (settings.RemoveOptionalTags && !settings.KeepTags.Contains(element.Name))
            {
                var nextElement = element.FindNextSibling<HtmlElement>();

                // Handle end tag omit
                var canOmitEndTag = element.Descriptor?.CanOmitEndTag;
                if (element.Kind == ElementKind.OpeningClosing && canOmitEndTag != null && canOmitEndTag(element, nextElement, false))
                {
                    element.Kind = ElementKind.Opening;
                }

                // Handle start tag omit
                var canOmitStartTag = element.Descriptor?.CanOmitStartTag;
                if ((element.Attributes == null || element.Attributes.Count == 0) && element.Kind == ElementKind.Opening && canOmitStartTag != null && canOmitStartTag(element, nextElement, false))
                {
                    element.Kind = ElementKind.None;
                }
            }

            var scriptOrStyle = GetElementMinificationType(element);
            if (scriptOrStyle != HtmlScriptStyleElement.NA)
	            TrimScriptOrStyle(element, scriptOrStyle);
        }

        void TrimPendingTextNodes()
        {
            if (pendingTagNonCollapsibleWithSpaces == 0)
            {
                HtmlText previousTextNode = null;
                HtmlText firstTextNode = null;
                for (int i = 0; i < pendingTexts.Count; i++)
                {
                    var textNode = pendingTexts[i];
                    if (firstTextNode == null)
                    {
                        firstTextNode = textNode;
                    }

                    var previousElement = textNode.PreviousSibling as HtmlElement;
                    var nextElement = textNode.NextSibling as HtmlElement;

                    var isPreviousElementPreservingSpace = previousElement != null &&
                                                           settings.InlineTagsPreservingSpacesAround.ContainsKey(
                                                               previousElement.Name);
                    var isNextElementPreservingSpace = nextElement != null &&
                                                       settings.InlineTagsPreservingSpacesAround.ContainsKey(nextElement
                                                           .Name);

                    // If we expect to keep one space after collapsing
                    var isFirstText = textNode == firstTextNode;
                    var isLastText = i + 1 == pendingTexts.Count;
                    if (!settings.KeepOneSpaceWhenCollapsing || isFirstText || isLastText)
                    {
                        var isPreviousTrailing = previousTextNode != null && previousTextNode.Slice.HasTrailingSpaces();
                        var isNextStartsBySpace = i + 1 >= pendingTexts.Count ||
                                                  pendingTexts[i + 1].Slice.StartsBySpace();

                        // We can trim the heading whitespaces if:
                        // - we don't have a previous element (either inline or parent container)
                        // - OR the previous element (sibling or parent) is not a tag that require preserving spaces around
                        // - OR the previous text node has already some trailing spaces
                        if (!isPreviousElementPreservingSpace && (previousTextNode == null || isPreviousTrailing) &&
                            (previousElement == null || isFirstText))
                        {
                            textNode.Slice.TrimStart();
                        }

                        // We can trim the traling whitespaces if:
                        // - we don't have a next element (either inline or parent container)
                        // - OR the next element (sibling or parent) is not a tag that require preserving spaces around
                        if (!isNextElementPreservingSpace && isNextStartsBySpace)
                        {
                            textNode.Slice.TrimEnd();
                        }
                    }

                    // If we are not in the context of a tag that doesn't accept to collapse whitespaces, 
                    // we can collapse them for this text node
                    textNode.Slice.CollapseSpaces();

                    // If the text node is empty, remove it from the tree
                    if (textNode.Slice.IsEmpty() || (textNode.Slice.IsEmptyOrWhiteSpace() &&
                                                     !isPreviousElementPreservingSpace &&
                                                     !isNextElementPreservingSpace))
                    {
                        textNode.Remove();
                        if (firstTextNode == textNode)
                        {
                            firstTextNode = null;
                        }
                    }
                    else
                    {
                        // Replace the previous textnode
                        previousTextNode = textNode;
                    }
                }

                // Trim any trailing spaces of the last known text node if we are moving to a block level
                if (previousTextNode != null && previousTextNode.NextSibling == null)
                {
                    previousTextNode.Slice.TrimEnd();
                    if (previousTextNode.Slice.IsEmpty())
                    {
                        previousTextNode.Remove();
                    }
                }
            }

            pendingTexts.Clear();
        }
        
        void TrimScriptOrStyle(HtmlElement element, HtmlScriptStyleElement type)
        {
            if (settings.RemoveScriptStyleTypeAttribute && type != HtmlScriptStyleElement.JSON) 
	            element.RemoveAttribute("type");

            if (type != HtmlScriptStyleElement.Style && !settings.MinifyJs)
	            return;

            if (type == HtmlScriptStyleElement.Style && !settings.MinifyCss)
	            return;

            var raw = element.FirstChild as HtmlRaw;
            if (raw == null)
                return;

            var slice = raw.Slice;

            slice.TrimStart();
            slice.TrimEnd();

            // If the text has a comment or CDATA, we won't try to minify it
            if (slice.StartsWith("<!--") || slice.StartsWithIgnoreCase("<![CDATA["))
	            return;

            var text = slice.ToString();

            var textMinified = MinifyJsOrCss(text, type);
            if (textMinified == null)
                return;

            raw.Slice = new StringSlice(textMinified);
        }
        
        string MinifyJsOrCss(string text, HtmlScriptStyleElement type)
        {
	        UglifyResult result;

	        if (type == HtmlScriptStyleElement.Style)
	        {
		        result = Uglify.Css(text, "inner_css", settings.CssSettings);
            }
            else if (type == HtmlScriptStyleElement.Javascript)
	        {
		        result = Uglify.Js(text, "inner_js", settings.JsSettings);
            }
            else if (type == HtmlScriptStyleElement.JSON)
	        {
		        var jsonSettings = settings.JsSettings.Clone();
		        jsonSettings.Format = JavaScriptFormat.JSON;
		        jsonSettings.SourceMode = JavaScriptSourceMode.Expression;
		        jsonSettings.IgnoreConditionalCompilation = true;
		        jsonSettings.KillSwitch |= (long)TreeModifications.BooleanLiteralsToNotOperators;
		        result = Uglify.Js(text, "inner_js", jsonSettings);
	        }
	        else
	        {
		        return text;
            }

	        if (result.Errors != null)
                Errors.AddRange(result.Errors);

            if (result.HasErrors)
            {
                HasErrors = true;
                return text;
            }

            return result.Code;
        }

        string MinifyCssAttribute(string text)
        {
	        var attributeCssSettings = this.settings.CssSettings.Clone();
	        attributeCssSettings.CssType = CssType.DeclarationList;
	        attributeCssSettings.OutputMode = OutputMode.SingleLine;
	        attributeCssSettings.OutputDeclarationWhitespace = this.settings.PrettyPrint;

            var result = Uglify.Css(text, "inner_css", attributeCssSettings);
            if (result.Errors != null)
                Errors.AddRange(result.Errors);

            if (result.HasErrors)
            {
                HasErrors = true;
                return text;
            }

            return result.Code;
        }

        string MinifyJsAttribute(string text)
        {
	        var attributeJsSettings = this.settings.JsSettings.Clone();
	        attributeJsSettings.SourceMode = JavaScriptSourceMode.EventHandler;
            var result = Uglify.Js(text, attributeJsSettings);
	        if (result.Errors != null) 
		        Errors.AddRange(result.Errors);

	        if (result.HasErrors)
	        {
		        HasErrors = true;
		        return text;
	        }

	        return result.Code;
        }
        
        bool TrimAttribute(HtmlElement element, HtmlAttribute attribute)
        {
            var tag = element.Name.ToLowerInvariant();
            var attributeNameLower = attribute.Name.ToLowerInvariant();
            var attr = attributeNameLower;

            if (settings.RemoveAttributes.Contains(attribute.Name))
                return true;

            if (attribute.Value != null)
            {
                if (settings.RemoveJavaScript)
                {
                    if (ScriptAttributes.ContainsKey(attr) || attribute.Value.Trim().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
	                    return true;
                }

                if (IsUriTypeAttribute(element.Name, attribute.Name))
                {
                    attribute.Value = attribute.Value.Trim();
                    return false;
                }
                if (attr == "class")
                {
                    attribute.Value = attribute.Value.Trim();
                    //if (options.sortClassName)
                    //{
                    //    attrValue = options.sortClassName(attrValue);
                    //}
                    // else
                    {
                        attribute.Value = CharHelper.CollapseWhitespaces(attribute.Value);
                    }
                    return attribute.Value == string.Empty;
                }

                if (attr == "style" && element.Descriptor != null && settings.MinifyCssAttributes)
                {
	                attribute.Value = MinifyCssAttribute(attribute.Value);
	                return attribute.Value == string.Empty;
                }

                if (settings.MinifyJsAttributes && ScriptAttributes.ContainsKey(attr))
                {
	                attribute.Value = MinifyJsAttribute(attribute.Value);
	                return attribute.Value == string.Empty;
                }
            }

            if (settings.ShortBooleanAttribute && attribute.Value == "true" && attributeNameLower != "value" && attributeNameLower != "aria-hidden")
            {
                attribute.Value = null;
            }

            if (settings.RemoveEmptyAttributes)
            {
                if ((attribute.Value != null || (attribute.Value == null && AttributesRemovedIfEmpty.ContainsKey(attr))) && attribute.Value.IsNullOrWhiteSpace())
                {
                    attribute.Value = string.Empty;

                    return (tag == "input" && attr == "value") || AttributesRemovedIfEmpty.ContainsKey(attr);
                }
            }

            if (!settings.AttributesCaseSensitive && xmlNamespaceCount == 0)
            {
                attribute.Name = attr;
            }

            return false;
        }

        static HtmlScriptStyleElement GetElementMinificationType(HtmlElement element)
        {
            if (element.Name.Equals("script", StringComparison.OrdinalIgnoreCase))
	            return GetJavascriptElementMinificationType(element.FindAttribute("type")?.Value);

            if (element.Name.Equals("style", StringComparison.OrdinalIgnoreCase) && IsAttributeValueCss(element.FindAttribute("type")?.Value))
	            return HtmlScriptStyleElement.Style;

            return HtmlScriptStyleElement.NA;
        }

        static HtmlScriptStyleElement GetJavascriptElementMinificationType(string value)
        {
	        if (string.IsNullOrEmpty(value))
		        return HtmlScriptStyleElement.Javascript;
            
			var text = value.Split(';')[0].ToLowerInvariant();
			switch (text)
			{
				case "text/javascript":
				case "text/ecmascript":
				case "text/jscript":
				case "application/javascript":
				case "application/x-javascript":
				case "application/ecmascript":
				return HtmlScriptStyleElement.Javascript;
				case "application/ld+json":
				case "application/json":
                    return HtmlScriptStyleElement.JSON;
	        }

			return HtmlScriptStyleElement.NA;
        }

        static bool IsAttributeValueCss(string value)
        {
            return string.IsNullOrEmpty(value) || value.Equals("text/css", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsUriTypeAttribute(string tag, string attr)
        {
            // Code from https://github.com/kangax/html-minifier/blob/gh-pages/src/htmlminifier.js
            return ((tag == "a" || tag == "area" || tag == "base") && attr == "href") ||
                   (tag == "img" && (attr == "src" || attr == "longdesc" || attr == "usemap")) ||
                   (tag == "object" &&
                    (attr == "classid" || attr == "codebase" || attr == "data" || attr == "usemap")) ||
                   (tag == "q" && attr == "cite") ||
                   (tag == "blockquote" && attr == "cite") ||
                   ((tag == "ins" || tag == "del") && attr == "cite") ||
                   (tag == "form" && attr == "action") ||
                   (tag == "input" && (attr == "src" || attr == "usemap")) ||
                   (tag == "head" && attr == "profile") ||
                   (tag == "script" && (attr == "src" || attr == "for"));
        }
    }
}