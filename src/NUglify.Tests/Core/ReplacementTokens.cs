using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using NUglify.Css;
using NUglify.JavaScript;
using NUglify.JavaScript.Visitors;
using NUnit.Framework;

namespace NUglify.Tests.Core
{
    /// <summary>
    /// Summary description for ReplacementTokens
    /// </summary>
    [TestFixture]
    public class ReplacementTokens
    {
        #region private fields

        static string s_inputFolder;

        static string s_outputFolder;

        static string s_expectedFolder;

        #endregion

        #region Additional test attributes

        /// <summary>
        ///Gets or sets the test context which provides
        ///information about and functionality for the current test run.
        ///</summary>
        public TestContext TestContext { get; set; }

        // Use ClassInitialize to run code before running the first test in the class
        [SetUp]
        public void Setup()
        {
            var testClassName = TestContext.CurrentContext.Test.ClassName.Substring(
                TestContext.CurrentContext.Test.ClassName.LastIndexOf('.') + 1);

            s_inputFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, @"TestData\Core", "Input", testClassName);
            s_outputFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, @"TestData\Core", "Output", testClassName);
            s_expectedFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, @"TestData\Core", "Expected", testClassName);

            // make sure the output folder exists
            Directory.CreateDirectory(s_outputFolder);
        }


        #endregion

        [Test]
        public void ReplacementStringsJS()
        {
            // reuse the same parser object
            var parser = new JSParser();

            // default should leave tokens intact
            var settings = new CodeSettings();
            var source = "var a = 'He said, %My.Token:foo%'";
            var actual = Parse(parser, settings, source);
            Assert.That(actual, Is.EqualTo("var a=\"He said, %My.Token:foo%\""));

            settings.ReplacementTokensApplyDefaults(new Dictionary<string, string> { 
                    { "my.token", "\"Now he's done it!\"" },
                    { "num_token", "123" },
                    { "my-json", "{\"a\": 1, \"b\": 2, \"c\": [ 1, 2, 3 ] }" },
                });
            settings.ReplacementFallbacks.Add("zero", "0");

            actual = Parse(parser, settings, source);
            Assert.That(actual, Is.EqualTo("var a='He said, \"Now he\\'s done it!\"'"));

            actual = Parse(parser, settings, "var b = '%Num_Token%';");
            Assert.That(actual, Is.EqualTo("var b=\"123\""));

            actual = Parse(parser, settings, "var c = '%My-JSON%';");
            Assert.That(actual, Is.EqualTo("var c='{\"a\": 1, \"b\": 2, \"c\": [ 1, 2, 3 ] }'"));
        }

        [Test]
        public void CurrentCultureReplacementTokensJS()
        {
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                var actual = Parse(new JSParser(), new CodeSettings(), "var a=%CurrentCulture.Name%;var b=%CurrentCulture.NumberFormat.CurrencySymbol%;var c=%CurrentCulture.DateTimeFormat.MonthNames.0%;");
                Assert.That(actual, Is.EqualTo("var a=\"en-US\",b=\"$\",c=\"January\""));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        [TestCaseSource(nameof(SupportedCulturePropertyCases))]
        public void CurrentCultureReplacementResolvesEverySupportedProperty(string propertyPath)
        {
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                var value = GetCulturePropertyValue(propertyPath);
                var settings = new CodeSettings();
                var actual = Parse(new JSParser(), settings, "var value=%CurrentCulture." + propertyPath + "%;");
                var expected = Parse(new JSParser(), settings, "var value=" + GetExpectedLiteral(value) + ";");

                Assert.That(actual, Is.EqualTo(expected), propertyPath);
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        [TestCase("")]
        [TestCase("NumberFormat")]
        [TestCase("DateTimeFormat")]
        [TestCase("TextInfo")]
        [TestCase("Calendar")]
        public void CurrentCultureReplacementCreatesLiteralsForSupportedObjectTypesJS(string propertyPath)
        {
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                var tokenPath = string.IsNullOrEmpty(propertyPath) ? "CurrentCulture" : "CurrentCulture." + propertyPath;
                var actual = Parse(new JSParser(), new CodeSettings(), "var value=%" + tokenPath + "%;");

                foreach (var expectedProperty in GetExpectedCultureObjectProperties(propertyPath))
                {
                    Assert.That(actual, Does.Contain(expectedProperty + ":"), expectedProperty);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        [Test]
        public void CurrentCultureReplacementLeavesUnknownPropertiesUnchangedJS()
        {
            var actual = Parse(new JSParser(), new CodeSettings(), "var value=%CurrentCulture.NumberFormat.UnknownProperty%;");

            Assert.That(actual, Is.EqualTo("var value=%CurrentCulture.NumberFormat.UnknownProperty%"));
        }

        [Test]
        public void ReplacementNodesJS()
        {
            // reuse the same parser object
            var parser = new JSParser();

            // default should leave tokens intact
            var settings = new CodeSettings();
            var source = "var a = %My.Token:foo%;";
            var actual = Parse(parser, settings, source);
            Assert.That(actual, Is.EqualTo("var a=%My.Token:foo%"));

            settings.ReplacementTokensApplyDefaults(new Dictionary<string, string> { 
                    { "my.token", "\"Now he's done it!\"" },
                    { "num_token", "123" },
                    { "my-json", "{\"a\": 1, \"b\": 2, \"c\": [ 1, 2, 3 ] }" },
                });
            settings.ReplacementFallbacks.Add("zero", "0");

            actual = Parse(parser, settings, source);
            Assert.That(actual, Is.EqualTo("var a=\"Now he's done it!\""));

            actual = Parse(parser, settings, "var b = %Num_Token%;");
            Assert.That(actual, Is.EqualTo("var b=123"));

            actual = Parse(parser, settings, "var c = %My-JSON%;");
            Assert.That(actual, Is.EqualTo("var c={\"a\":1,\"b\":2,\"c\":[1,2,3]}"));

            actual = Parse(parser, settings, "var d = '*%MissingToken:zero%*';");
            Assert.That(actual, Is.EqualTo("var d=\"*0*\""));

            actual = Parse(parser, settings, "var e = '*%MissingToken:ack%*';");
            Assert.That(actual, Is.EqualTo("var e=\"**\""));

            actual = Parse(parser, settings, "var f = '*%MissingToken:%*';");
            Assert.That(actual, Is.EqualTo("var f=\"**\""));
        }

        [Test]
        public void ReplacementFallbacksJS()
        {
            // reuse the same parser object
            var parser = new JSParser();

            // default should leave tokens intact
            var settings = new CodeSettings();

            settings.ReplacementTokensApplyDefaults(new Dictionary<string, string> { 
                    { "my.token", "\"Now he's done it!\"" },
                    { "num_token", "123" },
                    { "my-json", "{\"a\": 1, \"b\": 2, \"c\": [ 1, 2, 3 ] }" },
                });
            settings.ReplacementFallbacks.Add("zero", "0");

            var actual = Parse(parser, settings, "var a = %MissingToken:zero%;");
            Assert.That(actual, Is.EqualTo("var a=0"));

            actual = Parse(parser, settings, "var b = %MissingToken:ack% + 0;");
            Assert.That(actual, Is.EqualTo("var b=+0"));

            actual = Parse(parser, settings, "var c = %MissingToken:% + 0;");
            Assert.That(actual, Is.EqualTo("var c=+0"));

            actual = Parse(parser, settings, "var d = %MissingToken:%;debugger;throw 'why?';");
            Assert.That(actual, Is.EqualTo("var d=;throw\"why?\";"));
        }

        [Test]
        public void ReplacementTokensCSS()
        {
            var source = ReadFile(s_inputFolder, "replacements.css");

            var settings = new CssSettings();
            settings.ReplacementTokensApplyDefaults(new Dictionary<string, string> { 
                    { "MediaQueries.SnapMax", "600px" },
                    { "bing-orange", "#930" },
                    { "MetroSdk.Resolution", "24x" },
                    { "Global.Right", "right" },
                    { "dim-gray", "#cccccc" },
                    { "theme_name", "green" },
                    { "Module-ID", "weather" },
                });
            settings.ReplacementFallbacks.Add("full", "100%");
            settings.ReplacementFallbacks.Add("1x", "1x");
            settings.ReplacementFallbacks.Add("color", "#ff0000");

            var actual = Uglify.Css(source, settings);

            var expected = ReadFile(s_expectedFolder, "replacements.css");
            Assert.That(actual.Code, Is.EqualTo(expected));
        }

        string ReadFile(string folder, string fileName)
        {
            var inputPath = Path.Combine(folder, fileName);
            using (var reader = new StreamReader(inputPath))
            {
                return reader.ReadToEnd();
            }
        }

        static string[] GetExpectedCultureObjectProperties(string propertyPath)
        {
            switch (propertyPath)
            {
                case "":
                    return new[] { "Calendar", "CompareInfo", "CultureTypes", "DateTimeFormat", "DisplayName", "EnglishName", "IetfLanguageTag", "IsNeutralCulture", "IsReadOnly", "LCID", "Name", "NativeName", "NumberFormat", "OptionalCalendars", "Parent", "TextInfo", "ThreeLetterISOLanguageName", "ThreeLetterWindowsLanguageName", "TwoLetterISOLanguageName", "UseUserOverride" };
                case "NumberFormat":
                    return new[] { "CurrencyDecimalDigits", "CurrencyDecimalSeparator", "CurrencyGroupSeparator", "CurrencyGroupSizes", "CurrencyNegativePattern", "CurrencyPositivePattern", "CurrencySymbol", "IsReadOnly", "NaNSymbol", "NativeDigits", "NegativeInfinitySymbol", "NegativeSign", "NumberDecimalDigits", "NumberDecimalSeparator", "NumberGroupSeparator", "NumberGroupSizes", "NumberNegativePattern", "PercentDecimalDigits", "PercentDecimalSeparator", "PercentGroupSeparator", "PercentGroupSizes", "PercentNegativePattern", "PercentPositivePattern", "PercentSymbol", "PerMilleSymbol", "PositiveInfinitySymbol", "PositiveSign" };
                case "DateTimeFormat":
                    return new[] { "AbbreviatedDayNames", "AbbreviatedMonthGenitiveNames", "AbbreviatedMonthNames", "AMDesignator", "Calendar", "CalendarWeekRule", "DateSeparator", "DayNames", "FirstDayOfWeek", "FullDateTimePattern", "IsReadOnly", "LongDatePattern", "LongTimePattern", "MonthDayPattern", "MonthGenitiveNames", "MonthNames", "NativeCalendarName", "PMDesignator", "RFC1123Pattern", "ShortDatePattern", "ShortestDayNames", "ShortTimePattern", "SortableDateTimePattern", "TimeSeparator", "UniversalSortableDateTimePattern", "YearMonthPattern" };
                case "TextInfo":
                    return new[] { "ANSICodePage", "CultureName", "EBCDICCodePage", "IsReadOnly", "IsRightToLeft", "ListSeparator", "MacCodePage", "OEMCodePage" };
                case "Calendar":
                    return new[] { "AlgorithmType", "Eras", "IsReadOnly", "MaxSupportedDateTime", "MinSupportedDateTime", "TwoDigitYearMax" };
                default:
                    throw new ArgumentOutOfRangeException(nameof(propertyPath));
            }
        }

        static IEnumerable<TestCaseData> SupportedCulturePropertyCases()
        {
            foreach (var propertyPath in new[]
            {
                "CultureTypes", "DisplayName", "EnglishName", "IetfLanguageTag", "IsNeutralCulture", "IsReadOnly", "LCID", "Name", "NativeName", "ThreeLetterISOLanguageName", "ThreeLetterWindowsLanguageName", "TwoLetterISOLanguageName", "UseUserOverride",
                "NumberFormat.CurrencyDecimalDigits", "NumberFormat.CurrencyDecimalSeparator", "NumberFormat.CurrencyGroupSeparator", "NumberFormat.CurrencyGroupSizes", "NumberFormat.CurrencyNegativePattern", "NumberFormat.CurrencyPositivePattern", "NumberFormat.CurrencySymbol", "NumberFormat.IsReadOnly", "NumberFormat.NaNSymbol", "NumberFormat.NativeDigits", "NumberFormat.NegativeInfinitySymbol", "NumberFormat.NegativeSign", "NumberFormat.NumberDecimalDigits", "NumberFormat.NumberDecimalSeparator", "NumberFormat.NumberGroupSeparator", "NumberFormat.NumberGroupSizes", "NumberFormat.NumberNegativePattern", "NumberFormat.PercentDecimalDigits", "NumberFormat.PercentDecimalSeparator", "NumberFormat.PercentGroupSeparator", "NumberFormat.PercentGroupSizes", "NumberFormat.PercentNegativePattern", "NumberFormat.PercentPositivePattern", "NumberFormat.PercentSymbol", "NumberFormat.PerMilleSymbol", "NumberFormat.PositiveInfinitySymbol", "NumberFormat.PositiveSign",
                "DateTimeFormat.AbbreviatedDayNames", "DateTimeFormat.AbbreviatedMonthGenitiveNames", "DateTimeFormat.AbbreviatedMonthNames", "DateTimeFormat.AMDesignator", "DateTimeFormat.CalendarWeekRule", "DateTimeFormat.DateSeparator", "DateTimeFormat.DayNames", "DateTimeFormat.FirstDayOfWeek", "DateTimeFormat.FullDateTimePattern", "DateTimeFormat.IsReadOnly", "DateTimeFormat.LongDatePattern", "DateTimeFormat.LongTimePattern", "DateTimeFormat.MonthDayPattern", "DateTimeFormat.MonthGenitiveNames", "DateTimeFormat.MonthNames", "DateTimeFormat.NativeCalendarName", "DateTimeFormat.PMDesignator", "DateTimeFormat.RFC1123Pattern", "DateTimeFormat.ShortDatePattern", "DateTimeFormat.ShortestDayNames", "DateTimeFormat.ShortTimePattern", "DateTimeFormat.SortableDateTimePattern", "DateTimeFormat.TimeSeparator", "DateTimeFormat.UniversalSortableDateTimePattern", "DateTimeFormat.YearMonthPattern",
                "TextInfo.ANSICodePage", "TextInfo.CultureName", "TextInfo.EBCDICCodePage", "TextInfo.IsReadOnly", "TextInfo.IsRightToLeft", "TextInfo.ListSeparator", "TextInfo.MacCodePage", "TextInfo.OEMCodePage",
                "Calendar.AlgorithmType", "Calendar.Eras", "Calendar.IsReadOnly", "Calendar.MaxSupportedDateTime", "Calendar.MinSupportedDateTime", "Calendar.TwoDigitYearMax",
            })
            {
                yield return new TestCaseData(propertyPath);
            }
        }

        static object GetCulturePropertyValue(string propertyPath)
        {
            object value = CultureInfo.CurrentCulture;
            foreach (var propertyName in propertyPath.Split('.'))
            {
                var array = value as Array;
                int index;
                if (array != null && int.TryParse(propertyName, out index))
                {
                    value = array.GetValue(index);
                }
                else
                {
                    value = value.GetType().GetProperty(propertyName).GetValue(value, null);
                }
            }

            return value;
        }

        static string GetExpectedLiteral(object value)
        {
            if (value is string)
            {
                return JsonSerializer.Serialize(value);
            }

            if (value is DateTime)
            {
                return JsonSerializer.Serialize(((DateTime)value).ToString("s", CultureInfo.InvariantCulture));
            }

            if (value.GetType().IsEnum)
            {
                return JsonSerializer.Serialize(value.ToString());
            }

            return JsonSerializer.Serialize(value);
        }

        string Parse(JSParser parser, CodeSettings settings, string source)
        {
            var block = parser.Parse(source, settings);
            return OutputVisitor.Apply(block, settings);
        }
    }
}
