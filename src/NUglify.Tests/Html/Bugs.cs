// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.Text.RegularExpressions;
using NUglify.Html;
using NUnit.Framework;

namespace NUglify.Tests.Html
{
    [TestFixture]
    public class Bugs : TestHtmlParserBase
    {
        [Test]
        public void Bug73()
        {
            var settings = new HtmlSettings();

            input = @"
<p>para1</p>
<!-- <div class=""block__element--modifier"">something</div> -->
<h2>heading2</h2>
";
            equal(minify(input, settings), "<p>para1<h2>heading2</h2>");
        }
		
        [Test]
        public void Bug119()
        {
            input = "This is a fragment </p> and it continues here <a>";
            var htmlToText = Uglify.HtmlToText(input);
            equal(htmlToText.Code, "This is a fragment and it continues here ");
        }

        [Test]
        public void Bug169()
        {
	        input = "<html><head><title>Please indent me properly</title></head></html>";
	        var htmlSettings = HtmlSettings.Pretty();
	        htmlSettings.Indent = "\t";
	        var htmlToText = Uglify.Html(input, htmlSettings);
	        equal(htmlToText.Code, @"<html>
	<head>
		<title>
			Please indent me properly
		</title>
	</head>
</html>");
        }

        [Test]
        public void Bug170()
        {
	        input = "<html><head><title>Please indent me properly</title></head></html>";
	        var htmlSettings = HtmlSettings.Pretty();
	        htmlSettings.Indent = "\t";
	        htmlSettings.OutputTextNodesOnNewLine = false;
	        var htmlToText = Uglify.Html(input, htmlSettings);
	        equal(htmlToText.Code, @"<html>
	<head>
		<title>Please indent me properly</title>
	</head>
</html>");
        }
		[Test]
        public void Bug171()
        {
	        input = @"
<div>
	<pre>format 
me
	properly
 </pre>
</div>";
	        var htmlSettings = HtmlSettings.Pretty();
	        htmlSettings.IsFragmentOnly = true;
	        var htmlToText = Uglify.Html(input, htmlSettings);
	        equal(htmlToText.Code, @"<div>
  <pre>format 
me
	properly
 </pre>
</div>");
        }

		[Test]
        public void Bug172()
        {
	        input = @"
<div>
	<p onclick=""doSomething(1 + 2); "">click me</p>
</div>";
			var htmlSettings = new HtmlSettings();
			var htmlToText = Uglify.Html(input, htmlSettings);
			equal(htmlToText.Code, @"<div><p onclick=doSomething(3)>click me</div>");
        }

		[Test]
        public void Bug174()
        {
	        input = @"
<html>
	<head>
		<script>let x = 1; 
let y = function() { foo() }</script>
		<script></script>
		<style>h1 { color: red; }</style>
	</head>
</html>";
	        var htmlSettings = HtmlSettings.Pretty();
	        htmlSettings.Indent = "\t";
	        var htmlToText = Uglify.Html(input, htmlSettings);
	        equal(htmlToText.Code, @"<html>
	<head>
		<script>
			let x = 1; 
			let y = function() { foo() }
		</script>
		<script>
		</script>
		<style>
			h1 { color: red; }
		</style>
	</head>
</html>");
		}

		[Test]
		public void Bug182()
		{
			input = @"
<div class=defaultHeader style=""text-align: center; background-color: #00bfff"">
test
</div>";
			var htmlSettings = HtmlSettings.Pretty();
			htmlSettings.Indent = "\t";
			htmlSettings.IsFragmentOnly = true;
			var htmlToText = Uglify.Html(input, htmlSettings);
			equal(htmlToText.Code, @"<div class=""defaultHeader"" style=""text-align: center; background-color: #00bfff;"">
	test
</div>");
		}

		[Test]
		public void Bug184()
		{
			input = @"<div>
	<a>(Show)</a>
	<a>(Hide)</a>
</div>
<div>
	Inline text
	<a>(Show)</a>
	Between As
	<a>(Hide)</a>
</div>";
			var htmlSettings = HtmlSettings.Pretty();
			htmlSettings.RemoveOptionalTags = false;
			htmlSettings.Indent = "\t";
			htmlSettings.IsFragmentOnly = true;
			htmlSettings.OutputTextNodesOnNewLine = false;

			var htmlToText = Uglify.Html(input, htmlSettings);
			equal(htmlToText.Code, @"<div>
	<a>(Show)</a> <a>(Hide)</a>
</div>
<div>
	Inline text <a>(Show)</a> Between As <a>(Hide)</a>
</div>");
		}

		[Test]
		public void Bug206_MOTW()
		{
			var settings = HtmlSettings.Pretty();
			settings.Indent = "\t";
			settings.OutputTextNodesOnNewLine = false;
			settings.RemoveComments = true;
			settings.KeepCommentsRegex.Add(new Regex(@"saved from url="));

			input = @"<!-- saved from url=(0014)about:internet -->
<!DOCTYPE html>
<html>
<body>
<p>test</p>
</body>
</html>";
			equal(minify(input, settings), "<!-- saved from url=(0014)about:internet -->\n<!DOCTYPE html>\n<html>\n	<body>\n		<p>test</p>\n	</body>\n</html>");
		}

		[Test]
		public void Bug192()
		{
			var settings = HtmlSettings.Pretty();
			settings.RemoveOptionalTags = false;
			settings.Indent = "\t";
			settings.IsFragmentOnly = true;
			settings.OutputTextNodesOnNewLine = false;

			input = @"<%@ Page Title=""Computadores"" Language=""C#"" MaintainScrollPositionOnPostback=""true"" %>
<div>
	<a>(Show)</a>
</div>
<%-- comment --%>
<div>
	text
</div>";
			equal(minify(input, settings), @"<%@ Page Title=""Computadores"" Language=""C#"" MaintainScrollPositionOnPostback=""true"" %>
<div>
	<a>(Show)</a>
</div>
<%-- comment --%>
<div>text</div>");
		}


		[Test]
		public void Bug211()
		{
			input = "<p>Hello</p><p>This is a test<br/>This is another test</p><p>&nbsp;</p><p>Something</p>";
			var htmlToText = Uglify.HtmlToText(input, HtmlToTextOptions.KeepStructure);
			equal(htmlToText.Code, "Hello\nThis is a test\nThis is another test\n \nSomething\n");
		}



		[Test]
		public void Bug311()
		{
			HtmlSettings settings = new HtmlSettings();
			settings.TagsCaseSensitive = true;
			input = "<TagName>Case sensitive</TagName>";
			var htmlToText = Uglify.Html(input, settings);
			equal(htmlToText.Code, input);
		}
    }
}