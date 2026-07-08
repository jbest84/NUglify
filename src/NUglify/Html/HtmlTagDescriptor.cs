// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using NUglify.Helpers;

namespace NUglify.Html
{
    public class HtmlTagDescriptor
    {
        public delegate bool AcceptContentDelegate(HtmlElement parent, HtmlElement child, ref HtmlTagDescriptor childDescriptor);

        /// <summary>
        /// A delegate to check whether an end tag can be omitted.
        /// </summary>
        /// <param name="tag">The tag for which we want to test whether an end tag can be omitted.</param>
        /// <param name="nextSibling">The next sibling, or null if it is the last tag of its parent.</param>
        /// <param name="whileParsing">True when the parser is still consuming input; false when checking a completed tree.</param>
        /// <returns><c>true</c> if the end tag can be omitted; <c>false</c> otherwise.</returns>
        public delegate bool CanOmitDelegate(HtmlElement tag, HtmlElement nextSibling, bool whileParsing);

        public HtmlTagDescriptor(string name, ContentKind category, string[] parentTags, ContentKind parentKind, string[] acceptTags, ContentKind acceptContent = ContentKind.None, TagEndKind endKind = TagEndKind.Required, CanOmitDelegate canOmitEndTag = null, CanOmitDelegate canOmitStartTag = null)
        {
            Name = name;
            Category = category;
            ParentTags = parentTags;
            ParentKind = parentKind;
            AcceptContent = acceptContent;
            AcceptContentTags = acceptTags;
            EndKind = endKind;
            CanOmitStartTag = canOmitStartTag;
            CanOmitEndTag = canOmitEndTag;
        }

        public string Name { get; }

        public string[] ParentTags { get; }

        public ContentKind ParentKind { get; }

        public ContentKind Category { get; }

        public ContentKind AcceptContent { get; }

        public string[] AcceptContentTags { get; }

        public TagEndKind EndKind { get; }

        public CanOmitDelegate CanOmitStartTag { get; }

        public CanOmitDelegate CanOmitEndTag { get; }

        public bool AcceptParent(HtmlTagDescriptor parentDescriptor)
        {
            if (parentDescriptor == null)
            {
                return true;
            }

            //return ((ParentKind & parentDescriptor.Category) != 0)
            //       || (ParentTags != null && Array.IndexOf(ParentTags, Name) >= 0);

            return (Category & parentDescriptor.AcceptContent) != 0
                   || parentDescriptor.AcceptContent == ContentKind.Any
                   || (parentDescriptor.AcceptContentTags != null &&
                       Array.IndexOf(parentDescriptor.AcceptContentTags, Name) >= 0);
        }

        public bool TryAcceptContent(HtmlElement parent, ContentKind parentAcceptContentType, HtmlElement child)
        {
            if (parent.Descriptor == null || child.Descriptor == null)
            {
                return true;
            }

            return ((child.Descriptor.ParentKind & parent.Descriptor.Category) != 0)
                   || (child.Descriptor.ParentTags != null && Array.IndexOf(child.Descriptor.ParentTags, child.Name) >= 0);
            //return (childDescriptor.Category & parentAcceptContentType) != 0
            //       ||
            //       (parentDescriptor.AcceptContentTags != null &&
            //        parentDescriptor.AcceptContentTags.ContainsKey(child.Name));
        }

        public static HtmlTagDescriptor Find(string tag)
		{
			HtmlTagDescriptor tagDesc;
			TagDescriptors.TryGetValue(tag, out tagDesc);
			return tagDesc;
		}

