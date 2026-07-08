using System.Collections;
using System.Diagnostics;
using System.Reflection;
using NUglify.Css;
using NUglify.JavaScript;
using NUnit.Framework;

namespace NUglify.Tests.Core
{
    /// <summary>
    /// Summary description for CommandLine
    /// </summary>
    [TestFixture]
    public class CommandLine
    {
        public CommandLine()
        {
        }

        TestContext testContextInstance;

        /// <summary>
        ///Gets or sets the test context which provides
        ///information about and functionality for the current test run.
        ///</summary>
        public TestContext TestContext
        {
            get
            {
                return testContextInstance;
            }
            set
            {
                testContextInstance = value;
            }
        }

        [Test]
        public void ToArguments()
        {
            var testData = new ArgumentsData[] {
                new ArgumentsData(){CommandLine=null, Arguments=new string[0]},
                new ArgumentsData(){CommandLine="", Arguments=new string[0]},
                new ArgumentsData(){CommandLine="              ", Arguments=new string[0]},
                new ArgumentsData(){CommandLine="-ei:utf-8 -eo:utf-8 -warn:4 /G:jQuery -p -z", Arguments=new string[] {"-ei:utf-8","-eo:utf-8","-warn:4","/G:jQuery","-p","-z"}},
                new ArgumentsData(){CommandLine="\"foo bar.js\" -out \"c:\\test folder\\foo bar min.js\" ", Arguments=new string[] {"foo bar.js", "-out", "c:\\test folder\\foo bar min.js"}},
                new ArgumentsData(){CommandLine="foo\"bar\"ack", Arguments=new string[] {"foobarack"}},
                new ArgumentsData(){CommandLine="foo \"\"\"\" bar", Arguments=new string[] {"foo", "\"", "bar"}},
                new ArgumentsData(){CommandLine="now \" is the time \" for", Arguments=new string[] {"now"," is the time ","for"}},
                new ArgumentsData(){CommandLine="now \" is \"\"the\"\" time \" for", Arguments=new string[] {"now"," is \"the\" time ","for"}},
                new ArgumentsData(){CommandLine="now \"\" \" is \"\"the\"\" time \" for", Arguments=new string[] {"now", "", " is \"the\" time ","for"}},
            };

            var ndxTest = 0;
            foreach (var test in testData)
            {
                Trace.Write(string.Format("Parsing test {0}, command line: ", ++ndxTest));
                Trace.WriteLine(test.CommandLine ?? "<null pointer>");

                var argsActual = UglifyCommandParser.ToArguments(test.CommandLine);
                var argsExpected = test.Arguments;

                // assume succesful unless proven otherwise
                var success = true;

                Assert.That(argsActual.Length == argsExpected.Length, string.Format("Parsed arguments length {0} not equal to expected arguments length {1}", argsActual.Length, argsExpected.Length));
                Trace.WriteLine(string.Format("    {0} arguments", argsActual.Length));
                for (var ndxArg = 0; ndxArg < argsActual.Length; ++ndxArg)
                {
                    var theSame = string.CompareOrdinal(argsActual[ndxArg], argsExpected[ndxArg]) == 0;
                    Trace.WriteLine(string.Format("        {0}: {1} {3} {2}", ndxArg + 1, argsActual[ndxArg], argsExpected[ndxArg], theSame ? "==" : "!="));
                    success = theSame ? success : false;
                }

                Assert.That(success, string.Format("TEST {0} FAILED!", ndxTest));
            }
        }

