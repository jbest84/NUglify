// AtRules.cs
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

using NUglify.Css;
using NUglify.Tests.Css.Common;
using NUnit.Framework;

namespace NUglify.Tests.Css
{
    /// <summary>
    /// Summary description for AtRules
    /// </summary>
    [TestFixture]
    public class Bugs
    {
        [Test]
        public void Bug33()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void Bug56()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void Bug74()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void Bug181()
        {
	        var uglifyResult = Uglify.Css("p { color: red; }", new CssSettings { Indent = "   ", OutputMode = OutputMode.MultipleLines });
	        Assert.That(uglifyResult.Code, Is.EqualTo("p\n{\n   color: #f00\n}"));
        }

        [Test]
        public void Bug250()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void Bug270()
        {
	        TestHelper.Instance.RunTest();
        }

        [Test]
        public void Bug280()
        {
            Assert.That(Uglify.Css(":is(.container) :is(.bold) { font-weight: bold; }").Code, Is.EqualTo(":is(.container) :is(.bold){font-weight:bold}"));

            Assert.That(Uglify.Css(":is(.container):is(.bold) { font-weight: bold; }").Code, Is.EqualTo(":is(.container):is(.bold){font-weight:bold}"));

            Assert.That(Uglify.Css(".container :is(.bold) { font-weight: bold; }").Code, Is.EqualTo(".container :is(.bold){font-weight:bold}"));

            // Assert whether extra unnecessary spaces around parentheses are removed:
            Assert.That(Uglify.Css(":is(.container ) :is(.bold ) { font-weight: bold; }").Code, Is.EqualTo(":is(.container) :is(.bold){font-weight:bold}"));

            // Assert a more extensive scenario:
            Assert.That(Uglify.Css(@"body {
    font-family: 'Franklin Gothic Medium', 'Arial Narrow', Arial, sans-serif;
}

:root {
    --blue: 'blue';
    --five: 5px;
    --width: 100px;
    --half-width: calc(var(--width) / 2);
    --half-width-with-indent: calc(var(--half-width) + 16px);
    --half-width-with-indent: calc(10% + var(--half-width) + 16px);
}

.margin {
    margin: calc(2 * var(--five));
    padding: var(--half-width-with-indent);
}


:is(.container) :is(.bold) {
    font-weight: bold;
}
:is(.container) :where(.bold) {
    grid-template-areas: 'myArea myArea myArea myArea myArea';
}
div :is(.test),
div:is(.test) {
    color: var(--blue);
}
").Code, Is.EqualTo("body{font-family:'Franklin Gothic Medium','Arial Narrow',Arial,sans-serif}:root{--blue:'blue';--five:5px;--width:100px;--half-width:calc(var(--width)/2);--half-width-with-indent:calc(var(--half-width) + 16px);--half-width-with-indent:calc(10% + var(--half-width) + 16px)}.margin{margin:calc(2*var(--five));padding:var(--half-width-with-indent)}:is(.container) :is(.bold){font-weight:bold}:is(.container) :where(.bold){grid-template-areas:'myArea myArea myArea myArea myArea'}div :is(.test),div:is(.test){color:var(--blue)}"));
        }

        [Test]
        public void Bug302()
        {
	        Assert.That(Uglify.Css(@"
p {
	background-color: var(--_flumo-grid-secondary-border-color) !important;
}").Code, Is.EqualTo("p{background-color:var(--_flumo-grid-secondary-border-color)!important}"));
        }

        [Test]
        public void Bug309()
        {
	        Assert.That(Uglify.Css(@"
body
{
	border-top-width: 0.5vmax;
	--custom-property: 0px; 
}").Code, Is.EqualTo("body{border-top-width:.5vmax;--custom-property:0px}"));
        }


        [Test]
        public void Bug331()
        {
	        TestHelper.Instance.RunTest();
        }

        [Test]
        public void Bug412()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void Bug415()
        {
            var uglifyResult = Uglify.Css(@"
.test-class {
    max-height: 70dvh;
    overflow-y: hidden;
}");

            Assert.That(uglifyResult.Code, Is.EqualTo(".test-class{max-height:70dvh;overflow-y:hidden}"));
            Assert.That(uglifyResult.HasErrors, Is.False);
        }

        [Test]
        public void Bug317()
        {
	        Assert.That(Uglify.Css(@"
body
{
	border: none;
	border-top: none;
	border-right: none;
	border-bottom: none;
	border-left: none;
	outline: none;
}").Code, Is.EqualTo("body{border:0;border-top:0;border-right:0;border-bottom:0;border-left:0;outline:0}"));
        }

        [Test]
        public void Bug343()
        {
            Assert.That(Uglify.Css(@"
body
{
	border: 0 none;
	border: 0 none #f00;
}").Code, Is.EqualTo("body{border:0 none;border:0 none #f00}"));
        }


        [Test]
        public void Bug366()
        {
	        Assert.That(Uglify.Css(@"
#thisisanid :nth-child(1 of ul.firstclass ~ ul:not(.secondclass)) {
  background: #ff0000;
}
").Code, Is.EqualTo("#thisisanid :nth-child(1 of ul.firstclass~ul:not(.secondclass)){background:#f00}"));
        }


        [Test]
        public void Bug379()
        {
	        Assert.That(Uglify.Css(@"
.\!inline-flex {
    display: inline-flex!important
}
").Code, Is.EqualTo(".\\!inline-flex{display:inline-flex!important}"));
        }

        [Test]
        public void Bug384()
        {
            var uglifyResult = Uglify.Css("grid-template-rows: 0fr 0px 0% 0em;");
            Assert.That(uglifyResult.Code, Is.EqualTo("grid-template-rows: 0fr 0 0% 0;"));
        }
        
        [Test]
        public void Bug407()
        {
            //End statement semicolon is optional
            Assert.That(Uglify.Css(@"
*, ::before, ::after {
	--tw-contain-paint:  ;
	--tw-contain-style:
}").HasErrors, Is.False);
        }

        [Test]
        public void Bug424()
        {
            var uglifyResult = Uglify.Css(@"h1 {
                font-size: clamp(1.3rem, 1.4rem + 0.3vw, 1.7rem); }");

            //TestHelper.Instance.RunTest();  //this don't check if HasErrors is true or false! Doing manually
            Assert.That(uglifyResult.Code, Is.EqualTo("h1{font-size:clamp(1.3rem,1.4rem + .3vw,1.7rem)}"));
            Assert.That(uglifyResult.HasErrors, Is.False);
        }

        [Test]
        public void Bug439()
        {
            var src = @"
@media (width < 250px) or (height < 500px)
{
    body
    {
        color: red;
    }
}
";

            AssertMinified(src, "@media(width<250px) or (height<500px){body{color:#f00}}");
        }

        [Test]
        public void Bug439MediaQueryRangeGreaterOrEqual()
        {
            AssertMinified("@media (width >= 600px) { body { color: red; } }",
                "@media(width>=600px){body{color:#f00}}");
        }

        [Test]
        public void Bug439MediaQueryRangeEquals()
        {
            AssertMinified("@media (width = 600px) { body { color: red; } }",
                "@media(width=600px){body{color:#f00}}");
        }

        [Test]
        public void Bug439MediaQueryRangeDoubleSided()
        {
            AssertMinified("@media (400px <= width <= 700px) { body { color: red; } }",
                "@media(400px<=width<=700px){body{color:#f00}}");
        }

        [Test]
        public void Bug439MediaQueryRangeWithMediaTypeAnd()
        {
            AssertMinified("@media screen and (width > 100px) { body { color: red; } }",
                "@media screen and (width>100px){body{color:#f00}}");
        }

        private void AssertMinified(string source, string expected)
        {
            var result = Uglify.Css(source);
            Assert.That(result.HasErrors, Is.False,
                () => "Uglify errors:\n" + string.Join("\n", result.Errors));
            Assert.That(result.Code, Is.EqualTo(expected));
        }
    }
}
