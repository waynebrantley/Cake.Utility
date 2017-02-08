using System;
using System.Text.RegularExpressions;
using Cake.Common.Build.AppVeyor;
using Cake.Common.Build.AppVeyor.Data;
using Cake.Core;
using Cake.Core.IO;
using Cake.Core.Tooling;
using Cake.Testing;
using NSubstitute;
using NUnit.Framework;

namespace Cake.Utility.Tests
{
    [TestFixture]
    public class BuildVersionTests
    {
        private FakeEnvironment _environment;
        private FakeLog _log;
        private ICakeArguments _arguments;
        private IAppVeyorProvider _appVeyor;
        private AppVeyorEnvironmentInfo _appEnvironment;
        private IGlobber _globber;
        private FakeFileSystem _fileSystem;
        private IProcessRunner _processRunner;
        private IToolLocator _toolLocator;
        private const string FallBackVersion = "1.1.1";
        [SetUp]
        public void Setup()
        {
            _environment = FakeEnvironment.CreateWindowsEnvironment();
            _log = new FakeLog();
            _arguments = Substitute.For<ICakeArguments>();
            _appVeyor = Substitute.For<IAppVeyorProvider>();
            _appEnvironment = new AppVeyorEnvironmentInfo(_environment);
            _appVeyor.Environment.Returns(_appEnvironment);
            _globber = Substitute.For<IGlobber>();
            _fileSystem = new FakeFileSystem(_environment);
            _processRunner = Substitute.For<IProcessRunner>();
            _toolLocator = Substitute.For<IToolLocator>();
        }

        [Test]
        public void BuildVersion_SpecifiedOnCommandLine()
        {
            _arguments.HasArgument(VersionHelper.DefaultBuildVersionArgumentName).Returns(true);
            _arguments.GetArgument(VersionHelper.DefaultBuildVersionArgumentName).Returns("2.3.4");
            var versionHelper = GetVersionHelper(VersionHelper.DefaultDefaultBranchName);
            Assert.That(versionHelper.GetBaseVersionString(FallBackVersion), Is.EqualTo("2.3.4"));
            Assert.That(versionHelper.IsAppVeyor, Is.EqualTo(false));
        }

        [Test]
        public void BuildVersion_NoBuildEnv_UsesDefault()
        {
            _arguments.HasArgument(VersionHelper.DefaultBuildVersionArgumentName).Returns(false);
            var versionHelper = GetVersionHelper(VersionHelper.DefaultDefaultBranchName);
            var info = versionHelper.GetNextVersion(FallBackVersion);
            Assert.That(versionHelper.GetBaseVersionString(FallBackVersion), Is.EqualTo(FallBackVersion));
            Assert.That(versionHelper.IsAppVeyor, Is.EqualTo(false));

        }