        [Test]
        public void ToSettings()
        {
            var testData = new ArgumentsSettings[] {
                new ArgumentsSettings(){CommandLine="-warn:4 -ei:utf-8 -enc:out utf-8 /g:jQuery,$,Msn -p", JSSettings=new CodeSettings(){OutputMode=OutputMode.MultipleLines, LocalRenaming=LocalRenaming.KeepAll, KnownGlobalNamesList="jQuery,$,Msn", MinifyCode=false, KillSwitch=-2}, CssSettings=new CssSettings(){OutputMode=OutputMode.MultipleLines, KillSwitch=-2}, WarningLevel=4, EncodingInputName="utf-8", EncodingOutputName="utf-8"},
                new ArgumentsSettings(){CommandLine="-minify:false -rename:none", JSSettings=new CodeSettings(){MinifyCode=false, LocalRenaming=LocalRenaming.KeepAll}, CssSettings=null, WarningLevel=0},
                new ArgumentsSettings(){CommandLine="-define:foo,bar,ack,gag,42", JSSettings=new CodeSettings(){PreprocessorDefineList="foo,bar,ack,gag"}, CssSettings=new CssSettings(){PreprocessorDefineList="foo,bar,ack,gag"}},
                new ArgumentsSettings(){CommandLine="-ignore:foo,bar,ack,gag", JSSettings=new CodeSettings(){IgnoreErrorList="foo,bar,ack,gag"}, CssSettings=new CssSettings(){IgnoreErrorList="foo,bar,ack,gag"}},
                new ArgumentsSettings(){CommandLine="/aspnet:T -pretty:8 -term:Yes", JSSettings=new CodeSettings(){AllowEmbeddedAspNetBlocks=true,TermSemicolons=true,LocalRenaming=LocalRenaming.KeepAll,OutputMode=OutputMode.MultipleLines,Indent=new string(' ', 8), MinifyCode=false, KillSwitch=-2}, CssSettings=new CssSettings(){AllowEmbeddedAspNetBlocks=true,TermSemicolons=true,OutputMode=OutputMode.MultipleLines,Indent=new string(' ', 8), KillSwitch=-2}},
                new ArgumentsSettings(){CommandLine="/aspnet:F -minify:1 -kill:0 -pretty:0 -term:N", JSSettings=new CodeSettings(){AllowEmbeddedAspNetBlocks=false,TermSemicolons=false,LocalRenaming=LocalRenaming.KeepAll,OutputMode=OutputMode.MultipleLines,Indent=string.Empty}, CssSettings=new CssSettings(){AllowEmbeddedAspNetBlocks=false,TermSemicolons=false,OutputMode=OutputMode.MultipleLines,Indent=string.Empty}},
                new ArgumentsSettings(){CommandLine="-expr:minify -colors:hex -comments:All", JSSettings=null, CssSettings=new CssSettings(){MinifyExpressions=true,ColorNames=CssColor.Hex,CommentMode=CssComment.All}},
                new ArgumentsSettings(){CommandLine="-expr:raw -colors:MAJOR /comments:HaCkS", JSSettings=null, CssSettings=new CssSettings(){MinifyExpressions=false,ColorNames=CssColor.Major,CommentMode=CssComment.Hacks}},
                new ArgumentsSettings(){CommandLine="-cc:1 -comments:None -debug:true -inline:yes -literals:keep -literals:evAL -mac:Y -minify:T -new:Keep -reorder:yes -unused:remove -rename:all", 
                    JSSettings=new CodeSettings(){IgnoreConditionalCompilation=false,PreserveImportantComments=false,StripDebugStatements=false,InlineSafeStrings=true,EvalLiteralExpressions=true,MacSafariQuirks=true,MinifyCode=true,CollapseToLiteral=false,ReorderScopeDeclarations=true,RemoveUnneededCode=true,LocalRenaming=LocalRenaming.CrunchAll, PreprocessorDefineList="debug"}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-cc:0 -comments:ImportanT -debug:false -inline:no /literals:combine -literals:NoEval -mac:N -minify:F -new:Collapse -reorder:N -unused:keep -rename:localization", 
                    JSSettings=new CodeSettings(){IgnoreConditionalCompilation=true,PreserveImportantComments=true,StripDebugStatements=true,InlineSafeStrings=false,EvalLiteralExpressions=false,MacSafariQuirks=false,MinifyCode=false,CollapseToLiteral=true,ReorderScopeDeclarations=false,RemoveUnneededCode=false, LocalRenaming=LocalRenaming.KeepLocalizationVars}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="–debug:,", JSSettings=new CodeSettings(){StripDebugStatements=false,DebugLookupList="",PreprocessorDefineList="debug"}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-debug:N,", JSSettings=new CodeSettings(){StripDebugStatements=true,DebugLookupList=""}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-debug:N,Foo,Bar,Ack.Gag.Barf,14,Name.42.First", JSSettings=new CodeSettings(){StripDebugStatements=true,DebugLookupList="Foo,Bar,Ack.Gag.Barf"}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-global:foo,bar,ack,gag,212", JSSettings=new CodeSettings(){KnownGlobalNamesList="foo,bar,ack,gag"}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-norename:foo,bar,ack,gag,105 -rename:NoProps", JSSettings=new CodeSettings(){NoAutoRenameList="foo,bar,ack,gag", ManualRenamesProperties=false}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-rename:foo=bar,ack=gag,105=106", JSSettings=new CodeSettings(){RenamePairs="foo=bar,ack=gag", ManualRenamesProperties=true}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="–fnames:lock -evals:ignore", JSSettings=new CodeSettings(){PreserveFunctionNames=true, RemoveFunctionExpressionNames=false, EvalTreatment=EvalTreatment.Ignore}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-fnames:keep -evals:immediate", JSSettings=new CodeSettings(){PreserveFunctionNames=false, RemoveFunctionExpressionNames=false, EvalTreatment=EvalTreatment.MakeImmediateSafe}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-fnames:onlyref -evals:safeall", JSSettings=new CodeSettings(){PreserveFunctionNames=false, RemoveFunctionExpressionNames=true, EvalTreatment=EvalTreatment.MakeAllSafe}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="–kill:-1", JSSettings=new CodeSettings(){KillSwitch=-1}, CssSettings=new CssSettings(){KillSwitch=-1,CommentMode=CssComment.None}},
                new ArgumentsSettings(){CommandLine="–kill:0x1", JSSettings=new CodeSettings(){KillSwitch=1}, CssSettings=new CssSettings(){KillSwitch=1,CommentMode=CssComment.None}},
                new ArgumentsSettings(){CommandLine="-kill:2", JSSettings=new CodeSettings(){KillSwitch=2}, CssSettings=new CssSettings(){KillSwitch=2}},
                new ArgumentsSettings(){CommandLine="-kill:0xDAB0 -cc:BOOYAH! -warn:-1", JSSettings=new CodeSettings(){KillSwitch=0xdab0}, CssSettings=new CssSettings(){KillSwitch=55984}, WarningLevel=0},
                new ArgumentsSettings(){CommandLine="-enc:in ascii -EO:big5 -warn", JSSettings=null, CssSettings=null, EncodingInputName="ascii", EncodingOutputName="big5", WarningLevel=int.MaxValue},
                new ArgumentsSettings(){CommandLine="-css /js", JSSettings=new CodeSettings(), CssSettings=new CssSettings()},
                new ArgumentsSettings(){CommandLine="-rename:All -pretty:WHAM", JSSettings=new CodeSettings(){OutputMode=OutputMode.MultipleLines,Indent="    ", KillSwitch=-16777218}, CssSettings=new CssSettings(){OutputMode=OutputMode.MultipleLines,Indent="    ", KillSwitch=-2}},
                new ArgumentsSettings(){CommandLine="-rename:All -pretty:-10", JSSettings=new CodeSettings(){OutputMode=OutputMode.MultipleLines,Indent="    ", KillSwitch=-16777218}, CssSettings=new CssSettings(){OutputMode=OutputMode.MultipleLines,Indent="    ", KillSwitch=-2}},
                new ArgumentsSettings(){CommandLine="–rename:foo=bar,foo=ack", JSSettings=new CodeSettings(){RenamePairs="foo=bar"}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="–d -h -j -k -m", JSSettings=new CodeSettings(), CssSettings=null},
                new ArgumentsSettings(){CommandLine="-l -z -hCL", JSSettings=new CodeSettings() {CollapseToLiteral=false, TermSemicolons=true, LocalRenaming=LocalRenaming.KeepLocalizationVars}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="/HC", JSSettings=new CodeSettings() {}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-define:debug", JSSettings=new CodeSettings() {StripDebugStatements=false, PreprocessorDefineList="debug"}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-define:debug -debug:0", JSSettings=new CodeSettings() {StripDebugStatements=true}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-debug:0 -define:debug", JSSettings=new CodeSettings() {StripDebugStatements=false, PreprocessorDefineList="debug"}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-define:foo=bar,bob,ack=gag,BaT=CrAzY", JSSettings=new CodeSettings() {PreprocessorDefineList="foo=bar,bob,ack=gag,BaT=CrAzY"}, CssSettings=new CssSettings(){PreprocessorDefineList="foo=bar,bob,ack=gag,BaT=CrAzY"}},
                new ArgumentsSettings(){CommandLine="-define:foo=", JSSettings=null, CssSettings=new CssSettings(){PreprocessorDefineList="foo"}},
                new ArgumentsSettings(){CommandLine="-define:configuration=Debug -define:debug=Y,ralph=driver", JSSettings=new CodeSettings() {StripDebugStatements=false,PreprocessorDefineList="configuration=Debug,debug=Y,ralph=driver"}, CssSettings=new CssSettings(){PreprocessorDefineList="configuration=Debug,debug=Y,ralph=driver"}},
                new ArgumentsSettings(){CommandLine="-define:once=first -define:OnCE=Last", JSSettings=new CodeSettings() {PreprocessorDefineList="once=Last"}, CssSettings=new CssSettings(){PreprocessorDefineList="once=Last"}},
            };

            var ndxTest = 0;
            foreach (var test in testData)
            {
                Trace.Write(string.Format("Settings test {0}, command line: ", ++ndxTest));
                Trace.WriteLine(test.CommandLine ?? "<null pointer>");

                // parse the command line
                var switchParser = new UglifyCommandParser();
                switchParser.Parse(test.CommandLine);

                // assume succesful unless proven otherwise
                var success = true;

                // test the top-level properties
                if (switchParser.WarningLevel == test.WarningLevel)
                {
                    Trace.WriteLine(string.Format("\tParsed warning level {0} matches expectations", switchParser.WarningLevel));
                }
                else
                {
                    Trace.WriteLine(string.Format("\tFAIL: Parsed warning level is {0}, expected is {1}", switchParser.WarningLevel, test.WarningLevel));
                    success = false;
                }

                if (string.CompareOrdinal(switchParser.EncodingInputName, test.EncodingInputName) == 0)
                {
                    Trace.WriteLine(string.Format("\tParsed input encoding {0} matches expectations", switchParser.EncodingInputName));
                }
                else
                {
                    Trace.WriteLine(string.Format("\tFAIL: Parsed input encoding is {0}, expected is {1}", switchParser.EncodingInputName, test.EncodingInputName));
                    success = false;
                }

                if (string.CompareOrdinal(switchParser.EncodingOutputName, test.EncodingOutputName) == 0)
                {
                    Trace.WriteLine(string.Format("\tParsed output encoding {0} matches expectations", switchParser.EncodingOutputName));
                }
                else
                {
                    Trace.WriteLine(string.Format("\tFAIL: Parsed output encoding is {0}, expected is {1}", switchParser.EncodingOutputName, test.EncodingOutputName));
                    success = false;
                }

                // if we care about the JS settings....
                if (test.JSSettings != null)
                {
                    var jsSuccess = CheckSettings(switchParser.JSSettings, test.JSSettings);
                    if (!jsSuccess)
                    {
                        success = false;
                    }
                }

                // if we care about the CSS settings....
                if (test.CssSettings != null)
                {
                    var cssSuccess = CheckSettings(switchParser.CssSettings, test.CssSettings);
                    if (!cssSuccess)
                    {
                        success = false;
                    }
                }


                Assert.That(success, string.Format("\t****TEST {0} FAILED!", ndxTest));
            }
        }

        bool CheckSettings(object actual, object expected)
        {
            var success = true;
            var type = actual.GetType();
            foreach (var property in type.GetProperties(BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.Public))
            {
                var parsedProperty = property.GetValue(actual, null);
                var expectedProperty = property.GetValue(expected, null);

                var parsedCollection = parsedProperty as ICollection;
                var expectedCollection = expectedProperty as ICollection;
                if (parsedCollection != null || expectedCollection != null)
                {
                    // if one is null or not a collection, then it's a fail
                    if (parsedCollection == null || expectedCollection == null)
                    {
                        Trace.WriteLine(string.Format("\tFAIL: Parsed {3} property {2} is {0}, expected is {1}", 
                            (parsedCollection == null ? "<null>" : parsedCollection.ToString()), 
                            (expectedCollection == null ? "<null>" : expectedCollection.ToString()), 
                            property.Name, type.Name));
                        success = false;
                    }
                    else if (parsedCollection.Count != expectedCollection.Count)
                    {
                        Trace.WriteLine(string.Format("\tFAIL: Parsed {3} property {2} collection has {0} elements, expected has {1}", parsedCollection.Count, expectedCollection.Count, property.Name, type.Name));
                        success = false;
                    }
                    else
                    {
                        Trace.WriteLine(string.Format("\tParsed {2} property {1} has {0} elements, as expected", parsedCollection.Count, property.Name, type.Name));
                    }
                }
                else if (!object.Equals(parsedProperty, expectedProperty))
                {
                    if (property.Name.EndsWith("Collection"))
                    {
                        // ignore it
                        success = true;
                    }
                    else
                    {
                        Trace.WriteLine(string.Format("\tFAIL: Parsed {3} property {2} is {0}, expected is {1}", parsedProperty ?? "<null>", expectedProperty ?? "<null>", property.Name, type.Name));
                        success = false;
                    }
                }
                else
                {
                    Trace.WriteLine(string.Format("\tParsed {2} property {1} is {0}", parsedProperty ?? "<null>", property.Name, type.Name));
                }
            }
            return success;
        }

        class ArgumentsData
        {
            public string CommandLine { get; set; }
            public string[] Arguments { get; set; }
        }

        class ArgumentsSettings
        {
            public string CommandLine { get; set; }
            public CodeSettings JSSettings { get; set; }
            public CssSettings CssSettings { get; set; }
            public int WarningLevel { get; set; }
            public string EncodingInputName { get; set; }
            public string EncodingOutputName { get; set; }
        }
    }
}