        static readonly Dictionary<string, HtmlTagDescriptor> TagDescriptors = new Dictionary<string, HtmlTagDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = new HtmlTagDescriptor("a", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Interactive | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Transparent),
            ["abbr"] = new HtmlTagDescriptor("abbr", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["address"] = new HtmlTagDescriptor("address", ContentKind.Flow | ContentKind.Palpable, null, ContentKind.Flow, null, ContentKind.Flow),
            ["area"] = new HtmlTagDescriptor("area", ContentKind.Flow | ContentKind.Phrasing, null, ContentKind.Phrasing, null, ContentKind.None, TagEndKind.AutoSelfClosing),
            ["article"] = new HtmlTagDescriptor("article", ContentKind.Flow | ContentKind.Sectioning | ContentKind.Palpable, null, ContentKind.Flow, null, ContentKind.Flow),
            ["aside"] = new HtmlTagDescriptor("aside", ContentKind.Flow | ContentKind.Sectioning | ContentKind.Palpable, null, ContentKind.Flow, null, ContentKind.Flow),
            ["audio"] = new HtmlTagDescriptor("audio", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Embedded | ContentKind.Interactive | ContentKind.Palpable, null, ContentKind.Phrasing, new[] { "source", "track" }, ContentKind.Transparent),
            ["b"] = new HtmlTagDescriptor("b", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["base"] = new HtmlTagDescriptor("base", ContentKind.Metadata, new[] { "head", "template" }, ContentKind.None, null, ContentKind.None, TagEndKind.AutoSelfClosing),
            ["bdi"] = new HtmlTagDescriptor("bdi", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["bdo"] = new HtmlTagDescriptor("bdo", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["blockquote"] = new HtmlTagDescriptor("blockquote", ContentKind.Flow | ContentKind.SectioningRoot | ContentKind.Palpable, null, ContentKind.Flow, null, ContentKind.Flow),
            ["body"] = new HtmlTagDescriptor("body", ContentKind.SectioningRoot, new[] { "html" }, ContentKind.None, null, ContentKind.Flow, TagEndKind.Omission, BodyEndTagOmission, BodyStartTagOmission),
            ["br"] = new HtmlTagDescriptor("br", ContentKind.Flow | ContentKind.Phrasing, null, ContentKind.Phrasing, null, ContentKind.None, TagEndKind.AutoSelfClosing),
            ["button"] = new HtmlTagDescriptor("button", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Interactive | ContentKind.Listed | ContentKind.Labelable | ContentKind.Submittable | ContentKind.FormAssociated | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["canvas"] = new HtmlTagDescriptor("canvas", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Embedded | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Transparent),
            ["caption"] = new HtmlTagDescriptor("caption", ContentKind.None, new[] { "table", "template" }, ContentKind.None, null, ContentKind.Flow, TagEndKind.Omission, CanOmitEndTagForCaptionAndColgroup),
            ["cite"] = new HtmlTagDescriptor("cite", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["code"] = new HtmlTagDescriptor("code", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["col"] = new HtmlTagDescriptor("col", ContentKind.None, new[] { "colgroup", "template" }, ContentKind.None, null, ContentKind.None, TagEndKind.AutoSelfClosing),
            ["colgroup"] = new HtmlTagDescriptor("colgroup", ContentKind.None, new[] { "table", "template" }, ContentKind.None, new[] { "col", "template" }, ContentKind.None, TagEndKind.Omission, CanOmitEndTagForCaptionAndColgroup),
            ["data"] = new HtmlTagDescriptor("data", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["datalist"] = new HtmlTagDescriptor("datalist", ContentKind.Flow | ContentKind.Phrasing, null, ContentKind.Phrasing, new[] { "option" }, ContentKind.Phrasing | ContentKind.ScriptSupporting),
            ["dd"] = new HtmlTagDescriptor("dd", ContentKind.None, new[] { "dl", "template" }, ContentKind.None, null, ContentKind.Flow, TagEndKind.Omission, DefinitionDescriptionEndTagOmissionHandler),
            ["del"] = new HtmlTagDescriptor("del", ContentKind.Flow | ContentKind.Phrasing, null, ContentKind.Phrasing, null, ContentKind.Transparent),
            ["details"] = new HtmlTagDescriptor("details", ContentKind.Flow | ContentKind.SectioningRoot | ContentKind.Interactive | ContentKind.Palpable, null, ContentKind.Flow, new[] { "summary" }, ContentKind.Flow),
            ["dfn"] = new HtmlTagDescriptor("dfn", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["dialog"] = new HtmlTagDescriptor("dialog", ContentKind.Flow | ContentKind.SectioningRoot, null, ContentKind.Flow, null, ContentKind.Flow),
            ["div"] = new HtmlTagDescriptor("div", ContentKind.Flow | ContentKind.Palpable, null, ContentKind.Flow, null, ContentKind.Flow),
            ["dl"] = new HtmlTagDescriptor("dl", ContentKind.Flow | ContentKind.Palpable, null, ContentKind.Flow, new[] { "dt", "dd" }, ContentKind.ScriptSupporting),
            ["dt"] = new HtmlTagDescriptor("dt", ContentKind.None, new[] { "dl", "template" }, ContentKind.None, null, ContentKind.Flow, TagEndKind.Omission, DefinitionDescriptionEndTagOmissionHandler),
            ["em"] = new HtmlTagDescriptor("em", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["embed"] = new HtmlTagDescriptor("embed", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Embedded | ContentKind.Interactive | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.None, TagEndKind.AutoSelfClosing),
            ["fieldset"] = new HtmlTagDescriptor("fieldset", ContentKind.Flow | ContentKind.SectioningRoot | ContentKind.Listed | ContentKind.FormAssociated | ContentKind.Palpable, null, ContentKind.Flow, new[] { "legend" }, ContentKind.Flow),
            ["figcaption"] = new HtmlTagDescriptor("figcaption", ContentKind.None, new[] { "figure", "template" }, ContentKind.None, null, ContentKind.Flow),
            ["figure"] = new HtmlTagDescriptor("figure", ContentKind.Flow | ContentKind.SectioningRoot | ContentKind.Palpable, null, ContentKind.Flow, new[] { "figcaption" }, ContentKind.Flow),
            ["footer"] = new HtmlTagDescriptor("footer", ContentKind.Flow | ContentKind.Palpable, null, ContentKind.Flow, null, ContentKind.Flow),
            ["form"] = new HtmlTagDescriptor("form", ContentKind.Flow | ContentKind.Palpable, null, ContentKind.Flow, null, ContentKind.Flow),
            ["h1"] = new HtmlTagDescriptor("h1", ContentKind.Flow | ContentKind.Heading | ContentKind.Palpable, new[] { "hgroup" }, ContentKind.Flow, null, ContentKind.Phrasing),
            ["h2"] = new HtmlTagDescriptor("h2", ContentKind.Flow | ContentKind.Heading | ContentKind.Palpable, new[] { "hgroup" }, ContentKind.Flow, null, ContentKind.Phrasing),
            ["h3"] = new HtmlTagDescriptor("h3", ContentKind.Flow | ContentKind.Heading | ContentKind.Palpable, new[] { "hgroup" }, ContentKind.Flow, null, ContentKind.Phrasing),
            ["h4"] = new HtmlTagDescriptor("h4", ContentKind.Flow | ContentKind.Heading | ContentKind.Palpable, new[] { "hgroup" }, ContentKind.Flow, null, ContentKind.Phrasing),
            ["h5"] = new HtmlTagDescriptor("h5", ContentKind.Flow | ContentKind.Heading | ContentKind.Palpable, new[] { "hgroup" }, ContentKind.Flow, null, ContentKind.Phrasing),
            ["h6"] = new HtmlTagDescriptor("h6", ContentKind.Flow | ContentKind.Heading | ContentKind.Palpable, new[] { "hgroup" }, ContentKind.Flow, null, ContentKind.Phrasing),
            ["head"] = new HtmlTagDescriptor("head", ContentKind.None, new[] { "html" }, ContentKind.None, null, ContentKind.Metadata, TagEndKind.Omission, HeadEndTagOmission, HeadStartTagOmission),
            ["header"] = new HtmlTagDescriptor("header", ContentKind.Flow | ContentKind.Palpable, null, ContentKind.Flow, null, ContentKind.Flow),
            ["hgroup"] = new HtmlTagDescriptor("hgroup", ContentKind.Flow | ContentKind.Heading | ContentKind.Palpable, null, ContentKind.Flow, new[] { "h1", "h2", "h3", "h4", "h5", "h6", "template" }, ContentKind.None),
            ["hr"] = new HtmlTagDescriptor("hr", ContentKind.Flow, null, ContentKind.Flow, null, ContentKind.None, TagEndKind.AutoSelfClosing),
            ["html"] = new HtmlTagDescriptor("html", ContentKind.None, null, ContentKind.None, new[] { "head", "body" }, ContentKind.None, TagEndKind.Omission, HtmlStartAndEndTagOmission, HtmlStartAndEndTagOmission),
            ["i"] = new HtmlTagDescriptor("i", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["iframe"] = new HtmlTagDescriptor("iframe", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Embedded | ContentKind.Interactive | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Text),
            ["img"] = new HtmlTagDescriptor("img", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Embedded | ContentKind.Interactive | ContentKind.FormAssociated | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.None, TagEndKind.AutoSelfClosing),
            ["input"] = new HtmlTagDescriptor("input", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Interactive | ContentKind.Listed | ContentKind.Labelable | ContentKind.Submittable | ContentKind.Resettable | ContentKind.FormAssociated | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.None, TagEndKind.AutoSelfClosing),
            ["ins"] = new HtmlTagDescriptor("ins", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Transparent),
            ["kbd"] = new HtmlTagDescriptor("kbd", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["keygen"] = new HtmlTagDescriptor("keygen", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Interactive | ContentKind.Listed | ContentKind.Labelable | ContentKind.Submittable | ContentKind.Resettable | ContentKind.FormAssociated | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.None, TagEndKind.AutoSelfClosing),
            ["label"] = new HtmlTagDescriptor("label", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Interactive | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["legend"] = new HtmlTagDescriptor("legend", ContentKind.None, new[] { "fieldset", "template" }, ContentKind.None, null, ContentKind.Phrasing),
            ["li"] = new HtmlTagDescriptor("li", ContentKind.None, new[] { "ol", "ul", "menu", "template" }, ContentKind.None, null, ContentKind.Flow, TagEndKind.Omission, ListItemEndTagOmissionHandler),
            ["link"] = new HtmlTagDescriptor("link", ContentKind.Metadata | ContentKind.Flow | ContentKind.Phrasing, new[] { "head", "template", "noscript" }, ContentKind.Phrasing, null, ContentKind.None, TagEndKind.AutoSelfClosing),
            ["main"] = new HtmlTagDescriptor("main", ContentKind.Flow | ContentKind.Palpable, null, ContentKind.Flow, null, ContentKind.Flow),
            ["map"] = new HtmlTagDescriptor("map", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, new[] { "area" }, ContentKind.Transparent),
            ["mark"] = new HtmlTagDescriptor("mark", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["math"] = new HtmlTagDescriptor("math", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Embedded | ContentKind.Palpable | ContentKind.Xml, null, ContentKind.Phrasing, null, ContentKind.Any),
            ["menu"] = new HtmlTagDescriptor("menu", ContentKind.Flow | ContentKind.Palpable, new[] { "menu" }, ContentKind.Flow, new[] { "li", "menuitem", "hr", "menu" }, ContentKind.Flow | ContentKind.ScriptSupporting),
            ["menuitem"] = new HtmlTagDescriptor("menuitem", ContentKind.None, new[] { "menu", "template" }, ContentKind.None, null, ContentKind.Text, TagEndKind.Omission, MenuItemTagOmissionHandler),
            ["meta"] = new HtmlTagDescriptor("meta", ContentKind.Metadata | ContentKind.Flow | ContentKind.Phrasing, new[] { "head", "template", "noscript" }, ContentKind.Phrasing, null, ContentKind.None, TagEndKind.AutoSelfClosing),
            ["meter"] = new HtmlTagDescriptor("meter", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Labelable | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["nav"] = new HtmlTagDescriptor("nav", ContentKind.Flow | ContentKind.Sectioning | ContentKind.Palpable, null, ContentKind.Flow, null, ContentKind.Flow),
            ["noscript"] = new HtmlTagDescriptor("noscript", ContentKind.Metadata | ContentKind.Flow | ContentKind.Phrasing, new[] { "head", "template" }, ContentKind.Phrasing, null, ContentKind.Any),
            ["object"] = new HtmlTagDescriptor("object", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Embedded | ContentKind.Interactive | ContentKind.Listed | ContentKind.Submittable | ContentKind.FormAssociated | ContentKind.Palpable, null, ContentKind.Phrasing, new[] { "param" }, ContentKind.Transparent),
            ["ol"] = new HtmlTagDescriptor("ol", ContentKind.Flow | ContentKind.Palpable, null, ContentKind.Flow, new[] { "li" }, ContentKind.ScriptSupporting),
            ["optgroup"] = new HtmlTagDescriptor("optgroup", ContentKind.None, new[] { "select", "template" }, ContentKind.None, new[] { "option" }, ContentKind.ScriptSupporting, TagEndKind.Omission, OptGroupEndTagOmissionHandler),
            ["option"] = new HtmlTagDescriptor("option", ContentKind.None, new[] { "select", "datalist", "optgroup", "template" }, ContentKind.None, null, ContentKind.Text, TagEndKind.Omission, OptionEndTagOmissionHandler),
            ["output"] = new HtmlTagDescriptor("output", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Listed | ContentKind.Labelable | ContentKind.Resettable | ContentKind.FormAssociated | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["p"] = new HtmlTagDescriptor("p", ContentKind.Flow | ContentKind.Palpable, null, ContentKind.Flow, null, ContentKind.Phrasing, TagEndKind.Omission, ParagraphEndTagOmissionHandler),
            ["param"] = new HtmlTagDescriptor("param", ContentKind.None, new[] { "object", "template" }, ContentKind.None, null, ContentKind.None, TagEndKind.AutoSelfClosing),
            ["picture"] = new HtmlTagDescriptor("picture", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Embedded, null, ContentKind.Phrasing, new[] { "source", "img" }, ContentKind.ScriptSupporting),
            ["pre"] = new HtmlTagDescriptor("pre", ContentKind.Flow | ContentKind.Palpable, null, ContentKind.Flow, null, ContentKind.Phrasing),
            ["progress"] = new HtmlTagDescriptor("progress", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Labelable | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["q"] = new HtmlTagDescriptor("q", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["rp"] = new HtmlTagDescriptor("rp", ContentKind.None, new[] { "ruby", "template" }, ContentKind.None, null, ContentKind.Phrasing, TagEndKind.Omission, RubyEndTagOmissionHandler),
            ["rt"] = new HtmlTagDescriptor("rt", ContentKind.None, new[] { "ruby", "template" }, ContentKind.None, null, ContentKind.Phrasing, TagEndKind.Omission, RubyEndTagOmissionHandler),
            ["ruby"] = new HtmlTagDescriptor("ruby", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, new[] { "rt", "rp" }, ContentKind.Phrasing),
            ["s"] = new HtmlTagDescriptor("s", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["samp"] = new HtmlTagDescriptor("samp", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["script"] = new HtmlTagDescriptor("script", ContentKind.Metadata | ContentKind.Flow | ContentKind.Phrasing | ContentKind.ScriptSupporting, new[] { "head" }, ContentKind.Phrasing | ContentKind.ScriptSupporting, null, ContentKind.ScriptText),
            ["section"] = new HtmlTagDescriptor("section", ContentKind.Flow | ContentKind.Sectioning | ContentKind.Palpable, null, ContentKind.Flow, null, ContentKind.Flow),
            ["select"] = new HtmlTagDescriptor("select", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Interactive | ContentKind.Listed | ContentKind.Labelable | ContentKind.Submittable | ContentKind.Resettable | ContentKind.FormAssociated | ContentKind.Palpable, null, ContentKind.Phrasing, new[] { "option", "optgroup" }, ContentKind.ScriptSupporting),
            ["slot"] = new HtmlTagDescriptor("slot", ContentKind.Flow | ContentKind.Phrasing, null, ContentKind.Phrasing, null, ContentKind.Transparent),
            ["small"] = new HtmlTagDescriptor("small", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["source"] = new HtmlTagDescriptor("source", ContentKind.None, new[] { "picture", "video", "audio", "template" }, ContentKind.None, null, ContentKind.None, TagEndKind.AutoSelfClosing),
            ["span"] = new HtmlTagDescriptor("span", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["strong"] = new HtmlTagDescriptor("strong", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["style"] = new HtmlTagDescriptor("style", ContentKind.Metadata | ContentKind.Flow, new[] { "head", "noscript" }, ContentKind.Flow, null, ContentKind.ScriptText),
            ["sub"] = new HtmlTagDescriptor("sub", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["summary"] = new HtmlTagDescriptor("summary", ContentKind.None, new[] { "details" }, ContentKind.None, null, ContentKind.Phrasing),
            ["sup"] = new HtmlTagDescriptor("sup", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["svg"] = new HtmlTagDescriptor("svg", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Embedded | ContentKind.Palpable | ContentKind.Xml, null, ContentKind.Phrasing, null, ContentKind.Any),
            ["table"] = new HtmlTagDescriptor("table", ContentKind.Flow | ContentKind.Palpable, null, ContentKind.Flow, new[] { "caption", "colgroup", "thead", "tbody", "tfoot", "tr" }, ContentKind.ScriptSupporting),
            ["tbody"] = new HtmlTagDescriptor("tbody", ContentKind.None, new[] { "table", "template" }, ContentKind.None, new[] { "tr" }, ContentKind.ScriptSupporting, TagEndKind.Omission, THeadTBodyTagOmissionHandler),
            ["td"] = new HtmlTagDescriptor("td", ContentKind.SectioningRoot, new[] { "tr", "template" }, ContentKind.None, null, ContentKind.Flow, TagEndKind.Omission, TDTHTagOmissionHandler),
            ["template"] = new HtmlTagDescriptor("template", ContentKind.Metadata | ContentKind.Flow | ContentKind.Phrasing | ContentKind.ScriptSupporting, new[] { "metadata", "colgroup" }, ContentKind.Phrasing | ContentKind.ScriptSupporting, null, ContentKind.Any),
            ["textarea"] = new HtmlTagDescriptor("textarea", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Interactive | ContentKind.Listed | ContentKind.Labelable | ContentKind.Submittable | ContentKind.Resettable | ContentKind.FormAssociated | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Text),
            ["tfoot"] = new HtmlTagDescriptor("tfoot", ContentKind.None, new[] { "table", "template" }, ContentKind.None, new[] { "tr" }, ContentKind.ScriptSupporting, TagEndKind.Omission, CanOmitEndTagForTFoot),
            ["th"] = new HtmlTagDescriptor("th", ContentKind.Interactive, new[] { "tr", "template" }, ContentKind.None, null, ContentKind.Flow, TagEndKind.Omission, TDTHTagOmissionHandler),
            ["thead"] = new HtmlTagDescriptor("thead", ContentKind.None, new[] { "table", "template" }, ContentKind.None, new[] { "tr" }, ContentKind.ScriptSupporting, TagEndKind.Omission, THeadTBodyTagOmissionHandler),
            ["time"] = new HtmlTagDescriptor("time", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["title"] = new HtmlTagDescriptor("title", ContentKind.Metadata, new[] { "head", "template" }, ContentKind.None, null, ContentKind.Text),
            ["tr"] = new HtmlTagDescriptor("tr", ContentKind.None, new[] { "table", "thead", "tbody", "tfoot", "template" }, ContentKind.None, new[] { "th", "td" }, ContentKind.ScriptSupporting, TagEndKind.Omission, TableRowEndTagOmissionHandler),
            ["track"] = new HtmlTagDescriptor("track", ContentKind.None, new[] { "audio", "video", "template" }, ContentKind.None, null, ContentKind.None, TagEndKind.AutoSelfClosing),
            ["u"] = new HtmlTagDescriptor("u", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["ul"] = new HtmlTagDescriptor("ul", ContentKind.Flow | ContentKind.Palpable, null, ContentKind.Flow, new[] { "li" }, ContentKind.ScriptSupporting),
            ["var"] = new HtmlTagDescriptor("var", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Palpable, null, ContentKind.Phrasing, null, ContentKind.Phrasing),
            ["video"] = new HtmlTagDescriptor("video", ContentKind.Flow | ContentKind.Phrasing | ContentKind.Embedded | ContentKind.Interactive | ContentKind.Palpable, null, ContentKind.Phrasing, new[] { "source", "track" }, ContentKind.Transparent),
            ["wbr"] = new HtmlTagDescriptor("wbr", ContentKind.Flow | ContentKind.Phrasing, null, ContentKind.Phrasing, null, ContentKind.None, TagEndKind.AutoSelfClosing),
        };

        static readonly Dictionary<string, bool> OpenTagsClosingParagraph = new string[]
        {
            "address",
            "article",
            "aside",
            "blockquote",
            "details",
            "div",
            "dl",
            "fieldset",
            "figcaption",
            "figure",
            "footer",
            "form",
            "h1",
            "h2",
            "h3",
            "h4",
            "h5",
            "h6",
            "header",
            "hgroup",
            "hr",
            "main",
            "menu",
            "nav",
            "ol",
            "p",
            "pre",
            "section",
            "table",
            "ul",
            "li"
        }.ToDictionaryBool(false);

        static readonly Dictionary<string, bool> ParentTagsClosingParagraph = new string[]
        {
            "a",
            "audio",
            "del",
            "ins",
            "map",
            "noscript",
            "video"
        }.ToDictionaryBool(false);

        static bool ParagraphEndTagOmissionHandler(HtmlElement parent, HtmlElement nextSibling, bool whileParsing)
        {
            // it is a closing nextSibling that will close this parent, so we allow it
            return nextSibling != null
                ? OpenTagsClosingParagraph.ContainsKey(nextSibling.Name)
                : !ParentTagsClosingParagraph.ContainsKey(parent.Parent.Name);
        }

        static bool ListItemEndTagOmissionHandler(HtmlElement parent, HtmlElement nextSibling, bool whileParsing)
        {
            return nextSibling == null || nextSibling.Name.Equals("li", StringComparison.OrdinalIgnoreCase);
        }

        static bool DefinitionDescriptionEndTagOmissionHandler(HtmlElement parent, HtmlElement nextSibling, bool whileParsing)
        {
            return nextSibling == null || (nextSibling.Name.Equals("dt", StringComparison.OrdinalIgnoreCase) 
                || nextSibling.Name.Equals("dd", StringComparison.OrdinalIgnoreCase));
        }

        static bool RubyEndTagOmissionHandler(HtmlElement parent, HtmlElement nextSibling, bool whileParsing)
        {
            return nextSibling == null || (nextSibling.Name.Equals("rt", StringComparison.OrdinalIgnoreCase) 
                || nextSibling.Name.Equals("rp", StringComparison.OrdinalIgnoreCase));
        }

        static bool CanOmitEndTagForCaptionAndColgroup(HtmlElement tag, HtmlElement nextsibling, bool whileParsing)
        {
            return nextsibling == null ||
                   (nextsibling.Name.Equals("colgroup", StringComparison.OrdinalIgnoreCase)
                   || nextsibling.Name.Equals("thead", StringComparison.OrdinalIgnoreCase)
                   || nextsibling.Name.Equals("tbody", StringComparison.OrdinalIgnoreCase)
                    || nextsibling.Name.Equals("tr", StringComparison.OrdinalIgnoreCase)) &&
                   CanOmitIfFollowedByCommentOrSpace(tag, nextsibling);

        }

        static bool CanOmitEndTagForTFoot(HtmlElement tag, HtmlElement nextsibling, bool whileParsing)
        {
            return nextsibling == null ||
                   nextsibling.Name.Equals("tbody", StringComparison.OrdinalIgnoreCase);
        }

        static bool CanOmitIfFollowedByCommentOrSpace(HtmlElement parent, HtmlElement nextSibling)
        {
            return !(parent.FirstChild is HtmlComment || (parent.FirstChild is HtmlText && ((HtmlText)parent.FirstChild).Slice.StartsBySpace()));
        }

        static bool THeadTBodyTagOmissionHandler(HtmlElement parent, HtmlElement nextSibling, bool whileParsing)
        {
            return nextSibling == null || (nextSibling.Name.Equals("tbody", StringComparison.OrdinalIgnoreCase) 
                || nextSibling.Name.Equals("tfoot", StringComparison.OrdinalIgnoreCase));
        }

        static bool TableRowEndTagOmissionHandler(HtmlElement parent, HtmlElement nextSibling, bool whileParsing)
        {
            return nextSibling == null || nextSibling.Name.Equals("tr", StringComparison.OrdinalIgnoreCase);
        }

        static bool TDTHTagOmissionHandler(HtmlElement parent, HtmlElement nextSibling, bool whileParsing)
        {
            return nextSibling == null || (nextSibling.Name.Equals("td", StringComparison.OrdinalIgnoreCase) 
                || nextSibling.Name.Equals("th", StringComparison.OrdinalIgnoreCase)
                || nextSibling.Name.Equals("tr", StringComparison.OrdinalIgnoreCase));
        }

        static bool OptGroupEndTagOmissionHandler(HtmlElement parent, HtmlElement nextSibling, bool whileParsing)
        {
            return nextSibling == null || nextSibling.Name.Equals("optgroup", StringComparison.OrdinalIgnoreCase);
        }

        static bool OptionEndTagOmissionHandler(HtmlElement parent, HtmlElement nextSibling, bool whileParsing)
        {
            return nextSibling == null || (nextSibling.Name.Equals("optgroup", StringComparison.OrdinalIgnoreCase)
                || nextSibling.Name.Equals("option", StringComparison.OrdinalIgnoreCase));
        }

        static bool MenuItemTagOmissionHandler(HtmlElement parent, HtmlElement nextSibling, bool whileParsing)
        {
            return nextSibling == null || (nextSibling.Name.Equals("menuitem", StringComparison.OrdinalIgnoreCase)
                || nextSibling.Name.Equals("hr", StringComparison.OrdinalIgnoreCase)
                || nextSibling.Name.Equals("menu", StringComparison.OrdinalIgnoreCase));
        }

        static bool HtmlStartAndEndTagOmission(HtmlElement parent, HtmlElement nextSibling, bool whileParsing)
        {
            return (!whileParsing || nextSibling == null) && !(parent.NextSibling is HtmlComment);
        }

        static bool HeadStartTagOmission(HtmlElement parent, HtmlElement nextSibling, bool whileParsing)
        {
            // A head element's start tag may be omitted if the element is empty, or if the first thing inside the head element is an element. 
            return (!whileParsing || nextSibling == null) && (parent.FirstChild == null || parent.FirstChild is HtmlElement);
        }

        static bool HeadEndTagOmission(HtmlElement parent, HtmlElement nextSibling, bool whileParsing)
        {
            // A head element's end tag may be omitted if the head element is not immediately followed by a space character or a comment.
            return (!whileParsing || nextSibling == null) && (parent.NextSibling == null || (parent.FirstChild is HtmlText && !((HtmlText)parent.FirstChild).Slice.IsEmptyOrWhiteSpace()) || !(parent.NextSibling is HtmlComment));
        }

        static bool BodyStartTagOmission(HtmlElement parent, HtmlElement nextSibling, bool whileParsing)
        {
            // A body element's start tag may be omitted if:
            // - the element is empty, 
            // - or if the first thing inside the body element is not a space character or a comment, except if the first thing inside the body element is a meta, link, script, style, or template element. 
            var content = parent.FirstChild;
            var text = content as HtmlText;
            var element = content as HtmlElement;
            return (!whileParsing || nextSibling == null) && (content == null ||
                   (!(text != null && text.Slice.IsEmptyOrWhiteSpace()) && !(content is HtmlComment) &&
                    (element == null || (
                        element.Name != "meta"
                        && element.Name != "link"
                        && element.Name != "script"
                        && element.Name != "style"
                        && element.Name != "template"))));
        }


        static bool BodyEndTagOmission(HtmlElement parent, HtmlElement nextSibling, bool whileParsing)
        {
            // A body element's end tag may be omitted if the body element is not immediately followed by a comment.
            return (!whileParsing || nextSibling == null) && !(parent.NextSibling is HtmlComment);
        }
    }
}