        [TestCase(true)]
        [TestCase(false)]
        public void AppVeyor_BuildVersion_Read(bool isMaster)
        {
            _arguments.HasArgument(VersionHelper.DefaultBuildVersionArgumentName).Returns(false);
            _appVeyor.IsRunningOnAppVeyor.Returns(true);
            _environment.SetEnvironmentVariable("APPVEYOR_BUILD_VERSION", "2.3.4");
            var versionHelper = GetVersionHelper(isMaster ? VersionHelper.DefaultDefaultBranchName : "someFeature");

            Assert.That(versionHelper.IsAppVeyor, Is.EqualTo(true));
            Assert.That(versionHelper.GetBaseVersionString(FallBackVersion), Is.EqualTo("2.3.4"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BuildVersion_PreReleaseAdded(bool isPreRelease)
        {
            _arguments.HasArgument(VersionHelper.DefaultBuildVersionArgumentName).Returns(true);
            _arguments.GetArgument(VersionHelper.DefaultBuildVersionArgumentName).Returns("2.3.4");
            var versionHelper = GetVersionHelper(!isPreRelease ? VersionHelper.DefaultDefaultBranchName : "someFeature");
            var info = versionHelper.GetNextVersion(FallBackVersion);

            Assert.That(info.IsPreRelease, Is.EqualTo(isPreRelease));
            Assert.That(info.RootVersion, Is.EqualTo("2.3.4"));

            if (isPreRelease)
                Assert.That(info.FullVersion, Is.EqualTo(info.RootVersion + "-someFeature"));
            else
                Assert.That(info.FullVersion, Is.EqualTo(info.RootVersion));
        }

        [Test]
        public void BuildVersion_LongPreReleaseTruncated_AndDashesRemoved()
        {
            _arguments.HasArgument(VersionHelper.DefaultBuildVersionArgumentName).Returns(true);
            _arguments.GetArgument(VersionHelper.DefaultBuildVersionArgumentName).Returns("2.3.4");
            var versionHelper = GetVersionHelper("1234567890_1234567890_1234567890");
            var info = versionHelper.GetNextVersion(FallBackVersion);

            Assert.That(info.IsPreRelease, Is.EqualTo(true));
            Assert.That(info.RootVersion, Is.EqualTo("2.3.4"));
            Assert.That(info.FullVersion, Is.EqualTo(info.RootVersion + "-1234567890123456789"));
        }

        [TestCase("[deploy uat4]", true)]
        [TestCase("deploy uat4", false)]
        [TestCase("[DePloy   Uat4]", true)]
        [TestCase("[DoPloy   Uat4]", false)]
        [TestCase("[DePloy   ]", false)]
        public void DetectDeploymentTarget(string expression, bool shouldMatch)
        {
            var matches = VersionHelper.CommitMessageRegex.Match(expression);
            Assert.That(matches.Success, Is.EqualTo(shouldMatch));
            if (matches.Success)
            {
                Assert.That(matches.Groups.Count, Is.EqualTo(3));
                Assert.That(matches.Groups["command"].Value.ToLower(), Is.EqualTo("deploy"));
                Assert.That(matches.Groups["argument"].Value.ToLower(), Is.EqualTo("uat4"));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AppVeyor_AutoDeploy(bool isMaster)
        {
            _arguments.HasArgument(VersionHelper.DefaultBuildVersionArgumentName).Returns(false);
            _appVeyor.IsRunningOnAppVeyor.Returns(true);
            _environment.SetEnvironmentVariable("APPVEYOR_BUILD_VERSION", "2.3.4");
            _environment.SetEnvironmentVariable("APPVEYOR_REPO_COMMIT_MESSAGE_EXTENDED", "[deploy UAT5]");
            
            var versionHelper = GetVersionHelper(isMaster ? VersionHelper.DefaultDefaultBranchName : "someFeature");

            Assert.That(versionHelper.IsAppVeyor, Is.EqualTo(true));
            Assert.That(versionHelper.AutoDeploy, Is.EqualTo(!isMaster));
        }

        [TestCase("[deploy UAT5]", "uat5")]
        [TestCase("[deploy    uat7]", "uat7")]
        public void AppVeyor_AutoDeploy_RightMachine(string command, string targetMachine)
        {
            _arguments.HasArgument(VersionHelper.DefaultBuildVersionArgumentName).Returns(false);
            _appVeyor.IsRunningOnAppVeyor.Returns(true);
            _environment.SetEnvironmentVariable("APPVEYOR_BUILD_VERSION", "2.3.4");
            _environment.SetEnvironmentVariable("APPVEYOR_REPO_COMMIT_MESSAGE_EXTENDED", command);

            var versionHelper = GetVersionHelper("someFeature");

            Assert.That(versionHelper.IsAppVeyor, Is.EqualTo(true));
            Assert.That(versionHelper.AutoDeploy, Is.EqualTo(true));
            Assert.That(versionHelper.AutoDeployTarget, Is.EqualTo(targetMachine));


            foreach(string groupName in VersionHelper.CommitMessageRegex.GetGroupNames())
            {
                Console.WriteLine($"{groupName} : {versionHelper.CommitMessageMatches.Groups[groupName].Value}");
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Interactive_NoAutoDeploy(bool isMaster)
        {
            _arguments.HasArgument(VersionHelper.DefaultBuildVersionArgumentName).Returns(false);
            _appVeyor.IsRunningOnAppVeyor.Returns(false);

            var versionHelper = GetVersionHelper(isMaster ? VersionHelper.DefaultDefaultBranchName : "someFeature");

            Assert.That(versionHelper.AutoDeploy, Is.EqualTo(false));
        }


        [Test]
        public void RegExBuildsCorrectly()
        {
            string[] commands = { "Deploy" };
            var regExString= $@"\[(?<command>(?i){string.Join("|", commands)}) +(?<argument>[\w\.]+)\]+";
            Assert.That(regExString, Is.EqualTo(@"\[(?<command>(?i)Deploy) +(?<argument>[\w\.]+)\]+"));
            var builtRegEx = new Regex(regExString);
            Assert.That(builtRegEx.IsMatch("[deploy uat4]"), Is.True);
        }

        private VersionHelper GetVersionHelper(string branch)
        {
            return new VersionHelper(_environment, _log, _arguments,  _appVeyor, _globber, _fileSystem, _processRunner, _toolLocator) { Branch = branch };
        }
    }
}
