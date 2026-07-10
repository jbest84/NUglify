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
    public class TestScripts : TestHtmlParserBase
    {
        [Test]
        public void RemoveComments()
        {
            // Copyright(c) 2010 - 2016 Juriy "kangax" Zaytsev
            // MIT License - https://github.com/kangax/html-minifier/blob/gh-pages/LICENSE

            input = "<script><!--\nalert(1);\n--></script>";
            equal(minify(input), input);

            input = "<script><!--alert(2);--></script>";
            equal(minify(input), input);

            input = "<script><!--alert(3);\n--></script>";
            equal(minify(input), input);

            input = "<script><!--\nalert(4);--></script>";
            equal(minify(input), input);

            input = "<script><!--alert(5);\nalert(6);\nalert(7);--></script>";
            equal(minify(input), input);

            input = "<script><!--alert(8)</script>";
            equal(minify(input), input);

            input = "<script type=\"text/javascript\"> \n <!--\nalert(\"-->\"); -->\n\n   </script>";
            output = "<script> \n <!--\nalert(\"-->\"); -->\n\n   </script>";
            equal(minify(input), output);

            input = "<script type=\"text/javascript\"> \n <!--\nalert(\"-->\");\n -->\n\n   </script>";
            output = "<script> \n <!--\nalert(\"-->\");\n -->\n\n   </script>";
            equal(minify(input), output);

            input = "<script> //   <!--   \n  alert(1)   //  --> </script>";
            equal(minify(input), "<script>alert(1)</script>");

            input = "<script type=\"text/html\">\n<div>\n</div>\n<!-- aa -->\n</script>";
            output = "<script type=text/html>\n<div>\n</div>\n<!-- aa -->\n</script>";
            equal(minify(input), output);
        }

        [Test]
        public void LdJson()
        {

            input = @"
<script type=text/javascript>
var js=""somevar"";
</script>
<script type=application/ld+json>{
    ""@context"": ""http://schema.org/"",
    ""@type"": ""WebSite"",
    ""url"": ""https://domain.ext"",
    ""name"": ""Name Site"",
	""isChecked"": true,
    ""alternateName"": null
}</script>";
            output = @"<script>var js=""somevar""</script><script type=application/ld+json>{""@context"":""http://schema.org/"",""@type"":""WebSite"",""url"":""https://domain.ext"",""name"":""Name Site"",""isChecked"":true,""alternateName"":null}</script>";
            equal(minify(input), output);
        }
        [Test]
        public void Json()
        {

	        input = @"
<script type=text/javascript>
var js=""somevar"";
</script>
<script type=application/json>{
    ""@context"": ""http://schema.org/"",
    ""@type"": ""WebSite"",
    ""url"": ""https://domain.ext"",
    ""name"": ""Name Site"",
	""isChecked"": true,
    ""alternateName"": null
}</script>";
	        output = @"<script>var js=""somevar""</script><script type=application/json>{""@context"":""http://schema.org/"",""@type"":""WebSite"",""url"":""https://domain.ext"",""name"":""Name Site"",""isChecked"":true,""alternateName"":null}</script>";
	        equal(minify(input), output);
        }
    }
}