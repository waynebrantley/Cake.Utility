using Cake.Common.Build.AppVeyor;
using Cake.Common.Build.AppVeyor.Data;
using Cake.Common.Build.TeamCity;
using Cake.Core;
using Cake.Core.IO;
using Cake.Testing;
using NSubstitute;
using NUnit.Framework;

namespace Cake.Utility.Tests
{
    [TestFixture]
    public class BuildVersionTests
    {
        FakeEnvironment _environment;
        FakeLog _log;
        ITeamCityProvider _teamCity;
        ICakeArguments _arguments;
        IAppVeyorProvider _appVeyor;
        AppVeyorEnvironmentInfo _appEnvironment;
        IGlobber _globber;
        FakeFileSystem _fileSystem;
        private const string FallBackVersion = "1.1.1";
        [SetUp]
        public void Setup()
        {
            _environment = FakeEnvironment.CreateWindowsEnvironment();
            _log = new FakeLog();
            _teamCity = Substitute.For<ITeamCityProvider>();
            _arguments = Substitute.For<ICakeArguments>();
            _appVeyor = Substitute.For<IAppVeyorProvider>();
            _appEnvironment = new AppVeyorEnvironmentInfo(_environment);
            _appVeyor.Environment.Returns(_appEnvironment);
            _globber = Substitute.For<IGlobber>();
            _fileSystem = new FakeFileSystem(_environment);
        }

        [Test]
        public void BuildVersion_SpecifiedOnCommandLine()
        {
            _arguments.HasArgument(VersionHelper.DefaultBuildVersionArgumentName).Returns(true);
            _arguments.GetArgument(VersionHelper.DefaultBuildVersionArgumentName).Returns("2.3.4");
            var versionHelper = GetVersionHelper(VersionHelper.DefaultDefaultBranchName);
            Assert.That(versionHelper.GetBaseVersionString(FallBackVersion), Is.EqualTo("2.3.4"));
            Assert.That(versionHelper.IsMyGet, Is.EqualTo(false));
            Assert.That(versionHelper.IsTeamCity, Is.EqualTo(false));
            Assert.That(versionHelper.IsAppVeyor, Is.EqualTo(false));
        }

        [Test]
        public void BuildVersion_NoBuildEnv_UsesDefault()
        {
            _arguments.HasArgument(VersionHelper.DefaultBuildVersionArgumentName).Returns(false);
            var versionHelper = GetVersionHelper(VersionHelper.DefaultDefaultBranchName);
            var info = versionHelper.GetNextVersion(FallBackVersion);
            Assert.That(versionHelper.GetBaseVersionString(FallBackVersion), Is.EqualTo(FallBackVersion));
            Assert.That(versionHelper.IsMyGet, Is.EqualTo(false));
            Assert.That(versionHelper.IsTeamCity, Is.EqualTo(false));
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

            Assert.That(versionHelper.IsMyGet, Is.EqualTo(false));
            Assert.That(versionHelper.IsTeamCity, Is.EqualTo(false));
            Assert.That(versionHelper.IsAppVeyor, Is.EqualTo(true));
            Assert.That(versionHelper.GetBaseVersionString(FallBackVersion), Is.EqualTo("2.3.4"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TeamCity_BuildVersion_Read(bool isMaster)
        {
            _arguments.HasArgument(VersionHelper.DefaultBuildVersionArgumentName).Returns(false);
            _teamCity.IsRunningOnTeamCity.Returns(true);
            _environment.SetEnvironmentVariable("BUILD_NUMBER", "12");
            _environment.SetEnvironmentVariable(VersionHelper.DefaultMasterBaseVersionEnvironmentVariable, "2.3");
            _environment.SetEnvironmentVariable(VersionHelper.DefaultPreReleaseBaseVersionEnvironmentVariable, "1.2");
            var versionHelper = GetVersionHelper(isMaster ? VersionHelper.DefaultDefaultBranchName : "someFeature");
            Assert.That(versionHelper.IsMyGet, Is.EqualTo(false));
            Assert.That(versionHelper.IsTeamCity, Is.EqualTo(true));
            Assert.That(versionHelper.IsAppVeyor, Is.EqualTo(false));
            if (isMaster)
                Assert.That(versionHelper.GetBaseVersionString(FallBackVersion), Is.EqualTo("2.3.12"));
            else
                Assert.That(versionHelper.GetBaseVersionString(FallBackVersion), Is.EqualTo("1.2.12"));
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void TeamCity_BuildVersion_NoEnvCreatesLogs(bool provideMaster, bool providePrerelease)
        {
            _arguments.HasArgument(VersionHelper.DefaultBuildVersionArgumentName).Returns(false);
            _teamCity.IsRunningOnTeamCity.Returns(true);
            _environment.SetEnvironmentVariable("BUILD_NUMBER", "12");
            if (provideMaster)
                _environment.SetEnvironmentVariable(VersionHelper.DefaultMasterBaseVersionEnvironmentVariable, "2.3");
            if (providePrerelease)
                _environment.SetEnvironmentVariable(VersionHelper.DefaultPreReleaseBaseVersionEnvironmentVariable, "1.2");
            var versionHelper = GetVersionHelper(VersionHelper.DefaultDefaultBranchName);
            Assert.That(versionHelper.IsMyGet, Is.EqualTo(false));
            Assert.That(versionHelper.IsTeamCity, Is.EqualTo(true));
            if (!provideMaster || !providePrerelease)
                Assert.That(versionHelper.GetBaseVersionString(FallBackVersion), Is.EqualTo(FallBackVersion));
            Assert.That(_log.Entries.Count, Is.EqualTo(provideMaster && providePrerelease ? 0 : 1));

        }

        [Test]
        public void MyGet_BuildVersion_Read()
        {
            _arguments.HasArgument(VersionHelper.DefaultBuildVersionArgumentName).Returns(false);
            _environment.SetEnvironmentVariable("PackageVersion", "2.3.4");
            _environment.SetEnvironmentVariable("BuildRunner", "MyGet");
            var versionHelper = GetVersionHelper(VersionHelper.DefaultDefaultBranchName);
            Assert.That(versionHelper.IsMyGet, Is.EqualTo(true));
            Assert.That(versionHelper.IsTeamCity, Is.EqualTo(false));
            Assert.That(versionHelper.IsAppVeyor, Is.EqualTo(false));
            Assert.That(versionHelper.GetBaseVersionString(FallBackVersion), Is.EqualTo("2.3.4"));
        }

        [Test]
        public void MyGet_BuildVersion_UsesDefault()
        {
            _arguments.HasArgument(VersionHelper.DefaultBuildVersionArgumentName).Returns(false);
            _environment.SetEnvironmentVariable("BuildRunner", "MyGet");
            var versionHelper = GetVersionHelper(VersionHelper.DefaultDefaultBranchName);
            Assert.That(versionHelper.IsMyGet, Is.EqualTo(true));
            Assert.That(versionHelper.IsTeamCity, Is.EqualTo(false));
            Assert.That(versionHelper.IsAppVeyor, Is.EqualTo(false));
            Assert.That(versionHelper.GetBaseVersionString(FallBackVersion), Is.EqualTo(FallBackVersion));
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

        private VersionHelper GetVersionHelper(string branch)
        {
            return new VersionHelper(_environment, _log, _arguments, _teamCity, _appVeyor, _globber, _fileSystem, branch);
        }
    }
}
