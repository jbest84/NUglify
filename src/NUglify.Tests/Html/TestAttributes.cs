// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using NUglify.Html;
using NUnit.Framework;

namespace NUglify.Tests.Html
{
    /// <summary>
    /// Tests ported from html-minifier https://github.com/kangax/html-minifier/blob/gh-pages/tests/minifier.js
    /// </summary>
    [TestFixture]
    public class TestAttributes : TestHtmlParserBase
    {
        [Test]
        public void EmptyAttributes()
        {
            // Copyright(c) 2010 - 2016 Juriy "kangax" Zaytsev
            // MIT License - https://github.com/kangax/html-minifier/blob/gh-pages/LICENSE

            input = "<p id=\"\" class=\"\" STYLE=\" \" title=\"\n\" lang=\"\" dir=\"\">x</p>";
            equal(minify(input), "<p>x");

            input = "<p onclick=\"\"   ondblclick=\" \" onmousedown=\"\" ONMOUSEUP=\"\" onmouseover=\" \" onmousemove=\"\" onmouseout=\"\" " +
                    "onkeypress=\n\n  \"\n     \" onkeydown=\n\"\" onkeyup\n=\"\">x</p>";
            equal(minify(input), "<p>x");

            input = "<input onfocus=\"\" onblur=\"\" onchange=\" \" value=\" boo \">";
            equal(minify(input), "<input value=\" boo \">");

            input = "<input value=\"\" name=\"foo\">";
            equal(minify(input), "<input name=foo>");

            input = "<input value=\"true\" name=\"foo\" Aria-Hidden=\"true\">";
            equal(minify(input, new HtmlSettings { ShortBooleanAttribute = true}), "<input value=true name=foo aria-hidden=true>");

            input = "<img src=\"\" alt=\"\">";
            equal(minify(input), "<img src=\"\" alt=\"\">");

            // preserve unrecognized attribute
            // remove recognized attrs with unspecified values
            input = "<div data-foo class id style title lang dir onfocus onblur onchange onclick ondblclick onmousedown onmouseup onmouseover onmousemove onmouseout onkeypress onkeydown onkeyup></div>";
            equal(minify(input), "<div data-foo></div>");
        }

        [Test]
        public void CleanupClassStyleAttribute()
        {
            // Copyright(c) 2010 - 2016 Juriy "kangax" Zaytsev
            // MIT License - https://github.com/kangax/html-minifier/blob/gh-pages/LICENSE

            input = "<p class=\" foo bar  \">foo bar baz</p>";
            equal(minify(input), "<p class=\"foo bar\">foo bar baz");

            input = "<p class=\" foo      \">foo bar baz</p>";
            equal(minify(input), "<p class=foo>foo bar baz");

            input = "<p class=\"\n  \n foo   \n\n\t  \t\n   \">foo bar baz</p>";
            output = "<p class=foo>foo bar baz";
            equal(minify(input), output);

            input = "<p class=\"\n  \n foo   \n\n\t  \t\n  class1 class-23 \">foo bar baz</p>";
            output = "<p class=\"foo class1 class-23\">foo bar baz";
            equal(minify(input), output);

            // TODO
            //input = "<p style=\"    color: red; background-color: rgb(100, 75, 200);  \"></p>";
            //output = "<p style=\"color: red; background-color: rgb(100, 75, 200)\">";
            //equal(minify(input), output);

            //input = "<p style=\"font-weight: bold  ; \"></p>";
            //output = "<p style=\"font-weight: bold\">";
            //equal(minify(input), output);
        }

        [Test]
        public void CleanupUriAttributes()
        {
            // Copyright(c) 2010 - 2016 Juriy "kangax" Zaytsev
            // MIT License - https://github.com/kangax/html-minifier/blob/gh-pages/LICENSE

            input = "<a href=\"   http://example.com  \">x</a>";
            output = "<a href=http://example.com>x</a>";
            equal(minify(input), output);

            input = "<a href=\"  \t\t  \n \t  \">x</a>";
            output = "<a href=\"\">x</a>";
            equal(minify(input), output);

            input = "<img src=\"   http://example.com  \" title=\"bleh   \" longdesc=\"  http://example.com/longdesc \n\n   \t \">";
            output = "<img src=http://example.com title=\"bleh   \" longdesc=http://example.com/longdesc>";
            equal(minify(input), output);

            input = "<img src=\"\" usemap=\"   http://example.com  \">";
            output = "<img src=\"\" usemap=http://example.com>";
            equal(minify(input), output);

            input = "<form action=\"  somePath/someSubPath/someAction?foo=bar&baz=qux     \"></form>";
            output = "<form action=\"somePath/someSubPath/someAction?foo=bar&baz=qux\"></form>";
            equal(minify(input), output);

            input = "<BLOCKQUOTE cite=\" \n\n\n http://www.mycom.com/tolkien/twotowers.html     \"><P>foobar</P></BLOCKQUOTE>";
            output = "<blockquote cite=http://www.mycom.com/tolkien/twotowers.html><p>foobar</blockquote>";
            equal(minify(input), output);

            input = "<head profile=\"       http://gmpg.org/xfn/11    \"></head>";
            output = "<head profile=http://gmpg.org/xfn/11>";
            equal(minify(input), output);

            input = "<object codebase=\"   http://example.com  \"></object>";
            output = "<object codebase=http://example.com></object>";
            equal(minify(input), output);

            input = "<span profile=\"   1, 2, 3  \">foo</span>";
            equal(minify(input), input);

            input = "<div action=\"  foo-bar-baz \">blah</div>";
            equal(minify(input), input);
        }


        [Test]
        public void RemoveAttributes()
        {
            var settings = new HtmlSettings
            {
                RemoveAttributes =
                {
                    "data-test",
                    "class"
                }
            };

            input = "<div CLASS=\"a\" data-foo=\"bar\"><div id=\"id\"></div><p data-test=\"test\"></div>";
            output = "<div data-foo=bar><div id=id></div><p></div>";
            equal(minify(input, settings), output);
        }

        [Test]
        public void AlphaOrderAttributes()
        {
            var settings  = new HtmlSettings
            {
                AlphabeticallyOrderAttributes = true
            };
            input = "<div x=\"1\" y=\"1\" r=\"1\" q=\"1\" p=\"1\"></div>";
            equal(minify(input, settings), "<div p=1 q=1 r=1 x=1 y=1></div>");
        }
    }
}